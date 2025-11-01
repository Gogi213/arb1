import polars as pl
import numpy as np
from pathlib import Path
import glob

# ==== ПАРАМЕТРЫ АНАЛИЗА ====
DATA_ROOT = Path("data/market_data")
EXCHANGES = ["Binance", "GateIo"]   # Любые две биржи
SYMBOLS = None                      # None → искать общие автоматически
PROFIT_THRESHOLD = 0.003            # 0.3% - целевая доходность
TICK_WINDOW = 20                    # Кол-во тиков вперёд для анализа
MIN_TICK_USD = 50                   # Мин. размер трейда в USD для учёта

def who_is_fast(df: pl.DataFrame) -> tuple[str, str]:
    """Определяет быструю и медленную биржу по медианной задержке между сделками."""
    lat = df.group_by("Exchange").agg(
        pl.col("Timestamp").diff().dt.total_nanoseconds().median()
    ).sort("Timestamp")  # Меньше наносекунд = быстрее
    
    if lat.height < 2:
        raise ValueError("Недостаточно данных для сравнения скоростей бирж.")
        
    fast, slow = lat["Exchange"][0], lat["Exchange"][1]
    print(f"Определено: Быстрая биржа = {fast}, Медленная биржа = {slow}")
    return fast, slow

def common_symbols(root: Path, ex1: str, ex2: str) -> dict[str, dict[str, str]]:
    """
    Находит общие символы и возвращает словарь, где ключ - нормализованный символ,
    а значение - словарь с оригинальными именами для каждой биржи.
    Пример: {'BTCUSDT': {'Binance': 'BTCUSDT', 'GateIo': 'BTC_USDT'}}
    """
    path1 = root / f"exchange={ex1}"
    path2 = root / f"exchange={ex2}"

    if not path1.exists() or not path2.exists():
        return {}

    # Создаем словари: {нормализованное_имя: оригинальное_имя}
    map1 = {p.name.split('=')[1].replace('_', '').upper(): p.name.split('=')[1] for p in path1.iterdir() if p.is_dir()}
    map2 = {p.name.split('=')[1].replace('_', '').upper(): p.name.split('=')[1] for p in path2.iterdir() if p.is_dir()}
    
    common_normalized = set(map1.keys()) & set(map2.keys())
    
    result = {
        norm_sym: {
            ex1: map1[norm_sym],
            ex2: map2[norm_sym]
        }
        for norm_sym in common_normalized
    }
    
    print(f"Найдено {len(result)} общих символов после нормализации.")
    return result

def effective_only(df: pl.DataFrame, min_usd: float, win_ticks: int) -> pl.DataFrame:
    """
    Фильтрует сделки, оставляя только "эффективные":
    1. Сделки с объемом > min_usd.
    2. Сделки, после которых цена сместилась более чем на 0.05%.
    """
    # В Parquet файле колонка называется 'Quantity', а не 'Amount'
    df = df.filter(pl.col("Quantity") * pl.col("Price") >= min_usd)
    
    df = df.with_columns(
        pl.col("Price")
          .shift(-win_ticks)
          .sub(pl.col("Price"))
          .truediv(pl.col("Price"))
          .alias("fwd_return")
    )
    # Оставляем только те строки, где было движение цены
    return df.filter(pl.col("fwd_return").abs() > 0.0005).drop("fwd_return")

def find_price_jumps(df: pl.DataFrame, threshold: float, n_ticks: int) -> pl.DataFrame:
    """
    Находит моменты, когда цена росла на `threshold` за следующие `n_ticks` сделок.
    """
    df = df.sort("Timestamp")
    df = df.with_columns(
        ((pl.col("Price").shift(-n_ticks) - pl.col("Price")) / pl.col("Price"))
          .alias("price_increase")
    )
    return df.filter(pl.col("price_increase") > threshold)

def load_trades_for_symbol(exchange: str, symbol: str) -> pl.DataFrame:
    """
    Загружает все файлы сделок для указанной биржи и символа,
    используя glob для надежного поиска файлов в поддиректориях.
    """
    symbol_path = DATA_ROOT / f"exchange={exchange}" / f"symbol={symbol}"
    if not symbol_path.exists():
        print(f"Директория символа не найдена: {symbol_path}")
        return pl.DataFrame()

    # Используем glob для рекурсивного поиска всех нужных файлов
    search_pattern = str(symbol_path / "**/trades-*.parquet")
    print(f"Поиск файлов по паттерну: {search_pattern}")
    
    file_list = glob.glob(search_pattern, recursive=True)

    if not file_list:
        print(f"Файлы сделок не найдены для {exchange}/{symbol} по указанному пути.")
        return pl.DataFrame()
    
    print(f"Найдено {len(file_list)} файлов для {exchange}/{symbol}. Загрузка...")
    
    try:
        # Передаем список файлов напрямую в scan_parquet
        df = pl.scan_parquet(file_list).collect()
        return df
    except Exception as e:
        print(f"Ошибка при загрузке данных из {len(file_list)} файлов для {exchange}/{symbol}. Ошибка: {e}")
        return pl.DataFrame()

def main():
    """
    Основной процесс анализа:
    1. Находит общие символы.
    2. Для каждого символа:
        - Загружает данные.
        - Фильтрует "эффективные" сделки.
        - Определяет быструю/медленную биржу.
        - Ищет возможности для арбитража на медленной бирже.
    """
    print("--- Запуск анализа арбитражных возможностей ---")
    
    # SYMBOLS теперь должен быть списком нормализованных имен, если используется
    symbols_map = common_symbols(DATA_ROOT, *EXCHANGES)
    if SYMBOLS:
        symbols_map = {s: symbols_map[s] for s in SYMBOLS if s in symbols_map}

    if not symbols_map:
        print("Не найдено общих символов для анализа. Завершение.")
        return

    for norm_sym, original_names in symbols_map.items():
        print(f"\n--- Анализ символа: {norm_sym} ---")
        
        original_sym1 = original_names[EXCHANGES[0]]
        original_sym2 = original_names[EXCHANGES[1]]

        df1 = load_trades_for_symbol(EXCHANGES[0], original_sym1)
        df2 = load_trades_for_symbol(EXCHANGES[1], original_sym2)
        
        if df1.is_empty() or df2.is_empty():
            print(f"Пропуск {norm_sym}: отсутствуют данные для одной из бирж.")
            continue
            
        # Объединяем и фильтруем только значимые сделки
        df_all = pl.concat([df1, df2]).pipe(effective_only, MIN_TICK_USD, TICK_WINDOW)
        
        if df_all.is_empty():
            print(f"Пропуск {norm_sym}: не найдено 'эффективных' сделок после фильтрации.")
            continue

        try:
            fast_ex, slow_ex = who_is_fast(df_all)
        except ValueError as e:
            print(f"Пропуск {norm_sym}: {e}")
            continue

        # Ищем возможности на медленной бирже
        opportunities = find_price_jumps(
            df_all.filter(pl.col("Exchange") == slow_ex),
            PROFIT_THRESHOLD,
            TICK_WINDOW
        )
        
        print(f"Результат для {norm_sym}: Найдено {opportunities.height} потенциальных возможностей на {slow_ex} после импульсов с {fast_ex}")
        if not opportunities.is_empty():
            print("Пример возможностей:")
            print(opportunities.head(3))

        # TODO: Шаг 3 - для каждой возможности найти предшествующий сигнал на быстрой бирже.
        # TODO: Шаг 4 - статистическая валидация.

if __name__ == "__main__":
    main()