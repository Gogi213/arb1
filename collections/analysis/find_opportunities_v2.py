"""
Улучшенная версия find_opportunities.py - КЛАСТЕРНЫЙ ПОДХОД

Ключевая идея:
Крупная сделка ($100k) не исполняется одним трейдом, а разбивается
на множество мелких ($1k-$5k каждый) за короткое время (50-200мс).

Что делает скрипт:
1. Группирует сделки в КЛАСТЕРЫ (временные окна по 100мс)
2. Находит кластеры с крупным объемом (>$5k)
3. Проверяет двинулась ли цена после кластера
4. Определяет какая биржа обычно опережает (лидер)
5. Для кластеров на лидере ищет возможности на фолловере

Пример:
  t=0ms:    5 трейдов по $1000 = $5000 кластер
  t=100ms:  цена +0.1% (движение!)
  t=200ms:  на второй бирже еще старая цена -> ВОЗМОЖНОСТЬ!
"""
import polars as pl
import numpy as np
from numba import njit
from pathlib import Path
import glob

# ==== ПАРАМЕТРЫ ====
DATA_ROOT = Path("data/market_data")
EXCHANGES = ["Binance", "GateIo"]
SYMBOLS = None

# Параметры эффективных принтов
TIME_WINDOW_MS = 100     # Группируем сделки в окна по 100мс
MIN_CLUSTER_USD = 5000   # Минимальный объем кластера сделок
MIN_PRICE_MOVE = 0.0005  # 0.05% минимальное движение цены после кластера

# Параметры торговли
TARGET_PROFIT = 0.001    # 0.1% целевая прибыль

# ============================================================
# ФИЛЬТРАЦИЯ ЭФФЕКТИВНЫХ ПРИНТОВ (кластерный подход)
# ============================================================

def find_trade_clusters(df: pl.DataFrame, window_ms: int, min_cluster_usd: float) -> pl.DataFrame:
    """
    Группирует сделки в кластеры по временным окнам.

    Крупный ордер = много мелких сделок за короткое время.
    Находим такие кластеры и проверяем двинулась ли цена после них.
    """
    df = df.sort("Timestamp").with_columns([
        pl.col("Timestamp").dt.epoch("ms").alias("ts_ms"),
        (pl.col("Price") * pl.col("Quantity")).alias("volume_usd")
    ])

    # Группируем в временные окна
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
            pl.count().alias("trade_count")
        ])
        .filter(pl.col("total_volume_usd") >= min_cluster_usd)
        .sort("cluster_ms")
    )

    # Проверяем что произошло с ценой ПОСЛЕ кластера
    # Смотрим на следующий кластер (через 100-200мс)
    df_clustered = df_clustered.with_columns([
        pl.col("price_end").shift(-1).alias("price_after")
    ]).drop_nulls()

    # Вычисляем движение цены после кластера
    df_clustered = df_clustered.with_columns([
        ((pl.col("price_after") - pl.col("price_end")) / pl.col("price_end")).alias("price_move")
    ])

    # Оставляем только те кластеры после которых цена реально двинулась
    return df_clustered.filter(pl.col("price_move").abs() >= MIN_PRICE_MOVE)

# ============================================================
# ОПРЕДЕЛЕНИЕ ЛИДЕРА (простой метод)
# ============================================================

@njit(cache=True, fastmath=True)
def count_who_leads(
    ts1: np.ndarray,
    prices1: np.ndarray,
    ts2: np.ndarray,
    prices2: np.ndarray,
    price_threshold: float
) -> tuple:
    """
    Считает сколько раз каждая биржа опережает другую.

    Логика:
    - Для каждого движения цены на бирже 1
    - Ищем когда такое же движение произошло на бирже 2
    - Если на бирже 1 раньше -> +1 к счетчику биржи 1

    Возвращает: (count_1_leads, count_2_leads)
    """
    count_1_leads = 0
    count_2_leads = 0

    # Для каждой сделки на бирже 1
    for i in range(1, len(ts1)):
        price_change_1 = abs((prices1[i] - prices1[i-1]) / prices1[i-1])

        if price_change_1 < price_threshold:
            continue

        # Время когда цена изменилась на бирже 1
        t1 = ts1[i]
        target_price = prices1[i]

        # Ищем когда цена достигла такого же уровня на бирже 2
        # (с допуском ±0.1%)
        tolerance = target_price * 0.001

        for j in range(len(ts2)):
            if abs(prices2[j] - target_price) <= tolerance:
                t2 = ts2[j]

                # Кто был первым?
                if t1 < t2:
                    count_1_leads += 1
                elif t2 < t1:
                    count_2_leads += 1

                break  # Нашли первое совпадение

    return count_1_leads, count_2_leads

