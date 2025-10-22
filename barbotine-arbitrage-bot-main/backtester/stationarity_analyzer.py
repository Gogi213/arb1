#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Calculates stationarity metrics for a given time series.
"""

import polars as pl
from statsmodels.tsa.stattools import adfuller, kpss, coint
import statsmodels.api as sm
import numpy as np
from typing import Dict, Optional, Tuple

def calculate_cointegration_test(series1: pl.Series, series2: pl.Series) -> Tuple[Optional[float], Optional[float]]:
    """
    Performs the Engle-Granger two-step cointegration test.

    Args:
        series1: First time series (e.g., prices from exchange 1).
        series2: Second time series (e.g., prices from exchange 2).

    Returns:
        A tuple containing the cointegration p-value and the hedge ratio (beta).
        Returns (None, None) if the test cannot be performed.
    """
    if series1.len() != series2.len() or series1.len() < 20:
        return None, None
    
    y = series1.to_numpy()
    x = series2.to_numpy()
    
    # Step 1: Run OLS regression to find the hedge ratio
    x_with_const = sm.add_constant(x)
    model = sm.OLS(y, x_with_const)
    results = model.fit()
    
    # Ensure the model found a coefficient for the hedge ratio
    if len(results.params) < 2:
        return None, None
        
    hedge_ratio = results.params[1]
    
    # Step 2: Run the cointegration test on the residuals
    residuals = y - hedge_ratio * x
    try:
        coint_test_result = adfuller(residuals)
        p_value = coint_test_result[1]
        return p_value, hedge_ratio
    except Exception:
        return None, None

def hurst(ts: np.ndarray) -> float:
    """
    Calculates the Hurst Exponent for a time series.

    Args:
        ts: A numpy array representing the time series.

    Returns:
        The Hurst Exponent as a float.
    """
    if len(ts) < 20:
        return np.nan

    lags = range(2, 20)
    tau = [np.sqrt(np.std(np.subtract(ts[lag:], ts[:-lag]))) for lag in lags]
    poly = np.polyfit(np.log(lags), np.log(tau), 1)
    return poly[0] * 2.0

def calculate_stationarity_metrics(series: pl.Series) -> Dict[str, Optional[float]]:
    """
    Calculates a suite of stationarity metrics for a given Polars Series.

    Args:
        series: A Polars Series containing the time series data (e.g., price spread).

    Returns:
        A dictionary containing the calculated metrics.
    """
    if series.is_empty() or series.len() < 20:
        return {
            "adf_p_value": None,
            "kpss_p_value": None,
            "hurst_exponent": None
        }

    ts = series.to_numpy()

    try:
        adf_result = adfuller(ts)
        adf_p_value = adf_result[1]
    except Exception:
        adf_p_value = None

    try:
        kpss_result = kpss(ts, regression='c', nlags="auto")
        kpss_p_value = kpss_result[1]
    except Exception:
        kpss_p_value = None
        
    try:
        hurst_exponent = hurst(ts)
    except Exception:
        hurst_exponent = None

    return {
        "adf_p_value": adf_p_value,
        "kpss_p_value": kpss_p_value,
        "hurst_exponent": hurst_exponent
    }

def calculate_stationarity_score(metrics: Dict[str, Optional[float]]) -> float:
    """
    Calculates a stationarity score based on a combination of metrics.
    The score is normalized to be between 0 and 1.
    
    Args:
        metrics: A dictionary with p-values and the Hurst exponent.
        
    Returns:
        A float score between 0 and 1, where higher is better.
    """
    adf_p = metrics.get("adf_p_value")
    kpss_p = metrics.get("kpss_p_value")
    hurst = metrics.get("hurst_exponent")

    if adf_p is None or kpss_p is None or hurst is None:
        return 0.0

    # Normalize scores (0 to 1, where 1 is best)
    # ADF: Lower p-value is better. We want (1 - p_value).
    adf_score = 1.0 - adf_p
    
    # KPSS: Higher p-value is better (we want to fail to reject stationarity).
    # p-value is already 0-1, but we can give more weight to values > 0.05.
    kpss_score = 1.0 if kpss_p >= 0.05 else kpss_p / 0.05

    # Hurst: Value < 0.5 indicates mean-reversion. Score is higher as it approaches 0.
    hurst_score = 1.0 - (hurst * 2) if hurst < 0.5 else 0.0

    # Combine scores with weights
    # Weights can be tuned. Let's start with equal weights.
    score = (adf_score + kpss_score + hurst_score) / 3.0
    
    return max(0.0, min(1.0, score)) # Ensure score is within [0, 1]