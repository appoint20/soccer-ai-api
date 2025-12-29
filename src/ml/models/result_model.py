"""
Match result prediction model using XGBoost multi-class classifier.

Predicts Home Win (H), Draw (D), or Away Win (A).
"""
from typing import Any, Dict, List, Optional
import numpy as np
from datetime import datetime

try:
    import xgboost as xgb
    HAS_XGBOOST = True
except ImportError:
    HAS_XGBOOST = False

from sklearn.preprocessing import LabelEncoder

from src.ml.models.base_model import BaseModel


class ResultModel(BaseModel):
    """
    XGBoost model for predicting match results (H/D/A).
    
    Target: Multi-class (0=Home, 1=Draw, 2=Away)
    Target accuracy: 55% (realistic: 50-55%)
    """
    
    RESULT_CLASSES = ["H", "D", "A"]
    
    def __init__(self, version: str = "1.0.0"):
        """Initialize Result model."""
        super().__init__(
            model_name="result",
            model_type="xgboost",
            version=version,
        )
        
        if not HAS_XGBOOST:
            raise ImportError("xgboost is required. Install with: pip install xgboost")
        
        self.label_encoder = LabelEncoder()
        self.label_encoder.fit(self.RESULT_CLASSES)
    
    def get_default_hyperparameters(self) -> Dict[str, Any]:
        """Get default XGBoost hyperparameters for result prediction."""
        return {
            "n_estimators": 200,
            "max_depth": 6,
            "learning_rate": 0.1,
            "min_child_weight": 5,
            "subsample": 0.8,
            "colsample_bytree": 0.8,
            "gamma": 0.1,
            "reg_alpha": 0.1,
            "reg_lambda": 1.0,
            "objective": "multi:softprob",
            "num_class": 3,
            "eval_metric": "mlogloss",
            "use_label_encoder": False,
            "random_state": 42,
            "n_jobs": -1,
        }
    
    def get_hyperparameter_space(self) -> Dict[str, List]:
        """Get hyperparameter search space."""
        return {
            "max_depth": [4, 6, 8, 10],
            "learning_rate": [0.01, 0.05, 0.1],
            "n_estimators": [100, 200, 300, 500],
            "min_child_weight": [3, 5, 7],
            "subsample": [0.7, 0.8, 0.9],
            "colsample_bytree": [0.7, 0.8, 0.9],
            "gamma": [0, 0.1, 0.2],
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
        params.pop("use_label_encoder", None)
        
        self.model = xgb.XGBClassifier(**params)
        self.metadata["hyperparameters"] = params
        
        self.logger.info(f"Built XGBoost multi-class model with params: {params}")
    
    def encode_labels(self, y: np.ndarray) -> np.ndarray:
        """
        Encode string labels (H, D, A) to integers.
        
        Args:
            y: Array of string labels
            
        Returns:
            Array of integer labels
        """
        # Handle already encoded labels
        if np.issubdtype(y.dtype, np.integer):
            return y
        return self.label_encoder.transform(y)
    
    def decode_labels(self, y: np.ndarray) -> np.ndarray:
        """
        Decode integer labels back to strings.
        
        Args:
            y: Array of integer labels
            
        Returns:
            Array of string labels
        """
        return self.label_encoder.inverse_transform(y)
    
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
        Train the Result model.
        
        Args:
            X_train: Training features
            y_train: Training targets (H, D, A or 0, 1, 2)
            X_val: Validation features
            y_val: Validation targets
            early_stopping_rounds: Early stopping patience
            verbose: Whether to print training progress
            
        Returns:
            Dict with training metrics
        """
        if self.model is None:
            self.build_model()
        
        # Encode labels if needed
        y_train_encoded = self.encode_labels(y_train)
        y_val_encoded = self.encode_labels(y_val) if y_val is not None else None
        
        self.logger.info(f"Training on {len(X_train)} samples...")
        
        # Prepare eval set
        eval_set = [(X_train, y_train_encoded)]
        if X_val is not None and y_val_encoded is not None:
            eval_set.append((X_val, y_val_encoded))
        
        # Train model
        self.model.fit(
            X_train, y_train_encoded,
            eval_set=eval_set,
            verbose=verbose,
        )
        
        self.is_trained = True
        self.training_date = datetime.now().isoformat()
        
        # Record training metrics
        train_pred = self.model.predict(X_train)
        train_accuracy = (train_pred == y_train_encoded).mean()
        
        results = {
            "train_accuracy": float(train_accuracy),
            "train_samples": len(X_train),
        }
        
        if X_val is not None and y_val_encoded is not None:
            val_pred = self.model.predict(X_val)
            val_accuracy = (val_pred == y_val_encoded).mean()
            results["val_accuracy"] = float(val_accuracy)
            results["val_samples"] = len(X_val)
        
        # Calculate class distribution
        unique, counts = np.unique(y_train_encoded, return_counts=True)
        class_dist = {self.RESULT_CLASSES[i]: float(counts[j] / len(y_train_encoded)) 
                      for j, i in enumerate(unique)}
        results["class_distribution"] = class_dist
        
        self.training_history = results
        self.metadata["training_date"] = self.training_date
        self.metadata["training_samples"] = len(X_train)
        
        self.logger.info(f"Training complete. Train accuracy: {train_accuracy:.3f}")
        if "val_accuracy" in results:
            self.logger.info(f"Validation accuracy: {results['val_accuracy']:.3f}")
        
        return results
    
    def predict(self, X: np.ndarray) -> np.ndarray:
        """
        Predict match results.
        
        Args:
            X: Input features
            
        Returns:
            Predicted results as strings (H, D, A)
        """
        if not self.is_trained:
            raise ValueError("Model must be trained before prediction")
        
        encoded_preds = self.model.predict(X)
        return self.decode_labels(encoded_preds)
    
    def predict_proba(self, X: np.ndarray) -> np.ndarray:
        """
        Predict probabilities for each result.
        
        Args:
            X: Input features
            
        Returns:
            Array of shape (n_samples, 3) with [home_prob, draw_prob, away_prob]
        """
        if not self.is_trained:
            raise ValueError("Model must be trained before prediction")
        
        return self.model.predict_proba(X)
    
    def predict_with_confidence(
        self,
        X: np.ndarray,
        high_threshold: float = 0.50,
        medium_threshold: float = 0.40,
    ) -> List[Dict[str, Any]]:
        """
        Predict with confidence levels.
        
        Args:
            X: Input features
            high_threshold: Max probability threshold for HIGH confidence
            medium_threshold: Max probability threshold for MEDIUM confidence
            
        Returns:
            List of dicts with prediction, probabilities, and confidence
        """
        probas = self.predict_proba(X)
        
        results = []
        for proba in probas:
            max_prob = proba.max()
            predicted_class = self.RESULT_CLASSES[proba.argmax()]
            
            if max_prob >= high_threshold:
                confidence = "HIGH"
            elif max_prob >= medium_threshold:
                confidence = "MEDIUM"
            else:
                confidence = "LOW"
            
            results.append({
                "prediction": predicted_class,
                "probabilities": {
                    "home_win": float(proba[0]),
                    "draw": float(proba[1]),
                    "away_win": float(proba[2]),
                },
                "confidence": confidence,
            })
        
        return results
