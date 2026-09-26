"""Character reference images from Google's Gemini image model, anchored to identity images (Phase B remediation, charprod2).

A production reference set needs the SAME person in every view; Z-Image sheets hold one identity per sheet but not a fixed view
layout. This edits from anchor images instead: each request names its anchors and asks for one view or one sheet.

    python gemini_refs.py --anchor face.png [--anchor sheet.png] --prompt "..." --out ref.png [--aspect 21:9] [--size 4K]

Key: GEMINI_API_KEY, or OTHERREACH_REVIEW_CONFIG naming the JSON config that holds it (never written anywhere). Terms: the
Gemini API Additional Terms; Google claims no ownership of generated images; each carries an invisible SynthID watermark. The
request and the model's text reply are written beside the image (<out>.json).
"""
import argparse
import base64
import json
import os
import sys
import time

import requests

GEMINI = "https://generativelanguage.googleapis.com/v1beta"


def key():
    k = os.environ.get("GEMINI_API_KEY", "")
    cfg = os.environ.get("OTHERREACH_REVIEW_CONFIG")
    if not k and cfg and os.path.exists(cfg):
        for ep in json.load(open(cfg, encoding="utf-8")).get("openai_endpoints", []):
            if (ep.get("name") or "").lower() == "gemini":
                k = ep.get("api_key") or os.environ.get(ep.get("api_key_env", ""), "")
    if not k:
        raise SystemExit("no Gemini key")
    return k.strip()


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--anchor", action="append", default=[])
    ap.add_argument("--prompt", required=True)
    ap.add_argument("--out", required=True)
    ap.add_argument("--model", default="gemini-3-pro-image")
    ap.add_argument("--aspect", default="1:1")
    ap.add_argument("--size", default="2K")
    a = ap.parse_args()
    parts = []
    for p in a.anchor:
        mime = "image/png" if p.lower().endswith(".png") else "image/jpeg"
        parts.append({"inline_data": {"mime_type": mime, "data": base64.b64encode(open(p, "rb").read()).decode()}})
    parts.append({"text": a.prompt})
    body = {"contents": [{"role": "user", "parts": parts}],
            "generationConfig": {"responseModalities": ["TEXT", "IMAGE"], "imageConfig": {"aspectRatio": a.aspect, "imageSize": a.size}}}
    t0 = time.time()
    r = requests.post(f"{GEMINI}/models/{a.model}:generateContent", headers={"x-goog-api-key": key()}, json=body, timeout=900)
    data = r.json()
    text, saved = [], False
    for c in data.get("candidates", []):
        for p in c.get("content", {}).get("parts", []):
            inline = p.get("inline_data") or p.get("inlineData")
            if inline and not saved:
                open(a.out, "wb").write(base64.b64decode(inline["data"]))
                saved = True
            elif "text" in p:
                text.append(p["text"])
    json.dump({"model": a.model, "anchors": [os.path.abspath(p) for p in a.anchor], "prompt": a.prompt, "aspect": a.aspect, "size": a.size,
               "http": r.status_code, "seconds": round(time.time() - t0, 1), "saved": saved, "reply_text": "\n".join(text),
               "finish": [c.get("finishReason") for c in data.get("candidates", [])], "error": data.get("error")},
              open(a.out + ".json", "w", encoding="utf-8"), indent=2)
    print(json.dumps({"out": a.out, "saved": saved, "http": r.status_code, "seconds": round(time.time() - t0, 1)}))
    if not saved:
        sys.exit(1)


if __name__ == "__main__":
    main()
