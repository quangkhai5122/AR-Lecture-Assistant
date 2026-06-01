#!/usr/bin/env python3
from __future__ import annotations

import argparse
import base64
import json
from pathlib import Path

import requests
from PIL import Image


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--image", required=True, help="Path to sample image")
    parser.add_argument("--url", default="http://127.0.0.1:5000/pipeline/frame")
    parser.add_argument("--mock", action="store_true", help="Force backend mock mode")
    args = parser.parse_args()

    image_path = Path(args.image)
    image_bytes = image_path.read_bytes()
    with Image.open(image_path) as img:
        width, height = img.size

    payload = {
        "frame_id": image_path.stem,
        "image_base64": base64.b64encode(image_bytes).decode("utf-8"),
        "target_language": "vi",
        "mode": "slide_translation",
        "mock": args.mock,
        "image_width": width,
        "image_height": height,
    }

    response = requests.post(args.url, json=payload, timeout=30)
    response.raise_for_status()
    print(json.dumps(response.json(), ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
