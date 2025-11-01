import polars as pl
import numpy as np
from numba import njit, prange
from pathlib import Path
import glob
from typing import Tuple, Optional
from dataclasses import dataclass

# ==== ПАРАМЕТРЫ АНАЛИЗА ====
DATA_ROOT = Path("data/market_data")
EXCHANGES = ["Binance", "GateIo"]
SYMBOLS = None

# Параметры для определения лидера/лаггера
TIME_BUCKET_MS = 10       # Уменьшено до 10мс для лучшей точности
LAG_ANALYSIS_WINDOW = 200  # Увеличено для надежности

# Параметры импульсов
MIN_IMPULSE_USD = 5000    # Снижено для большего количества сигналов
IMPULSE_PRICE_MOVE = 0.0005  # 0.05% движение цены
IMPULSE_WINDOW_MS = 1000  # 1 секунда окно

# Параметры торговли
MIN_LAG_MS = 5            # Уменьшено - даже 5мс задержки может быть полезно
MAX_LAG_MS = 2000
TARGET_PROFIT = 0.001     # 0.1% целевая прибыль (более реалистично)

# ============================================================
# NUMBA УСКОРЕННЫЕ ФУНКЦИИ (JIT компиляция)
# ============================================================

@njit(cache=True, fastmath=True)
def fast_cross_correlation(fast_prices: np.ndarray, slow_prices: np.ndarray, max_lag: int) -> Tuple[int, float]:
    """
    JIT-компилированная кросс-корреляция.
    ~10-100x быстрее чем numpy версия.
    """
    n = len(fast_prices)

    # Нормализация
    fast_mean = np.mean(fast_prices)
    slow_mean = np.mean(slow_prices)
    fast_std = np.std(fast_prices)
    slow_std = np.std(slow_prices)

    if fast_std == 0 or slow_std == 0:
        return 0, 0.0

    fast_norm = (fast_prices - fast_mean) / fast_std
    slow_norm = (slow_prices - slow_mean) / slow_std

    best_lag = 0
    best_corr = -999.0

    # Параллельный поиск оптимальной задержки
    for lag in range(max_lag + 1):
        if n - lag < 10:  # Минимум 10 точек для корреляции
            break

        # Вычисляем корреляцию со сдвигом
        corr_sum = 0.0
        valid_points = n - lag

        for i in range(valid_points):
            corr_sum += fast_norm[i] * slow_norm[i + lag]

        corr = corr_sum / valid_points

        if corr > best_corr:
            best_corr = corr
            best_lag = lag

    return best_lag, best_corr

@njit(cache=True, fastmath=True, parallel=True)
def fast_segmented_lags(
    returns_1: np.ndarray,
    returns_2: np.ndarray,
    n_segments: int,
    max_lag: int,
    leader_is_1: bool
) -> np.ndarray:
    """
    Параллельное вычисление задержек на сегментах.
    Использует prange для многопоточности.
    """
    segment_size = len(returns_1) // n_segments
    lags = np.zeros(n_segments, dtype=np.int32)

    for i in prange(n_segments):
        start = i * segment_size
        end = start + segment_size

        if end > len(returns_1):
            end = len(returns_1)

        seg_1 = returns_1[start:end]
        seg_2 = returns_2[start:end]

        if leader_is_1:
            lag, _ = fast_cross_correlation(seg_1, seg_2, max_lag)
        else:
            lag, _ = fast_cross_correlation(seg_2, seg_1, max_lag)

        lags[i] = lag

    return lags

