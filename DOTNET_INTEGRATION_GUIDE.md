# .NET Integration Guide

## Quick Start (.NET + Python Integration)

### 1. Prerequisites

```bash
# Check .NET version (should be 10.0.0)
dotnet --version

# Check Python version (should be 3.x)
python3 --version

# Check SQLite database exists
ls -lh src/soccer-ai-api/data/soccer.db
```

### 2. Build .NET Solution

```bash
cd /Users/shivm/Workspace/soccer-gpt-api

# Clean previous builds
dotnet clean

# Build entire solution
dotnet build

# Expected output:
#   ✅ soccer-ai-api.csproj
#   ✅ soccer-ai-application.csproj
#   ✅ soccer-ai-infrastructure.csproj
```

### 3. Run Tests

```bash
# Run all unit and integration tests
dotnet test

# Expected: All tests should pass
```

### 4. Train ML Models (Python)

```bash
# Before running API, ensure ML models are trained
cd scripts/ml

# Activate venv
source .venv/bin/activate

# Extract features from SQLite
python3 extract_features.py

# Train models and export to ONNX
python3 train_models.py

# Deactivate
deactivate

# Verify models exist
ls -lh scripts/ml/models/*.onnx
```

### 5. Run .NET API

```bash
# From project root
dotnet run --project src/soccer-ai-api

# Expected output:
#   info: Microsoft.Hosting.Lifetime[14]
#   info:       Now listening on: http://localhost:5000
#   info:       Now listening on: https://localhost:5001
#
#   info: SoccerAi.Infrastructure.Services.MlPredictionService[0]
#   Loading ONNX model: over25_model.onnx
#   Loading ONNX model: btts_model.onnx
#   Loading ONNX model: goals_2_3_model.onnx
#   Loading ONNX model: hda_model.onnx
#   ✅ All models loaded successfully
```

### 6. Test API Endpoints

```bash
# In another terminal
# Test health check
curl http://localhost:5000/health

# Test analysis endpoint
curl "http://localhost:5000/api/analysis?date=2025-03-01&language=en"

# Expected response: JSON with match predictions
```

---

## Architecture Overview

### Folder Structure

