"""
АДАПТИВНАЯ версия - автоматически подстраивается под каждый актив

Ключевые улучшения:
1. Автокалибровка порогов объема под каждую монету
2. Динамическое определение лидера (каждые 5 минут)
3. Учет спреда (bid-ask)
"""
import polars as pl
import numpy as np
from numba import njit
from pathlib import Path
import glob
from dataclasses import dataclass

# ==== ПАРАМЕТРЫ ====
DATA_ROOT = Path("data/market_data")
EXCHANGES = ["Binance", "GateIo"]
SYMBOLS = None

# Адаптивные параметры (НЕ хардкод!)
TIME_WINDOW_MS = 100
LEADER_RECALC_MINUTES = 5  # Пересчитываем лидера каждые 5 минут

# Параметры калибровки
CLUSTER_PERCENTILE = 0.7   # 70-й перцентиль объема = порог кластера
MIN_PRICE_MOVE = 0.0005    # 0.05%

# Параметры торговли
ESTIMATED_SPREAD_PCT = 0.001  # 0.1% - типичный спред на крипте
TARGET_PROFIT = 0.002         # 0.2% целевая прибыль (с учетом спреда)

# ============================================================
# АДАПТИВНАЯ КАЛИБРОВКА
# ============================================================

@dataclass
class AssetProfile:
    """Профиль актива - автоматически калибруется"""
    symbol: str
    min_cluster_usd: float
    time_window_ms: int
    median_trade_interval_ms: float
    total_volume_usd: float

def calibrate_asset(df: pl.DataFrame, symbol: str) -> AssetProfile:
    """
    Автоматически подбирает параметры под актив

    Логика:
    - BTC торгуется большими объемами → высокий порог кластера
    - Альткоин торгуется мелкими → низкий порог
    """
    # Вычисляем объемы сделок
    df = df.with_columns([
        (pl.col("Price") * pl.col("Quantity")).alias("volume_usd")
    ])

    # Находим характерный объем (70-й перцентиль)
    volume_threshold = df["volume_usd"].quantile(CLUSTER_PERCENTILE)

    # Минимальный кластер = 3x от характерного объема одной сделки
    min_cluster_usd = volume_threshold * 3

    # Защита от слишком маленьких значений
    if min_cluster_usd < 100:
        min_cluster_usd = 100

    # Вычисляем медианный интервал между сделками
    df_sorted = df.sort("Timestamp")
    intervals_ms = df_sorted["Timestamp"].diff().drop_nulls().dt.total_milliseconds()
    median_interval = intervals_ms.median()

    # Адаптивное окно = 10x медианный интервал
    # (чтобы в окне было ~10 сделок)
    adaptive_window = max(median_interval * 10, TIME_WINDOW_MS)

    # Общий объем
    total_volume = df["volume_usd"].sum()

    return AssetProfile(
        symbol=symbol,
        min_cluster_usd=float(min_cluster_usd),
        time_window_ms=float(adaptive_window),
        median_trade_interval_ms=float(median_interval),
        total_volume_usd=float(total_volume)
    )

# ============================================================
# КЛАСТЕРИЗАЦИЯ (как раньше, но с адаптивными параметрами)
# ============================================================