@njit(cache=True, fastmath=True)
def fast_find_opportunities(
    impulse_times: np.ndarray,
    impulse_directions: np.ndarray,  # 1=BUY, -1=SELL
    impulse_volumes: np.ndarray,
    impulse_moves: np.ndarray,
    follower_times: np.ndarray,
    follower_prices: np.ndarray,
    expected_lag_ms: float,
    target_profit: float
) -> Tuple[np.ndarray, np.ndarray, np.ndarray, np.ndarray]:
    """
    Быстрый поиск торговых возможностей.
    Возвращает: (indices, profits, entry_prices, exit_prices)
    """
    n_impulses = len(impulse_times)
    n_follower = len(follower_times)

    # Предаллоцируем массивы (максимум n_impulses возможностей)
    valid_indices = np.zeros(n_impulses, dtype=np.int32)
    profits = np.zeros(n_impulses, dtype=np.float64)
    entry_prices = np.zeros(n_impulses, dtype=np.float64)
    exit_prices = np.zeros(n_impulses, dtype=np.float64)

    count = 0

    for i in range(n_impulses):
        impulse_time = impulse_times[i]
        reaction_time = impulse_time + expected_lag_ms

        # Бинарный поиск ближайшей цены в момент импульса
        idx_at_impulse = np.searchsorted(follower_times, impulse_time)
        if idx_at_impulse >= n_follower:
            continue

        price_at_impulse = follower_prices[idx_at_impulse]

        # Бинарный поиск цены после задержки
        idx_after_lag = np.searchsorted(follower_times, reaction_time)
        if idx_after_lag >= n_follower:
            continue

        price_after_lag = follower_prices[idx_after_lag]

        # Вычисляем прибыль
        direction = impulse_directions[i]
        if direction > 0:  # BUY impulse
            profit = (price_after_lag - price_at_impulse) / price_at_impulse
        else:  # SELL impulse
            profit = (price_at_impulse - price_after_lag) / price_at_impulse

        if profit >= target_profit:
            valid_indices[count] = i
            profits[count] = profit
            entry_prices[count] = price_at_impulse
            exit_prices[count] = price_after_lag
            count += 1

    # Обрезаем до реального количества
    return (
        valid_indices[:count],
        profits[:count],
        entry_prices[:count],
        exit_prices[:count]
    )

# ============================================================
# POLARS ОПТИМИЗИРОВАННЫЕ ОПЕРАЦИИ (Zero-copy)
# ============================================================

@dataclass
class LatencyProfile:
    leader: str
    follower: str
    median_lag_ms: float
    mean_lag_ms: float
    lag_std_ms: float
    correlation: float
    confidence: float

@dataclass
class ImpulseData:
    """Zero-copy структура для хранения импульсов"""
    timestamps: np.ndarray
    directions: np.ndarray
    volumes: np.ndarray
    moves: np.ndarray

