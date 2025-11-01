"""
Простой и эффективный анализ: находит моменты когда одна биржа
движется быстрее другой.

Работает с любыми данными, без сложной математики.
"""
import polars as pl
import numpy as np
from pathlib import Path
import glob
from numba import njit

# ==== ПАРАМЕТРЫ ====
DATA_ROOT = Path("data/market_data")
EXCHANGES = ["Binance", "GateIo"]
SYMBOLS = None  # None = все общие символы

# Параметры поиска
PRICE_MOVE_THRESHOLD = 0.002  # 0.2% движение цены считается значимым
LOOK_AHEAD_MS = 500           # Смотрим на 500мс вперед на второй бирже
MIN_PROFIT = 0.001            # 0.1% минимальная прибыль

# ============================================================
# NUMBA ФУНКЦИИ
# ============================================================

@njit(cache=True, fastmath=True)
def find_price_leads(
    ts1: np.ndarray,      # timestamps биржи 1
    prices1: np.ndarray,  # цены биржи 1
    ts2: np.ndarray,      # timestamps биржи 2
    prices2: np.ndarray,  # цены биржи 2
    move_threshold: float,
    look_ahead_ms: float,
    min_profit: float
) -> tuple:
    """
    Находит моменты когда биржа 1 двинулась, а биржа 2 - еще нет.

    Возвращает:
    - ts_opportunities: timestamp возможности
    - price1_at_move: цена на бирже 1 в момент движения
    - price2_before: цена на бирже 2 ДО движения
    - price2_after: цена на бирже 2 ПОСЛЕ (через look_ahead_ms)
    - potential_profit: потенциальная прибыль
    """
    opportunities_ts = []
    opportunities_price1 = []
    opportunities_price2_before = []
    opportunities_price2_after = []
    opportunities_profit = []

    # Для каждой сделки на бирже 1
    for i in range(1, len(ts1)):
        # Проверяем было ли значимое движение
        price_change = (prices1[i] - prices1[i-1]) / prices1[i-1]

        if abs(price_change) < move_threshold:
            continue

        t_move = ts1[i]
        price_at_move = prices1[i]

        # Ищем цену на бирже 2 в момент движения
        idx_before = np.searchsorted(ts2, t_move)
        if idx_before >= len(ts2) or idx_before == 0:
            continue

        price2_before = prices2[idx_before - 1]  # Цена до движения

        # Ищем цену на бирже 2 через look_ahead_ms
        t_after = t_move + look_ahead_ms
        idx_after = np.searchsorted(ts2, t_after)
        if idx_after >= len(ts2):
            continue

        price2_after = prices2[idx_after]

        # Вычисляем потенциальную прибыль
        # Если цена на бирже 1 выросла, мы ожидаем что на бирже 2 тоже вырастет
        if price_change > 0:
            # Покупаем на бирже 2 по старой цене, продаем после роста
            profit = (price2_after - price2_before) / price2_before
        else:
            # Продаем на бирже 2 по старой цене, покупаем после падения
            profit = (price2_before - price2_after) / price2_before

        if profit >= min_profit:
            opportunities_ts.append(t_move)
            opportunities_price1.append(price_at_move)
            opportunities_price2_before.append(price2_before)
            opportunities_price2_after.append(price2_after)
            opportunities_profit.append(profit)

    return (
        np.array(opportunities_ts, dtype=np.int64),
        np.array(opportunities_price1, dtype=np.float64),
        np.array(opportunities_price2_before, dtype=np.float64),
        np.array(opportunities_price2_after, dtype=np.float64),
        np.array(opportunities_profit, dtype=np.float64)
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

# ============================================================
# ГЛАВНАЯ ФУНКЦИЯ
# ============================================================

def analyze_pair(symbol: str, originals: dict) -> pl.DataFrame:
    """Анализирует одну пару символов"""

    # Загружаем данные
    df1 = load_trades(EXCHANGES[0], originals[EXCHANGES[0]])
    df2 = load_trades(EXCHANGES[1], originals[EXCHANGES[1]])

    if df1.is_empty() or df2.is_empty():
        return pl.DataFrame()

    # Готовим данные для Numba
    df1_sorted = df1.sort("Timestamp").with_columns([
        pl.col("Timestamp").dt.epoch("ms").alias("ts_ms")
    ])
    df2_sorted = df2.sort("Timestamp").with_columns([
        pl.col("Timestamp").dt.epoch("ms").alias("ts_ms")
    ])

    ts1 = df1_sorted["ts_ms"].to_numpy()
    prices1 = df1_sorted["Price"].cast(pl.Float64).to_numpy()
    ts2 = df2_sorted["ts_ms"].to_numpy()
    prices2 = df2_sorted["Price"].cast(pl.Float64).to_numpy()

    # Прямое направление: биржа 1 ведет, биржа 2 следует
    (ts_fwd, p1_fwd, p2b_fwd, p2a_fwd, profit_fwd) = find_price_leads(
        ts1, prices1, ts2, prices2,
        PRICE_MOVE_THRESHOLD, LOOK_AHEAD_MS, MIN_PROFIT
    )

    # Обратное направление: биржа 2 ведет, биржа 1 следует
    (ts_rev, p2_rev, p1b_rev, p1a_rev, profit_rev) = find_price_leads(
        ts2, prices2, ts1, prices1,
        PRICE_MOVE_THRESHOLD, LOOK_AHEAD_MS, MIN_PROFIT
    )

    results = []

    # Добавляем прямые возможности
    for i in range(len(ts_fwd)):
        results.append({
            "symbol": symbol,
            "timestamp": ts_fwd[i],
            "leader": EXCHANGES[0],
            "follower": EXCHANGES[1],
            "leader_price": p1_fwd[i],
            "follower_price_before": p2b_fwd[i],
            "follower_price_after": p2a_fwd[i],
            "potential_profit_pct": profit_fwd[i] * 100,
            "lag_window_ms": LOOK_AHEAD_MS
        })

    # Добавляем обратные возможности
    for i in range(len(ts_rev)):
        results.append({
            "symbol": symbol,
            "timestamp": ts_rev[i],
            "leader": EXCHANGES[1],
            "follower": EXCHANGES[0],
            "leader_price": p2_rev[i],
            "follower_price_before": p1b_rev[i],
            "follower_price_after": p1a_rev[i],
            "potential_profit_pct": profit_rev[i] * 100,
            "lag_window_ms": LOOK_AHEAD_MS
        })

    if not results:
        return pl.DataFrame()

    return pl.DataFrame(results)

def main():
    print("=== Поиск латентных возможностей (простой метод) ===\n")
    print(f"Параметры:")
    print(f"  - Движение цены: >{PRICE_MOVE_THRESHOLD*100:.2f}%")
    print(f"  - Окно анализа: {LOOK_AHEAD_MS}мс")
    print(f"  - Мин. прибыль: >{MIN_PROFIT*100:.2f}%")
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
        print(f"[>] {norm_sym}... ", end="", flush=True)

        opps = analyze_pair(norm_sym, originals)

        if opps.is_empty():
            print("нет возможностей")
        else:
            print(f"НАЙДЕНО {len(opps)} возможностей!")
            all_opportunities.append(opps)

    # Итоговый отчет
    if all_opportunities:
        print(f"\n{'='*60}")
        print("=== РЕЗУЛЬТАТЫ ===")
        print(f"{'='*60}\n")

        df_all = pl.concat(all_opportunities, rechunk=False)

        print(f"[+] Всего возможностей: {len(df_all):,}")
        print(f"\n[*] По биржам:")
        summary = df_all.group_by("leader").agg([
            pl.count().alias("count"),
            pl.col("potential_profit_pct").mean().alias("avg_profit_pct")
        ]).sort("count", descending=True)
        print(summary)

        print(f"\n[*] По символам (топ-10):")
        by_symbol = df_all.group_by("symbol").agg([
            pl.count().alias("count"),
            pl.col("potential_profit_pct").mean().alias("avg_profit_pct"),
            pl.col("potential_profit_pct").max().alias("max_profit_pct")
        ]).sort("count", descending=True).head(10)
        print(by_symbol)

        print(f"\n[TOP-20] лучших возможностей:")
        top = df_all.sort("potential_profit_pct", descending=True).head(20)
        print(top.select([
            "symbol", "leader", "potential_profit_pct", "lag_window_ms"
        ]))

        # Сохраняем
        output_file = Path("analysis/opportunities_simple.parquet")
        output_file.parent.mkdir(exist_ok=True)
        df_all.write_parquet(output_file)
        print(f"\n[SAVED] {output_file}")

        # Статистика
        print(f"\n{'='*60}")
        print("=== КАК ИСПОЛЬЗОВАТЬ ===")
        print(f"{'='*60}")
        print(f"""
1. Подключись к WebSocket {df_all['leader'][0]} (он обычно быстрее)
2. Отслеживай движения цены >{PRICE_MOVE_THRESHOLD*100:.1f}%
3. Когда видишь движение - быстро ставь ордер на {df_all['follower'][0]}
4. У тебя есть ~{LOOK_AHEAD_MS}мс чтобы успеть
5. Средняя прибыль: {df_all['potential_profit_pct'].mean():.3f}%
        """)
    else:
        print("\n[!] Возможностей не найдено")
        print("Попробуй изменить параметры в начале файла")

if __name__ == "__main__":
    main()
