"""
FastAPI main application for soccer-gpt-api.
"""
from datetime import datetime
from contextlib import asynccontextmanager
from typing import Optional

from fastapi import FastAPI, HTTPException, Query
from fastapi.middleware.cors import CORSMiddleware
from fastapi.responses import JSONResponse

from src.api.schemas import (
    HealthResponse,
    AnalyzeMatchesResponse,
    GenerateTicketsResponse,
    ModelsInfoResponse,
    ErrorResponse,
)
from src.api.routers import predictions, models
from src.utils.logger import get_logger

logger = get_logger("API")

# Application version
VERSION = "1.0.0"


@asynccontextmanager
async def lifespan(app: FastAPI):
    """Application lifespan events."""
    logger.info("Starting soccer-gpt-api...")
    
    # Startup: Load models
    try:
        from src.api.routers.predictions import load_prediction_service
        load_prediction_service()
        logger.info("Prediction service loaded successfully")
    except Exception as e:
        logger.warning(f"Could not load prediction service: {e}")
    
    yield
    
    # Shutdown
    logger.info("Shutting down soccer-gpt-api...")


# Create FastAPI app
app = FastAPI(
    title="Soccer GPT API",
    description="European Soccer Match Prediction API with ML-powered predictions",
    version=VERSION,
    lifespan=lifespan,
    docs_url="/docs",  # Swagger
    redoc_url="/redoc",  # ReDoc
)

# Scalar API docs at /scalar
SCALAR_HTML = """
<!DOCTYPE html>
<html>
<head>
    <title>Soccer GPT API - Scalar</title>
    <meta charset="utf-8"/>
    <meta name="viewport" content="width=device-width, initial-scale=1"/>
</head>
<body>
    <script id="api-reference" data-url="/openapi.json"></script>
    <script src="https://cdn.jsdelivr.net/npm/@scalar/api-reference"></script>
</body>
</html>
"""

@app.get("/scalar", include_in_schema=False)
async def scalar_docs():
    """Scalar API documentation."""
    from fastapi.responses import HTMLResponse
    return HTMLResponse(content=SCALAR_HTML)

# CORS middleware
app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)


# Include routers
app.include_router(predictions.router, tags=["Predictions"])
app.include_router(models.router, prefix="/models", tags=["Models"])


# ============== Root Endpoints ==============

@app.get("/", include_in_schema=False)
async def root():
    """Root endpoint redirect to docs."""
    return {"message": "Soccer GPT API", "docs": "/docs"}


@app.get("/health", response_model=HealthResponse, tags=["System"])
async def health_check():
    """
    Health check endpoint.
    
    Returns API status and version.
    """
    return HealthResponse(
        status="healthy",
        version=VERSION,
        timestamp=datetime.now().isoformat(),
    )


# ============== Error Handlers ==============

@app.exception_handler(HTTPException)
async def http_exception_handler(request, exc: HTTPException):
    """Handle HTTP exceptions."""
    return JSONResponse(
        status_code=exc.status_code,
        content=ErrorResponse(
            error=exc.detail,
            timestamp=datetime.now().isoformat(),
        ).model_dump(),
    )


@app.exception_handler(Exception)
async def general_exception_handler(request, exc: Exception):
    """Handle general exceptions."""
    logger.error(f"Unhandled exception: {exc}")
    return JSONResponse(
        status_code=500,
        content=ErrorResponse(
            error="Internal server error",
            detail=str(exc),
            timestamp=datetime.now().isoformat(),
        ).model_dump(),
    )


# Run with: uvicorn src.api.main:app --reload
