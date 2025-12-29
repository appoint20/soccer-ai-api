#!/usr/bin/env python
"""
Train ML models using historical match data.

This script:
1. Loads historical matches from Excel files
2. Generates features using FeatureEngineeringService
3. Trains Over2.5, BTTS, and Result models for each tier
4. Saves trained models to models/ directory
"""
import sys
from pathlib import Path
from datetime import datetime

# Add project root to path
sys.path.insert(0, str(Path(__file__).parent.parent))

from src.data.loaders.excel_loader import ExcelLoader
from src.data.loaders.data_processor import DataProcessor
from src.data.storage.json_storage import JSONStorage
from src.domain.services.feature_engineering_service import FeatureEngineeringService
from src.ml.trainers.model_trainer import ModelTrainer
from src.ml.evaluators.model_evaluator import ModelEvaluator
from src.utils.logger import get_logger

logger = get_logger("TrainModels")


def main():
    """Main training function."""
    print("=" * 60)
    print("Soccer GPT API - ML Model Training")
    print("=" * 60)
    print()
    
    # Step 1: Load historical matches
    print("[1/4] Loading historical match data...")
    
    storage = JSONStorage()
    matches_file = "data/processed/matches.json"
    
    # Check if processed matches exist
    matches = storage.load(matches_file)
    
    if not matches:
        print("  No processed matches found. Loading from Excel files...")
        
        loader = ExcelLoader()
        processor = DataProcessor()
        
        excel_dir = Path("data/raw/historical")
        excel_files = list(excel_dir.glob("*.xlsx"))
        
        if not excel_files:
            print("  ERROR: No Excel files found in data/raw/historical/")
            return
        
        all_matches = []
        for excel_file in sorted(excel_files):
            print(f"  Loading {excel_file.name}...")
            df = loader.load(excel_file)
            if df is not None and not df.empty:
                # Process the dataframe
                processed_df = processor.process_historical_data(df)
                # Convert to match entities
                match_entities = processor.convert_to_matches(processed_df)
                all_matches.extend([m.to_dict() for m in match_entities])
        
        if not all_matches:
            print("  ERROR: No matches loaded from Excel files")
            return
        
        # Save processed matches
        storage.save(all_matches, matches_file)
        matches = all_matches
        print(f"  Saved {len(matches)} matches to {matches_file}")
    
    print(f"  Loaded {len(matches)} historical matches")
    print()
    
    # Step 2: Generate features
    print("[2/4] Generating features for ML training...")
    
    feature_service = FeatureEngineeringService()
    
    # Only use matches with complete data for training
    valid_matches = [
        m for m in matches
        if m.get("fthg") is not None and m.get("ftag") is not None
    ]
    
    print(f"  Valid matches with scores: {len(valid_matches)}")
    
    # Sample for faster training (use all for production)
    sample_size = min(5000, len(valid_matches))
    sample_matches = valid_matches[-sample_size:]  # Most recent
    
    print(f"  Using {sample_size} most recent matches for training")
    
    features = feature_service.generate_training_features(sample_matches)
    print(f"  Generated {len(features)} feature vectors")
    print()
    
    # Step 3: Train models
    print("[3/4] Training ML models...")
    
    trainer = ModelTrainer(models_dir="models")
    evaluator = ModelEvaluator()
    
    # Train for tier1 (top leagues)
    print()
    print("  Training Tier 1 models (E0, D1, I1, SP1, F1)...")
    tier1_features = trainer.filter_by_tier(features, "tier1")
    print(f"    Tier 1 samples: {len(tier1_features)}")
    
    if len(tier1_features) > 100:
        tier1_results = trainer.train_all_models(tier1_features, "tier1")
        print_results("Tier 1", tier1_results)
    else:
        print("    Skipped (not enough samples)")
    
    # Train for tier2
    print()
    print("  Training Tier 2 models (E1, I2, F2)...")
    tier2_features = trainer.filter_by_tier(features, "tier2")
    print(f"    Tier 2 samples: {len(tier2_features)}")
    
    if len(tier2_features) > 100:
        tier2_results = trainer.train_all_models(tier2_features, "tier2")
        print_results("Tier 2", tier2_results)
    else:
        print("    Skipped (not enough samples)")
    
    # Train for tier3
    print()
    print("  Training Tier 3 models (E2, E3)...")
    tier3_features = trainer.filter_by_tier(features, "tier3")
    print(f"    Tier 3 samples: {len(tier3_features)}")
    
    if len(tier3_features) > 100:
        tier3_results = trainer.train_all_models(tier3_features, "tier3")
        print_results("Tier 3", tier3_results)
    else:
        print("    Skipped (not enough samples)")
    
    # Step 4: Summary
    print()
    print("[4/4] Training complete!")
    print()
    print("=" * 60)
    print("Models saved to: models/<tier>/<model_type>/")
    print("=" * 60)


def print_results(tier: str, results: dict):
    """Print training results."""
    for model_name, result in results.items():
        if "error" in result:
            print(f"    {model_name}: ERROR - {result['error']}")
        else:
            train_acc = result.get("train_accuracy", 0)
            val_acc = result.get("val_accuracy", 0)
            test_acc = result.get("test_accuracy", 0)
            print(f"    {model_name}: train={train_acc:.3f}, val={val_acc:.3f}, test={test_acc:.3f}")


if __name__ == "__main__":
    main()
