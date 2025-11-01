"""
Измеряем РЕАЛЬНЫЙ лаг между биржами

Отвечает на вопросы:
1. Сколько РЕАЛЬНО времени между движениями (не фиксированное окно)?
2. Насколько стабилен этот лаг?
3. С какой частотой возможности?
4. Зависит ли лаг от времени суток / волатильности?
"""
import polars as pl
import numpy as np
from numba import njit
from pathlib import Path
import glob
from datetime import datetime

# ==== ПАРАМЕТРЫ ====
DATA_ROOT = Path("data/market_data")
EXCHANGES = ["Binance", "GateIo"]
SYMBOLS = None  # None = все

# Пороги для детекции движения (увеличиваем!)
PRICE_MOVE_THRESHOLD = 0.005  # 0.5% (было 0.2%)
MIN_PROFITABLE_LAG = 0.003    # 0.3% прибыль после спреда (было 0.1%)

# Параметры анализа
MAX_LAG_WINDOW_MS = 2000  # Максимум 2 секунды ждем
MIN_LAG_WINDOW_MS = 10    # Минимум 10мс (меньше = не успеем)

# ============================================================
# NUMBA: ИЗМЕРЕНИЕ РЕАЛЬНОГО ЛАГА
# ============================================================

@njit(cache=True, fastmath=True)
def measure_real_lags(
    ts1: np.ndarray,          # timestamps биржи 1 (лидер)
    prices1: np.ndarray,      # цены биржи 1
    ts2: np.ndarray,          # timestamps биржи 2 (фолловер)
    prices2: np.ndarray,      # цены биржи 2
    move_threshold: float,
    min_profit: float,
    max_lag_ms: float
) -> tuple:
    """
    Измеряет РЕАЛЬНЫЙ лаг между движениями на биржах.

    Возвращает:
    - lag_times_ms: реальное время лага для каждой возможности
    - move_timestamps: когда произошло движение
    - move_magnitudes: величина движения на лидере
    - follower_moves: величина движения на фолловере
    - actual_profit: реальная прибыль
    """
    lag_times = []
    move_ts = []
    move_mags = []
    follower_move_mags = []
    actual_profits = []

    # Для каждого значимого движения на бирже 1
    for i in range(1, len(ts1)):
        price_change = (prices1[i] - prices1[i-1]) / prices1[i-1]

        if abs(price_change) < move_threshold:
            continue

        t_leader_move = ts1[i]
        price_leader = prices1[i]
        direction = 1.0 if price_change > 0 else -1.0

        # Находим цену на фолловере в момент движения лидера
        idx_follower_at_signal = np.searchsorted(ts2, t_leader_move)
        if idx_follower_at_signal >= len(ts2) or idx_follower_at_signal == 0:
            continue

        price_follower_entry = prices2[idx_follower_at_signal - 1]

        # Ищем когда фолловер РЕАЛЬНО догнал (а не через фиксированные 500мс!)
        # Критерий: цена на фолловере изменилась в том же направлении
        found_lag = False

        for j in range(idx_follower_at_signal, len(ts2)):
            t_follower = ts2[j]
            lag_ms = t_follower - t_leader_move

            # Если прошло слишком много времени - отбрасываем
            if lag_ms > max_lag_ms:
                break

            price_follower_now = prices2[j]
            follower_change = (price_follower_now - price_follower_entry) / price_follower_entry

            # Проверяем двинулся ли фолловер в том же направлении
            if direction > 0:  # Лидер вырос
                profit = follower_change
            else:  # Лидер упал
                profit = -follower_change

            # Если прибыль достаточная - фиксируем лаг
            if profit >= min_profit:
                lag_times.append(lag_ms)
                move_ts.append(t_leader_move)
                move_mags.append(abs(price_change))
                follower_move_mags.append(abs(follower_change))
                actual_profits.append(profit)
                found_lag = True
                break

    return (
        np.array(lag_times, dtype=np.float64),
        np.array(move_ts, dtype=np.int64),
        np.array(move_mags, dtype=np.float64),
        np.array(follower_move_mags, dtype=np.float64),
        np.array(actual_profits, dtype=np.float64)
    )

# ============================================================
# УТИЛИТЫ
# ============================================================

def normalize_symbol(symbol: str) -> str:
    return symbol.replace('_', '').replace('-', '').upper()

