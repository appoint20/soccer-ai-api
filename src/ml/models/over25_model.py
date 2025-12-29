"""
Over 2.5 Goals prediction model using XGBoost.

Binary classifier predicting if a match will have more than 2.5 total goals.
"""
from typing import Any, Dict, List, Optional
import numpy as np
from datetime import datetime

try:
    import xgboost as xgb
    HAS_XGBOOST = True
except ImportError:
    HAS_XGBOOST = False

from sklearn.preprocessing import StandardScaler

from src.ml.models.base_model import BaseModel


class Over25Model(BaseModel):
    """
    XGBoost model for predicting Over 2.5 goals.
    
    Target: Binary (1 = over 2.5, 0 = under 2.5)
    Target accuracy: 75%
    """
    
    def __init__(self, version: str = "1.0.0"):
        """Initialize Over 2.5 model."""
        super().__init__(
            model_name="over25",
            model_type="xgboost",
            version=version,
        )
        
        if not HAS_XGBOOST:
            raise ImportError("xgboost is required. Install with: pip install xgboost")
        
        self.scaler = StandardScaler()
        self._use_scaler = False  # XGBoost doesn't require scaling
    
    def get_default_hyperparameters(self) -> Dict[str, Any]:
        """Get default XGBoost hyperparameters for Over 2.5 prediction."""
        return {
            "n_estimators": 200,
            "max_depth": 5,
            "learning_rate": 0.1,
            "min_child_weight": 3,
            "subsample": 0.8,
            "colsample_bytree": 0.8,
            "gamma": 0.1,
            "reg_alpha": 0.1,
            "reg_lambda": 1.0,
            "scale_pos_weight": 1.0,  # Adjust for class imbalance
            "objective": "binary:logistic",
            "eval_metric": "logloss",
            "use_label_encoder": False,
            "random_state": 42,
            "n_jobs": -1,
        }
    
    def get_hyperparameter_space(self) -> Dict[str, List]:
        """Get hyperparameter search space."""
        return {
            "max_depth": [3, 5, 7, 10],
            "learning_rate": [0.01, 0.05, 0.1, 0.2],
            "n_estimators": [100, 200, 300, 500],
            "min_child_weight": [1, 3, 5],
            "subsample": [0.7, 0.8, 0.9, 1.0],
            "colsample_bytree": [0.7, 0.8, 0.9, 1.0],
            "gamma": [0, 0.1, 0.2],
            "scale_pos_weight": [0.8, 1.0, 1.2],
        }
    
    def build_model(self, hyperparameters: Optional[Dict] = None):
        """
        Build XGBoost classifier with given hyperparameters.
        
        Args:
            hyperparameters: Optional custom hyperparameters
        """
        params = self.get_default_hyperparameters()
        if hyperparameters:
            params.update(hyperparameters)
        
        # Remove non-XGBoost params
        use_label_encoder = params.pop("use_label_encoder", False)
        
        self.model = xgb.XGBClassifier(**params)
        self.metadata["hyperparameters"] = params
        
        self.logger.info(f"Built XGBoost model with params: {params}")
    
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
        Train the Over 2.5 model.
        
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
        
        # Prepare eval set
        eval_set = [(X_train, y_train)]
        if X_val is not None and y_val is not None:
            eval_set.append((X_val, y_val))
        
        # Train model
        self.model.fit(
            X_train, y_train,
            eval_set=eval_set,
            verbose=verbose,
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
        Predict whether matches will have over 2.5 goals.
        
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
        Predict probability of over 2.5 goals.
        
        Args:
            X: Input features
            
        Returns:
            Probability of positive class (over 2.5)
        """
        if not self.is_trained:
            raise ValueError("Model must be trained before prediction")
        
        # Returns [prob_0, prob_1], we want prob_1
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
            # Distance from 0.5 determines confidence
            certainty = abs(proba - 0.5) * 2  # Scale to 0-1
            
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
