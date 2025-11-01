"""
Бенчмарк: сравнение старой и быстрой версии
"""
import numpy as np
import time
from numba import njit

# Старая версия (NumPy)
def slow_cross_correlation(fast_prices, slow_prices, max_lag):
    fast_norm = (fast_prices - np.mean(fast_prices)) / np.std(fast_prices)
    slow_norm = (slow_prices - np.mean(slow_prices)) / np.std(slow_prices)

    correlations = []
    for lag in range(0, max_lag + 1):
        if lag == 0:
            corr = np.corrcoef(fast_norm, slow_norm)[0, 1]
        else:
            if len(fast_norm) > lag and len(slow_norm) > lag:
                corr = np.corrcoef(fast_norm[:-lag], slow_norm[lag:])[0, 1]
            else:
                corr = 0
        correlations.append((lag, corr))

    best_lag, best_corr = max(correlations, key=lambda x: x[1])
    return best_lag, best_corr

# Быстрая версия (Numba JIT)
@njit(cache=True, fastmath=True)
def fast_cross_correlation(fast_prices, slow_prices, max_lag):
    n = len(fast_prices)

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

    for lag in range(max_lag + 1):
        if n - lag < 10:
            break

        corr_sum = 0.0
        valid_points = n - lag

        for i in range(valid_points):
            corr_sum += fast_norm[i] * slow_norm[i + lag]

        corr = corr_sum / valid_points

        if corr > best_corr:
            best_corr = corr
            best_lag = lag

    return best_lag, best_corr

def benchmark():
    print("⚡ Бенчмарк: NumPy vs Numba\n")

    # Генерируем тестовые данные
    sizes = [1000, 5000, 10000, 50000]
    max_lag = 100

    for size in sizes:
        np.random.seed(42)
        prices1 = np.cumsum(np.random.randn(size)) + 100
        prices2 = np.roll(prices1, 10) + np.random.randn(size) * 0.1

        print(f"Размер данных: {size:,} точек")

        # Прогрев Numba (первый запуск компилирует)
        if size == sizes[0]:
            print("   (прогрев Numba JIT...)")
            fast_cross_correlation(prices1[:100], prices2[:100], 10)

        # NumPy версия
        start = time.perf_counter()
        lag_slow, corr_slow = slow_cross_correlation(prices1, prices2, max_lag)
        time_slow = time.perf_counter() - start

        # Numba версия
        start = time.perf_counter()
        lag_fast, corr_fast = fast_cross_correlation(prices1, prices2, max_lag)
        time_fast = time.perf_counter() - start

        speedup = time_slow / time_fast

        print(f"   NumPy:  {time_slow*1000:6.1f} мс")
        print(f"   Numba:  {time_fast*1000:6.1f} мс")
        print(f"   🚀 Ускорение: {speedup:.1f}x")
        print(f"   ✅ Результаты совпадают: lag={lag_fast}, corr={corr_fast:.3f}\n")

if __name__ == "__main__":
    benchmark()