def who_is_faster(df: pl.DataFrame, ex1: str, ex2: str) -> tuple[str, str]:
    """
    Определяет какая биржа быстрее (лидер) и медленнее (фолловер).

    Возвращает: (leader, follower)
    """
    df1 = (
        df.filter(pl.col("Exchange") == ex1)
        .sort("Timestamp")
        .with_columns(pl.col("Timestamp").dt.epoch("ms").alias("ts_ms"))
    )

    df2 = (
        df.filter(pl.col("Exchange") == ex2)
        .sort("Timestamp")
        .with_columns(pl.col("Timestamp").dt.epoch("ms").alias("ts_ms"))
    )

    if df1.is_empty() or df2.is_empty():
        raise ValueError("Одна из бирж не имеет данных")

    # Берем первые 1000 сделок для быстрого анализа
    ts1 = df1.head(1000)["ts_ms"].to_numpy()
    prices1 = df1.head(1000)["Price"].cast(pl.Float64).to_numpy()
    ts2 = df2.head(1000)["ts_ms"].to_numpy()
    prices2 = df2.head(1000)["Price"].cast(pl.Float64).to_numpy()

    count_1_leads, count_2_leads = count_who_leads(
        ts1, prices1, ts2, prices2, MIN_PRICE_MOVE
    )

    print(f"    {ex1} опережает: {count_1_leads} раз")
    print(f"    {ex2} опережает: {count_2_leads} раз")

    if count_1_leads > count_2_leads:
        return ex1, ex2
    else:
        return ex2, ex1

# ============================================================
# ПОИСК ВОЗМОЖНОСТЕЙ (Numba)
# ============================================================

