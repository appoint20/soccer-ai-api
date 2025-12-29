"""
Models router for model information and retraining.
"""
from datetime import datetime
from typing import Optional

from fastapi import APIRouter, HTTPException, BackgroundTasks

from src.api.schemas import ModelsInfoResponse, ModelInfo
from src.domain.services.prediction_service import PredictionService
from src.utils.logger import get_logger

router = APIRouter()
logger = get_logger("ModelsRouter")


@router.get("/info", response_model=ModelsInfoResponse)
async def get_models_info():
    """
    Get information about loaded models.
    
    Returns:
    - Model information including version, training date, and status
    """
    service = PredictionService()
    
    try:
        service.load_models()
    except Exception as e:
        logger.warning(f"Could not load models: {e}")
    
    info = service.get_model_info()
    
    models_dict = {}
    for name, model_info in info.get("models", {}).items():
        models_dict[name] = ModelInfo(
            model_name=model_info.get("model_name", name),
            model_type=model_info.get("model_type", "unknown"),
            version=model_info.get("version", "1.0.0"),
            is_trained=model_info.get("is_trained", False),
            training_date=model_info.get("training_date"),
            n_features=model_info.get("n_features", 0),
        )
    
    return ModelsInfoResponse(
        tier=info.get("tier", "tier1"),
        models_loaded=info.get("models_loaded", False),
        models=models_dict,
    )


@router.post("/retrain")
async def retrain_models(
    background_tasks: BackgroundTasks,
    tier: str = "tier1",
):
    """
    Trigger model retraining.
    
    Starts retraining in the background.
    
    Parameters:
    - **tier**: Model tier to retrain (tier1, tier2, tier3)
    
    Returns:
    - Confirmation that retraining has started
    """
    logger.info(f"Retraining requested for tier: {tier}")
    
    # Add retraining task to background
    background_tasks.add_task(_retrain_models, tier)
    
    return {
        "status": "started",
        "tier": tier,
        "message": "Model retraining started in background",
        "timestamp": datetime.now().isoformat(),
    }


async def _retrain_models(tier: str):
    """Background task to retrain models."""
    logger.info(f"Starting background retraining for {tier}...")
    
    try:
        from src.ml.trainers import ModelTrainer
        from src.data.storage.json_storage import JSONStorage
        from src.domain.services.feature_engineering_service import FeatureEngineeringService
        
        # Load matches
        storage = JSONStorage()
        matches = storage.load("data/processed/matches.json") or []
        
        if not matches:
            logger.error("No matches found for training")
            return
        
        # Generate features
        feature_service = FeatureEngineeringService()
        features = feature_service.generate_training_features(matches)
        
        # Train models
        trainer = ModelTrainer()
        tier_features = trainer.filter_by_tier(features, tier)
        results = trainer.train_all_models(tier_features, tier)
        
        logger.info(f"Retraining complete: {results}")
        
    except Exception as e:
        logger.error(f"Retraining failed: {e}")
