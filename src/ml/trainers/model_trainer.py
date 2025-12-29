"""
Model trainer for orchestrating the ML training pipeline.

Handles data preparation, training, hyperparameter tuning, and evaluation.
"""
from datetime import datetime
from pathlib import Path
from typing import Any, Dict, List, Optional, Tuple, Union
import json
import numpy as np

from sklearn.model_selection import train_test_split, StratifiedKFold

from src.ml.models.base_model import BaseModel
from src.ml.models.over25_model import Over25Model
from src.ml.models.btts_model import BTTSModel
from src.ml.models.result_model import ResultModel
from src.utils.logger import get_logger


class ModelTrainer:
    """
    Main training orchestrator for all ML models.
    
    Handles:
    - Data preparation and splitting
    - Model training
    - Hyperparameter tuning
    - Cross-validation
    - Model saving
    """
    
    # League tiers for separate model training
    TIER1_LEAGUES = ["E0", "D1", "I1", "SP1", "F1"]  # Top leagues
    TIER2_LEAGUES = ["E1", "I2", "F2"]  # Second divisions
    TIER3_LEAGUES = ["E2", "E3"]  # Lower leagues
    
    def __init__(
        self,
        models_dir: Union[str, Path] = "models",
        random_state: int = 42,
    ):
        """
        Initialize model trainer.
        
        Args:
            models_dir: Directory to save trained models
            random_state: Random seed for reproducibility
        """
        self.models_dir = Path(models_dir)
        self.random_state = random_state
        
        self.logger = get_logger("ModelTrainer")
        
        self.models: Dict[str, BaseModel] = {}
        self.training_results: Dict[str, Any] = {}
    
    def prepare_data(
        self,
        features: List[Dict[str, Any]],
        target: str = "over25",
        test_size: float = 0.2,
        val_size: float = 0.2,
    ) -> Tuple[np.ndarray, np.ndarray, np.ndarray, np.ndarray, np.ndarray, np.ndarray, List[str]]:
        """
        Prepare data for training.
        
        Args:
            features: List of feature dicts from FeatureEngineeringService
            target: Target variable ('over25', 'btts', or 'result')
            test_size: Fraction for test set
            val_size: Fraction for validation set
            
        Returns:
            Tuple of (X_train, X_val, X_test, y_train, y_val, y_test, feature_names)
        """
        self.logger.info(f"Preparing data for {target} prediction...")
        
        # Extract features and targets
        X_list = []
        y_list = []
        
        for feat in features:
            # Skip if no targets
            if not feat.get("targets"):
                continue
            
            # Get target value
            targets = feat["targets"]
            if target == "over25":
                y_val = targets.get("over25")
            elif target == "btts":
                y_val = targets.get("btts")
            elif target == "result":
                y_val = targets.get("result")
            else:
                raise ValueError(f"Unknown target: {target}")
            
            if y_val is None:
                continue
            
            # Flatten features
            flat = self._flatten_features(feat)
            X_list.append(flat)
            y_list.append(1 if y_val == True else (0 if y_val == False else y_val))
        
        if not X_list:
            raise ValueError("No valid samples found in features")
        
        # Get feature names
        feature_names = list(X_list[0].keys())
        
        # Convert to numpy arrays
        X = np.array([[f.get(name, 0.0) for name in feature_names] for f in X_list])
        y = np.array(y_list)
        
        # Handle NaN
        X = np.nan_to_num(X, nan=0.0)
        
        self.logger.info(f"Prepared {len(X)} samples with {len(feature_names)} features")
        
        # Split: first train+val vs test
        X_trainval, X_test, y_trainval, y_test = train_test_split(
            X, y, test_size=test_size, random_state=self.random_state, stratify=y
        )
        
        # Then train vs val
        val_fraction = val_size / (1 - test_size)
        X_train, X_val, y_train, y_val = train_test_split(
            X_trainval, y_trainval, test_size=val_fraction, 
            random_state=self.random_state, stratify=y_trainval
        )
        
        self.logger.info(f"Split: train={len(X_train)}, val={len(X_val)}, test={len(X_test)}")
        
        return X_train, X_val, X_test, y_train, y_val, y_test, feature_names
    
    def train_model(
        self,
        model: BaseModel,
        X_train: np.ndarray,
        y_train: np.ndarray,
        X_val: np.ndarray,
        y_val: np.ndarray,
        feature_names: List[str],
        hyperparameters: Optional[Dict] = None,
    ) -> Dict[str, Any]:
        """
        Train a model with given data.
        
        Args:
            model: Model instance to train
            X_train: Training features
            y_train: Training targets
            X_val: Validation features
            y_val: Validation targets
            feature_names: List of feature names
            hyperparameters: Optional custom hyperparameters
            
        Returns:
            Dict with training results
        """
        self.logger.info(f"Training {model.model_name} model...")
        
        # Build model
        model.build_model(hyperparameters)
        model.set_feature_names(feature_names)
        
        # Train
        results = model.train(X_train, y_train, X_val, y_val)
        
        return results
    
    def train_all_models(
        self,
        features: List[Dict[str, Any]],
        tier: str = "tier1",
    ) -> Dict[str, Dict[str, Any]]:
        """
        Train all three models for a tier.
        
        Args:
            features: List of feature dicts
            tier: League tier ('tier1', 'tier2', 'tier3')
            
        Returns:
            Dict mapping model name to training results
        """
        self.logger.info(f"Training all models for {tier}...")
        
        all_results = {}
        
        # Train Over 2.5 model
        try:
            X_train, X_val, X_test, y_train, y_val, y_test, feat_names = \
                self.prepare_data(features, target="over25")
            
            over25_model = Over25Model()
            results = self.train_model(
                over25_model, X_train, y_train, X_val, y_val, feat_names
            )
            
            # Evaluate on test set
            test_results = over25_model.validate(X_test, y_test)
            results["test_accuracy"] = test_results["accuracy"]
            
            # Save model
            model_path = self.models_dir / tier / "over25" / "model"
            over25_model.save(model_path)
            
            all_results["over25"] = results
            self.models["over25"] = over25_model
            
        except Exception as e:
            self.logger.error(f"Failed to train over25 model: {e}")
            all_results["over25"] = {"error": str(e)}
        
        # Train BTTS model
        try:
            X_train, X_val, X_test, y_train, y_val, y_test, feat_names = \
                self.prepare_data(features, target="btts")
            
            btts_model = BTTSModel()
            results = self.train_model(
                btts_model, X_train, y_train, X_val, y_val, feat_names
            )
            
            # Evaluate on test set
            test_results = btts_model.validate(X_test, y_test)
            results["test_accuracy"] = test_results["accuracy"]
            
            # Save model
            model_path = self.models_dir / tier / "btts" / "model"
            btts_model.save(model_path)
            
            all_results["btts"] = results
            self.models["btts"] = btts_model
            
        except Exception as e:
            self.logger.error(f"Failed to train btts model: {e}")
            all_results["btts"] = {"error": str(e)}
        
        # Train Result model
        try:
            X_train, X_val, X_test, y_train, y_val, y_test, feat_names = \
                self.prepare_data(features, target="result")
            
            result_model = ResultModel()
            results = self.train_model(
                result_model, X_train, y_train, X_val, y_val, feat_names
            )
            
            # Evaluate on test set
            test_results = result_model.validate(X_test, y_test)
            results["test_accuracy"] = test_results["accuracy"]
            
            # Save model
            model_path = self.models_dir / tier / "result" / "model"
            result_model.save(model_path)
            
            all_results["result"] = results
            self.models["result"] = result_model
            
        except Exception as e:
            self.logger.error(f"Failed to train result model: {e}")
            all_results["result"] = {"error": str(e)}
        
        # Save training results
        self.training_results[tier] = all_results
        self._save_training_results(tier, all_results)
        
        return all_results
    
    def cross_validate(
        self,
        model: BaseModel,
        X: np.ndarray,
        y: np.ndarray,
        n_folds: int = 5,
    ) -> Dict[str, Any]:
        """
        Perform k-fold cross-validation.
        
        Args:
            model: Model to validate
            X: Features
            y: Targets
            n_folds: Number of folds
            
        Returns:
            Dict with CV results
        """
        self.logger.info(f"Running {n_folds}-fold cross-validation...")
        
        kfold = StratifiedKFold(n_splits=n_folds, shuffle=True, random_state=self.random_state)
        
        fold_scores = []
        
        for fold, (train_idx, val_idx) in enumerate(kfold.split(X, y)):
            X_train, X_val = X[train_idx], X[val_idx]
            y_train, y_val = y[train_idx], y[val_idx]
            
            # Build fresh model
            model.build_model()
            model.train(X_train, y_train)
            
            # Validate
            results = model.validate(X_val, y_val)
            fold_scores.append(results["accuracy"])
            
            self.logger.info(f"Fold {fold + 1}: {results['accuracy']:.3f}")
        
        return {
            "mean_accuracy": float(np.mean(fold_scores)),
            "std_accuracy": float(np.std(fold_scores)),
            "fold_scores": fold_scores,
        }
    
    def _flatten_features(self, features: Dict[str, Any]) -> Dict[str, float]:
        """Flatten nested features to single-level dict."""
        flat = {}
        
        # Home features
        home = features.get("home_features", {})
        for key, value in home.items():
            if isinstance(value, (int, float)):
                flat[f"home_{key}"] = float(value)
            elif isinstance(value, str):
                # Encode string features
                if value == "improving":
                    flat[f"home_{key}"] = 1.0
                elif value == "declining":
                    flat[f"home_{key}"] = -1.0
                else:
                    flat[f"home_{key}"] = 0.0
        
        # Away features
        away = features.get("away_features", {})
        for key, value in away.items():
            if isinstance(value, (int, float)):
                flat[f"away_{key}"] = float(value)
            elif isinstance(value, str):
                if value == "improving":
                    flat[f"away_{key}"] = 1.0
                elif value == "declining":
                    flat[f"away_{key}"] = -1.0
                else:
                    flat[f"away_{key}"] = 0.0
        
        # H2H features
        h2h = features.get("h2h_features", {})
        for key, value in h2h.items():
            if isinstance(value, (int, float)):
                flat[f"h2h_{key}"] = float(value)
        
        # Referee features
        ref = features.get("referee_features", {})
        for key, value in ref.items():
            if isinstance(value, (int, float)):
                flat[f"ref_{key}"] = float(value)
        
        # Context features
        ctx = features.get("match_context", {})
        for key, value in ctx.items():
            if isinstance(value, (int, float)):
                flat[f"ctx_{key}"] = float(value)
            elif isinstance(value, bool):
                flat[f"ctx_{key}"] = 1.0 if value else 0.0
            elif isinstance(value, str):
                # Encode season
                seasons = {"Winter": 0, "Spring": 1, "Summer": 2, "Autumn": 3}
                flat[f"ctx_{key}"] = float(seasons.get(value, 3))
        
        return flat
    
    def _save_training_results(self, tier: str, results: Dict) -> None:
        """Save training results to JSON."""
        output_dir = Path("data/evaluation")
        output_dir.mkdir(parents=True, exist_ok=True)
        
        output_file = output_dir / f"training_results_{tier}.json"
        
        with open(output_file, "w") as f:
            json.dump({
                "tier": tier,
                "timestamp": datetime.now().isoformat(),
                "results": results,
            }, f, indent=2, default=str)
        
        self.logger.info(f"Saved training results to {output_file}")
    
    def filter_by_tier(
        self,
        features: List[Dict[str, Any]],
        tier: str,
    ) -> List[Dict[str, Any]]:
        """
        Filter features by league tier.
        
        Args:
            features: All features
            tier: Tier to filter ('tier1', 'tier2', 'tier3')
            
        Returns:
            Filtered features
        """
        if tier == "tier1":
            leagues = self.TIER1_LEAGUES
        elif tier == "tier2":
            leagues = self.TIER2_LEAGUES
        elif tier == "tier3":
            leagues = self.TIER3_LEAGUES
        else:
            return features
        
        return [f for f in features if f.get("league") in leagues]
