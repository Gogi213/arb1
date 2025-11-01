"""
OFI (Order Flow Imbalance) подход для латентного арбитража

Ключевые отличия от кластерного подхода:
1. Определяет НАПРАВЛЕНИЕ движения (BUY/SELL), не просто объем
2. Multi-timeframe верификация (опционально)
3. Адаптивные пороги (никаких хардкодов)

Как работает OFI:
- Классифицируем каждую сделку как агрессивную покупку или продажу
- Накапливаем дисбаланс в окне времени
- Сильный дисбаланс на лидере → ожидаем движение на фолловере
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

# OFI параметры (адаптивные, калибруются автоматически)
OFI_WINDOW_MS = 100           # Окно накопления OFI
IMBALANCE_PERCENTILE = 0.85   # Берем топ-15% дисбалансов
MIN_PRICE_MOVE = 0.0005       # 0.05% минимальное движение

# Multi-timeframe verification (для верификации концепции)
MULTI_TIMEFRAME_ENABLED = True
TIMEFRAMES_MS = [50, 100, 200]  # Проверяем на 3 таймфреймах
MIN_CONFIRMATIONS = 2           # Минимум 2 из 3 подтверждений

# Торговля
TARGET_PROFIT = 0.001    # 0.1% целевая прибыль
LOOK_AHEAD_MS = 500      # Окно для проверки прибыли

# ============================================================
# ОПРЕДЕЛЕНИЕ AGGRESSOR SIDE (эвристический метод)
# ============================================================

@njit(cache=True, fastmath=True)
def classify_aggressor_side(
    prices: np.ndarray,
    volumes: np.ndarray
) -> np.ndarray:
    """
    Определяет сторону агрессора эвристически.

    Логика:
    - Если цена выросла → BUY aggressor → +volume
    - Если цена упала → SELL aggressor → -volume
    - Если цена не изменилась → используем предыдущее направление

    Возвращает signed_volumes (положительные = покупки, отрицательные = продажи)
    """
    signed_volumes = np.zeros(len(prices), dtype=np.float64)

    # Первая сделка - нейтральная
    signed_volumes[0] = 0.0

    for i in range(1, len(prices)):
        if prices[i] > prices[i-1]:
            # Цена выросла = BUY aggressor
            signed_volumes[i] = volumes[i]
        elif prices[i] < prices[i-1]:
            # Цена упала = SELL aggressor
            signed_volumes[i] = -volumes[i]
        else:
            # Цена не изменилась - сохраняем направление
            signed_volumes[i] = signed_volumes[i-1] if i > 0 else 0.0

    return signed_volumes

# ============================================================
# OFI РАСЧЕТ
# ============================================================

@njit(cache=True, fastmath=True)
def calculate_ofi(
    timestamps_ms: np.ndarray,
    signed_volumes: np.ndarray,
    window_ms: int
) -> tuple:
    """
    Вычисляет Order Flow Imbalance в скользящих окнах.

    Возвращает:
    - window_starts_ms: начало каждого окна
    - ofi_values: накопленный дисбаланс в окне
    - total_volume: суммарный объем в окне (для нормализации)
    """
    if len(timestamps_ms) == 0:
        return np.array([], dtype=np.int64), np.array([], dtype=np.float64), np.array([], dtype=np.float64)

    # Группируем по временным окнам
    window_starts = timestamps_ms // window_ms * window_ms
    unique_windows = np.unique(window_starts)

    ofi_values = np.zeros(len(unique_windows), dtype=np.float64)
    total_volumes = np.zeros(len(unique_windows), dtype=np.float64)

    for i, window in enumerate(unique_windows):
        # Находим все сделки в этом окне
        mask = window_starts == window

        # OFI = сумма signed volumes
        ofi_values[i] = np.sum(signed_volumes[mask])

        # Total volume = сумма absolute volumes
        total_volumes[i] = np.sum(np.abs(signed_volumes[mask]))

    return unique_windows, ofi_values, total_volumes

# ============================================================
# MULTI-TIMEFRAME CONFIRMATION
# ============================================================

def calculate_ofi_multitimeframe(
    df: pl.DataFrame,
    timeframes_ms: list[int]
) -> dict[int, pl.DataFrame]:
    """
    Вычисляет OFI на нескольких таймфреймах.

    Возвращает dict: {timeframe_ms: DataFrame с OFI}
    """
    results = {}

    # Подготовка данных
    df_sorted = df.sort("Timestamp").with_columns([
        pl.col("Timestamp").dt.epoch("ms").alias("ts_ms"),
        (pl.col("Price").cast(pl.Float64) * pl.col("Quantity").cast(pl.Float64)).alias("volume_usd")
    ])

    ts = df_sorted["ts_ms"].to_numpy()
    prices = df_sorted["Price"].cast(pl.Float64).to_numpy()
    volumes = df_sorted["volume_usd"].to_numpy()

    # Классифицируем aggressor side
    signed_volumes = classify_aggressor_side(prices, volumes)

    # Вычисляем OFI для каждого таймфрейма
    for window_ms in timeframes_ms:
        window_starts, ofi_vals, total_vols = calculate_ofi(ts, signed_volumes, window_ms)

        if len(window_starts) == 0:
            results[window_ms] = pl.DataFrame()
            continue

        # Нормализуем OFI по объему (OFI / total_volume)
        normalized_ofi = np.divide(
            ofi_vals,
            total_vols,
            out=np.zeros_like(ofi_vals),
            where=total_vols != 0
        )

        results[window_ms] = pl.DataFrame({
            "window_ms": window_starts,
            "ofi": ofi_vals,
            "total_volume": total_vols,
            "ofi_normalized": normalized_ofi,
            "timeframe": window_ms
        })

    return results

def find_confirmed_signals(
    ofi_multi: dict[int, pl.DataFrame],
    min_confirmations: int,
    imbalance_threshold: float
) -> pl.DataFrame:
    """
    Находит сигналы подтвержденные на нескольких таймфреймах.

    Сигнал считается подтвержденным если |normalized_ofi| > threshold
    на min_confirmations таймфреймах.
    """
    if not ofi_multi or all(df.is_empty() for df in ofi_multi.values()):
        return pl.DataFrame()

    # Собираем все сигналы
    all_signals = []

    for timeframe_ms, df in ofi_multi.items():
        if df.is_empty():
            continue

        # Фильтруем значимые дисбалансы
        strong_signals = df.filter(
            pl.col("ofi_normalized").abs() >= imbalance_threshold
        )

        if not strong_signals.is_empty():
            all_signals.append(strong_signals.with_columns([
                pl.lit(timeframe_ms).alias("timeframe")
            ]))

    if not all_signals:
        return pl.DataFrame()

    df_all = pl.concat(all_signals, rechunk=False)

    # Группируем близкие по времени сигналы (в пределах 200мс)
    # Упрощенный подход: считаем подтверждения по округленному времени
    df_confirmed = (
        df_all
        .with_columns([
            (pl.col("window_ms") // 200 * 200).alias("time_bucket")
        ])
        .group_by("time_bucket")
        .agg([
            pl.col("window_ms").first().alias("timestamp_ms"),
            pl.col("ofi_normalized").mean().alias("avg_ofi"),
            pl.col("total_volume").sum().alias("total_volume"),
            pl.count().alias("confirmations"),
            (pl.col("ofi_normalized").mean() > 0).sum().alias("buy_votes"),
            (pl.col("ofi_normalized").mean() < 0).sum().alias("sell_votes")
        ])
        .filter(pl.col("confirmations") >= min_confirmations)
        .with_columns([
            pl.when(pl.col("avg_ofi") > 0)
              .then(pl.lit("BUY"))
              .otherwise(pl.lit("SELL"))
              .alias("direction")
        ])
        .sort("timestamp_ms")
    )

    return df_confirmed

# ============================================================
# ADAPTIVE CALIBRATION
# ============================================================

def calibrate_ofi_threshold(df: pl.DataFrame, percentile: float) -> float:
    """
    Адаптивная калибровка порога OFI на основе процентиля.

    Вместо хардкода "$5000" используем статистику данных.
    """
    df_with_volume = df.with_columns([
        (pl.col("Price").cast(pl.Float64) * pl.col("Quantity").cast(pl.Float64)).alias("volume_usd")
    ])

    # Вычисляем OFI для калибровки
    df_sorted = df_with_volume.sort("Timestamp").with_columns([
        pl.col("Timestamp").dt.epoch("ms").alias("ts_ms")
    ])

    ts = df_sorted["ts_ms"].to_numpy()
    prices = df_sorted["Price"].cast(pl.Float64).to_numpy()
    volumes = df_sorted["volume_usd"].to_numpy()

    signed_volumes = classify_aggressor_side(prices, volumes)
    window_starts, ofi_vals, total_vols = calculate_ofi(ts, signed_volumes, OFI_WINDOW_MS)

    if len(ofi_vals) == 0:
        return 0.0

    # Нормализуем
    normalized_ofi = np.divide(
        np.abs(ofi_vals),
        total_vols,
        out=np.zeros_like(ofi_vals, dtype=np.float64),
        where=total_vols != 0
    )

    # Берем percentile
    threshold = np.percentile(normalized_ofi[normalized_ofi > 0], percentile * 100)

    return threshold

# ============================================================
# ПОИСК ВОЗМОЖНОСТЕЙ
# ============================================================

@njit(cache=True, fastmath=True)
def find_ofi_opportunities(
    signal_ts: np.ndarray,      # Timestamps сигналов OFI
    signal_directions: np.ndarray,  # +1 = BUY, -1 = SELL
    follower_ts: np.ndarray,    # Timestamps сделок на фолловере
    follower_prices: np.ndarray,  # Цены на фолловере
    look_ahead_ms: float,
    target_profit: float
) -> tuple:
    """
    Находит торговые возможности на основе OFI сигналов.
    """
    opp_ts = []
    opp_direction = []
    opp_entry_price = []
    opp_exit_price = []
    opp_profit = []

    for i in range(len(signal_ts)):
        t_signal = signal_ts[i]
        direction = signal_directions[i]  # +1 or -1

        # Находим цену на фолловере в момент сигнала
        idx_entry = np.searchsorted(follower_ts, t_signal)
        if idx_entry >= len(follower_ts):
            continue

        entry_price = follower_prices[idx_entry]

        # Находим цену через look_ahead_ms
        t_exit = t_signal + look_ahead_ms
        idx_exit = np.searchsorted(follower_ts, t_exit)
        if idx_exit >= len(follower_ts):
            continue

        exit_price = follower_prices[idx_exit]

        # Вычисляем прибыль в зависимости от направления
        if direction > 0:  # BUY signal
            profit = (exit_price - entry_price) / entry_price
        else:  # SELL signal
            profit = (entry_price - exit_price) / entry_price

        if profit >= target_profit:
            opp_ts.append(t_signal)
            opp_direction.append(direction)
            opp_entry_price.append(entry_price)
            opp_exit_price.append(exit_price)
            opp_profit.append(profit)

    return (
        np.array(opp_ts, dtype=np.int64),
        np.array(opp_direction, dtype=np.float64),
        np.array(opp_entry_price, dtype=np.float64),
        np.array(opp_exit_price, dtype=np.float64),
        np.array(opp_profit, dtype=np.float64)
    )

# ============================================================
# ОПРЕДЕЛЕНИЕ ЛИДЕРА
# ============================================================

@njit(cache=True, fastmath=True)
def count_who_leads(
    ts1: np.ndarray,
    prices1: np.ndarray,
    ts2: np.ndarray,
    prices2: np.ndarray,
    threshold: float
) -> tuple:
    """Считает кто чаще первым двигает цену"""
    count_1_leads = 0
    count_2_leads = 0

    for i in range(1, min(len(ts1), 1000)):  # Проверяем первую 1000 сделок
        if abs((prices1[i] - prices1[i-1]) / prices1[i-1]) < threshold:
            continue

        t1 = ts1[i]
        idx2 = np.searchsorted(ts2, t1)

        if idx2 < len(ts2):
            t2 = ts2[idx2]
            if t1 < t2:
                count_1_leads += 1
            else:
                count_2_leads += 1

    return count_1_leads, count_2_leads

def who_is_faster(df: pl.DataFrame, ex1: str, ex2: str) -> tuple[str, str]:
    """Определяет лидера простым методом"""
    df1 = df.filter(pl.col("Exchange") == ex1).sort("Timestamp")
    df2 = df.filter(pl.col("Exchange") == ex2).sort("Timestamp")

    if df1.is_empty() or df2.is_empty():
        raise ValueError("Нет данных для одной из бирж")

    ts1 = df1["Timestamp"].dt.epoch("ms").to_numpy()
    ts2 = df2["Timestamp"].dt.epoch("ms").to_numpy()
    prices1 = df1["Price"].cast(pl.Float64).to_numpy()
    prices2 = df2["Price"].cast(pl.Float64).to_numpy()

    count1, count2 = count_who_leads(ts1, prices1, ts2, prices2, MIN_PRICE_MOVE)

    if count1 > count2:
        return ex1, ex2
    else:
        return ex2, ex1

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

# ============================================================
# ГЛАВНАЯ ФУНКЦИЯ
# ============================================================

def main():
    print("=== OFI (Order Flow Imbalance) Analyzer ===\n")
    print(f"Параметры:")
    print(f"  - OFI окно: {OFI_WINDOW_MS}мс")
    print(f"  - Порог дисбаланса: {IMBALANCE_PERCENTILE*100:.0f}% percentile (ADAPTIVE)")
    print(f"  - Multi-timeframe: {'ENABLED' if MULTI_TIMEFRAME_ENABLED else 'DISABLED'}")
    if MULTI_TIMEFRAME_ENABLED:
        print(f"    - Таймфреймы: {TIMEFRAMES_MS}")
        print(f"    - Мин. подтверждений: {MIN_CONFIRMATIONS}")
    print(f"  - Целевая прибыль: {TARGET_PROFIT*100:.2f}%")
    print()

    symbols_map = common_symbols(DATA_ROOT, *EXCHANGES)
    if SYMBOLS:
        symbols_map = {s: symbols_map[s] for s in SYMBOLS if s in symbols_map}

    if not symbols_map:
        print("[!] Нет общих символов")
        return

    print(f"[*] Найдено {len(symbols_map)} общих символов\n")

    all_opportunities = []

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

        df_all = pl.concat([df1, df2], rechunk=False)

        # 1. Определяем лидера
        print(f"\n[1] Определение лидера...")
        try:
            leader, follower = who_is_faster(df_all, EXCHANGES[0], EXCHANGES[1])
            print(f"    LEADER: {leader}")
            print(f"    FOLLOWER: {follower}")
        except ValueError as e:
            print(f"    [!] {e}")
            continue

        # 2. Калибруем адаптивный порог
        print(f"\n[2] Адаптивная калибровка порога OFI...")
        df_leader = df_all.filter(pl.col("Exchange") == leader)
        threshold = calibrate_ofi_threshold(df_leader, IMBALANCE_PERCENTILE)
        print(f"    Порог normalized OFI: {threshold:.4f}")

        if threshold == 0.0:
            print("    [!] Не удалось калибровать")
            continue

        # 3. Вычисляем OFI
        if MULTI_TIMEFRAME_ENABLED:
            print(f"\n[3] Расчет OFI (multi-timeframe)...")
            ofi_multi = calculate_ofi_multitimeframe(df_leader, TIMEFRAMES_MS)

            for tf, df_ofi in ofi_multi.items():
                if not df_ofi.is_empty():
                    print(f"    {tf}ms: {len(df_ofi)} окон, max |OFI|: {df_ofi['ofi_normalized'].abs().max():.4f}")

            # Находим подтвержденные сигналы
            print(f"\n[4] Поиск подтвержденных сигналов...")
            signals = find_confirmed_signals(ofi_multi, MIN_CONFIRMATIONS, threshold)

            if signals.is_empty():
                print("    [!] Нет подтвержденных сигналов")
                continue

            print(f"    Найдено: {len(signals)} сигналов")
            print(f"    BUY: {signals.filter(pl.col('direction') == 'BUY').height}")
            print(f"    SELL: {signals.filter(pl.col('direction') == 'SELL').height}")
        else:
            print(f"\n[3] Расчет OFI (single timeframe)...")
            ofi_result = calculate_ofi_multitimeframe(df_leader, [OFI_WINDOW_MS])
            df_ofi = ofi_result[OFI_WINDOW_MS]

            if df_ofi.is_empty():
                print("    [!] Нет данных OFI")
                continue

            signals = df_ofi.filter(
                pl.col("ofi_normalized").abs() >= threshold
            ).with_columns([
                pl.when(pl.col("ofi_normalized") > 0)
                  .then(pl.lit("BUY"))
                  .otherwise(pl.lit("SELL"))
                  .alias("direction"),
                pl.col("window_ms").alias("timestamp_ms")
            ])

            print(f"    Найдено: {len(signals)} сигналов")

        # 5. Поиск торговых возможностей
        print(f"\n[5] Поиск торговых возможностей на {follower}...")

        df_follower = df_all.filter(pl.col("Exchange") == follower).sort("Timestamp")
        follower_ts = df_follower["Timestamp"].dt.epoch("ms").to_numpy()
        follower_prices = df_follower["Price"].cast(pl.Float64).to_numpy()

        signal_ts = signals["timestamp_ms"].to_numpy()
        signal_dir = np.where(
            signals["direction"].to_numpy() == "BUY",
            1.0,
            -1.0
        )

        (opp_ts, opp_dir, opp_entry, opp_exit, opp_profit) = find_ofi_opportunities(
            signal_ts, signal_dir,
            follower_ts, follower_prices,
            LOOK_AHEAD_MS, TARGET_PROFIT
        )

        if len(opp_ts) == 0:
            print("    [!] Возможностей не найдено")
            continue

        print(f"    [+] Найдено: {len(opp_ts)} возможностей!")

        # Формируем результат
        df_opportunities = pl.DataFrame({
            "symbol": norm_sym,
            "timestamp": opp_ts,
            "leader": leader,
            "follower": follower,
            "direction": np.where(opp_dir > 0, "BUY", "SELL"),
            "entry_price": opp_entry,
            "exit_price": opp_exit,
            "profit_pct": opp_profit * 100
        })

        print(f"\n    Топ-5 возможностей:")
        print(df_opportunities.sort("profit_pct", descending=True).head(5).select([
            "direction", "profit_pct", "entry_price", "exit_price"
        ]))

        all_opportunities.append(df_opportunities)

    # Итоговый отчет
    if all_opportunities:
        print(f"\n{'='*60}")
        print("=== РЕЗУЛЬТАТЫ ===")
        print(f"{'='*60}\n")

        df_all = pl.concat(all_opportunities, rechunk=False)

        print(f"[+] Всего возможностей: {len(df_all):,}")
        print(f"\n[*] По направлениям:")
        direction_stats = df_all.group_by("direction").agg([
            pl.count().alias("count"),
            pl.col("profit_pct").mean().alias("avg_profit_pct"),
            pl.col("profit_pct").max().alias("max_profit_pct")
        ]).sort("count", descending=True)
        print(direction_stats)

        print(f"\n[*] По символам (топ-10):")
        symbol_stats = df_all.group_by("symbol").agg([
            pl.count().alias("count"),
            pl.col("profit_pct").mean().alias("avg_profit_pct")
        ]).sort("count", descending=True).head(10)
        print(symbol_stats)

        print(f"\n[TOP-20] лучших возможностей:")
        top = df_all.sort("profit_pct", descending=True).head(20)
        print(top.select([
            "symbol", "direction", "leader", "profit_pct"
        ]))

        # Сохраняем
        output_file = Path("analysis/opportunities_ofi.parquet")
        output_file.parent.mkdir(exist_ok=True)
        df_all.write_parquet(output_file)
        print(f"\n[SAVED] {output_file}")

        # Рекомендации
        print(f"\n{'='*60}")
        print("=== КАК ИСПОЛЬЗОВАТЬ ===")
        print(f"{'='*60}")

        main_leader = df_all.group_by("leader").agg(
            pl.count().alias("count")
        ).sort("count", descending=True)["leader"][0]

        buy_pct = len(df_all.filter(pl.col("direction") == "BUY")) / len(df_all) * 100

        print(f"""
