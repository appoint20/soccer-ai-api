# 🎯 Detailed Action Plan: Database & Scripts Cleanup

## Current Status Assessment

### ✅ What's Good
- Database: SQLite only ✅
- Location: `/Users/shivm/Workspace/soccer.db` ✅
- ML Models: All present and trained ✅
- Scripts: ML pipeline scripts exist ✅

### ❌ What Needs Fixing
1. **extract_features.py** has hardcoded PostgreSQL connection (won't work with SQLite)
2. **requirements.txt** missing sqlalchemy and other dependencies
3. **deploy_gcloud.sh** is obsolete (Terraform replaces it)
4. **Python venvs** (.venv, venv) should be cleaned or rebuilt

### Current Directory Structure
```
scripts/
├── deploy_gcloud.sh          ← DELETE (obsolete)
└── ml/
    ├── .venv/                ← DELETE (rebuild fresh)
    ├── venv/                 ← DELETE (rebuild fresh)
    ├── extract_features.py   ← FIX (PostgreSQL → SQLite)
    ├── train_models.py       ← KEEP (no changes)
    ├── export_onnx.py        ← KEEP (no changes)
    ├── requirements.txt      ← FIX (add sqlalchemy)
    ├── training_data.parquet ← KEEP
    └── models/               ← KEEP (all ONNX files)
```

---

## Step 1: Fix extract_features.py

### Change the Database Connection

**File**: `/Users/shivm/Workspace/soccer-gpt-api/scripts/ml/extract_features.py`

**Line 13 - CHANGE FROM:**
```python
DB_URL = "postgresql+psycopg2://postgres@localhost:5432/soccer_ai"
```

**CHANGE TO:**
```python
DB_URL = "sqlite:////Users/shivm/Workspace/soccer.db"
```

**Execute Command:**
```bash
cd /Users/shivm/Workspace/soccer-gpt-api/scripts/ml

# Edit the file
sed -i.bak 's|postgresql+psycopg2://postgres@localhost:5432/soccer_ai|sqlite:////Users/shivm/Workspace/soccer.db|g' extract_features.py

# Verify change
grep "DB_URL" extract_features.py
```

**Expected Output:**
```
DB_URL = "sqlite:////Users/shivm/Workspace/soccer.db"
```

---

## Step 2: Fix requirements.txt

### Add Missing Dependencies

**File**: `/Users/shivm/Workspace/soccer-gpt-api/scripts/ml/requirements.txt`

**Current Content:**
```
pandas>=2.0.0
numpy>=1.24.0
pyarrow>=14.0.0
scikit-learn>=1.3.0
xgboost>=2.0.0
joblib>=1.3.0
onnxmltools>=1.11.0
onnxconverter-common>=1.13.0
onnx>=1.14.0
```

**Updated Content (ADD THESE LINES):**
```
pandas>=2.0.0
numpy>=1.24.0
pyarrow>=14.0.0
scikit-learn>=1.3.0
xgboost>=2.0.0
joblib>=1.3.0
onnxmltools>=1.11.0
onnxconverter-common>=1.13.0
onnx>=1.14.0
sqlalchemy>=2.0.0
```

**Execute Command:**
```bash
cd /Users/shivm/Workspace/soccer-gpt-api/scripts/ml

# Add sqlalchemy
echo "sqlalchemy>=2.0.0" >> requirements.txt

# Verify
cat requirements.txt
```

---

## Step 3: Clean Up Old Python Virtual Environments

### Delete Old venv Directories

**Why**: Virtual environments are machine-specific and should be rebuilt fresh

```bash
cd /Users/shivm/Workspace/soccer-gpt-api/scripts/ml

# Remove old environments
rm -rf .venv
rm -rf venv

# Verify deletion
ls -la
```

**Expected Result:** Only files remain, no .venv or venv directories

---

## Step 4: Create Fresh Python Virtual Environment

### Setup New venv with SQLite Support

```bash
cd /Users/shivm/Workspace/soccer-gpt-api/scripts/ml

# Create new virtual environment
python3 -m venv .venv

# Activate it
source .venv/bin/activate

# Upgrade pip
pip install --upgrade pip

# Install dependencies
pip install -r requirements.txt

# Verify installation
pip list | grep -E "sqlalchemy|pandas|xgboost"

# Deactivate when done
deactivate
```

**Expected Output (pip list):**
```
pandas                2.x.x
numpy                 1.x.x
sqlalchemy            2.x.x
xgboost               2.x.x
scikit-learn          1.x.x
...
```

---

## Step 5: Test Data Extraction with SQLite

### Run Feature Extraction Pipeline

```bash
cd /Users/shivm/Workspace/soccer-gpt-api/scripts/ml

# Activate venv
source .venv/bin/activate

# Run extraction
python3 extract_features.py

# Deactivate
deactivate
```

**Expected Output:**
```
Connected to SQLite database
Loaded [N] finished fixtures from SQLite
Calculated rolling features...
Saved to training_data.parquet
```

**Troubleshooting if it fails:**
```bash
# Check database exists
ls -lh /Users/shivm/Workspace/soccer.db

# Verify SQLite tables
sqlite3 /Users/shivm/Workspace/soccer.db ".tables"

# Count finished fixtures
sqlite3 /Users/shivm/Workspace/soccer.db "SELECT COUNT(*) FROM Fixtures WHERE Status='FT';"
```

---

## Step 6: Test Model Training

### Train XGBoost Models

```bash
cd /Users/shivm/Workspace/soccer-gpt-api/scripts/ml

# Activate venv
source .venv/bin/activate

# Run training
python3 train_models.py

# Deactivate
deactivate
```

**Expected Output:**
```
Training over25_model...
Training btts_model...
Training goals_2_3_model...
Training hda_model...
Exporting to ONNX...
Models saved to ./models/
```

**Verify Models Generated:**
```bash
ls -lah models/
# Should show: *.json files (training snapshots)
# and *.onnx files (C# runtime models)
```

---

## Step 7: Delete Obsolete Deployment Script

### Remove Old Shell Script

```bash
cd /Users/shivm/Workspace/soccer-gpt-api/scripts

# Delete obsolete deployment script
rm deploy_gcloud.sh

# Verify deletion
ls -la scripts/
# Should only show: ml/ directory
```

**Why Delete:**
- Terraform in `terraform/` directory replaces this
- GCP Cloud Run deployments handled by Terraform
- No longer maintained or used

---

## Step 8: Verify Everything Works Together

### Test Complete ML Pipeline Integration

```bash
# 1. Build the application
cd /Users/shivm/Workspace/soccer-gpt-api
dotnet build

# 2. Run unit tests
dotnet test

# 3. Run the API
dotnet run --project src/soccer-ai-api

# 4. In another terminal, test prediction endpoint
curl "http://localhost:5000/api/analysis?date=2025-03-01&language=en"

# 5. Check logs for model loading
# Should see: "Loading ONNX model: over25_model.onnx"
```

---

## Summary: Before & After

### Before Cleanup ❌
```
scripts/
├── deploy_gcloud.sh      (obsolete)
└── ml/
    ├── .venv/            (broken for SQLite)
    ├── venv/             (old environment)
    ├── extract_features.py (PostgreSQL config)
    ├── requirements.txt   (missing dependencies)
    ├── train_models.py    (✅ fine)
    ├── export_onnx.py     (✅ fine)
    ├── models/           (✅ fine)
    └── training_data.parquet (✅ fine)
```

### After Cleanup ✅
```
scripts/
└── ml/
    ├── .venv/            (fresh, SQLite-ready)
    ├── extract_features.py (SQLite connection)
    ├── requirements.txt   (with sqlalchemy)
    ├── train_models.py    (✅ unchanged)
    ├── export_onnx.py     (✅ unchanged)
    ├── models/           (✅ unchanged)
    └── training_data.parquet (✅ unchanged)
```

---

## Complete Cleanup Script

If you want to automate all steps, run this:

```bash
#!/bin/bash
set -e

PROJECT_ROOT="/Users/shivm/Workspace/soccer-gpt-api"
SCRIPTS_DIR="$PROJECT_ROOT/scripts"
ML_DIR="$SCRIPTS_DIR/ml"

echo "🧹 Starting cleanup and fix..."

# Step 1: Fix extract_features.py
echo "1️⃣  Fixing extract_features.py..."
sed -i.bak 's|postgresql+psycopg2://postgres@localhost:5432/soccer_ai|sqlite:////Users/shivm/Workspace/soccer.db|g' "$ML_DIR/extract_features.py"
echo "✅ Fixed database connection"

# Step 2: Fix requirements.txt
echo "2️⃣  Updating requirements.txt..."
if ! grep -q "sqlalchemy" "$ML_DIR/requirements.txt"; then
    echo "sqlalchemy>=2.0.0" >> "$ML_DIR/requirements.txt"
fi
echo "✅ Added sqlalchemy dependency"

# Step 3: Clean old venvs
echo "3️⃣  Removing old virtual environments..."
rm -rf "$ML_DIR/.venv" "$ML_DIR/venv"
echo "✅ Deleted old venvs"

# Step 4: Delete obsolete script
echo "4️⃣  Removing obsolete deploy_gcloud.sh..."
rm -f "$SCRIPTS_DIR/deploy_gcloud.sh"
echo "✅ Deleted deploy_gcloud.sh"

# Step 5: Create fresh venv
echo "5️⃣  Creating fresh Python virtual environment..."
python3 -m venv "$ML_DIR/.venv"
source "$ML_DIR/.venv/bin/activate"
pip install --upgrade pip
pip install -r "$ML_DIR/requirements.txt"
deactivate
echo "✅ Fresh venv created"

echo ""
echo "🎉 Cleanup complete!"
echo ""
echo "Next steps:"
echo "1. Test extraction: cd scripts/ml && source .venv/bin/activate && python3 extract_features.py"
echo "2. Test training: python3 train_models.py"
echo "3. Test API: dotnet run --project src/soccer-ai-api"
```

**Save & Run:**
```bash
# Save script
cat > /tmp/cleanup.sh << 'EOF'
[paste the script above]
EOF

# Make executable
chmod +x /tmp/cleanup.sh

# Run
/tmp/cleanup.sh
```

---

## ✅ Final Verification Checklist

After completing all steps:

```
[ ] extract_features.py uses SQLite connection
    grep "sqlite://" scripts/ml/extract_features.py

[ ] requirements.txt has sqlalchemy
    grep "sqlalchemy" scripts/ml/requirements.txt

[ ] Old venvs deleted
    ls scripts/ml/ | grep -v "\.venv"

[ ] deploy_gcloud.sh deleted
    [ ! -f scripts/deploy_gcloud.sh ] && echo "✅ Deleted"

[ ] Fresh venv exists
    ls -la scripts/ml/.venv/bin/

[ ] ML models still present
    ls scripts/ml/models/*.onnx

[ ] Application builds
    dotnet build

[ ] Tests pass
    dotnet test

[ ] API runs
    dotnet run --project src/soccer-ai-api &
    sleep 5 && curl http://localhost:5000/health
    pkill -f "dotnet run"

[ ] Database intact
    sqlite3 /Users/shivm/Workspace/soccer.db ".tables"
```

---

## Database Verification Commands

Verify your SQLite database is healthy:

```bash
# Check database file
ls -lh ~/Workspace/soccer.db

# View tables
sqlite3 ~/Workspace/soccer.db ".tables"

# Count records
sqlite3 ~/Workspace/soccer.db "SELECT 'Teams:' as table_name, COUNT(*) as count FROM Teams
UNION ALL
SELECT 'Fixtures', COUNT(*) FROM Fixtures
UNION ALL
SELECT 'Users', COUNT(*) FROM Users;"

# Check finished fixtures (needed for ML)
sqlite3 ~/Workspace/soccer.db "SELECT COUNT(*) as finished_fixtures FROM Fixtures WHERE Status='FT';"

# View database schema
sqlite3 ~/Workspace/soccer.db ".schema Fixtures" | head -20
```

---

## Production Readiness

After cleanup, your setup will be:

✅ **Development-Ready**: SQLite works perfectly for development
✅ **ML Pipeline Fixed**: Scripts work with SQLite
✅ **Clean Scripts**: Only essential ML scripts
✅ **Fresh Environment**: New Python venv with dependencies
✅ **No Legacy Code**: Obsolete scripts removed

**For Production**, consider:
1. Migrate SQLite → PostgreSQL (AWS RDS or GCP Cloud SQL)
2. Store trained models in Cloud Storage (GCS, S3)
3. Schedule ML jobs via Cloud Scheduler
4. Use managed databases for scalability
5. Add database encryption at rest

---
