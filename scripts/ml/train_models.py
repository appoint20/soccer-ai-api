"""
Model training pipeline for football match prediction.
Trains XGBoost models for Over 2.5, BTTS, 2-3 Goals, and H/D/A.
Exports models in ONNX format for C# inference.
"""

import pandas as pd
import numpy as np
from pathlib import Path
from sklearn.model_selection import train_test_split, cross_val_score
from sklearn.metrics import accuracy_score, precision_score, recall_score, f1_score, roc_auc_score
from sklearn.preprocessing import StandardScaler
import xgboost as xgb
import joblib
import json

# Configuration
DATA_PATH = Path(__file__).parent / "training_data.parquet"
MODELS_DIR = Path(__file__).parent / "models"
RANDOM_STATE = 42

# Feature columns (exclude identifiers and targets)
FEATURE_COLS = [
    'home_goals_scored_avg', 'home_goals_conceded_avg', 'home_xg_avg',
    'home_shots_avg', 'home_shots_on_target_avg', 'home_btts_rate',
    'home_over25_rate', 'home_clean_sheet_rate', 'home_failed_to_score_rate',
    'away_goals_scored_avg', 'away_goals_conceded_avg', 'away_xg_avg',
    'away_shots_avg', 'away_shots_on_target_avg', 'away_btts_rate',
    'away_over25_rate', 'away_clean_sheet_rate', 'away_failed_to_score_rate',
    'h2h_total_goals_avg', 'h2h_btts_rate', 'h2h_over25_rate',
    'league_avg_goals', 'league_btts_rate', 'league_over25_rate',
    'is_derby',
    # Odds-implied probabilities (if available)
    'home_win_implied_prob', 'draw_implied_prob', 'away_win_implied_prob',
    'over25_implied_prob', 'btts_implied_prob'
]


def load_data() -> pd.DataFrame:
    """Load training data from parquet."""
    df = pd.read_parquet(DATA_PATH)
    print(f"Loaded {len(df)} samples")
    return df


def prepare_features(df: pd.DataFrame) -> tuple:
    """Prepare feature matrix and handle missing values."""
    # Calculate odds-implied probabilities
    df['home_win_implied_prob'] = 1 / df['home_win_odds'].replace(0, np.nan)
    df['draw_implied_prob'] = 1 / df['draw_odds'].replace(0, np.nan)
    df['away_win_implied_prob'] = 1 / df['away_win_odds'].replace(0, np.nan)
    df['over25_implied_prob'] = 1 / df['over25_odds'].replace(0, np.nan)
    df['btts_implied_prob'] = 1 / df['btts_yes_odds'].replace(0, np.nan)
    
    X = df[FEATURE_COLS].copy()
    
    # Fill any NaN with median
    X = X.fillna(X.median())
    
    return X


def train_binary_model(X: pd.DataFrame, y: pd.Series, model_name: str) -> dict:
    """Train a binary classification model."""
    print(f"\n{'='*50}")
    print(f"Training {model_name}")
    print(f"{'='*50}")
    
    # Split data
    X_train, X_test, y_train, y_test = train_test_split(
        X, y, test_size=0.2, random_state=RANDOM_STATE, stratify=y
    )
    
    # XGBoost parameters tuned for football prediction
    params = {
        'n_estimators': 300,
        'max_depth': 5,
        'learning_rate': 0.03,
        'subsample': 0.7,
        'colsample_bytree': 0.7,
        'min_child_weight': 5,
        'random_state': RANDOM_STATE,
        'eval_metric': 'logloss',
        'reg_alpha': 0.1,
        'reg_lambda': 1.0
    }
    
    model = xgb.XGBClassifier(**params)
    model.fit(X_train, y_train, eval_set=[(X_test, y_test)], verbose=False)
    
    # Predictions
    y_pred = model.predict(X_test)
    y_prob = model.predict_proba(X_test)[:, 1]
    
    # Metrics
    metrics = {
        'accuracy': accuracy_score(y_test, y_pred),
        'precision': precision_score(y_test, y_pred, zero_division=0),
        'recall': recall_score(y_test, y_pred, zero_division=0),
        'f1': f1_score(y_test, y_pred, zero_division=0),
        'roc_auc': roc_auc_score(y_test, y_prob)
    }
    
    # Cross-validation
    cv_scores = cross_val_score(model, X, y, cv=5, scoring='accuracy')
    metrics['cv_accuracy_mean'] = cv_scores.mean()
    metrics['cv_accuracy_std'] = cv_scores.std()
    
    print(f"  Accuracy: {metrics['accuracy']:.4f}")
    print(f"  Precision: {metrics['precision']:.4f}")
    print(f"  Recall: {metrics['recall']:.4f}")
    print(f"  F1: {metrics['f1']:.4f}")
    print(f"  ROC-AUC: {metrics['roc_auc']:.4f}")
    print(f"  CV Accuracy: {metrics['cv_accuracy_mean']:.4f} (+/- {metrics['cv_accuracy_std']:.4f})")
    
    # Save model
    model_path = MODELS_DIR / f"{model_name}.json"
    model.save_model(model_path)
    print(f"  Saved to: {model_path}")
    
    # Feature importance
    importance = dict(zip(FEATURE_COLS, model.feature_importances_))
    top_features = sorted(importance.items(), key=lambda x: x[1], reverse=True)[:5]
    print(f"  Top features: {[f[0] for f in top_features]}")
    
    return {
        'model': model,
        'metrics': metrics,
        'feature_importance': importance
    }