@njit(cache=True, fastmath=True)
def find_trading_opportunities(
    # Эффективные принты на лидере
    leader_ts: np.ndarray,
    leader_prices: np.ndarray,
    # Все сделки на фолловере
    follower_ts: np.ndarray,
    follower_prices: np.ndarray,
    # Параметры
    target_profit: float
) -> tuple:
    """
    Для каждого эффективного принта на лидере:
    1. Смотрим цену на фолловере В МОМЕНТ принта
    2. Смотрим цену на фолловере ЧЕРЕЗ 500мс
    3. Если разница >= target_profit -> это возможность
    """
    opp_ts = []
    opp_leader_price = []
    opp_follower_before = []
    opp_follower_after = []
    opp_profit = []

    for i in range(len(leader_ts)):
        t_leader = leader_ts[i]
        p_leader = leader_prices[i]

        # Цена на фолловере В МОМЕНТ импульса на лидере
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

        # Вычисляем прибыль
        # Направление движения определяем по лидеру
        if i > 0:
            leader_direction = p_leader - leader_prices[i-1]
        else:
            continue

        if leader_direction > 0:
            # Лидер вырос -> ожидаем рост на фолловере
            profit = (p_follower_after - p_follower_before) / p_follower_before
        else:
            # Лидер упал -> ожидаем падение на фолловере
            profit = (p_follower_before - p_follower_after) / p_follower_before

        if profit >= target_profit:
            opp_ts.append(t_leader)
            opp_leader_price.append(p_leader)
            opp_follower_before.append(p_follower_before)
            opp_follower_after.append(p_follower_after)
            opp_profit.append(profit)

    return (
        np.array(opp_ts, dtype=np.int64),
        np.array(opp_leader_price, dtype=np.float64),
        np.array(opp_follower_before, dtype=np.float64),
        np.array(opp_follower_after, dtype=np.float64),
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
    print("=== Поиск возможностей (кластерный анализ) ===\n")
    print(f"Параметры:")
    print(f"  - Временное окно кластера: {TIME_WINDOW_MS}мс")
    print(f"  - Мин. объем кластера: ${MIN_CLUSTER_USD:,}")
    print(f"  - Мин. движение цены: {MIN_PRICE_MOVE*100:.2f}%")
    print(f"  - Целевая прибыль: {TARGET_PROFIT*100:.2f}%")
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

        print(f"  Загружено: {len(df1):,} + {len(df2):,} сделок")

        # Объединяем
        df_all = pl.concat([df1, df2])

        # 1. Определяем лидера
        print(f"\n  [1] Определение лидера...")
        try:
            leader, follower = who_is_faster(df_all, EXCHANGES[0], EXCHANGES[1])
            print(f"      LEADER: {leader}")
            print(f"      FOLLOWER: {follower}")
        except ValueError as e:
            print(f"  [!] {e}\n")
            continue

        # 2. Фильтруем эффективные принты (кластеры) на лидере
        print(f"\n  [2] Поиск кластеров крупных сделок на {leader}...")
        df_leader_clusters = find_trade_clusters(
            df_all.filter(pl.col("Exchange") == leader),
            TIME_WINDOW_MS,
            MIN_CLUSTER_USD
        )

        if df_leader_clusters.is_empty():
            print(f"  [!] Нет значимых кластеров\n")
            continue

        print(f"      Найдено: {len(df_leader_clusters):,} кластеров")
        print(f"      Средний объем: ${df_leader_clusters['total_volume_usd'].mean():,.0f}")
        print(f"      Макс. объем: ${df_leader_clusters['total_volume_usd'].max():,.0f}")

        # 3. Готовим данные для Numba
        # Кластеры на лидере (уже отсортированы)
        leader_ts = df_leader_clusters["cluster_ms"].to_numpy()
        leader_prices = df_leader_clusters["price_end"].cast(pl.Float64).to_numpy()

        # Все сделки на фолловере
        follower_data = (
            df_all
            .filter(pl.col("Exchange") == follower)
            .sort("Timestamp")
            .with_columns(pl.col("Timestamp").dt.epoch("ms").alias("ts_ms"))
        )

        follower_ts = follower_data["ts_ms"].to_numpy()
        follower_prices = follower_data["Price"].cast(pl.Float64).to_numpy()

        # 4. Поиск возможностей (JIT)
        print(f"\n  [3] Поиск торговых возможностей на {follower}...")

        (opp_ts, opp_lp, opp_fb, opp_fa, opp_profit) = find_trading_opportunities(
            leader_ts, leader_prices,
            follower_ts, follower_prices,
            TARGET_PROFIT
        )

        if len(opp_ts) == 0:
            print(f"  [!] Возможностей не найдено\n")
            continue

        print(f"      НАЙДЕНО: {len(opp_ts)} возможностей!")
        print(f"      Средняя прибыль: {opp_profit.mean()*100:.3f}%")
        print(f"      Макс. прибыль: {opp_profit.max()*100:.3f}%")

        # Сохраняем результаты
        df_opps = pl.DataFrame({
            "symbol": [norm_sym] * len(opp_ts),
            "timestamp": opp_ts,
            "leader": [leader] * len(opp_ts),
            "follower": [follower] * len(opp_ts),
            "leader_price": opp_lp,
            "follower_price_before": opp_fb,
            "follower_price_after": opp_fa,
            "profit_pct": opp_profit * 100
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
        print(f"[+] Средняя прибыль: {df_all['profit_pct'].mean():.3f}%")
        print(f"[+] Медианная прибыль: {df_all['profit_pct'].median():.3f}%")

        print(f"\n[*] По биржам-лидерам:")
        print(df_all.group_by("leader").agg([
            pl.count().alias("count"),
            pl.col("profit_pct").mean().alias("avg_profit_pct")
        ]).sort("count", descending=True))

        print(f"\n[*] Топ-10 символов:")
        print(df_all.group_by("symbol").agg([
            pl.count().alias("count"),
            pl.col("profit_pct").mean().alias("avg_profit"),
            pl.col("profit_pct").max().alias("max_profit")
        ]).sort("count", descending=True).head(10))

        print(f"\n[TOP-15] лучших возможностей:")
        print(df_all.sort("profit_pct", descending=True).head(15))

        # Сохраняем
        output = Path("analysis/opportunities_effective_prints.parquet")
        output.parent.mkdir(exist_ok=True)
        df_all.write_parquet(output)
        print(f"\n[SAVED] {output}")

        # Рекомендации
        leader_stats = df_all.group_by("leader").agg(pl.count().alias("count"))
        main_leader = leader_stats.sort("count", descending=True)["leader"][0]

        print(f"\n{'='*60}")
        print("=== РЕКОМЕНДАЦИИ ===")
        print(f"{'='*60}")
        print(f"""
1. {main_leader} чаще всего является лидером
2. Подключись к WebSocket {main_leader}
3. Отслеживай КЛАСТЕРЫ крупных сделок:
   - Много трейдов за {TIME_WINDOW_MS}мс
   - Суммарный объем >${MIN_CLUSTER_USD:,}$
4. Когда видишь кластер -> быстро ставь ордер на второй бирже
5. У тебя ~500мс чтобы успеть
6. Ожидаемая прибыль: ~{df_all['profit_pct'].median():.2f}%

ВАЖНО: Крупный ордер = много мелких трейдов, смотри на сумму!
        """)
    else:
        print("\n[!] Возможностей не найдено")

if __name__ == "__main__":
    main()
