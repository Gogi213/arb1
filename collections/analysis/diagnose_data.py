"""
Диагностика данных - проверяет частоту сделок и синхронизацию
"""
import polars as pl
from pathlib import Path
import glob

DATA_ROOT = Path("data/market_data")
SYMBOL = "ADAUSDT"  # Проверим на ADA

def load_sample(exchange, symbol):
    """Загружает небольшую выборку данных"""
    symbol_path = DATA_ROOT / f"exchange={exchange}" / f"symbol={symbol}"
    if not symbol_path.exists():
        return pl.DataFrame()

    search_pattern = str(symbol_path / "**/trades-*.parquet")
    file_list = glob.glob(search_pattern, recursive=True)[:1]  # Только первый файл

    if not file_list:
        return pl.DataFrame()

    return pl.read_parquet(file_list[0])

print(f"=== Диагностика данных для {SYMBOL} ===\n")

# Binance
print("[*] Загрузка Binance...")
df_binance = load_sample("Binance", SYMBOL)
if not df_binance.is_empty():
    print(f"    Записей: {len(df_binance):,}")
    print(f"    Колонки: {', '.join(df_binance.columns)}")

    # Временные интервалы
    if "Timestamp" in df_binance.columns:
        df_sorted = df_binance.sort("Timestamp")
        intervals_ns = df_sorted["Timestamp"].diff().drop_nulls().dt.total_nanoseconds()

        print(f"\n    Интервалы между сделками (мс):")
        print(f"      Min: {intervals_ns.min() / 1_000_000:.2f}")
        print(f"      Median: {intervals_ns.median() / 1_000_000:.2f}")
        print(f"      Max: {intervals_ns.max() / 1_000_000:.2f}")
        print(f"      Mean: {intervals_ns.mean() / 1_000_000:.2f}")
else:
    print("    [!] Нет данных")

print("\n" + "="*60 + "\n")

# GateIo
print("[*] Загрузка GateIo...")
df_gateio = load_sample("GateIo", "ADA_USDT")
if not df_gateio.is_empty():
    print(f"    Записей: {len(df_gateio):,}")
    print(f"    Колонки: {', '.join(df_gateio.columns)}")

    # Временные интервалы
    if "Timestamp" in df_gateio.columns:
        df_sorted = df_gateio.sort("Timestamp")
        intervals_ns = df_sorted["Timestamp"].diff().drop_nulls().dt.total_nanoseconds()

        print(f"\n    Интервалы между сделками (мс):")
        print(f"      Min: {intervals_ns.min() / 1_000_000:.2f}")
        print(f"      Median: {intervals_ns.median() / 1_000_000:.2f}")
        print(f"      Max: {intervals_ns.max() / 1_000_000:.2f}")
        print(f"      Mean: {intervals_ns.mean() / 1_000_000:.2f}")
else:
    print("    [!] Нет данных")

print("\n" + "="*60)
print("[*] Рекомендации:")
print("    - Если median > 100ms: данные слишком разреженные, бакеты нужно увеличить")
print("    - Если median < 10ms: данные детальные, можно уменьшить бакеты")
print("    - Оптимально: бакет = 2-5x median интервала")