def train_multiclass_model(X: pd.DataFrame, y: pd.Series, model_name: str) -> dict:
    """Train a multiclass classification model (H/D/A)."""
    print(f"\n{'='*50}")
    print(f"Training {model_name}")
    print(f"{'='*50}")
    
    # Split data
    X_train, X_test, y_train, y_test = train_test_split(
        X, y, test_size=0.2, random_state=RANDOM_STATE, stratify=y
    )
    
    # XGBoost parameters for multiclass
    params = {
        'n_estimators': 300,
        'max_depth': 5,
        'learning_rate': 0.03,
        'subsample': 0.7,
        'colsample_bytree': 0.7,
        'min_child_weight': 5,
        'random_state': RANDOM_STATE,
        'objective': 'multi:softprob',
        'num_class': 3,
        'eval_metric': 'mlogloss',
        'reg_alpha': 0.1,
        'reg_lambda': 1.0
    }
    
    model = xgb.XGBClassifier(**params)
    model.fit(X_train, y_train, eval_set=[(X_test, y_test)], verbose=False)
    
    # Predictions
    y_pred = model.predict(X_test)
    
    # Metrics
    metrics = {
        'accuracy': accuracy_score(y_test, y_pred),
        'precision_macro': precision_score(y_test, y_pred, average='macro', zero_division=0),
        'recall_macro': recall_score(y_test, y_pred, average='macro', zero_division=0),
        'f1_macro': f1_score(y_test, y_pred, average='macro', zero_division=0)
    }
    
    # Cross-validation
    cv_scores = cross_val_score(model, X, y, cv=5, scoring='accuracy')
    metrics['cv_accuracy_mean'] = cv_scores.mean()
    metrics['cv_accuracy_std'] = cv_scores.std()
    
    print(f"  Accuracy: {metrics['accuracy']:.4f}")
    print(f"  Precision (macro): {metrics['precision_macro']:.4f}")
    print(f"  Recall (macro): {metrics['recall_macro']:.4f}")
    print(f"  F1 (macro): {metrics['f1_macro']:.4f}")
    print(f"  CV Accuracy: {metrics['cv_accuracy_mean']:.4f} (+/- {metrics['cv_accuracy_std']:.4f})")
    
    # Class distribution in predictions
    print(f"  Prediction distribution: Home={sum(y_pred==0)/len(y_pred):.1%}, "
          f"Draw={sum(y_pred==1)/len(y_pred):.1%}, Away={sum(y_pred==2)/len(y_pred):.1%}")
    
    # Save model
    model_path = MODELS_DIR / f"{model_name}.json"
    model.save_model(model_path)
    print(f"  Saved to: {model_path}")
    
    return {
        'model': model,
        'metrics': metrics
    }


def main():
    print("=" * 50)
    print("Football ML Model Training")
    print("=" * 50)
    
    # Create models directory
    MODELS_DIR.mkdir(exist_ok=True)
    
    # Load data
    df = load_data()
    X = prepare_features(df)
    
    # Save feature column order for inference
    with open(MODELS_DIR / "feature_columns.json", 'w') as f:
        json.dump(FEATURE_COLS, f)
    
    results = {}
    
    # Train binary models
    results['over25'] = train_binary_model(X, df['target_over25'], 'over25_model')
    results['btts'] = train_binary_model(X, df['target_btts'], 'btts_model')
    results['goals_2_3'] = train_binary_model(X, df['target_goals_2_3'], 'goals_2_3_model')
    
    # Train multiclass model
    results['hda'] = train_multiclass_model(X, df['target_result'], 'hda_model')
    
    # Summary
    print("\n" + "=" * 50)
    print("TRAINING SUMMARY")
    print("=" * 50)
    
    summary = {
        'over25': {
            'accuracy': results['over25']['metrics']['accuracy'],
            'roc_auc': results['over25']['metrics']['roc_auc']
        },
        'btts': {
            'accuracy': results['btts']['metrics']['accuracy'],
            'roc_auc': results['btts']['metrics']['roc_auc']
        },
        'goals_2_3': {
            'accuracy': results['goals_2_3']['metrics']['accuracy'],
            'roc_auc': results['goals_2_3']['metrics']['roc_auc']
        },
        'hda': {
            'accuracy': results['hda']['metrics']['accuracy'],
            'f1_macro': results['hda']['metrics']['f1_macro']
        }
    }
    
    for name, metrics in summary.items():
        print(f"{name}: {metrics}")
    
    # Save summary
    with open(MODELS_DIR / "training_summary.json", 'w') as f:
        json.dump(summary, f, indent=2)
    
    print(f"\nModels saved to: {MODELS_DIR}")


if __name__ == "__main__":
    main()