```
src/
├── 📦 soccer-ai-api/              # Presentation Layer (ASP.NET Core)
│   ├── Controllers/               # HTTP endpoints
│   │   ├── AnalysisController.cs  # GET /api/analysis
│   │   ├── CombinationsController.cs
│   │   ├── VerificationController.cs
│   │   └── ...
│   ├── Program.cs                 # Startup, DI configuration
│   ├── appsettings.json          # Configuration (SQLite path)
│   └── soccer-ai-api.csproj
│
├── 📦 soccer-ai-application/      # Business Logic Layer (Domain)
│   ├── Features/                  # CQRS - Queries & Commands
│   │   ├── Analysis/
│   │   │   ├── GetMatchAnalysisQuery.cs
│   │   │   ├── GetMatchAnalysisHandler.cs    # Orchestrator
│   │   │   └── GetMatchAnalysisResponse.cs
│   │   ├── Combinations/
│   │   │   ├── GetMatchCombinationQuery.cs
│   │   │   ├── GetMatchCombinationHandler.cs # Orchestrator
│   │   │   └── GetMatchCombinationResponse.cs
│   │   └── Verification/
│   │       ├── GetFixturesVerificationQuery.cs
│   │       ├── GetFixturesVerificationHandler.cs
│   │       └── ...
│   │
│   ├── Interfaces/                # Service abstractions
│   │   ├── IMatchAnalysisService.cs
│   │   ├── IMlPredictionService.cs
│   │   ├── IGeminiAnalysisService.cs
│   │   └── ... (20+ interfaces)
│   │
│   ├── Services/                  # Domain services
│   │   ├── PoissonCalculationService.cs
│   │   ├── MonteCarloService.cs
│   │   ├── TrapDetectionService.cs
│   │   └── ... (14+ services)
│   │
│   ├── Models/                    # DTOs & Value Objects
│   │   ├── FixtureAnalysis.cs
│   │   ├── WeightedPrediction.cs
│   │   ├── StatisticalModels.cs
│   │   └── ... (30+ models)
│   │
│   ├── Entities/                  # Domain Entities
│   │   ├── Fixture.cs
│   │   ├── Team.cs
│   │   ├── User.cs
│   │   └── ...
│   │
│   ├── Helpers/                   # Common utilities
│   │   ├── FixtureQueryHelper.cs  # Database queries
│   │   ├── FormScoreCalculator.cs # Form calculations
│   │   └── ...
│   │
│   └── soccer-ai-application.csproj
│
├── 📦 soccer-ai-infrastructure/   # Data Access Layer
│   ├── Persistence/
│   │   ├── ApplicationDbContext.cs # EF Core DbContext
│   │   │   ├── DbSet<Fixture>
│   │   │   ├── DbSet<Team>
│   │   │   ├── DbSet<User>
│   │   │   └── DbSet<UserCombination>
│   │   │
│   │   └── Migrations/
│   │       ├── 20260301131440_InitSqlite.Designer.cs
│   │       ├── 20260301131440_InitSqlite.cs
│   │       └── ApplicationDbContextModelSnapshot.cs
│   │
│   ├── Services/                  # Infrastructure implementations
│   │   ├── MlPredictionService.cs # Loads ONNX models ⭐
│   │   ├── MatchAnalysisService.cs# Orchestrates analysis
│   │   ├── GeminiAnalysisService.cs
│   │   ├── FixtureSyncService.cs  # Syncs from API-Football
│   │   ├── TeamSyncService.cs
│   │   └── ... (20+ services)
│   │
│   ├── Configuration/
│   │   ├── GeminiOptions.cs
│   │   └── ApiFootballConfiguration.cs
│   │
│   └── soccer-ai-infrastructure.csproj
│
    ├── Program.cs                 # Worker startup
    ├── Worker/
    │   ├── SyncJobRunner.cs       # Executes jobs
    │   ├── WorkerCommandParser.cs # Command parsing
    │   └── WorkerJob.cs           # Job definitions
    │
```

---

## Database Integration

### SQLite Configuration

**File:** `src/soccer-ai-api/appsettings.json`

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Data Source=data/soccer.db"
  },
  "Logging": {...},
  "AllowedHosts": "*"
}
```

**File:** `src/soccer-ai-infrastructure/DependencyInjection.cs`

```csharp
services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlite(
        configuration.GetConnectionString("DefaultConnection")
    ),
    contextLifetime: ServiceLifetime.Scoped
);
```

### Entity Definitions

**File:** `src/soccer-ai-infrastructure/Persistence/ApplicationDbContext.cs`

```csharp
public class ApplicationDbContext : DbContext
{
    public DbSet<Fixture> Fixtures { get; set; }
    public DbSet<Team> Teams { get; set; }
    public DbSet<User> Users { get; set; }
    public DbSet<UserCombination> UserCombinations { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Foreign key constraints
        modelBuilder.Entity<Fixture>()
            .HasOne(f => f.HomeTeam)
            .WithMany()
            .HasForeignKey(f => f.HomeTeamId)
            .OnDelete(DeleteBehavior.Restrict);

        // Unique indices
        modelBuilder.Entity<Fixture>()
            .HasIndex(f => new { f.ApiId, f.LeagueId })
            .IsUnique();

        // Migrations handled automatically
    }
}
```

### Migrations

```bash
# Check migration status
dotnet ef migrations list --project src/soccer-ai-infrastructure

# Apply pending migrations (done automatically on startup)
# WorkerCommand: "migrate" runs them explicitly
```

---

## ML Model Integration

### Model Loading (Startup)

**File:** `src/soccer-ai-infrastructure/Services/MlPredictionService.cs`

```csharp
public class MlPredictionService : IMlPredictionService
{
    private readonly ILogger<MlPredictionService> _logger;
    private readonly Dictionary<string, InferenceSession> _sessions;