def common_symbols(root: Path, ex1: str, ex2: str) -> dict[str, dict[str, str]]:
    """Находит общие символы"""
    path1 = root / f"exchange={ex1}"
    path2 = root / f"exchange={ex2}"

    if not path1.exists() or not path2.exists():
        return {}

    def get_symbol_map(path: Path) -> dict[str, str]:
        symbol_map = {}
        for p in path.iterdir():
            if p.is_dir() and p.name.startswith("symbol="):
                original = p.name.split('=', 1)[1]
                normalized = normalize_symbol(original)
                symbol_map[normalized] = original
        return symbol_map

    map1 = get_symbol_map(path1)
    map2 = get_symbol_map(path2)

    common = set(map1.keys()) & set(map2.keys())

    return {
        norm: {ex1: map1[norm], ex2: map2[norm]}
        for norm in common
    }

def load_trades(exchange: str, symbol: str) -> pl.DataFrame:
    """Загружает сделки"""
    symbol_path = DATA_ROOT / f"exchange={exchange}" / f"symbol={symbol}"
    if not symbol_path.exists():
        return pl.DataFrame()

    files = glob.glob(str(symbol_path / "**/trades-*.parquet"), recursive=True)
    if not files:
        return pl.DataFrame()

    try:
        return (
            pl.scan_parquet(files)
            .select(["Timestamp", "Price", "Quantity", "Exchange"])
            .collect()
        )
    except:
        return pl.DataFrame()

@njit(cache=True, fastmath=True)
def determine_leader(
    ts1: np.ndarray,
    prices1: np.ndarray,
    ts2: np.ndarray,
    prices2: np.ndarray,
    threshold: float
) -> tuple:
    """Определяет кто чаще лидер"""
    count1 = 0
    count2 = 0

    for i in range(1, min(len(ts1), 1000)):
        if abs((prices1[i] - prices1[i-1]) / prices1[i-1]) < threshold:
            continue

        t1 = ts1[i]
        idx2 = np.searchsorted(ts2, t1)

        if idx2 < len(ts2):
            if t1 < ts2[idx2]:
                count1 += 1
            else:
                count2 += 1

    return count1, count2

# ============================================================
# АНАЛИЗ ВРЕМЕННЫХ ПАТТЕРНОВ
# ============================================================

def analyze_temporal_patterns(timestamps_ms: np.ndarray, lags_ms: np.ndarray) -> dict:
    """Анализирует как лаг зависит от времени суток"""

    # Конвертируем timestamps в datetime
    timestamps_dt = [datetime.fromtimestamp(ts / 1000) for ts in timestamps_ms]

    # Группируем по часам
    hourly_stats = {}
    for ts_dt, lag in zip(timestamps_dt, lags_ms):
        hour = ts_dt.hour
        if hour not in hourly_stats:
            hourly_stats[hour] = []
        hourly_stats[hour].append(lag)

    # Вычисляем статистики по часам
    results = {}
    for hour, lags in hourly_stats.items():
        lags_arr = np.array(lags)
        results[hour] = {
            "count": len(lags),
            "median_lag_ms": np.median(lags_arr),
            "std_lag_ms": np.std(lags_arr),
            "min_lag_ms": np.min(lags_arr),
            "max_lag_ms": np.max(lags_arr)
        }

    return results

# ============================================================
# ГЛАВНАЯ ФУНКЦИЯ
# ============================================================

