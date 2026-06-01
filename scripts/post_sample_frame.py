#!/usr/bin/env python3
from __future__ import annotations

import argparse
import base64
import json
import sys
from pathlib import Path

import requests
from PIL import Image


def main():
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8")

    parser = argparse.ArgumentParser()
    parser.add_argument("--image", required=True, help="Path to sample image")
    parser.add_argument("--url", default="http://127.0.0.1:5000/pipeline/frame")
    mode = parser.add_mutually_exclusive_group()
    mode.add_argument("--mock", action="store_true", help="Use backend mock mode")
    mode.add_argument("--real", action="store_true", help="Use real OCR/translation providers")
    parser.add_argument("--target-language", default="vi")
    parser.add_argument("--ocr-provider", choices=["mock", "paddleocr", "tesseract", "google"])
    parser.add_argument("--translation-provider", choices=["mock", "libretranslate", "google"])
    parser.add_argument("--timeout", type=float, default=30.0, help="HTTP timeout in seconds")
    args = parser.parse_args()

    image_path = Path(args.image)
    image_bytes = image_path.read_bytes()
    with Image.open(image_path) as img:
        width, height = img.size

    payload = {
        "frame_id": image_path.stem,
        "image_base64": base64.b64encode(image_bytes).decode("utf-8"),
        "target_language": args.target_language,
        "mode": "slide_translation",
        "mock": not args.real,
        "image_width": width,
        "image_height": height,
    }
    if args.ocr_provider:
        payload["ocr_provider"] = args.ocr_provider
    if args.translation_provider:
        payload["translation_provider"] = args.translation_provider

    response = requests.post(args.url, json=payload, timeout=args.timeout)
    if not response.ok:
        _print_response_body(response)
        response.raise_for_status()
    print(json.dumps(response.json(), ensure_ascii=False, indent=2))


def _print_response_body(response: requests.Response) -> None:
    content_type = response.headers.get("content-type", "")
    if "application/json" in content_type.lower():
        try:
            print(json.dumps(response.json(), ensure_ascii=False, indent=2))
            return
        except ValueError:
            pass

    print(response.text)


if __name__ == "__main__":
    main()
