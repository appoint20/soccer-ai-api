"""
Soccer-GPT API - Main Entry Point
"""
from fastapi import FastAPI
from fastapi.middleware.cors import CORSMiddleware
from fastapi.openapi.docs import get_swagger_ui_html

from app.api.routes import leagues, matches, tickets, backtest

# API version
API_VERSION = "v1"

app = FastAPI(
    title="Soccer-GPT API",
    description="""
    ## Football Betting Predictions API
    
    Predictions powered by ML + Poisson + Monte Carlo analysis.
    
    ### Endpoints
    - **Leagues** - Get supported leagues
    - **Matches** - Analyze upcoming fixtures
    - **Tickets** - Generate betting tickets
    - **Backtest** - View historical performance
    
    ### Features
    - Pattern detection (69.3% accuracy when consensus)
    - Trap detection for risky bets
    - ChatGPT analysis integration
    """,
    version="1.0.0",
    docs_url=None,  # Custom docs
    redoc_url=None
)

# CORS for SwiftUI app
app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)

# API v1 routes
app.include_router(leagues.router, prefix=f"/api/{API_VERSION}", tags=["Leagues"])
app.include_router(matches.router, prefix=f"/api/{API_VERSION}", tags=["Matches"])
app.include_router(tickets.router, prefix=f"/api/{API_VERSION}", tags=["Tickets"])
app.include_router(backtest.router, prefix=f"/api/{API_VERSION}", tags=["Backtest"])


@app.get("/")
async def root():
    return {
        "message": "Soccer-GPT API",
        "version": "1.0.0",
        "api_version": API_VERSION,
        "docs": "/docs"
    }


@app.get("/health")
async def health():
    return {"status": "healthy"}


@app.get("/docs", include_in_schema=False)
async def custom_swagger_ui_html():
    """Scalar API Documentation"""
    return get_swagger_ui_html(
        openapi_url="/openapi.json",
        title="Soccer-GPT API Documentation",
        swagger_favicon_url="https://fastapi.tiangolo.com/img/favicon.png"
    )


# Scalar documentation (alternative)
SCALAR_HTML = """
<!DOCTYPE html>
<html>
<head>
    <title>Soccer-GPT API</title>
    <meta charset="utf-8"/>
    <meta name="viewport" content="width=device-width, initial-scale=1"/>
</head>
<body>
    <script id="api-reference" data-url="/openapi.json"></script>
    <script src="https://cdn.jsdelivr.net/npm/@scalar/api-reference"></script>
</body>
</html>
"""

from fastapi.responses import HTMLResponse

@app.get("/scalar", response_class=HTMLResponse, include_in_schema=False)
async def scalar_docs():
    """Scalar API Documentation (Modern UI)"""
    return SCALAR_HTML