def find_trade_clusters(df: pl.DataFrame, window_ms: int, min_cluster_usd: float) -> pl.DataFrame:
    """Группирует сделки в кластеры"""
    df = df.sort("Timestamp").with_columns([
        pl.col("Timestamp").dt.epoch("ms").alias("ts_ms"),
        (pl.col("Price") * pl.col("Quantity")).alias("volume_usd")
    ])

    df_clustered = (
        df
        .with_columns([
            (pl.col("ts_ms") // window_ms * window_ms).alias("cluster_ms")
        ])
        .group_by("cluster_ms")
        .agg([
            pl.col("Timestamp").first().alias("timestamp"),
            pl.col("volume_usd").sum().alias("total_volume_usd"),
            pl.col("Price").first().alias("price_start"),
            pl.col("Price").last().alias("price_end"),
            pl.col("Price").min().alias("price_min"),  # Для оценки спреда
            pl.col("Price").max().alias("price_max"),
            pl.count().alias("trade_count")
        ])
        .filter(pl.col("total_volume_usd") >= min_cluster_usd)
        .sort("cluster_ms")
    )

    # Движение цены после кластера
    df_clustered = df_clustered.with_columns([
        pl.col("price_end").shift(-1).alias("price_after")
    ]).drop_nulls()

    df_clustered = df_clustered.with_columns([
        ((pl.col("price_after") - pl.col("price_end")) / pl.col("price_end")).alias("price_move"),
        # Оценка спреда внутри кластера
        ((pl.col("price_max") - pl.col("price_min")) / pl.col("price_end")).alias("cluster_spread")
    ])

    return df_clustered.filter(pl.col("price_move").abs() >= MIN_PRICE_MOVE)

# ============================================================
# ДИНАМИЧЕСКОЕ ОПРЕДЕЛЕНИЕ ЛИДЕРА
# ============================================================

@njit(cache=True, fastmath=True)
def count_who_leads(
    ts1: np.ndarray,
    prices1: np.ndarray,
    ts2: np.ndarray,
    prices2: np.ndarray,
    price_threshold: float
) -> tuple:
    """Считает кто чаще опережает (Numba JIT)"""
    count_1_leads = 0
    count_2_leads = 0

    for i in range(1, len(ts1)):
        price_change_1 = abs((prices1[i] - prices1[i-1]) / prices1[i-1])
        if price_change_1 < price_threshold:
            continue

        t1 = ts1[i]
        target_price = prices1[i]
        tolerance = target_price * 0.001

        for j in range(len(ts2)):
            if abs(prices2[j] - target_price) <= tolerance:
                t2 = ts2[j]
                if t1 < t2:
                    count_1_leads += 1
                elif t2 < t1:
                    count_2_leads += 1
                break

    return count_1_leads, count_2_leads

def who_is_faster_adaptive(df: pl.DataFrame, ex1: str, ex2: str, time_window_minutes: int = 5) -> list[dict]:
    """
    Динамически определяет лидера в КАЖДОМ временном окне

    Возвращает: список {"start_time", "end_time", "leader", "follower", "confidence"}
    """
    # Разбиваем данные на окна по N минут
    df = df.sort("Timestamp").with_columns([
        pl.col("Timestamp").dt.epoch("ms").alias("ts_ms")
    ])

    min_ts = df["ts_ms"].min()
    max_ts = df["ts_ms"].max()

    window_size_ms = time_window_minutes * 60 * 1000

    leaders = []

    for window_start in range(min_ts, max_ts, window_size_ms):
        window_end = window_start + window_size_ms

        # Данные в этом окне
        df_window = df.filter(
            (pl.col("ts_ms") >= window_start) &
            (pl.col("ts_ms") < window_end)
        )

        if df_window.is_empty():
            continue

        # Разделяем по биржам
        df1 = df_window.filter(pl.col("Exchange") == ex1).head(500)
        df2 = df_window.filter(pl.col("Exchange") == ex2).head(500)

        if df1.is_empty() or df2.is_empty():
            continue

        # Numba анализ
        ts1 = df1["ts_ms"].to_numpy()
        prices1 = df1["Price"].cast(pl.Float64).to_numpy()
        ts2 = df2["ts_ms"].to_numpy()
        prices2 = df2["Price"].cast(pl.Float64).to_numpy()

        count_1, count_2 = count_who_leads(ts1, prices1, ts2, prices2, MIN_PRICE_MOVE)

        if count_1 + count_2 == 0:
            continue

        if count_1 > count_2:
            leader, follower = ex1, ex2
            confidence = count_1 / (count_1 + count_2)
        else:
            leader, follower = ex2, ex1
            confidence = count_2 / (count_1 + count_2)

        leaders.append({
            "start_time": window_start,
            "end_time": window_end,
            "leader": leader,
            "follower": follower,
            "confidence": confidence,
            "leader_count": max(count_1, count_2),
            "follower_count": min(count_1, count_2)
        })

    return leaders

# ============================================================
# ПОИСК ВОЗМОЖНОСТЕЙ (с учетом спреда)
# ============================================================

@njit(cache=True, fastmath=True)
def find_trading_opportunities(
    leader_ts: np.ndarray,
    leader_prices: np.ndarray,
    follower_ts: np.ndarray,
    follower_prices: np.ndarray,
    target_profit: float,
    estimated_spread: float
) -> tuple:
    """Ищет возможности с учетом спреда"""
    opp_ts = []
    opp_lp = []
    opp_fb = []
    opp_fa = []
    opp_profit = []

    for i in range(len(leader_ts)):
        t_leader = leader_ts[i]
        p_leader = leader_prices[i]

        # Цена на фолловере В МОМЕНТ импульса
        idx_before = np.searchsorted(follower_ts, t_leader)
        if idx_before >= len(follower_ts) or idx_before == 0:
            continue

        p_follower_before = follower_prices[idx_before - 1]

        # Цена на фолловере ЧЕРЕЗ 500мс
        t_after = t_leader + 500
        idx_after = np.searchsorted(follower_ts, t_after)
        if idx_after >= len(follower_ts):
            continue

        p_follower_after = follower_prices[idx_after]

        # Направление движения
        if i > 0:
            leader_direction = p_leader - leader_prices[i-1]
        else:
            continue

        if leader_direction > 0:
            profit = (p_follower_after - p_follower_before) / p_follower_before
        else:
            profit = (p_follower_before - p_follower_after) / p_follower_before

        # Вычитаем спред
        net_profit = profit - estimated_spread

        if net_profit >= target_profit:
            opp_ts.append(t_leader)
            opp_lp.append(p_leader)
            opp_fb.append(p_follower_before)
            opp_fa.append(p_follower_after)
            opp_profit.append(net_profit)

    return (
        np.array(opp_ts, dtype=np.int64),
        np.array(opp_lp, dtype=np.float64),
        np.array(opp_fb, dtype=np.float64),
        np.array(opp_fa, dtype=np.float64),
        np.array(opp_profit, dtype=np.float64)
    )

# ============================================================
# УТИЛИТЫ
# ============================================================

def normalize_symbol(s: str) -> str:
    return s.replace('_', '').replace('-', '').upper()

def common_symbols(root: Path, ex1: str, ex2: str) -> dict:
    path1 = root / f"exchange={ex1}"
    path2 = root / f"exchange={ex2}"

    if not path1.exists() or not path2.exists():
        return {}

    def get_map(p):
        return {
            normalize_symbol(d.name.split('=')[1]): d.name.split('=')[1]
            for d in p.iterdir()
            if d.is_dir() and d.name.startswith("symbol=")
        }

    map1, map2 = get_map(path1), get_map(path2)
    common = set(map1.keys()) & set(map2.keys())

    return {n: {ex1: map1[n], ex2: map2[n]} for n in common}

def load_trades(exchange: str, symbol: str) -> pl.DataFrame:
    path = DATA_ROOT / f"exchange={exchange}" / f"symbol={symbol}"
    if not path.exists():
        return pl.DataFrame()

    files = glob.glob(str(path / "**/trades-*.parquet"), recursive=True)
    if not files:
        return pl.DataFrame()

    try:
        return pl.scan_parquet(files).select([
            "Timestamp", "Price", "Quantity", "Exchange"
        ]).collect()
    except:
        return pl.DataFrame()

# ============================================================
# ГЛАВНАЯ ФУНКЦИЯ
# ============================================================

def main():
    print("=== АДАПТИВНЫЙ поиск возможностей ===\n")
    print("Особенности:")
    print("  - Автокалибровка порогов под каждый актив")
    print("  - Динамическое определение лидера (каждые 5 мин)")
    print(f"  - Учет спреда (~{ESTIMATED_SPREAD_PCT*100:.2f}%)")
    print()

    symbols_map = common_symbols(DATA_ROOT, *EXCHANGES)
    if SYMBOLS:
        symbols_map = {s: symbols_map[s] for s in SYMBOLS if s in symbols_map}

    if not symbols_map:
        print("[!] Нет общих символов")
        return

    print(f"[+] Найдено {len(symbols_map)} общих символов\n")

    all_opportunities = []

    for norm_sym, originals in symbols_map.items():
        print(f"{'='*60}")
        print(f"[>] {norm_sym}")
        print(f"{'='*60}")

        # Загружаем
        df1 = load_trades(EXCHANGES[0], originals[EXCHANGES[0]])
        df2 = load_trades(EXCHANGES[1], originals[EXCHANGES[1]])

        if df1.is_empty() or df2.is_empty():
            print("  [-] Нет данных\n")
            continue

        df_all = pl.concat([df1, df2])
        print(f"  Загружено: {len(df1):,} + {len(df2):,} сделок")

        # 1. АДАПТИВНАЯ КАЛИБРОВКА
        print(f"\n  [1] Калибровка параметров...")
        profile = calibrate_asset(df_all, norm_sym)

        print(f"      Мин. кластер: ${profile.min_cluster_usd:,.0f}")
        print(f"      Временное окно: {profile.time_window_ms:.0f}мс")
        print(f"      Медиана интервала: {profile.median_trade_interval_ms:.1f}мс")

        # 2. ДИНАМИЧЕСКОЕ ОПРЕДЕЛЕНИЕ ЛИДЕРА
        print(f"\n  [2] Динамический анализ лидера (окна по {LEADER_RECALC_MINUTES}мин)...")
        leader_windows = who_is_faster_adaptive(
            df_all, EXCHANGES[0], EXCHANGES[1], LEADER_RECALC_MINUTES
        )

        if not leader_windows:
            print(f"  [!] Не удалось определить лидера\n")
            continue

        # Показываем статистику
        leaders_summary = {}
        for lw in leader_windows:
            leaders_summary[lw["leader"]] = leaders_summary.get(lw["leader"], 0) + 1

        print(f"      Всего окон: {len(leader_windows)}")
        for ex, count in leaders_summary.items():
            pct = count / len(leader_windows) * 100
            print(f"      {ex} лидирует: {count} окон ({pct:.1f}%)")

        # Основной лидер
        main_leader = max(leaders_summary, key=leaders_summary.get)
        main_follower = EXCHANGES[0] if main_leader == EXCHANGES[1] else EXCHANGES[1]

        print(f"      Основной лидер: {main_leader}")

        # 3. Поиск кластеров на основном лидере
        print(f"\n  [3] Поиск кластеров на {main_leader}...")
        df_leader_clusters = find_trade_clusters(
            df_all.filter(pl.col("Exchange") == main_leader),
            profile.time_window_ms,
            profile.min_cluster_usd
        )

        if df_leader_clusters.is_empty():
            print(f"  [!] Нет значимых кластеров\n")
            continue

        print(f"      Найдено: {len(df_leader_clusters):,} кластеров")
        print(f"      Средний объем: ${df_leader_clusters['total_volume_usd'].mean():,.0f}")

        # 4. Поиск возможностей
        print(f"\n  [4] Поиск возможностей на {main_follower}...")

        leader_ts = df_leader_clusters["cluster_ms"].to_numpy()
        leader_prices = df_leader_clusters["price_end"].cast(pl.Float64).to_numpy()

        follower_data = (
            df_all
            .filter(pl.col("Exchange") == main_follower)
            .sort("Timestamp")
            .with_columns(pl.col("Timestamp").dt.epoch("ms").alias("ts_ms"))
        )

        follower_ts = follower_data["ts_ms"].to_numpy()
        follower_prices = follower_data["Price"].cast(pl.Float64).to_numpy()

        (opp_ts, opp_lp, opp_fb, opp_fa, opp_profit) = find_trading_opportunities(
            leader_ts, leader_prices,
            follower_ts, follower_prices,
            TARGET_PROFIT,
            ESTIMATED_SPREAD_PCT
        )

        if len(opp_ts) == 0:
            print(f"  [!] Возможностей нет\n")
            continue

        print(f"      НАЙДЕНО: {len(opp_ts)} возможностей (с учетом спреда)!")
        print(f"      Средняя прибыль: {opp_profit.mean()*100:.3f}%")
        print(f"      Макс. прибыль: {opp_profit.max()*100:.3f}%")

        # Сохраняем
        df_opps = pl.DataFrame({
            "symbol": [norm_sym] * len(opp_ts),
            "timestamp": opp_ts,
            "leader": [main_leader] * len(opp_ts),
            "follower": [main_follower] * len(opp_ts),
            "leader_price": opp_lp,
            "follower_price_before": opp_fb,
            "follower_price_after": opp_fa,
            "net_profit_pct": opp_profit * 100,  # Уже с учетом спреда
            "calibrated_cluster_usd": [profile.min_cluster_usd] * len(opp_ts)
        })

        all_opportunities.append(df_opps)
        print()

    # Итоговый отчет
    if all_opportunities:
        print(f"{'='*60}")
        print("=== ИТОГОВЫЙ ОТЧЕТ ===")
        print(f"{'='*60}\n")

        df_all = pl.concat(all_opportunities, rechunk=False)

        print(f"[+] Всего возможностей: {len(df_all):,}")
        print(f"[+] Средняя прибыль (нетто): {df_all['net_profit_pct'].mean():.3f}%")

        print(f"\n[*] По биржам-лидерам:")
        print(df_all.group_by("leader").agg([
            pl.count().alias("count"),
            pl.col("net_profit_pct").mean().alias("avg_profit")
        ]).sort("count", descending=True))

        print(f"\n[TOP-15] лучших возможностей:")
        print(df_all.sort("net_profit_pct", descending=True).head(15))

        # Сохраняем
        output = Path("analysis/opportunities_adaptive.parquet")
        output.parent.mkdir(exist_ok=True)
        df_all.write_parquet(output)
        print(f"\n[SAVED] {output}")

    else:
        print("\n[!] Возможностей не найдено")

if __name__ == "__main__":
    main()