def main():
    print("=== Измерение РЕАЛЬНОГО лага ===\n")
    print(f"Параметры:")
    print(f"  - Минимальное движение: {PRICE_MOVE_THRESHOLD*100:.1f}%")
    print(f"  - Минимальная прибыль: {MIN_PROFITABLE_LAG*100:.1f}%")
    print(f"  - Максимальное окно лага: {MAX_LAG_WINDOW_MS}мс")
    print()

    symbols_map = common_symbols(DATA_ROOT, *EXCHANGES)
    if SYMBOLS:
        symbols_map = {s: symbols_map[s] for s in SYMBOLS if s in symbols_map}

    if not symbols_map:
        print("[!] Нет общих символов")
        return

    print(f"[*] Найдено {len(symbols_map)} общих символов\n")

    all_results = []

    for norm_sym, originals in symbols_map.items():
        print(f"\n{'='*60}")
        print(f"[>] {norm_sym}")
        print(f"{'='*60}")

        # Загружаем данные
        df1 = load_trades(EXCHANGES[0], originals[EXCHANGES[0]])
        df2 = load_trades(EXCHANGES[1], originals[EXCHANGES[1]])

        if df1.is_empty() or df2.is_empty():
            print("[-] Нет данных")
            continue

        print(f"    Загружено: {len(df1):,} + {len(df2):,} сделок")

        # Определяем лидера
        df1_sorted = df1.sort("Timestamp")
        df2_sorted = df2.sort("Timestamp")

        ts1 = df1_sorted["Timestamp"].dt.epoch("ms").to_numpy()
        prices1 = df1_sorted["Price"].cast(pl.Float64).to_numpy()
        ts2 = df2_sorted["Timestamp"].dt.epoch("ms").to_numpy()
        prices2 = df2_sorted["Price"].cast(pl.Float64).to_numpy()

        count1, count2 = determine_leader(ts1, prices1, ts2, prices2, PRICE_MOVE_THRESHOLD)

        if count1 > count2:
            leader_idx, follower_idx = 0, 1
            leader_ts, leader_prices = ts1, prices1
            follower_ts, follower_prices = ts2, prices2
        else:
            leader_idx, follower_idx = 1, 0
            leader_ts, leader_prices = ts2, prices2
            follower_ts, follower_prices = ts1, prices1

        leader = EXCHANGES[leader_idx]
        follower = EXCHANGES[follower_idx]

        print(f"    LEADER: {leader} (вел {count1 if leader_idx == 0 else count2} раз)")
        print(f"    FOLLOWER: {follower}")

        # Измеряем реальные лаги
        print(f"\n    [*] Измерение реальных лагов...")

        (lags, move_ts, move_mags, follower_mags, profits) = measure_real_lags(
            leader_ts, leader_prices,
            follower_ts, follower_prices,
            PRICE_MOVE_THRESHOLD,
            MIN_PROFITABLE_LAG,
            MAX_LAG_WINDOW_MS
        )

        if len(lags) == 0:
            print(f"    [!] Нет возможностей с новыми порогами")
            continue

        print(f"    [+] Найдено: {len(lags)} возможностей")

        # Статистика лагов
        print(f"\n    === СТАТИСТИКА ЛАГОВ ===")
        print(f"    Median lag: {np.median(lags):.1f}мс")
        print(f"    Mean lag: {np.mean(lags):.1f}мс")
        print(f"    Std lag: {np.std(lags):.1f}мс")
        print(f"    Min lag: {np.min(lags):.1f}мс")
        print(f"    Max lag: {np.max(lags):.1f}мс")
        print(f"\n    Percentiles:")
        print(f"      10%: {np.percentile(lags, 10):.1f}мс")
        print(f"      25%: {np.percentile(lags, 25):.1f}мс")
        print(f"      50%: {np.percentile(lags, 50):.1f}мс")
        print(f"      75%: {np.percentile(lags, 75):.1f}мс")
        print(f"      90%: {np.percentile(lags, 90):.1f}мс")

        # Стабильность лага
        cv = np.std(lags) / np.mean(lags)  # Coefficient of variation
        print(f"\n    Coefficient of Variation: {cv:.2f}")
        if cv < 0.3:
            print(f"    -> СТАБИЛЬНЫЙ лаг (можно торговать!)")
        elif cv < 0.6:
            print(f"    -> УМЕРЕННО стабильный (осторожно)")
        else:
            print(f"    -> НЕСТАБИЛЬНЫЙ лаг (опасно торговать)")

        # Статистика прибыли
        print(f"\n    === СТАТИСТИКА ПРИБЫЛИ ===")
        print(f"    Mean profit: {np.mean(profits)*100:.3f}%")
        print(f"    Median profit: {np.median(profits)*100:.3f}%")
        print(f"    Max profit: {np.max(profits)*100:.3f}%")

        # Частота возможностей
        time_range_ms = move_ts[-1] - move_ts[0]
        time_range_hours = time_range_ms / (1000 * 3600)
        freq_per_hour = len(lags) / time_range_hours if time_range_hours > 0 else 0

        print(f"\n    === ЧАСТОТА ===")
        print(f"    Период данных: {time_range_hours:.1f} часов")
        print(f"    Возможностей в час: {freq_per_hour:.2f}")
        print(f"    Возможностей в день: {freq_per_hour * 24:.1f}")

        # Временные паттерны
        print(f"\n    === ВРЕМЕННЫЕ ПАТТЕРНЫ ===")
        temporal = analyze_temporal_patterns(move_ts, lags)

        if temporal:
            print(f"    {'Hour':<6} {'Count':<8} {'Median Lag':<12} {'Std':<8}")
            print(f"    {'-'*40}")
            for hour in sorted(temporal.keys()):
                stats = temporal[hour]
                print(f"    {hour:02d}:00  {stats['count']:<8} {stats['median_lag_ms']:<12.1f} {stats['std_lag_ms']:<8.1f}")

        # Сохраняем результаты
        df_result = pl.DataFrame({
            "symbol": norm_sym,
            "leader": leader,
            "follower": follower,
            "lag_ms": lags,
            "move_timestamp": move_ts,
            "leader_move_pct": move_mags * 100,
            "follower_move_pct": follower_mags * 100,
            "profit_pct": profits * 100
        })

        all_results.append(df_result)

    # Итоговый отчет
    if all_results:
        print(f"\n{'='*60}")
        print("=== ИТОГОВЫЙ ОТЧЕТ ===")
        print(f"{'='*60}\n")

        df_all = pl.concat(all_results, rechunk=False)

        print(f"[+] Всего возможностей: {len(df_all):,}")
        print(f"\n[*] Глобальная статистика лагов:")
        print(f"    Median: {df_all['lag_ms'].median():.1f}мс")
        print(f"    Mean: {df_all['lag_ms'].mean():.1f}мс")
        print(f"    Std: {df_all['lag_ms'].std():.1f}мс")

        print(f"\n[*] Глобальная статистика прибыли:")
        print(f"    Mean: {df_all['profit_pct'].mean():.3f}%")
        print(f"    Median: {df_all['profit_pct'].median():.3f}%")

        print(f"\n[*] По символам (топ-10):")
        symbol_stats = df_all.group_by("symbol").agg([
            pl.len().alias("count"),
            pl.col("lag_ms").median().alias("median_lag_ms"),
            pl.col("profit_pct").mean().alias("avg_profit_pct"),
            pl.col("profit_pct").max().alias("max_profit_pct")
        ]).sort("count", descending=True).head(10)
        print(symbol_stats)

        print(f"\n[TOP-20] лучших возможностей:")
        top = df_all.sort("profit_pct", descending=True).head(20)
        print(top.select([
            "symbol", "leader", "lag_ms", "profit_pct"
        ]))

        # Сохраняем
        output_file = Path("analysis/real_lag_analysis.parquet")
        output_file.parent.mkdir(exist_ok=True)
        df_all.write_parquet(output_file)
        print(f"\n[SAVED] {output_file}")

        # Критические выводы
        print(f"\n{'='*60}")
        print("=== КРИТИЧЕСКИЕ ВЫВОДЫ ===")
        print(f"{'='*60}")

        median_lag = df_all['lag_ms'].median()
        std_lag = df_all['lag_ms'].std()
        cv_global = std_lag / df_all['lag_ms'].mean()

        print(f"\n1. РЕАЛЬНЫЙ ЛАГ: {median_lag:.0f}мс (не фиксированные 500мс!)")

        if cv_global < 0.3:
            print(f"2. СТАБИЛЬНОСТЬ: ВЫСОКАЯ (CV={cv_global:.2f}) - можно торговать")
        elif cv_global < 0.6:
            print(f"2. СТАБИЛЬНОСТЬ: СРЕДНЯЯ (CV={cv_global:.2f}) - осторожно")
        else:
            print(f"2. СТАБИЛЬНОСТЬ: НИЗКАЯ (CV={cv_global:.2f}) - опасно")

        total_hours = (df_all['move_timestamp'].max() - df_all['move_timestamp'].min()) / (1000 * 3600)
        freq = len(df_all) / total_hours
        print(f"3. ЧАСТОТА: {freq:.2f} возможностей/час = {freq*24:.0f} в день")

        median_profit = df_all['profit_pct'].median()
        print(f"4. ПРИБЫЛЬ: медиана {median_profit:.3f}% (после спреда 0.1-0.2% = реально ~{median_profit-0.15:.3f}%)")

        print(f"\n5. ТОРГОВАЯ СТРАТЕГИЯ:")
        if median_lag < 100:
            print(f"   - Лаг <100мс -> нужен ОЧЕНЬ быстрый execution (колокация?)")
        elif median_lag < 300:
            print(f"   - Лаг ~{median_lag:.0f}мс -> можно торговать с WebSocket + быстрый API")
        else:
            print(f"   - Лаг >{median_lag:.0f}мс -> есть время, но конкуренция высокая")

        if freq < 1:
            print(f"   - Частота низкая ({freq:.2f}/час) -> мониторить несколько символов")
        else:
            print(f"   - Частота нормальная ({freq:.2f}/час) -> можно фокусироваться")

    else:
        print("\n[!] Возможностей не найдено")
        print("Попробуй уменьшить пороги PRICE_MOVE_THRESHOLD и MIN_PROFITABLE_LAG")

if __name__ == "__main__":
    main()
