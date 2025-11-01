"""
ТОРГОВЫЙ ИНСТРУМЕНТ: Поиск импульсов на быстрой бирже для торговли на лагающей

Execution time: 8ms на Gate
Задача: Найти монеты где можно успеть торговать импульсы

Логика:
1. Автоматическая калибровка порогов (на основе волатильности, спреда)
2. Определение лидера/фолловера
3. Детекция импульсов на лидере
4. Измерение торгового окна
5. Фильтрация по execution constraints
6. Выдача торгуемых символов
"""
import polars as pl
import numpy as np
from numba import njit
from pathlib import Path
import glob

# ==== EXECUTION PARAMETERS ====
EXECUTION_TIME_MS = 8        # Время исполнения ордера на Gate
SAFETY_BUFFER_MS = 10        # Буфер безопасности
MIN_TRADEABLE_WINDOW_MS = EXECUTION_TIME_MS + SAFETY_BUFFER_MS  # 18ms минимум

# Торговые ограничения
MAX_LAG_MS = 500             # Максимум ждем 500мс (больше = конкуренция высокая)
MIN_OPPORTUNITIES_PER_HOUR = 1.0  # Минимум 1 возможность в час
MIN_SAMPLE_SIZE = 10         # Минимум событий для статистики

# Стабильность
MAX_CV = 0.8                 # Coefficient of Variation <0.8 (иначе слишком нестабильно)

# ==== DATA ====
DATA_ROOT = Path("data/market_data")
EXCHANGES = ["Binance", "GateIo"]
SYMBOLS = None

# ============================================================
# ADAPTIVE CALIBRATION
# ============================================================

def calibrate_thresholds(df: pl.DataFrame) -> dict:
    """
    Автоматическая калибровка порогов на основе:
    - Volatility (ATR-like)
    - Spread estimation
    - Trade size distribution

    Возвращает: {
        'min_impulse_pct': минимальное движение для детекции импульса,
        'min_profit_pct': минимальная прибыль после спреда,
        'spread_estimate_pct': оценка спреда
    }
    """
    df_with_returns = df.with_columns([
        (pl.col("Price").cast(pl.Float64) * pl.col("Quantity").cast(pl.Float64)).alias("volume_usd")
    ]).sort("Timestamp")

    # Считаем price changes между сделками
    prices = df_with_returns["Price"].cast(pl.Float64).to_numpy()

    if len(prices) < 100:
        return None

    price_changes = np.abs(np.diff(prices) / prices[:-1])
    price_changes = price_changes[price_changes > 0]  # Убираем нули

    if len(price_changes) == 0:
        return None

    # Volatility: 80th percentile price change
    volatility = np.percentile(price_changes, 80)

    # Spread estimate: median of small price changes (likely spread bounces)
    small_changes = price_changes[price_changes < np.percentile(price_changes, 30)]
    spread_estimate = np.median(small_changes) if len(small_changes) > 0 else volatility * 0.5

    # Min impulse: должно быть больше 2x spread (чтобы не путать с шумом)
    min_impulse = max(spread_estimate * 2, volatility * 0.5)

    # Min profit: spread + 0.1% (комиссии и запас)
    min_profit = spread_estimate + 0.001

    return {
        'min_impulse_pct': min_impulse,
        'min_profit_pct': min_profit,
        'spread_estimate_pct': spread_estimate,
        'volatility_pct': volatility
    }

# ============================================================
# LEADER/FOLLOWER DETECTION
# ============================================================