1. Подключись к WebSocket {main_leader} (лидер)
2. Отслеживай OFI (Order Flow Imbalance):
   - Накапливай signed volume в окнах {OFI_WINDOW_MS}мс
   - Сильный дисбаланс = потенциальное движение цены
3. Когда |normalized_OFI| > {threshold:.4f}:
   - BUY signal ({buy_pct:.0f}% сигналов) = ожидай рост цены
   - SELL signal ({100-buy_pct:.0f}% сигналов) = ожидай падение цены
4. Торгуй на второй бирже В НАПРАВЛЕНИИ сигнала
5. Средняя прибыль: {df_all['profit_pct'].mean():.3f}%

ПРЕИМУЩЕСТВО OFI: знаешь не только ЧТО будет движение, но и КУДА!
        """)

        # Верификация multi-timeframe
        if MULTI_TIMEFRAME_ENABLED:
            print(f"\n{'='*60}")
            print("=== ВЕРИФИКАЦИЯ MULTI-TIMEFRAME ===")
            print(f"{'='*60}")
            print(f"""
Концепция: сигнал сильнее если подтвержден на нескольких таймфреймах.

Результат:
- Всего возможностей с подтверждением: {len(df_all):,}
- Средняя прибыль: {df_all['profit_pct'].mean():.3f}%

ВЫВОД: {'Работает!' if len(df_all) > 0 else 'Не работает'}
            """)

    else:
        print("\n[!] Возможностей не найдено")

if __name__ == "__main__":
    main()
