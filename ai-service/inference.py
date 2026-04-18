"""
Model inference layer.
Supports Mistral-7B-Instruct and LLaMA-3-8B-Instruct via HuggingFace Transformers.
Uses Apple MPS (Metal) on Apple Silicon for GPU-accelerated inference.
Models are loaded lazily on first use and cached in memory.
"""
from __future__ import annotations

import json
import logging
import os
import re
import threading
from dataclasses import dataclass, field
from typing import Optional

import torch
from transformers import AutoModelForCausalLM, AutoTokenizer, pipeline

logger = logging.getLogger(__name__)

# ─── Model identifiers ────────────────────────────────────────────────────────
MODEL_MISTRAL = "mistralai/Mistral-7B-Instruct-v0.3"
MODEL_LLAMA3 = "meta-llama/Meta-Llama-3-8B-Instruct"

# ─── Device selection ─────────────────────────────────────────────────────────
def _get_device() -> str:
    env_device = os.environ.get("DEVICE")
    if env_device in ("cpu", "mps", "cuda"):
        return env_device
    if torch.backends.mps.is_available():
        return "mps"
    if torch.cuda.is_available():
        return "cuda"
    return "cpu"

DEVICE = _get_device()
logger.info(f"[Inference] Using device: {DEVICE}")

# ─── Pipeline cache ───────────────────────────────────────────────────────────
@dataclass
class _ModelCache:
    pipeline: object = None
    lock: threading.Lock = field(default_factory=threading.Lock)

_cache: dict[str, _ModelCache] = {
    "mistral": _ModelCache(),
    "llama3":  _ModelCache(),
}


def _load_pipeline(model_key: str) -> object:
    """Load and cache a HuggingFace text-generation pipeline."""
    cache = _cache[model_key]
    if cache.pipeline is not None:
        return cache.pipeline

    with cache.lock:
        if cache.pipeline is not None:   # double-checked
            return cache.pipeline

        model_id = MODEL_MISTRAL if model_key == "mistral" else MODEL_LLAMA3
        hf_token = os.environ.get("HUGGINGFACE_TOKEN")  # required for LLaMA 3

        logger.info(f"[Inference] Loading model: {model_id} on {DEVICE}")

        # Use float16 for MPS/CUDA, float32 for CPU
        dtype = torch.float16 if DEVICE in ("mps", "cuda") else torch.float32

        tokenizer = AutoTokenizer.from_pretrained(
            model_id,
            token=hf_token,
            use_fast=True,
        )

        model = AutoModelForCausalLM.from_pretrained(
            model_id,
            token=hf_token,
            torch_dtype=dtype,
            device_map="auto" if DEVICE == "cuda" else None,
            low_cpu_mem_usage=True,
        )

        if DEVICE == "mps":
            model = model.to("mps")

        pipe = pipeline(
            "text-generation",
            model=model,
            tokenizer=tokenizer,
            device=0 if DEVICE == "cuda" else (DEVICE if DEVICE == "mps" else -1),
            torch_dtype=dtype,
        )

        cache.pipeline = pipe
        logger.info(f"[Inference] Model ready: {model_id}")
        return pipe


def _extract_json(text: str) -> str:
    """Strip markdown code fences and return only the JSON content."""
    text = text.strip()
    # Strip ```json ... ``` fences if present
    text = re.sub(r"^```(?:json)?\s*", "", text)
    text = re.sub(r"\s*```$", "", text)
    return text.strip()


def run_inference(
    system_prompt: str,
    user_content: str,
    model_key: str = "mistral",
    max_new_tokens: int = 4096,
    temperature: float = 0.05,
    do_sample: bool = False,
) -> str:
    """
    Run inference on the selected model and return raw text output.
    Raises RuntimeError if the model fails.
    """
    pipe = _load_pipeline(model_key)

    # Build chat messages in the instruct format both models support
    messages = [
        {"role": "system", "content": system_prompt},
        {"role": "user",   "content": user_content},
    ]

    tokenizer = pipe.tokenizer
    # Apply the model's chat template if available
    try:
        prompt_text = tokenizer.apply_chat_template(
            messages,
            tokenize=False,
            add_generation_prompt=True,
        )
    except Exception:
        # Fallback: simple concatenation
        prompt_text = (
            f"<s>[INST] <<SYS>>\n{system_prompt}\n<</SYS>>\n\n{user_content} [/INST]"
        )

    outputs = pipe(
        prompt_text,
        max_new_tokens=max_new_tokens,
        do_sample=do_sample,
        temperature=temperature,
        return_full_text=False,
    )

    raw = outputs[0]["generated_text"]
    return _extract_json(raw)


def parse_json_output(raw: str) -> object:
    """Parse JSON from model output with best-effort cleanup."""
    try:
        return json.loads(raw)
    except json.JSONDecodeError:
        # Try to find the first JSON array or object in the text
        match = re.search(r"(\[.*\]|\{.*\})", raw, re.DOTALL)
        if match:
            return json.loads(match.group(1))
        raise ValueError(f"Model output is not valid JSON:\n{raw[:500]}")
