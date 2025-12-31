"""
Models router for model information and retraining.
"""
from datetime import datetime
from fastapi import APIRouter, BackgroundTasks, Depends, HTTPException

from src.api.schemas import ModelsInfoResponse, ModelInfo, ModelTier
from src.domain.services.prediction_service import PredictionService
from src.domain.services.model_retraining_service import ModelRetrainingService
from src.api.dependencies import get_prediction_service, get_retraining_service
from src.utils.logger import get_logger

router = APIRouter()
logger = get_logger("ModelsRouter")


@router.get("/info", response_model=ModelsInfoResponse)
async def get_models_info(
    service: PredictionService = Depends(get_prediction_service),
):
    """
    Get information about loaded models.
    
    Returns:
    - Model information including version, training date, and status
    """
    # Service is already initialized and models loaded at startup
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
    tier: ModelTier = ModelTier.TIER1,
    service: ModelRetrainingService = Depends(get_retraining_service),
):
    """
    Trigger model retraining.
    
    Starts retraining in the background. Uses concurrency lock.
    
    Parameters:
    - **tier**: Model tier to retrain (tier1, tier2, tier3)
    
    Returns:
    - Confirmation that retraining has started
    """
    logger.info(f"Retraining requested for tier: {tier}")
    
    # Check lock status before submitting background task to provide immediate feedback
    # Note: There's a tiny race condition window here between check and execution, 
    # but the service handles the lock safely anyway.
    status = service.get_status()
    if status.get("status") == "running":
         raise HTTPException(status_code=409, detail=f"Retraining already in progress for {status.get('tier')}")

    # Add retraining task to background
    # We pass the method itself. FastAPI will run it in a threadpool since it's a synchronous method.
    background_tasks.add_task(service.start_retraining, tier)
    
    return {
        "status": "started",
        "tier": tier,
        "message": "Model retraining started in background",
        "timestamp": datetime.now().isoformat(),
    }


@router.get("/retrain/status")
async def get_retraining_status(
    service: ModelRetrainingService = Depends(get_retraining_service),
):
    """
    Get current retraining status.
    
    Returns:
    - Status of current or last training run
    """
    return service.get_status()