    public MlPredictionService(ILogger<MlPredictionService> logger)
    {
        _logger = logger;
        _sessions = new Dictionary<string, InferenceSession>();
        LoadModels();
    }

    private void LoadModels()
    {
        string[] modelNames =
        {
            "over25_model",
            "btts_model",
            "goals_2_3_model",
            "hda_model"
        };

        foreach (var modelName in modelNames)
        {
            string modelPath = $"scripts/ml/models/{modelName}.onnx";

            if (!File.Exists(modelPath))
                throw new FileNotFoundException($"Model not found: {modelPath}");

            _sessions[modelName] = new InferenceSession(modelPath);
            _logger.LogInformation("Loaded ONNX model: {ModelName}", modelName);
        }
    }

    public async Task<Dictionary<string, double>> PredictAsync(
        Dictionary<string, float> features,
        CancellationToken ct = default)
    {
        // Convert features to tensor
        // Run inference through all 4 models
        // Return probabilities
        return await Task.Run(() => RunInference(features), ct);
    }

    private Dictionary<string, double> RunInference(Dictionary<string, float> features)
    {
        var results = new Dictionary<string, double>();

        foreach (var (modelName, session) in _sessions)
        {
            // Load features into tensor
            var input = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor("features", CreateTensor(features))
            };

            // Run model
            using var output = session.Run(input);
            var probability = output.First().AsEnumerable<float>().First();

            results[modelName] = probability;
        }

        return results;
    }
}
```

### Model invocation Example

**File:** `src/soccer-ai-application/Services/ProbabilityPipeline.cs`

```csharp
public async Task<MlPredictions> ExecutePipelineAsync(Fixture fixture)
{
    // Build feature dictionary from fixture
    var features = BuildFeatures(fixture); // 60+ features

    // Run through all 4 models
    var predictions = await _mlService.PredictAsync(features);

    return new MlPredictions
    {
        Over25Probability = predictions["over25_model"],
        BttsProbability = predictions["btts_model"],
        Goals23Probability = predictions["goals_2_3_model"],
        HdaProbabilities = ParseHdaPredictions(predictions["hda_model"])
    };
}
```

---

## Request → Response Flow

### Example: Match Analysis Request

```
1. HTTP Request
   GET /api/analysis?date=2025-03-01&language=en

2. AnalysisController
   └── Mediator.Send(GetMatchAnalysisQuery)

3. GetMatchAnalysisHandler (Orchestrator)
   ├── Load fixtures from SQLite via FixtureQueryHelper
   ├── For each fixture:
   │   └── Call IMatchAnalysisService.AnalyzeFixtureAsync()
   ├── Batch Gemini AI analysis
   ├── Map results via AnalysisResponseMapper
   └── Return GetMatchAnalysisResponse

4. MatchAnalysisService (Core Logic)
   ├── Extract team statistics
   ├── Run Poisson distribution calculations
   ├── Run Monte Carlo simulations
   ├── Run ML predictions via MlPredictionService
   │   └── ONNX Runtime executes 4 models
   ├── Decision service qualification
   └── Return FixtureAnalysis

5. HTTP Response (JSON)
   {
     "matches": [
       {
         "prediction": {
           "over25": { "prediction": true, "probability": 0.65 },
           "btts": { "prediction": false, "probability": 0.42 },
           ...
         }
       }
     ]
   }
```

---

## Dependency Injection Setup

**File:** `src/soccer-ai-api/Program.cs`

```csharp
var builder = WebApplication.CreateBuilder(args);

// Add layers
builder.Services.AddApplicationServices();  // Business logic
builder.Services.AddInfrastructureServices(builder.Configuration); // Data + ML
builder.Services.AddApiServices(); // CQRS & Auth

// Add database
builder.Services.AddDbContext<ApplicationDbContext>();

// Create app
var app = builder.Build();

// Apply migrations
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    await db.Database.MigrateAsync();
}

