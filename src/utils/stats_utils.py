"""
Statistical utility functions for feature engineering.

Provides helpers for calculating averages, weights, trends,
and statistical metrics.
"""
from typing import List, Optional, Union
import math


def calculate_exponential_weights(n_items: int, decay: float = 0.8) -> List[float]:
    """
    Generate exponential decay weights for time-weighted calculations.
    
    Most recent item gets weight 1.0, then decay^1, decay^2, etc.
    
    Args:
        n_items: Number of items to weight
        decay: Decay factor (0.8 means each older item is 80% weight)
        
    Returns:
        List of weights (most recent first)
        
    Example:
        >>> calculate_exponential_weights(5, 0.8)
        [1.0, 0.8, 0.64, 0.512, 0.4096]
    """
    if n_items <= 0:
        return []
    
    return [decay ** i for i in range(n_items)]


def weighted_average(values: List[float], weights: Optional[List[float]] = None) -> float:
    """
    Calculate weighted average of values.
    
    Args:
        values: List of numeric values
        weights: Optional weights (uses equal weights if not provided)
        
    Returns:
        Weighted average, or 0.0 if no valid values
    """
    if not values:
        return 0.0
    
    # Filter out None/NaN values
    clean_values = []
    clean_weights = []
    
    for i, v in enumerate(values):
        if v is not None and not (isinstance(v, float) and math.isnan(v)):
            clean_values.append(float(v))
            if weights and i < len(weights):
                clean_weights.append(weights[i])
            else:
                clean_weights.append(1.0)
    
    if not clean_values:
        return 0.0
    
    total_weight = sum(clean_weights)
    if total_weight == 0:
        return 0.0
    
    weighted_sum = sum(v * w for v, w in zip(clean_values, clean_weights))
    return weighted_sum / total_weight


def calculate_rolling_average(
    values: List[float],
    window: int = 5
) -> List[float]:
    """
    Calculate rolling average over a window.
    
    Args:
        values: List of values (oldest first)
        window: Window size
        
    Returns:
        List of rolling averages
    """
    if not values or window <= 0:
        return []
    
    result = []
    for i in range(len(values)):
        start_idx = max(0, i - window + 1)
        window_values = values[start_idx:i+1]
        valid_values = [v for v in window_values if v is not None]
        
        if valid_values:
            result.append(sum(valid_values) / len(valid_values))
        else:
            result.append(0.0)
    
    return result


def detect_trend(values: List[float], min_points: int = 3) -> str:
    """
    Detect if values are trending up, down, or stable.
    
    Uses simple linear regression direction.
    
    Args:
        values: List of values (oldest first)
        min_points: Minimum points needed for trend detection
        
    Returns:
        'improving', 'declining', or 'stable'
    """
    if not values or len(values) < min_points:
        return "stable"
    
    # Simple approach: compare recent half to earlier half
    mid = len(values) // 2
    early = values[:mid] if mid > 0 else values
    recent = values[mid:] if mid > 0 else values
    
    early_avg = sum(early) / len(early) if early else 0
    recent_avg = sum(recent) / len(recent) if recent else 0
    
    threshold = 0.1  # 10% change threshold
    
    if early_avg == 0:
        return "stable"
    
    change = (recent_avg - early_avg) / abs(early_avg)
    
    if change > threshold:
        return "improving"
    elif change < -threshold:
        return "declining"
    else:
        return "stable"


def normalize_value(
    value: float,
    min_val: float,
    max_val: float
) -> float:
    """
    Normalize value to 0-1 range.
    
    Args:
        value: Value to normalize
        min_val: Minimum value in range
        max_val: Maximum value in range
        
    Returns:
        Normalized value between 0 and 1
    """
    if max_val == min_val:
        return 0.5
    
    normalized = (value - min_val) / (max_val - min_val)
    return max(0.0, min(1.0, normalized))


def safe_divide(numerator: float, denominator: float, default: float = 0.0) -> float:
    """
    Safe division that handles zero denominator.
    
    Args:
        numerator: Numerator
        denominator: Denominator
        default: Value to return if denominator is zero
        
    Returns:
        Result of division or default
    """
    if denominator == 0:
        return default
    return numerator / denominator


def calculate_rate(count: int, total: int) -> float:
    """
    Calculate rate/percentage.
    
    Args:
        count: Number of occurrences
        total: Total number of events
        
    Returns:
        Rate between 0.0 and 1.0
    """
    if total == 0:
        return 0.0
    return count / total


def calculate_form_points(results: List[str]) -> int:
    """
    Calculate points from a list of results.
    
    Args:
        results: List of 'W', 'D', 'L' results
        
    Returns:
        Total points (W=3, D=1, L=0)
    """
    points = 0
    for result in results:
        if result == "W":
            points += 3
        elif result == "D":
            points += 1
    return points


def results_to_form_string(results: List[str], max_length: int = 5) -> str:
    """
    Convert results list to form string.
    
    Args:
        results: List of 'W', 'D', 'L' results (most recent first)
        max_length: Maximum length of form string
        
    Returns:
        Form string (e.g., 'WWDLW')
    """
    return "".join(results[:max_length])


def calculate_goals_stats(
    goals_list: List[int]
) -> dict:
    """
    Calculate goal statistics from a list of goals.
    
    Args:
        goals_list: List of goals per match
        
    Returns:
        Dict with avg, total, min, max
    """
    if not goals_list:
        return {
            "avg": 0.0,
            "total": 0,
            "min": 0,
            "max": 0,
            "matches": 0,
        }
    
    valid_goals = [g for g in goals_list if g is not None]
    
    if not valid_goals:
        return {
            "avg": 0.0,
            "total": 0,
            "min": 0,
            "max": 0,
            "matches": 0,
        }
    
    return {
        "avg": round(sum(valid_goals) / len(valid_goals), 3),
        "total": sum(valid_goals),
        "min": min(valid_goals),
        "max": max(valid_goals),
        "matches": len(valid_goals),
    }


def calculate_confidence_score(
    probability: float,
    sample_size: int,
    min_sample: int = 5
) -> str:
    """
    Calculate confidence level based on probability and sample size.
    
    Args:
        probability: Predicted probability
        sample_size: Number of data points used
        min_sample: Minimum sample for "high" confidence
        
    Returns:
        'high', 'medium', or 'low'
    """
    # Adjust for sample size
    if sample_size < min_sample:
        return "low"
    
    # Based on probability distance from 0.5
    certainty = abs(probability - 0.5) * 2  # 0 to 1 scale
    
    if certainty >= 0.4 and sample_size >= 10:
        return "high"
    elif certainty >= 0.2 and sample_size >= 5:
        return "medium"
    else:
        return "low"


def clamp(value: float, min_val: float, max_val: float) -> float:
    """
    Clamp value to a range.
    
    Args:
        value: Value to clamp
        min_val: Minimum allowed value
        max_val: Maximum allowed value
        
    Returns:
        Clamped value
    """
    return max(min_val, min(max_val, value))


def round_to_precision(value: float, precision: int = 3) -> float:
    """
    Round value to specified decimal precision.
    
    Args:
        value: Value to round
        precision: Number of decimal places
        
    Returns:
        Rounded value
    """
    if value is None or (isinstance(value, float) and math.isnan(value)):
        return 0.0
    return round(value, precision)
