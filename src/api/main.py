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
from src.api.routers import analysis, tickets, models, leagues, backtest
from src.utils.logger import get_logger
from src.api.dependencies import ServiceContainer

logger = get_logger("API")

# Application version
VERSION = "1.0.0"


@asynccontextmanager
async def lifespan(app: FastAPI):
    """Application lifespan events."""
    logger.info("Starting soccer-gpt-api...")
    
    # Initialize all services
    ServiceContainer.init_services()
    
    # Initialize Scheduler
    from src.domain.services.scheduler_service import SchedulerService
    scheduler = SchedulerService()
    await scheduler.start()
    
    # Startup: Sanitize fixtures and historical files
    try:
        from src.data.loaders.excel_sanitizer import ExcelSanitizer
        from pathlib import Path
        sanitizer = ExcelSanitizer()
        
        # 1. Sanitize upcoming fixtures
        excel_path = Path("data/raw/upcoming/fixtures.xlsx")
        csv_path = Path("data/raw/upcoming/fixtures.csv")
        clean_path = Path("data/raw/upcoming/fixtures_clean.csv")
        
        if excel_path.exists():
            if not clean_path.exists() or clean_path.stat().st_size == 0:
                df = sanitizer.sanitize(excel_path, clean_path)
                if df is not None:
                    logger.info(f"Sanitized upcoming fixtures: {len(df)} matches")
            else:
                logger.info(f"Using cached upcoming fixtures: {clean_path}")

        elif csv_path.exists():
             if not clean_path.exists() or clean_path.stat().st_size == 0:
                df = sanitizer.sanitize(csv_path, clean_path)
                if df is not None:
                    logger.info(f"Sanitized upcoming fixtures: {len(df)} matches")
             else:
                logger.info(f"Using cached upcoming fixtures: {clean_path}")
        
        # 2. Sanitize historical Excel files
        historical_dir = Path("data/raw/historical")
        if historical_dir.exists():
            for excel_file in historical_dir.glob("*.xlsx"):
                clean_file = excel_file.with_suffix(".csv")
                # Skip if CSV exists and is newer than Excel
                if clean_file.exists() and clean_file.stat().st_mtime >= excel_file.stat().st_mtime:
                    logger.info(f"Using cached historical data: {clean_file.name}")
                    continue
                    
                df = sanitizer.sanitize(excel_file, clean_file)
                if df is not None:
                    logger.info(f"Sanitized {excel_file.name}: {len(df)} matches")
            
            for xls_file in historical_dir.glob("*.xls"):
                if not xls_file.name.endswith(".xlsx"):
                    clean_file = xls_file.with_suffix(".csv")
                    if clean_file.exists() and clean_file.stat().st_mtime >= xls_file.stat().st_mtime:
                         logger.info(f"Using cached historical data: {clean_file.name}")
                         continue

                    df = sanitizer.sanitize(xls_file, clean_file)
                    if df is not None:
                        logger.info(f"Sanitized {xls_file.name}: {len(df)} matches")
    except Exception as e:
        logger.warning(f"Could not sanitize data files: {e}")
    
    # Run data health check
    try:
        from src.domain.services.data_health_service import DataHealthService
        from src.domain.services.upcoming_match_service import SUPPORTED_LEAGUES
        
        health_service = DataHealthService()
        health_report = health_service.validate_on_startup(
            historical_matches=ServiceContainer.historical_matches,
            supported_leagues=SUPPORTED_LEAGUES,
        )
        if not health_report.is_healthy:
            logger.warning(f"Data health issues: {health_report.warnings}")
    except Exception as e:
        logger.warning(f"Could not run data health check: {e}")
    
    # Set historical matches for V2 router
    try:
        from src.api.routers.analyze_v2 import set_historical_matches
        set_historical_matches(ServiceContainer.historical_matches)
    except Exception as e:
        logger.warning(f"Could not set historical matches for V2: {e}")
    
    # Startup complete
    logger.info("Startup complete")
    
    yield
    
    # Shutdown
    await scheduler.stop()
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
app.include_router(analysis.router, tags=["Analysis"])
app.include_router(tickets.router, tags=["Tickets"])
app.include_router(models.router, prefix="/models", tags=["Models"])
app.include_router(leagues.router)
app.include_router(backtest.router, prefix="/backtest", tags=["Backtest"])


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