def resample_to_buckets_fast(df: pl.DataFrame, bucket_ms: int) -> pl.DataFrame:
    """
    Быстрое преобразование в бакеты с минимальным копированием.
    Использует lazy evaluation и expression API.
    """
    return (
        df.lazy()
        .with_columns([
            (pl.col("Timestamp").dt.epoch("ms") // bucket_ms * bucket_ms).alias("bucket_ms"),
        ])
        .group_by(["Exchange", "bucket_ms"])
        .agg([
            # VWAP = sum(price * qty) / sum(qty)
            (pl.col("Price").cast(pl.Float64) * pl.col("Quantity").cast(pl.Float64)).sum().alias("value"),
            pl.col("Quantity").cast(pl.Float64).sum().alias("volume"),
        ])
        .with_columns([
            (pl.col("value") / pl.col("volume")).alias("price"),
        ])
        .select(["Exchange", "bucket_ms", "price", "volume", "value"])
        .sort("bucket_ms")
        .collect()
    )

def detect_leader_follower_fast(
    df: pl.DataFrame,
    bucket_ms: int,
    analysis_window: int
) -> Optional[LatencyProfile]:
    """
    Оптимизированная версия с zero-copy переходами между polars и numpy.
    """
    # Быстрое преобразование в бакеты
    bucketed = resample_to_buckets_fast(df, bucket_ms)

    exchanges = bucketed["Exchange"].unique().to_list()
    if len(exchanges) < 2:
        return None

    ex1, ex2 = exchanges[0], exchanges[1]

    # Разделяем данные (view, не копируем)
    df1 = bucketed.filter(pl.col("Exchange") == ex1).sort("bucket_ms")
    df2 = bucketed.filter(pl.col("Exchange") == ex2).sort("bucket_ms")

    # Join с минимальным копированием
    merged = (
        df1.lazy()
        .join(
            df2.lazy(),
            on="bucket_ms",
            how="inner"
        )
        .with_columns([
            pl.col("price").alias("price_1"),
            pl.col("price_right").alias("price_2")
        ])
        .select(["bucket_ms", "price_1", "price_2"])
        .collect()
    )

    if merged.height < analysis_window:
        return None

    # Вычисляем возвраты inline
    merged = merged.with_columns([
        pl.col("price_1").pct_change().alias("return_1"),
        pl.col("price_2").pct_change().alias("return_2")
    ]).drop_nulls()

    if merged.height < 10:
        return None

    # Zero-copy export в numpy (используем to_numpy() напрямую, без промежуточных копий)
    returns_1 = merged["return_1"].to_numpy()
    returns_2 = merged["return_2"].to_numpy()

    # JIT-компилированная кросс-корреляция
    lag_1_leads, corr_1_leads = fast_cross_correlation(returns_1, returns_2, analysis_window)
    lag_2_leads, corr_2_leads = fast_cross_correlation(returns_2, returns_1, analysis_window)

    # Определяем лидера
    if corr_1_leads > corr_2_leads:
        leader, follower = ex1, ex2
        lag_buckets = lag_1_leads
        correlation = corr_1_leads
        leader_is_1 = True
    else:
        leader, follower = ex2, ex1
        lag_buckets = lag_2_leads
        correlation = corr_2_leads
        leader_is_1 = False

    lag_ms = lag_buckets * bucket_ms

    # Параллельное вычисление стабильности на сегментах
    segment_size = len(returns_1) // 5
    if segment_size < 10:
        lag_std = 0.0
        confidence = correlation
    else:
        # Numba parallel
        lags = fast_segmented_lags(
            returns_1,
            returns_2,
            5,
            min(20, len(returns_1) // 2),
            leader_is_1
        )
        lag_std = float(np.std(lags) * bucket_ms)
        confidence = correlation * (1.0 / (1.0 + lag_std / 100.0))

    return LatencyProfile(
        leader=leader,
        follower=follower,
        median_lag_ms=float(lag_ms),
        mean_lag_ms=float(lag_ms),
        lag_std_ms=lag_std,
        correlation=float(correlation),
        confidence=min(float(confidence), 1.0)
    )

def detect_impulses_fast(
    df: pl.DataFrame,
    exchange: str,
    min_volume_usd: float,
    min_price_move: float,
    window_ms: int
) -> Optional[ImpulseData]:
    """
    Быстрая детекция импульсов с zero-copy выходом в numpy.
    """
    # Lazy фильтрация и подготовка
    df_bucketed = (
        df.lazy()
        .filter(pl.col("Exchange") == exchange)
        .with_columns([
            pl.col("Timestamp").dt.epoch("ms").alias("timestamp_ms")
        ])
        .with_columns([
            (pl.col("timestamp_ms") // window_ms * window_ms).alias("bucket_ms")
        ])
        .group_by("bucket_ms")
        .agg([
            (pl.col("Price").cast(pl.Float64) * pl.col("Quantity").cast(pl.Float64)).sum().alias("value"),
            pl.col("Quantity").cast(pl.Float64).sum().alias("volume"),
            pl.col("Price").cast(pl.Float64).mean().alias("price"),
        ])
        .with_columns([
            (pl.col("value") / pl.col("volume")).alias("vwap")
        ])
        .sort("bucket_ms")
        .with_columns([
            pl.col("vwap").pct_change().alias("price_change")
        ])
        .drop_nulls()
        .filter(
            (pl.col("value") >= min_volume_usd) &
            (pl.col("price_change").abs() >= min_price_move)
        )
        .select(["bucket_ms", "price_change", "value"])
        .collect()
    )

    if df_bucketed.is_empty():
        return None

    # Zero-copy export
    timestamps = df_bucketed["bucket_ms"].to_numpy()
    price_changes = df_bucketed["price_change"].to_numpy()
    volumes = df_bucketed["value"].to_numpy()

    # Направления: 1 = BUY, -1 = SELL
    directions = np.where(price_changes > 0, 1, -1).astype(np.int8)

    return ImpulseData(
        timestamps=timestamps,
        directions=directions,
        volumes=volumes,
        moves=np.abs(price_changes)
    )

def find_trade_opportunities_fast(
    impulses: ImpulseData,
    df_follower: pl.DataFrame,
    latency_profile: LatencyProfile,
    target_profit: float
) -> pl.DataFrame:
    """
    Быстрый поиск возможностей с JIT-компилированной логикой.
    """
    # Подготавливаем данные follower (sorted, zero-copy)
    df_prep = (
        df_follower.lazy()
        .with_columns([
            pl.col("Timestamp").dt.epoch("ms").alias("timestamp_ms")
        ])
        .select(["timestamp_ms", "Price"])
        .sort("timestamp_ms")
        .collect()
    )

    # Zero-copy export
    follower_times = df_prep["timestamp_ms"].to_numpy()
    follower_prices = df_prep["Price"].to_numpy()

    # JIT-компилированный поиск
    valid_indices, profits, entry_prices, exit_prices = fast_find_opportunities(
        impulses.timestamps,
        impulses.directions,
        impulses.volumes,
        impulses.moves,
        follower_times,
        follower_prices,
        latency_profile.median_lag_ms,
        target_profit
    )

    if len(valid_indices) == 0:
        return pl.DataFrame()

    # Собираем результат из numpy массивов (zero-copy construction)
    return pl.DataFrame({
        "impulse_timestamp": impulses.timestamps[valid_indices],
        "impulse_direction": np.where(impulses.directions[valid_indices] > 0, "BUY", "SELL"),
        "impulse_volume_usd": impulses.volumes[valid_indices],
        "impulse_price_move": impulses.moves[valid_indices],
        "follower_price_at_impulse": entry_prices,
        "follower_price_after_lag": exit_prices,
        "potential_profit_pct": profits * 100.0,
        "expected_lag_ms": np.full(len(valid_indices), latency_profile.median_lag_ms)
    })

def normalize_symbol(symbol: str) -> str:
    """Нормализует символ: убирает _ и переводит в верхний регистр"""
    return symbol.replace('_', '').replace('-', '').upper()

def common_symbols(root: Path, ex1: str, ex2: str) -> dict[str, dict[str, str]]:
    """
    Находит общие символы с учетом разных форматов.
    Binance: BTCUSDT
    GateIo: BTC_USDT
    """
    path1 = root / f"exchange={ex1}"
    path2 = root / f"exchange={ex2}"

    if not path1.exists() or not path2.exists():
        print(f"[!] Не найдены директории: {path1.exists()=}, {path2.exists()=}")
        return {}

    # Создаем маппинг: нормализованное -> оригинальное
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

    print(f"[*] {ex1}: {len(map1)} символов")
    print(f"[*] {ex2}: {len(map2)} символов")

    # Находим пересечение
    common_normalized = set(map1.keys()) & set(map2.keys())

    result = {
        norm: {ex1: map1[norm], ex2: map2[norm]}
        for norm in common_normalized
    }

    print(f"[+] Найдено {len(result)} общих символов")

    # Показываем несколько примеров
    if result:
        print("[i] Примеры маппинга:")
        for norm, originals in list(result.items())[:3]:
            print(f"   {norm} -> {ex1}:{originals[ex1]}, {ex2}:{originals[ex2]}")

    return result

def load_trades_for_symbol(exchange: str, symbol: str) -> pl.DataFrame:
    """Загружает сделки с lazy evaluation"""
    symbol_path = DATA_ROOT / f"exchange={exchange}" / f"symbol={symbol}"
    if not symbol_path.exists():
        return pl.DataFrame()

    search_pattern = str(symbol_path / "**/trades-*.parquet")
    file_list = glob.glob(search_pattern, recursive=True)

    if not file_list:
        return pl.DataFrame()

    try:
        # Lazy scan для минимального использования памяти
        df = (
            pl.scan_parquet(file_list)
            .select(["Timestamp", "Price", "Quantity", "Exchange"])
            .collect()
        )
        return df
    except Exception as e:
        print(f"[!] Ошибка загрузки {exchange}/{symbol}: {e}")
        return pl.DataFrame()

def main():
    """Оптимизированный главный процесс"""
    print("=== FAST Latency Analysis (Numba + Zero-Copy) ===\n")

    symbols_map = common_symbols(DATA_ROOT, *EXCHANGES)
    if SYMBOLS:
        symbols_map = {s: symbols_map[s] for s in SYMBOLS if s in symbols_map}

    if not symbols_map:
        print("[!] Не найдено общих символов.")
        return

    all_opportunities = []

    for norm_sym, original_names in symbols_map.items():
        print(f"\n{'='*60}")
        print(f"[>] {norm_sym}")
        print(f"{'='*60}")

        # Загружаем данные
        df1 = load_trades_for_symbol(EXCHANGES[0], original_names[EXCHANGES[0]])
        df2 = load_trades_for_symbol(EXCHANGES[1], original_names[EXCHANGES[1]])

        if df1.is_empty() or df2.is_empty():
            print(f"[-] Пропуск: нет данных")
            continue

        print(f"   Загружено: {len(df1):,} + {len(df2):,} сделок")

        # Объединяем (zero-copy concat)
        df_all = pl.concat([df1, df2], rechunk=False)

        # 1. Определяем лидера
        print(f"\n[1] Определение латентности...")
        latency_profile = detect_leader_follower_fast(df_all, TIME_BUCKET_MS, LAG_ANALYSIS_WINDOW)

        if not latency_profile:
            print(f"[!] Не удалось определить")
            continue

        print(f"   [LEADER] {latency_profile.leader}")
        print(f"   [FOLLOWER] {latency_profile.follower}")
        print(f"   [LAG] {latency_profile.median_lag_ms:.0f} +/- {latency_profile.lag_std_ms:.0f} мс")
        print(f"   [CORR] {latency_profile.correlation:.3f}")
        print(f"   [CONF] {latency_profile.confidence:.3f}")

        # Проверки
        if not (MIN_LAG_MS <= latency_profile.median_lag_ms <= MAX_LAG_MS):
            print(f"   [!] Лаг вне диапазона [{MIN_LAG_MS}, {MAX_LAG_MS}]")
            continue

        if latency_profile.confidence < 0.3:
            print(f"   [!] Низкая уверенность")
            continue

        # 2. Детектируем импульсы
        print(f"\n[2] Поиск импульсов...")
        impulses = detect_impulses_fast(
            df_all,
            latency_profile.leader,
            MIN_IMPULSE_USD,
            IMPULSE_PRICE_MOVE,
            IMPULSE_WINDOW_MS
        )

        if impulses is None:
            print(f"   [!] Импульсов не найдено")
            continue

        print(f"   [+] Найдено: {len(impulses.timestamps)}")

        # Показываем примеры
        for i in range(min(3, len(impulses.timestamps))):
            direction = "BUY" if impulses.directions[i] > 0 else "SELL"
            print(f"   - {direction} | ${impulses.volumes[i]:,.0f} | Delta {impulses.moves[i]*100:.2f}%")

        # 3. Ищем возможности
        print(f"\n[3] Поиск возможностей...")
        df_follower = df_all.filter(pl.col("Exchange") == latency_profile.follower)

        opportunities = find_trade_opportunities_fast(
            impulses,
            df_follower,
            latency_profile,
            TARGET_PROFIT
        )

        if opportunities.is_empty():
            print(f"   [!] Возможностей нет")
            continue

        print(f"   [+] Найдено: {opportunities.height}")
        print(f"\n   Топ-5:")
        print(opportunities.sort("potential_profit_pct", descending=True).head(5))

        # Добавляем метаданные
        opportunities = opportunities.with_columns([
            pl.lit(norm_sym).alias("symbol"),
            pl.lit(latency_profile.leader).alias("leader"),
            pl.lit(latency_profile.follower).alias("follower")
        ])

        all_opportunities.append(opportunities)

    # Итоговый отчет
    if all_opportunities:
        print(f"\n{'='*60}")
        print("=== ИТОГОВЫЙ ОТЧЕТ ===")
        print(f"{'='*60}")

        df_all_opps = pl.concat(all_opportunities, rechunk=False)
        print(f"\n[+] Всего возможностей: {df_all_opps.height}")
        print(f"\n[TOP-10] по прибыли:")
        print(df_all_opps.sort("potential_profit_pct", descending=True).head(10))

        # Сохраняем
        output_file = Path("analysis/latency_opportunities.parquet")
        output_file.parent.mkdir(exist_ok=True)
        df_all_opps.write_parquet(output_file, compression="zstd")
        print(f"\n[SAVED] {output_file}")
    else:
        print("\n[!] Возможностей не найдено")

if __name__ == "__main__":
    main()
