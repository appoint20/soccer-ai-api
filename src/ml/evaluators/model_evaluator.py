"""
Model evaluator for comprehensive performance analysis.

Calculates accuracy, precision, recall, F1, AUC-ROC, and generates reports.
"""
from typing import Any, Dict, List, Optional, Union
import json
from pathlib import Path
from datetime import datetime
import numpy as np

from sklearn.metrics import (
    accuracy_score,
    precision_score,
    recall_score,
    f1_score,
    roc_auc_score,
    confusion_matrix,
    classification_report,
)

from src.ml.models.base_model import BaseModel
from src.utils.logger import get_logger


class ModelEvaluator:
    """
    Comprehensive model performance evaluation.
    
    Handles:
    - Binary classification metrics (Over 2.5, BTTS)
    - Multi-class classification metrics (Result)
    - Confidence-stratified evaluation
    - Per-league evaluation
    - Comparison reports
    """
    
    def __init__(self):
        """Initialize evaluator."""
        self.logger = get_logger("ModelEvaluator")
        self.evaluation_results: Dict[str, Any] = {}
    
    def evaluate_binary_classifier(
        self,
        y_true: np.ndarray,
        y_pred: np.ndarray,
        y_proba: Optional[np.ndarray] = None,
    ) -> Dict[str, Any]:
        """
        Comprehensive evaluation for binary classification.
        
        Args:
            y_true: True labels
            y_pred: Predicted labels
            y_proba: Predicted probabilities (optional)
            
        Returns:
            Dict with all metrics
        """
        results = {
            "accuracy": float(accuracy_score(y_true, y_pred)),
            "precision": float(precision_score(y_true, y_pred, zero_division=0)),
            "recall": float(recall_score(y_true, y_pred, zero_division=0)),
            "f1_score": float(f1_score(y_true, y_pred, zero_division=0)),
            "samples": len(y_true),
        }
        
        # Confusion matrix
        cm = confusion_matrix(y_true, y_pred)
        results["confusion_matrix"] = cm.tolist()
        results["true_negatives"] = int(cm[0, 0])
        results["false_positives"] = int(cm[0, 1])
        results["false_negatives"] = int(cm[1, 0])
        results["true_positives"] = int(cm[1, 1])
        
        # AUC-ROC if probabilities available
        if y_proba is not None:
            try:
                results["auc_roc"] = float(roc_auc_score(y_true, y_proba))
            except ValueError:
                results["auc_roc"] = None
        
        # Class distribution
        results["class_distribution"] = {
            "positive": float(y_true.mean()),
            "negative": float(1 - y_true.mean()),
        }
        
        return results
    
    def evaluate_multiclass_classifier(
        self,
        y_true: np.ndarray,
        y_pred: np.ndarray,
        y_proba: Optional[np.ndarray] = None,
        class_labels: List[str] = None,
    ) -> Dict[str, Any]:
        """
        Evaluation for multi-class classification.
        
        Args:
            y_true: True labels (encoded)
            y_pred: Predicted labels (encoded)
            y_proba: Predicted probabilities (optional)
            class_labels: List of class names
            
        Returns:
            Dict with all metrics
        """
        if class_labels is None:
            class_labels = ["H", "D", "A"]
        
        results = {
            "accuracy": float(accuracy_score(y_true, y_pred)),
            "samples": len(y_true),
        }
        
        # Per-class metrics
        precision = precision_score(y_true, y_pred, average=None, zero_division=0)
        recall = recall_score(y_true, y_pred, average=None, zero_division=0)
        f1 = f1_score(y_true, y_pred, average=None, zero_division=0)
        
        results["per_class"] = {}
        for i, label in enumerate(class_labels):
            if i < len(precision):
                results["per_class"][label] = {
                    "precision": float(precision[i]),
                    "recall": float(recall[i]),
                    "f1_score": float(f1[i]),
                }
        
        # Macro and weighted averages
        results["macro_f1"] = float(f1_score(y_true, y_pred, average="macro", zero_division=0))
        results["weighted_f1"] = float(f1_score(y_true, y_pred, average="weighted", zero_division=0))
        
        # Confusion matrix
        cm = confusion_matrix(y_true, y_pred)
        results["confusion_matrix"] = cm.tolist()
        
        # Top-2 accuracy (correct if true class in top 2 predictions)
        if y_proba is not None:
            top2_correct = 0
            for i, proba in enumerate(y_proba):
                top2_classes = np.argsort(proba)[-2:]
                if y_true[i] in top2_classes:
                    top2_correct += 1
            results["top2_accuracy"] = float(top2_correct / len(y_true))
        
        return results
    
    def evaluate_by_confidence(
        self,
        y_true: np.ndarray,
        y_pred: np.ndarray,
        y_proba: np.ndarray,
        thresholds: Dict[str, float] = None,
    ) -> Dict[str, Any]:
        """
        Evaluate accuracy stratified by confidence level.
        
        Args:
            y_true: True labels
            y_pred: Predicted labels
            y_proba: Predicted probabilities
            thresholds: Confidence thresholds
            
        Returns:
            Dict with accuracy per confidence level
        """
        if thresholds is None:
            thresholds = {"high": 0.70, "medium": 0.60}
        
        results = {}
        
        # Calculate confidence (distance from 0.5)
        confidence = np.abs(y_proba - 0.5) * 2
        
        # High confidence
        high_mask = confidence >= (thresholds["high"] - 0.5) * 2
        if high_mask.sum() > 0:
            results["high"] = {
                "accuracy": float((y_pred[high_mask] == y_true[high_mask]).mean()),
                "count": int(high_mask.sum()),
                "percentage": float(high_mask.mean()),
            }
        
        # Medium confidence
        medium_mask = (confidence >= (thresholds["medium"] - 0.5) * 2) & ~high_mask
        if medium_mask.sum() > 0:
            results["medium"] = {
                "accuracy": float((y_pred[medium_mask] == y_true[medium_mask]).mean()),
                "count": int(medium_mask.sum()),
                "percentage": float(medium_mask.mean()),
            }
        
        # Low confidence
        low_mask = ~high_mask & ~medium_mask
        if low_mask.sum() > 0:
            results["low"] = {
                "accuracy": float((y_pred[low_mask] == y_true[low_mask]).mean()),
                "count": int(low_mask.sum()),
                "percentage": float(low_mask.mean()),
            }
        
        return results
    
    def evaluate_by_league(
        self,
        y_true: np.ndarray,
        y_pred: np.ndarray,
        leagues: np.ndarray,
    ) -> Dict[str, Any]:
        """
        Calculate accuracy per league.
        
        Args:
            y_true: True labels
            y_pred: Predicted labels
            leagues: Array of league codes
            
        Returns:
            Dict with accuracy per league
        """
        results = {}
        
        for league in np.unique(leagues):
            mask = leagues == league
            if mask.sum() > 0:
                results[league] = {
                    "accuracy": float((y_pred[mask] == y_true[mask]).mean()),
                    "count": int(mask.sum()),
                }
        
        return results
    
    def evaluate_model(
        self,
        model: BaseModel,
        X_test: np.ndarray,
        y_test: np.ndarray,
        model_type: str = "binary",
    ) -> Dict[str, Any]:
        """
        Full evaluation of a trained model.
        
        Args:
            model: Trained model
            X_test: Test features
            y_test: Test targets
            model_type: 'binary' or 'multiclass'
            
        Returns:
            Complete evaluation report
        """
        self.logger.info(f"Evaluating {model.model_name} model...")
        
        y_pred = model.predict(X_test)
        y_proba = model.predict_proba(X_test)
        
        if model_type == "binary":
            results = self.evaluate_binary_classifier(y_test, y_pred, y_proba)
            
            # Add confidence-stratified evaluation
            results["by_confidence"] = self.evaluate_by_confidence(
                y_test, y_pred, y_proba
            )
        else:
            results = self.evaluate_multiclass_classifier(y_test, y_pred, y_proba)
        
        # Add feature importance
        results["feature_importance"] = model.get_feature_importance()
        
        # Add model info
        results["model_info"] = model.get_info()
        
        return results
    
    def generate_report(
        self,
        evaluations: Dict[str, Dict[str, Any]],
        output_path: Union[str, Path] = None,
    ) -> Dict[str, Any]:
        """
        Generate comprehensive evaluation report.
        
        Args:
            evaluations: Dict mapping model names to evaluation results
            output_path: Optional path to save report
            
        Returns:
            Report dict
        """
        report = {
            "timestamp": datetime.now().isoformat(),
            "summary": {},
            "details": evaluations,
        }
        
        # Summary
        for model_name, eval_results in evaluations.items():
            report["summary"][model_name] = {
                "accuracy": eval_results.get("accuracy"),
                "f1_score": eval_results.get("f1_score", eval_results.get("macro_f1")),
                "samples": eval_results.get("samples"),
            }
        
        # Save if path provided
        if output_path:
            output_path = Path(output_path)
            output_path.parent.mkdir(parents=True, exist_ok=True)
            
            with open(output_path, "w") as f:
                json.dump(report, f, indent=2, default=str)
            
            self.logger.info(f"Saved evaluation report to {output_path}")
        
        return report
    
    def compare_models(
        self,
        models: Dict[str, BaseModel],
        X_test: np.ndarray,
        y_test: np.ndarray,
    ) -> Dict[str, Any]:
        """
        Compare multiple models on same test set.
        
        Args:
            models: Dict mapping model names to model instances
            X_test: Test features
            y_test: Test targets
            
        Returns:
            Comparison report
        """
        comparison = {
            "models": [],
            "best_model": None,
            "best_accuracy": 0.0,
        }
        
        for name, model in models.items():
            results = model.validate(X_test, y_test)
            
            comparison["models"].append({
                "name": name,
                "accuracy": results["accuracy"],
                "type": model.model_type,
            })
            
            if results["accuracy"] > comparison["best_accuracy"]:
                comparison["best_accuracy"] = results["accuracy"]
                comparison["best_model"] = name
        
        return comparison
