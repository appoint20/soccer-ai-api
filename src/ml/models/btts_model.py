"""
Both Teams To Score (BTTS) prediction model using LightGBM.

Binary classifier predicting if both teams will score in a match.
"""
from typing import Any, Dict, List, Optional
import numpy as np
from datetime import datetime

try:
    import lightgbm as lgb
    HAS_LIGHTGBM = True
except ImportError:
    HAS_LIGHTGBM = False

from sklearn.preprocessing import StandardScaler

from src.ml.models.base_model import BaseModel


class BTTSModel(BaseModel):
    """
    LightGBM model for predicting Both Teams To Score.
    
    Target: Binary (1 = both teams score, 0 = at least one team fails to score)
    Target accuracy: 80% (ambitious), realistic: 70-75%
    """
    
    def __init__(self, version: str = "1.0.0"):
        """Initialize BTTS model."""
        super().__init__(
            model_name="btts",
            model_type="lightgbm",
            version=version,
        )
        
        if not HAS_LIGHTGBM:
            raise ImportError("lightgbm is required. Install with: pip install lightgbm")
        
        self.scaler = StandardScaler()
        self._use_scaler = False  # LightGBM doesn't require scaling
    
    def get_default_hyperparameters(self) -> Dict[str, Any]:
        """Get default LightGBM hyperparameters for BTTS prediction."""
        # Stronger regularization to prevent overfitting
        return {
            "n_estimators": 150,  # Reduced from 200
            "num_leaves": 31,  # Reduced from 50 (less complex)
            "max_depth": 5,  # Reduced from 7
            "learning_rate": 0.05,  # Reduced from 0.1
            "min_child_samples": 50,  # Increased from 30
            "subsample": 0.7,  # Reduced for regularization
            "colsample_bytree": 0.7,  # Reduced for regularization
            "reg_alpha": 0.5,  # Increased L1 regularization
            "reg_lambda": 1.0,  # Increased L2 regularization
            "class_weight": "balanced",
            "objective": "binary",
            "metric": "binary_logloss",
            "random_state": 42,
            "n_jobs": -1,
            "verbose": -1,
        }
    
    def get_hyperparameter_space(self) -> Dict[str, List]:
        """Get hyperparameter search space."""
        return {
            "num_leaves": [31, 50, 70, 100],
            "max_depth": [5, 7, 10, -1],
            "learning_rate": [0.01, 0.05, 0.1],
            "n_estimators": [100, 200, 300],
            "min_child_samples": [20, 30, 50],
            "subsample": [0.7, 0.8, 0.9],
            "colsample_bytree": [0.7, 0.8, 0.9],
            "reg_alpha": [0.0, 0.1, 0.5],
            "reg_lambda": [0.0, 0.1, 0.5],
        }
    
    def build_model(self, hyperparameters: Optional[Dict] = None):
        """
        Build LightGBM classifier with given hyperparameters.
        
        Args:
            hyperparameters: Optional custom hyperparameters
        """
        params = self.get_default_hyperparameters()
        if hyperparameters:
            params.update(hyperparameters)
        
        self.model = lgb.LGBMClassifier(**params)
        self.metadata["hyperparameters"] = params
        
        self.logger.info(f"Built LightGBM model with params: {params}")
    
    def train(
        self,
        X_train: np.ndarray,
        y_train: np.ndarray,
        X_val: Optional[np.ndarray] = None,
        y_val: Optional[np.ndarray] = None,
        early_stopping_rounds: int = 20,
        verbose: bool = False,
        **kwargs,
    ) -> Dict[str, Any]:
        """
        Train the BTTS model.
        
        Args:
            X_train: Training features
            y_train: Training targets (0 or 1)
            X_val: Validation features
            y_val: Validation targets
            early_stopping_rounds: Early stopping patience
            verbose: Whether to print training progress
            
        Returns:
            Dict with training metrics
        """
        if self.model is None:
            self.build_model()
        
        self.logger.info(f"Training on {len(X_train)} samples...")
        
        # Prepare callbacks
        callbacks = []
        if X_val is not None and y_val is not None:
            callbacks.append(lgb.early_stopping(early_stopping_rounds, verbose=verbose))
        
        # Train model
        eval_set = []
        if X_val is not None and y_val is not None:
            eval_set = [(X_val, y_val)]
        
        self.model.fit(
            X_train, y_train,
            eval_set=eval_set if eval_set else None,
            callbacks=callbacks if callbacks else None,
        )
        
        self.is_trained = True
        self.training_date = datetime.now().isoformat()
        
        # Record training metrics
        train_pred = self.predict(X_train)
        train_accuracy = (train_pred == y_train).mean()
        
        results = {
            "train_accuracy": float(train_accuracy),
            "train_samples": len(X_train),
        }
        
        if X_val is not None:
            val_pred = self.predict(X_val)
            val_accuracy = (val_pred == y_val).mean()
            results["val_accuracy"] = float(val_accuracy)
            results["val_samples"] = len(X_val)
        
        # Calculate class distribution
        results["class_distribution"] = {
            "positive": float(y_train.mean()),
            "negative": float(1 - y_train.mean()),
        }
        
        self.training_history = results
        self.metadata["training_date"] = self.training_date
        self.metadata["training_samples"] = len(X_train)
        
        self.logger.info(f"Training complete. Train accuracy: {train_accuracy:.3f}")
        if "val_accuracy" in results:
            self.logger.info(f"Validation accuracy: {results['val_accuracy']:.3f}")
        
        return results
    
    def predict(self, X: np.ndarray) -> np.ndarray:
        """
        Predict whether both teams will score.
        
        Args:
            X: Input features
            
        Returns:
            Binary predictions (0 or 1)
        """
        if not self.is_trained:
            raise ValueError("Model must be trained before prediction")
        
        return self.model.predict(X)
    
    def predict_proba(self, X: np.ndarray) -> np.ndarray:
        """
        Predict probability of both teams scoring.
        
        Args:
            X: Input features
            
        Returns:
            Probability of positive class (BTTS = Yes)
        """
        if not self.is_trained:
            raise ValueError("Model must be trained before prediction")
        
        proba = self.model.predict_proba(X)
        return proba[:, 1]
    
    def predict_with_confidence(
        self,
        X: np.ndarray,
        high_threshold: float = 0.70,
        medium_threshold: float = 0.60,
    ) -> List[Dict[str, Any]]:
        """
        Predict with confidence levels.
        
        Args:
            X: Input features
            high_threshold: Probability threshold for HIGH confidence
            medium_threshold: Probability threshold for MEDIUM confidence
            
        Returns:
            List of dicts with prediction, probability, and confidence
        """
        probas = self.predict_proba(X)
        
        results = []
        for proba in probas:
            if proba >= high_threshold or proba <= (1 - high_threshold):
                confidence = "HIGH"
            elif proba >= medium_threshold or proba <= (1 - medium_threshold):
                confidence = "MEDIUM"
            else:
                confidence = "LOW"
            
            results.append({
                "prediction": "YES" if proba > 0.5 else "NO",
                "probability": float(proba),
                "confidence": confidence,
            })
        
        return results