app.Run();
```

---

## API Endpoints

### Analysis Endpoint

```
GET /api/analysis?date=2025-03-01&language=en

Response:
{
  "matches": [...],
  "summary": {
    "totalMatches": 10,
    "correctMatches": 7,
    "accuracyRate": 70.0
  }
}
```

### Combinations Endpoint

```
GET /api/combinations?date=2025-03-01&language=en

Response:
{
  "combinations": [
    {
      "name": "High Value Goals Double",
      "matches": [...]
    }
  ]
}
```

### Verification Endpoints

```
GET /api/verify/fixtures?limit=50&offset=0
GET /api/verify/teams?limit=50&offset=0

POST /api/verify/sync/fixtures/12?season=2024
POST /api/verify/sync/standings/12?season=2024
```

---

## Troubleshooting

### Issue: "Models not found" on startup

**File:** Check `scripts/ml/models/` directory

```bash
ls -lh /Users/shivm/Workspace/soccer-gpt-api/scripts/ml/models/
# Should show: *.onnx files

# If missing, run:
cd scripts/ml
python3 train_models.py
python3 export_onnx.py
```

### Issue: "Database not found"

**Check:**

```bash
# Verify database file exists
ls -lh src/soccer-ai-api/data/soccer.db

# Verify connection string in appsettings.json
cat src/soccer-ai-api/appsettings.json | grep DefaultConnection

# Test connection
sqlite3 src/soccer-ai-api/data/soccer.db ".tables"
```

### Issue: "Type not registered in container"

**Check:**

```bash
# Ensure all services are registered in DependencyInjection.cs
grep -n "AddScoped\|AddSingleton" src/soccer-ai-infrastructure/DependencyInjection.cs

# Common issue: MlPredictionService not registered
# Should have: services.AddSingleton<IMlPredictionService, MlPredictionService>();
```

### Issue: "ONNX Runtime error"

**Check:**

```bash
# Models might be corrupted
python3 -c "
import onnx
model = onnx.load('scripts/ml/models/over25_model.onnx')
onnx.checker.check_model(model)
print('✅ Model is valid')
"

# If corrupted, retrain:
cd scripts/ml
python3 train_models.py
```

---

## Performance Optimization

### Caching

```csharp
// Fixtures cached for 12 hours
var fixtures = await _dbContext.Fixtures
    .Where(f => f.Date >= startDate && f.Date < endDate)
    .AsNoTracking()
    .ToListAsync(); // Untracked query for read-only
```

### Batch Processing

```csharp
// Process multiple fixtures in batches
foreach (var batch in fixtures.Chunk(100))
{
    // Analyze batch in parallel
    var tasks = batch.Select(f => _analysisService.AnalyzeFixtureAsync(f));
    await Task.WhenAll(tasks);
}
```

### Connection Pooling

SQLite uses file-based connections, so no connection pooling needed. All queries are directed to single file.

---

## Production Deployment

### Docker

```dockerfile
# Dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:10.0

COPY --from=build /app/publish /app

# Include ML models
COPY scripts/ml/models /app/scripts/ml/models/

WORKDIR /app
EXPOSE 8000
ENTRYPOINT ["dotnet", "soccer-ai-api.dll"]
```

### Environment Variables

```bash
# Override via environment
export ConnectionStrings__DefaultConnection="Data Source=data/soccer.db"
export ASPNETCORE_URLS="http://0.0.0.0:8000"
export Logging__LogLevel__Default="Warning"
```

---

## Summary

| Component | Status | Location |
|-----------|--------|----------|
| **API** | ✅ Ready | src/soccer-ai-api |
| **Database** | ✅ SQLite | src/soccer-ai-api/data/soccer.db |
| **ML Models** | ✅ ONNX | scripts/ml/models/ |
| **Integration** | ✅ Complete | DependencyInjection.cs |
| **Tests** | ✅ Ready | tests/soccer-ai-integration-tests |

Everything is configured and ready for local development and production deployment!

---
