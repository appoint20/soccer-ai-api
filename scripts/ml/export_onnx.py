"""
Export XGBoost models to ONNX format for C# inference.
"""

import xgboost as xgb
from pathlib import Path
import json

MODELS_DIR = Path(__file__).parent / "models"

MODELS_DIR = Path(__file__).parent / "models"


def export_to_onnx():
    try:
        from onnxmltools import convert_xgboost
        from onnxmltools.convert.common.data_types import FloatTensorType
    except ImportError:
        print("onnxmltools not installed. Install with: pip install onnxmltools skl2onnx onnx")
        return
    
    # Load feature columns for input shape
    with open(MODELS_DIR / "feature_columns.json") as f:
        feature_cols = json.load(f)
    
    n_features = len(feature_cols)
    initial_type = [('input', FloatTensorType([None, n_features]))]
    
    models = ['over25_model', 'btts_model', 'goals_2_3_model', 'hda_model']
    
    for model_name in models:
        print(f"Converting {model_name}...")
        
        model_path = MODELS_DIR / f"{model_name}.json"
        model = xgb.XGBClassifier()
        model.load_model(model_path)
        
        # Convert to ONNX
        onnx_model = convert_xgboost(model, initial_types=initial_type)
        
        # Save
        onnx_path = MODELS_DIR / f"{model_name}.onnx"
        with open(onnx_path, 'wb') as f:
            f.write(onnx_model.SerializeToString())
        
        print(f"  Saved to: {onnx_path}")


if __name__ == "__main__":
    export_to_onnx()
