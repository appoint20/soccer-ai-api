from threading import Lock
from datetime import datetime
import time
from typing import Dict, Any, Optional

from src.utils.logger import get_logger
from src.api.schemas import ModelTier
from src.domain.services.feature_engineering_service import FeatureEngineeringService
from src.data.storage.json_storage import JSONStorage
from src.ml.trainers import ModelTrainer

class ModelRetrainingService:
    """
    Service to handle model retraining with concurrency protection and status tracking.
    Shared instance should be used to ensure locking works.
    """
    
    def __init__(self):
        self.logger = get_logger("ModelRetrainingService")
        self._lock = Lock()
        self._storage = JSONStorage()
        self._status_file = "data/system/retrain_status.json"
        
        # Load persisted status or default
        saved_status = self._storage.load(self._status_file)
        if saved_status:
            self._status = saved_status
        else:
            self._status = {
                "status": "idle",
                "message": "No training run recently",
                "last_run": None,
                "current_tier": None,
                "timestamp": None
            }

    def start_retraining(self, tier: ModelTier):
        """
        Orchestrate the retraining process.
        This method is blocking and IS INTENDED to be run in a background thread/task.
        """
        # Concurrency protection
        if not self._lock.acquire(blocking=False):
            self.logger.warning(f"Retraining already in progress. Skipping request for {tier}")
            return
        
        try:
            self.logger.info(f"Starting retraining for {tier}...")
            self._update_status("running", f"Retraining {tier}...", tier=tier)
            start_time = time.time()
            
            # 1. Load Data
            matches = self._storage.load("data/processed/matches.json")
            if not matches:
                raise ValueError("No matches found for training")
            
            self.logger.info(f"Loaded {len(matches)} matches for training")
            
            # 2. Generate Features
            feature_service = FeatureEngineeringService()
            features = feature_service.generate_training_features(matches)
            
            # 3. Train Models
            trainer = ModelTrainer()
            # tier is Enum, use .value
            tier_val = tier.value
            
            tier_features = trainer.filter_by_tier(features, tier_val)
            results = trainer.train_all_models(tier_features, tier_val)
            
            duration = round(time.time() - start_time, 2)
            message = f"Successfully retrained {tier_val} in {duration}s"
            
            self.logger.info(f"Retraining success: {message}")
            self._update_status(
                "success", 
                message, 
                tier=tier,
                details=results
            )
            
        except Exception as e:
            self.logger.error(f"Retraining failed: {e}")
            self._update_status("failed", str(e), tier=tier)
        finally:
            self._lock.release()

    def get_status(self) -> Dict[str, Any]:
        """Get current training status."""
        return self._status

    def _update_status(self, status: str, message: str, tier: Optional[ModelTier] = None, details: Any = None):
        """Update internal status and persist to disk."""
        self._status = {
            "status": status,
            "message": message,
            "timestamp": datetime.now().isoformat(),
            "tier": tier,
            "details": details
        }
        # Persist to disk
        try:
            self._storage.save(self._status, self._status_file)
        except Exception as e:
            self.logger.error(f"Failed to persist retrain status: {e}")
