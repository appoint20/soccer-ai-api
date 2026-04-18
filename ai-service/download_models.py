import os
import torch
from huggingface_hub import snapshot_download

def download_models():
    # Model identifiers
    models = [
        "mistralai/Mistral-7B-Instruct-v0.3",
        "meta-llama/Meta-Llama-3-8B-Instruct"
    ]
    
    hf_token = os.environ.get("HUGGINGFACE_TOKEN")
    
    for model_id in models:
        print(f"--- Downloading {model_id} ---")
        try:
            # Download the full repository to the default cache directory
            snapshot_download(
                repo_id=model_id,
                token=hf_token,
                local_files_only=False,
                # We don't necessarily need every single file, 
                # but snapshot_download is the standard for "baking"
            )
            print(f"Successfully downloaded {model_id}")
        except Exception as e:
            if "meta-llama" in model_id and not hf_token:
                print(f"Warning: Could not download {model_id} because HUGGINGFACE_TOKEN is missing. This model will not be baked.")
            else:
                print(f"Error downloading {model_id}: {e}")

if __name__ == "__main__":
    download_models()
