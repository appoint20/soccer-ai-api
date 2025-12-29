"""
Abstract base class for all machine learning models.

Provides consistent interface for training, prediction, saving/loading,
and feature importance extraction.
"""
from abc import ABC, abstractmethod
from datetime import datetime
from pathlib import Path
from typing import Any, Dict, List, Optional, Tuple, Union
import json
import joblib
import numpy as np

from src.utils.logger import get_logger


class BaseModel(ABC):
    """
    Abstract base class for all ML models.
    
    All prediction models (Over25, BTTS, Result) inherit from this class
    to ensure consistent interface and functionality.
    """
    
    def __init__(
        self,
        model_name: str,
        model_type: str,
        version: str = "1.0.0",
    ):
        """
        Initialize base model.
        
        Args:
            model_name: Name of the model (e.g., "over25", "btts", "result")
            model_type: Algorithm type (e.g., "xgboost", "lightgbm")
            version: Model version string
        """
        self.model_name = model_name
        self.model_type = model_type
        self.version = version
        
        self.model = None
        self.is_trained = False
        self.feature_names: List[str] = []
        self.scaler = None
        self.training_date: Optional[str] = None
        
        self.metadata: Dict[str, Any] = {
            "model_name": model_name,
            "model_type": model_type,
            "version": version,
            "created_at": datetime.now().isoformat(),
        }
        
        self.training_history: Dict[str, Any] = {}
        self.logger = get_logger(f"{self.__class__.__name__}")
    
    @abstractmethod
    def build_model(self, hyperparameters: Optional[Dict] = None):
        """
        Build the underlying ML model with given hyperparameters.
        
        Args:
            hyperparameters: Dict of hyperparameters for the model
        """
        pass
    
    @abstractmethod
    def train(
        self,
        X_train: np.ndarray,
        y_train: np.ndarray,
        X_val: Optional[np.ndarray] = None,
        y_val: Optional[np.ndarray] = None,
        **kwargs,
    ) -> Dict[str, Any]:
        """
        Train the model on training data.
        
        Args:
            X_train: Training features
            y_train: Training targets
            X_val: Optional validation features
            y_val: Optional validation targets
            **kwargs: Additional training arguments
            
        Returns:
            Dict with training metrics and history
        """
        pass
    
    @abstractmethod
    def predict(self, X: np.ndarray) -> np.ndarray:
        """
        Make predictions on input features.
        
        Args:
            X: Input features
            
        Returns:
            Predicted classes/values
        """
        pass
    
    @abstractmethod
    def predict_proba(self, X: np.ndarray) -> np.ndarray:
        """
        Predict probabilities for each class.
        
        Args:
            X: Input features
            
        Returns:
            Predicted probabilities (shape depends on model type)
        """
        pass
    
    def get_default_hyperparameters(self) -> Dict[str, Any]:
        """
        Get default hyperparameters for this model.
        
        Returns:
            Dict of default hyperparameter values
        """
        return {}
    
    def get_hyperparameter_space(self) -> Dict[str, List]:
        """
        Get hyperparameter search space for tuning.
        
        Returns:
            Dict mapping param names to lists of values to try
        """
        return {}
    
    def get_feature_importance(self) -> Dict[str, float]:
        """
        Get feature importance scores.
        
        Returns:
            Dict mapping feature names to importance scores
        """
        if self.model is None or not self.is_trained:
            return {}
        
        try:
            if hasattr(self.model, "feature_importances_"):
                importances = self.model.feature_importances_
                return dict(zip(self.feature_names, importances.tolist()))
        except Exception as e:
            self.logger.warning(f"Could not get feature importance: {e}")
        
        return {}
    
    def validate(
        self,
        X_test: np.ndarray,
        y_test: np.ndarray,
    ) -> Dict[str, float]:
        """
        Validate model on test data.
        
        Args:
            X_test: Test features
            y_test: Test targets
            
        Returns:
            Dict with validation metrics
        """
        if not self.is_trained:
            raise ValueError("Model must be trained before validation")
        
        y_pred = self.predict(X_test)
        y_proba = self.predict_proba(X_test)
        
        # Calculate basic metrics
        accuracy = (y_pred == y_test).mean()
        
        return {
            "accuracy": float(accuracy),
            "samples": len(y_test),
        }
    
    def save(self, filepath: Union[str, Path]) -> Path:
        """
        Save model to disk.
        
        Args:
            filepath: Path to save model (without extension)
            
        Returns:
            Path where model was saved
        """
        filepath = Path(filepath)
        filepath.parent.mkdir(parents=True, exist_ok=True)
        
        # Save model file
        model_path = filepath.with_suffix(".pkl")
        
        save_data = {
            "model": self.model,
            "scaler": self.scaler,
            "feature_names": self.feature_names,
            "metadata": self.metadata,
            "training_history": self.training_history,
            "is_trained": self.is_trained,
            "model_name": self.model_name,
            "model_type": self.model_type,
            "version": self.version,
            "training_date": self.training_date,
        }
        
        joblib.dump(save_data, model_path)
        
        # Save metadata as JSON for easy inspection
        metadata_path = filepath.with_suffix(".json")
        with open(metadata_path, "w") as f:
            json.dump({
                "model_name": self.model_name,
                "model_type": self.model_type,
                "version": self.version,
                "training_date": self.training_date,
                "is_trained": self.is_trained,
                "n_features": len(self.feature_names),
                "metadata": self.metadata,
            }, f, indent=2, default=str)
        
        self.logger.info(f"Model saved to {model_path}")
        return model_path
    
    def load(self, filepath: Union[str, Path]) -> "BaseModel":
        """
        Load model from disk.
        
        Args:
            filepath: Path to load model from
            
        Returns:
            Self for chaining
        """
        filepath = Path(filepath)
        if not filepath.suffix:
            filepath = filepath.with_suffix(".pkl")
        
        if not filepath.exists():
            raise FileNotFoundError(f"Model file not found: {filepath}")
        
        save_data = joblib.load(filepath)
        
        self.model = save_data["model"]
        self.scaler = save_data.get("scaler")
        self.feature_names = save_data.get("feature_names", [])
        self.metadata = save_data.get("metadata", {})
        self.training_history = save_data.get("training_history", {})
        self.is_trained = save_data.get("is_trained", False)
        self.model_name = save_data.get("model_name", self.model_name)
        self.model_type = save_data.get("model_type", self.model_type)
        self.version = save_data.get("version", self.version)
        self.training_date = save_data.get("training_date")
        
        self.logger.info(f"Model loaded from {filepath}")
        return self
    
    def set_feature_names(self, feature_names: List[str]) -> None:
        """Set the feature names used by this model."""
        self.feature_names = list(feature_names)
        self.metadata["n_features"] = len(feature_names)
    
    def update_metadata(self, **kwargs) -> None:
        """Update model metadata."""
        self.metadata.update(kwargs)
    
    def get_info(self) -> Dict[str, Any]:
        """
        Get model information summary.
        
        Returns:
            Dict with model info
        """
        return {
            "model_name": self.model_name,
            "model_type": self.model_type,
            "version": self.version,
            "is_trained": self.is_trained,
            "training_date": self.training_date,
            "n_features": len(self.feature_names),
            "metadata": self.metadata,
        }
    
    def __repr__(self) -> str:
        trained_str = "trained" if self.is_trained else "untrained"
        return f"{self.__class__.__name__}({self.model_name}, {self.model_type}, {trained_str})"