@njit(cache=True, fastmath=True)
def determine_leader_robust(
    ts1: np.ndarray,
    prices1: np.ndarray,
    ts2: np.ndarray,
    prices2: np.ndarray,
    threshold: float
) -> tuple:
    """
    Определяет лидера более надежно.
    Считает на всех данных (не только первые 1000).
    """
    count1 = 0
    count2 = 0

    # Берем sample (если очень много данных)
    sample_size = min(len(ts1), 5000)
    step = max(1, len(ts1) // sample_size)

    for i in range(step, len(ts1), step):
        price_change = abs((prices1[i] - prices1[i-step]) / prices1[i-step])

        if price_change < threshold:
            continue

        t1 = ts1[i]
        idx2 = np.searchsorted(ts2, t1)

        if idx2 < len(ts2) and idx2 > 0:
            # Проверяем была ли похожая цена на бирже 2 раньше
            price_diff = abs(prices2[idx2-1] - prices1[i]) / prices1[i]

            if price_diff < threshold:  # Уже была похожая цена
                # Проверяем кто первым достиг этой цены
                if ts2[idx2-1] < t1:
                    count2 += 1
                else:
                    count1 += 1
            else:
                # Новая цена, биржа 1 первая
                count1 += 1

    return count1, count2

# ============================================================
# IMPULSE DETECTION & LAG MEASUREMENT
# ============================================================

@njit(cache=True, fastmath=True)
def find_impulses_with_lag(
    leader_ts: np.ndarray,
    leader_prices: np.ndarray,
    follower_ts: np.ndarray,
    follower_prices: np.ndarray,
    min_impulse: float,
    min_profit: float,
    max_lag_ms: float,
    min_lag_ms: float
) -> tuple:
    """
    Находит импульсы на лидере и измеряет торговое окно на фолловере.

    Возвращает только торгуемые импульсы (где lag в приемлемых рамках).
    """
    lag_times = []
    impulse_timestamps = []
    impulse_magnitudes = []
    profit_actual = []

    for i in range(1, len(leader_ts)):
        # Детектируем импульс
        price_change = (leader_prices[i] - leader_prices[i-1]) / leader_prices[i-1]

        if abs(price_change) < min_impulse:
            continue

        t_impulse = leader_ts[i]
        direction = 1.0 if price_change > 0 else -1.0

        # Находим цену на фолловере в момент импульса
        idx_follower = np.searchsorted(follower_ts, t_impulse)
        if idx_follower >= len(follower_ts) or idx_follower == 0:
            continue

        entry_price = follower_prices[idx_follower - 1]

        # Ищем торговое окно
        for j in range(idx_follower, len(follower_ts)):
            lag = follower_ts[j] - t_impulse

            if lag > max_lag_ms:
                break

            if lag < min_lag_ms:  # Слишком быстро, не успеем
                continue

            current_price = follower_prices[j]
            price_move = (current_price - entry_price) / entry_price

            # Прибыль с учетом направления
            if direction > 0:
                profit = price_move
            else:
                profit = -price_move

            # Если достигнута целевая прибыль
            if profit >= min_profit:
                lag_times.append(lag)
                impulse_timestamps.append(t_impulse)
                impulse_magnitudes.append(abs(price_change))
                profit_actual.append(profit)
                break

    return (
        np.array(lag_times, dtype=np.float64),
        np.array(impulse_timestamps, dtype=np.int64),
        np.array(impulse_magnitudes, dtype=np.float64),
        np.array(profit_actual, dtype=np.float64)
    )

# ============================================================
# TRADING OPPORTUNITY ANALYSIS
# ============================================================

def analyze_trading_opportunity(
    symbol: str,
    leader: str,
    follower: str,
    lags: np.ndarray,
    timestamps: np.ndarray,
    impulse_mags: np.ndarray,
    profits: np.ndarray,
    calibration: dict
) -> dict:
    """
    Анализирует возможность торговли и возвращает статистику.
    """
    if len(lags) < MIN_SAMPLE_SIZE:
        return None  # Недостаточно данных

    # Статистика лагов
    median_lag = np.median(lags)
    mean_lag = np.mean(lags)
    std_lag = np.std(lags)
    cv = std_lag / mean_lag if mean_lag > 0 else 999

    # Проверка на торгуемость
    if cv > MAX_CV:
        return None  # Слишком нестабильно

    if median_lag < MIN_TRADEABLE_WINDOW_MS:
        return None  # Слишком быстро, не успеем

    # Частота
    time_range_hours = (timestamps[-1] - timestamps[0]) / (1000 * 3600)
    freq_per_hour = len(lags) / time_range_hours if time_range_hours > 0 else 0

    if freq_per_hour < MIN_OPPORTUNITIES_PER_HOUR:
        return None  # Слишком редко

    # Прибыльность
    median_profit = np.median(profits)
    mean_profit = np.mean(profits)

    # Win rate (сколько раз прибыль > spread)
    wins = np.sum(profits > calibration['spread_estimate_pct'])
    win_rate = wins / len(profits)

    return {
        'symbol': symbol,
        'leader': leader,
        'follower': follower,
        'count': len(lags),
        'median_lag_ms': median_lag,
        'mean_lag_ms': mean_lag,
        'std_lag_ms': std_lag,
        'cv': cv,
        'freq_per_hour': freq_per_hour,
        'median_profit_pct': median_profit * 100,
        'mean_profit_pct': mean_profit * 100,
        'win_rate': win_rate,
        'calibration': calibration
    }

# ============================================================
# UTILITIES
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

# ============================================================
# MAIN
# ============================================================

def main():
    output = []

    output.append("=" * 70)
    output.append("TRADING INSTRUMENT: Impulse Detection for Lagging Exchange Trading")
    output.append("=" * 70)
    output.append("")
    output.append(f"Execution parameters:")
    output.append(f"  - Execution time: {EXECUTION_TIME_MS}ms")
    output.append(f"  - Safety buffer: {SAFETY_BUFFER_MS}ms")
    output.append(f"  - Min tradeable window: {MIN_TRADEABLE_WINDOW_MS}ms")
    output.append(f"  - Max lag: {MAX_LAG_MS}ms")
    output.append(f"  - Max CV: {MAX_CV}")
    output.append(f"  - Min freq: {MIN_OPPORTUNITIES_PER_HOUR}/hour")
    output.append("")

    symbols_map = common_symbols(DATA_ROOT, *EXCHANGES)
    if SYMBOLS:
        symbols_map = {s: symbols_map[s] for s in SYMBOLS if s in symbols_map}

    if not symbols_map:
        output.append("[!] No common symbols")
        print("\n".join(output))
        return

    output.append(f"[*] Analyzing {len(symbols_map)} symbols...")
    output.append("")

    tradeable_opportunities = []

    for norm_sym, originals in symbols_map.items():
        # Load data
        df1 = load_trades(EXCHANGES[0], originals[EXCHANGES[0]])
        df2 = load_trades(EXCHANGES[1], originals[EXCHANGES[1]])

        if df1.is_empty() or df2.is_empty():
            continue

        # 1. Calibrate thresholds
        cal1 = calibrate_thresholds(df1)
        cal2 = calibrate_thresholds(df2)

        if cal1 is None or cal2 is None:
            continue

        # Use average calibration
        calibration = {
            'min_impulse_pct': (cal1['min_impulse_pct'] + cal2['min_impulse_pct']) / 2,
            'min_profit_pct': max(cal1['min_profit_pct'], cal2['min_profit_pct']),
            'spread_estimate_pct': (cal1['spread_estimate_pct'] + cal2['spread_estimate_pct']) / 2,
            'volatility_pct': (cal1['volatility_pct'] + cal2['volatility_pct']) / 2
        }

        # Prepare data
        df1_sorted = df1.sort("Timestamp")
        df2_sorted = df2.sort("Timestamp")

        ts1 = df1_sorted["Timestamp"].dt.epoch("ms").to_numpy()
        prices1 = df1_sorted["Price"].cast(pl.Float64).to_numpy()
        ts2 = df2_sorted["Timestamp"].dt.epoch("ms").to_numpy()
        prices2 = df2_sorted["Price"].cast(pl.Float64).to_numpy()

        # 2. Determine leader
        count1, count2 = determine_leader_robust(
            ts1, prices1, ts2, prices2,
            calibration['min_impulse_pct']
        )

        if count1 == 0 and count2 == 0:
            continue  # No significant moves

        if count1 > count2:
            leader_ts, leader_prices = ts1, prices1
            follower_ts, follower_prices = ts2, prices2
            leader, follower = EXCHANGES[0], EXCHANGES[1]
        else:
            leader_ts, leader_prices = ts2, prices2
            follower_ts, follower_prices = ts1, prices1
            leader, follower = EXCHANGES[1], EXCHANGES[0]

        # 3. Find impulses and measure lag
        (lags, timestamps, impulse_mags, profits) = find_impulses_with_lag(
            leader_ts, leader_prices,
            follower_ts, follower_prices,
            calibration['min_impulse_pct'],
            calibration['min_profit_pct'],
            MAX_LAG_MS,
            MIN_TRADEABLE_WINDOW_MS
        )

        if len(lags) == 0:
            continue

        # 4. Analyze trading opportunity
        opportunity = analyze_trading_opportunity(
            norm_sym, leader, follower,
            lags, timestamps, impulse_mags, profits,
            calibration
        )

        if opportunity is not None:
            tradeable_opportunities.append(opportunity)

    # Results
    if not tradeable_opportunities:
        output.append("")
        output.append("[!] NO TRADEABLE OPPORTUNITIES FOUND")
        output.append("")
        output.append("This means:")
        output.append("  - No symbols with stable lag patterns")
        output.append("  - Or lags are too fast (<18ms) or too slow (>500ms)")
        output.append("  - Or frequency too low (<1/hour)")
        output.append("")
        output.append("Try adjusting:")
        output.append("  - MAX_LAG_MS (increase if lags are slow)")
        output.append("  - MIN_OPPORTUNITIES_PER_HOUR (decrease if market is slow)")
        output.append("  - MAX_CV (increase if willing to accept more instability)")
    else:
        output.append("=" * 70)
        output.append(f"FOUND {len(tradeable_opportunities)} TRADEABLE SYMBOLS")
        output.append("=" * 70)
        output.append("")

        # Sort by quality score
        for opp in tradeable_opportunities:
            # Quality score: lower CV, higher frequency, higher profit
            opp['quality_score'] = (
                (1 / (opp['cv'] + 0.1)) *
                opp['freq_per_hour'] *
                opp['mean_profit_pct']
            )

        tradeable_opportunities.sort(key=lambda x: x['quality_score'], reverse=True)

        # Print each opportunity
        for i, opp in enumerate(tradeable_opportunities, 1):
            output.append(f"[{i}] {opp['symbol']}")
            output.append(f"    Leader: {opp['leader']} -> Follower: {opp['follower']}")
            output.append(f"    ")
            output.append(f"    LAG STATS:")
            output.append(f"      Median lag: {opp['median_lag_ms']:.1f}ms (execution={EXECUTION_TIME_MS}ms)")
            output.append(f"      Mean lag: {opp['mean_lag_ms']:.1f}ms +/- {opp['std_lag_ms']:.1f}ms")
            output.append(f"      Stability (CV): {opp['cv']:.2f} {'[STABLE]' if opp['cv'] < 0.4 else '[MODERATE]' if opp['cv'] < 0.6 else '[UNSTABLE]'}")
            output.append(f"    ")
            output.append(f"    PROFIT:")
            output.append(f"      Median: {opp['median_profit_pct']:.3f}%")
            output.append(f"      Mean: {opp['mean_profit_pct']:.3f}%")
            output.append(f"      Win rate: {opp['win_rate']*100:.1f}%")
            output.append(f"    ")
            output.append(f"    FREQUENCY:")
            output.append(f"      {opp['freq_per_hour']:.2f} opportunities/hour")
            output.append(f"      ~{opp['freq_per_hour']*24:.0f} opportunities/day")
            output.append(f"    ")
            output.append(f"    CALIBRATION:")
            output.append(f"      Min impulse: {opp['calibration']['min_impulse_pct']*100:.3f}%")
            output.append(f"      Min profit: {opp['calibration']['min_profit_pct']*100:.3f}%")
            output.append(f"      Spread estimate: {opp['calibration']['spread_estimate_pct']*100:.3f}%")
            output.append(f"    ")
            output.append(f"    TRADEABLE: YES (with {EXECUTION_TIME_MS}ms execution)")
            output.append(f"    Quality score: {opp['quality_score']:.2f}")
            output.append("")

        # Summary
        output.append("=" * 70)
        output.append("TRADING RECOMMENDATIONS")
        output.append("=" * 70)
        output.append("")

        best = tradeable_opportunities[0]
        output.append(f"BEST SYMBOL: {best['symbol']}")
        output.append(f"  - Monitor {best['leader']} for impulses >{best['calibration']['min_impulse_pct']*100:.3f}%")
        output.append(f"  - Trade on {best['follower']} when impulse detected")
        output.append(f"  - Expected lag window: {best['median_lag_ms']:.0f}ms (you have {best['median_lag_ms'] - EXECUTION_TIME_MS:.0f}ms safety margin)")
        output.append(f"  - Expected profit: {best['median_profit_pct']:.3f}% per trade")
        output.append(f"  - Expected frequency: {best['freq_per_hour']:.1f} trades/hour")
        output.append("")

        # Save to file
        df_opportunities = pl.DataFrame(tradeable_opportunities)
        output_file = Path("analysis/tradeable_impulses.parquet")
        output_file.parent.mkdir(exist_ok=True)
        df_opportunities.write_parquet(output_file)
        output.append(f"[SAVED] {output_file}")

    # Write to file
    with open("analysis/TRADEABLE_IMPULSES.txt", "w", encoding="utf-8") as f:
        f.write("\n".join(output))

    print("\n".join(output))

if __name__ == "__main__":
    main()
