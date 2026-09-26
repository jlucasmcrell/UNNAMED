"""Blind external visual review: candidates under shuffled labels (A, B, C, D...) sent to Gemini and Pegasus, raw answers kept.

The reviewers never learn which candidate is new, which one this project made, or which one is expected to win: each run shuffles
the candidates onto letters (the key is written beside the evidence, never sent), images carry only their letter, and uploads
carry neutral names.

    python blind_review.py stills --name veg_near --out DIR --candidate current=a.png --candidate hybrid=b.png [--focus vegetation]
    python blind_review.py videos --name run_loop --out DIR --candidate current=a.mp4 --candidate ual=b.mp4 [--focus animation]
    python blind_review.py models

Stills: Gemini sees every labelled image in one request; Pegasus (video only) watches a slideshow reel of the same labelled
images, 5 s each - a real video watch of stills, recorded as such. Videos: both watch one reel, each clip introduced by a
2-second letter card. Keys: GEMINI_API_KEY / TWELVELABS_API_KEY, or OTHERREACH_REVIEW_CONFIG naming a JSON config that holds
them (the Prompt Studio layout: openai_endpoints[name=gemini].api_key and twelvelabs_api_key). Keys are never written anywhere.
"""
import argparse
import base64
import hashlib
import json
import os
import random
import shutil
import string
import subprocess
import sys
import tempfile
import time

import requests
from PIL import Image, ImageDraw, ImageFont

GEMINI = "https://generativelanguage.googleapis.com/v1beta"
GEMINI_UPLOAD = "https://generativelanguage.googleapis.com/upload/v1beta"
PEGASUS = "https://api.twelvelabs.io/v1.3/analyze"
PEGASUS_RAW_LIMIT = 21 * 1024 * 1024
DEFAULT_GEMINI = "gemini-3.8-flash"

CORE_QUESTION = (
    "Would an ordinary player looking at this believe it is from a modern commercially released RPG rather than a prototype? "
    "Identify every visual reason for your answer.")
VIDEO_QUESTION = "Identify every motion, transition, deformation, clipping, timing or camera issue that breaks the illusion."

RUBRIC = [
    "contemporary commercial PC RPG appearance", "prototype vs production impression", "anatomy", "head/neck integration",
    "face construction", "eyes", "hair", "hands", "skin", "clothing/material quality", "visible equipment", "animation naturalness",
    "foot sliding", "posture", "locomotion", "combat readability", "vegetation dimensionality", "visible billboard/card artifacts",
    "tree silhouette and volume", "ground material repetition", "terrain/vegetation integration", "lighting", "enemy readability",
    "VFX quality", "VFX clipping", "HUD professionalism", "scene coherence", "visual identity (a distinct dark frontier "
    "science-fantasy world rather than a generic fantasy demo)"]

FOCUS = {
    "character": ["anatomy", "head/neck integration", "face construction", "eyes", "hair", "hands", "skin",
                  "clothing/material quality", "visible equipment", "posture", "lighting"],
    "animation": ["animation naturalness", "foot sliding", "posture", "locomotion", "combat readability", "head/neck integration",
                  "hands"],
    "vegetation": ["vegetation dimensionality", "visible billboard/card artifacts", "tree silhouette and volume",
                   "ground material repetition", "terrain/vegetation integration", "lighting", "scene coherence"],
    "vfx": ["VFX quality", "VFX clipping", "combat readability", "lighting", "scene coherence"],
    "enemy": ["enemy readability", "anatomy", "clothing/material quality", "lighting", "combat readability"],
    "face": [],
    "hud": ["HUD professionalism", "scene coherence", "visual identity (a distinct dark frontier science-fantasy world rather "
            "than a generic fantasy demo)"],
}


def keys():
    gemini, pegasus = os.environ.get("GEMINI_API_KEY", ""), os.environ.get("TWELVELABS_API_KEY", "")
    config = os.environ.get("OTHERREACH_REVIEW_CONFIG")
    if config and os.path.exists(config):
        cfg = json.load(open(config, encoding="utf-8"))
        for ep in cfg.get("openai_endpoints", []):
            if (ep.get("name") or "").lower() == "gemini" and not gemini:
                gemini = ep.get("api_key") or os.environ.get(ep.get("api_key_env", ""), "")
        pegasus = pegasus or (cfg.get("twelvelabs_api_key") or "")
    return gemini.strip(), pegasus.strip()


FACE_QUESTIONS = [
    ("IDENTITY", "Does it convincingly represent the intended person shown in reference image R? (R is the target, not a candidate.)"),
    ("ANATOMY", "Does it look like a plausible, finished human head and face?"),
    ("FACE", "Are the eyes, eyelids, mouth, lips, teeth and jaw convincing? List every uncanny or broken detail."),
    ("NECK", "Does the head actually belong to the body - the neck, its join to the chest and shoulders?"),
    ("SKIN AND HAIR", "Are the skin and the hair/scalp convincing?"),
    ("ANIMATION", "Where motion is shown: do the blink, the mouth, the expressions and the eye and head movement deform naturally? "
                  "n/a where none is shown."),
    ("PRODUCTION", "Would this pass as a character in a modern commercially released RPG? Verdict: commercial / borderline / prototype."),
]


def question(kind, labels, focus, context):
    if focus == "face":
        shown = "image" if kind == "stills" else "clip"
        lines = [f"You are a character art director reviewing {len(labels)} {shown}s of candidate game characters' faces, labelled "
                 f"{', '.join(labels)}. " + (context + " " if context else ""),
                 "Review each labelled candidate independently and critically; the labels are random and say nothing about origin.",
                 "For EACH candidate answer every heading below SEPARATELY, in words, citing what you see (do not merge them into one "
                 "score):"]
        lines += [f"- {h}: {q}" for h, q in FACE_QUESTIONS]
        lines.append("Then give two rankings, best to worst: one for IDENTITY and one for PRODUCTION readiness.")
        return "\n".join(lines)
    items = FOCUS.get(focus, RUBRIC) if focus else RUBRIC
    shown = "image" if kind == "stills" else "clip"
    lines = [
        f"You are an art director reviewing {len(labels)} {shown}{'s' if len(labels) > 1 else ''} from a third-person PC role-playing "
        f"game, labelled {', '.join(labels)}. " + (context + " " if context else ""),
        "Review each labelled candidate independently and critically. Do not assume any candidate is better; the labels are random.",
        f"1. For EACH candidate: {CORE_QUESTION} Give a verdict (commercial / borderline / prototype).",
    ]
    if kind == "videos":
        lines.append(f"2. For EACH candidate: {VIDEO_QUESTION} Give timestamps within the reel.")
    lines.append(f"{3 if kind == 'videos' else 2}. Score each candidate 1-10 on each of: " + "; ".join(items) +
                 ". Write n/a where a criterion does not apply to what is shown.")
    lines.append(f"{4 if kind == 'videos' else 3}. Rank the candidates from best to worst and say what would most improve each.")
    return "\n".join(lines)


def font(size):
    for f in ("C:/Windows/Fonts/segoeuib.ttf", "C:/Windows/Fonts/arialbd.ttf"):
        if os.path.exists(f):
            return ImageFont.truetype(f, size)
    return ImageFont.load_default()


def labelled(path, label, out):
    im = Image.open(path).convert("RGB")
    d = ImageDraw.Draw(im)
    s = max(28, im.height // 18)
    d.rectangle([0, 0, int(s * 1.6), int(s * 1.4)], fill=(0, 0, 0))
    d.text((int(s * 0.35), int(s * 0.12)), label, fill=(255, 255, 255), font=font(s))
    im.save(out, quality=92)
    return out


def card(label, size, out):
    im = Image.new("RGB", size, (0, 0, 0))
    ImageDraw.Draw(im).text((size[0] // 2 - size[1] // 10, size[1] // 2 - size[1] // 8), label, fill=(255, 255, 255),
                            font=font(size[1] // 4))
    im.save(out)
    return out


def run(cmd):
    r = subprocess.run(cmd, capture_output=True, text=True)
    if r.returncode != 0:
        raise SystemExit(f"{cmd[0]} failed: {r.stderr[-800:]}")


def probe_size(path):
    r = subprocess.run(["ffprobe", "-v", "error", "-select_streams", "v:0", "-show_entries", "stream=width,height", "-of",
                        "csv=p=0", path], capture_output=True, text=True)
    w, h = r.stdout.strip().split(",")[:2]
    return int(w), int(h)


def reel_from_stills(images, out, seconds=5):
    # Pegasus takes 1:1 to 2.4:1 only: every still is letterboxed onto a 16:9 frame.
    size = (1920, 1080)
    with tempfile.TemporaryDirectory() as tmp:
        framed = []
        for i, (lab, img) in enumerate(images):
            im = Image.open(img).convert("RGB")
            s = min(size[0] / im.width, size[1] / im.height)
            im = im.resize((max(2, int(im.width * s)), max(2, int(im.height * s))), Image.LANCZOS)
            canvas = Image.new("RGB", size, (0, 0, 0))
            canvas.paste(im, ((size[0] - im.width) // 2, (size[1] - im.height) // 2))
            path = os.path.join(tmp, f"framed{i}.png")
            canvas.save(path)
            framed.append((lab, path))
        _reel(framed, out, seconds, size)


def _reel(images, out, seconds, size):
    with tempfile.TemporaryDirectory() as tmp:
        parts = []
        for i, (_, img) in enumerate(images):
            part = os.path.join(tmp, f"{i}.mp4")
            run(["ffmpeg", "-y", "-v", "error", "-loop", "1", "-i", img, "-t", str(seconds), "-vf",
                 f"scale={size[0]}:{size[1]},format=yuv420p", "-r", "24", "-c:v", "libx264", "-crf", "18", part])
            parts.append(part)
        concat(parts, out)


def reel_from_clips(clips, out):
    w, h = probe_size(clips[0][1])
    w, h = min(w, 1920) // 2 * 2, min(h, 1080) // 2 * 2
    with tempfile.TemporaryDirectory() as tmp:
        parts = []
        for i, (label, clip) in enumerate(clips):
            c = card(label, (w, h), os.path.join(tmp, f"card{i}.png"))
            cp = os.path.join(tmp, f"card{i}.mp4")
            run(["ffmpeg", "-y", "-v", "error", "-loop", "1", "-i", c, "-f", "lavfi", "-i", "anullsrc=r=48000:cl=stereo", "-t", "2",
                 "-vf", "format=yuv420p", "-r", "30", "-c:v", "libx264", "-c:a", "aac", "-shortest", cp])
            vp = os.path.join(tmp, f"clip{i}.mp4")
            run(["ffmpeg", "-y", "-v", "error", "-i", clip, "-f", "lavfi", "-i", "anullsrc=r=48000:cl=stereo", "-map", "0:v:0",
                 "-map", "1:a:0", "-shortest", "-vf", f"scale={w}:{h}:force_original_aspect_ratio=decrease,pad={w}:{h}:(ow-iw)/2:"
                 f"(oh-ih)/2,format=yuv420p", "-r", "30", "-c:v", "libx264", "-crf", "20", "-c:a", "aac", vp])
            parts += [cp, vp]
        concat(parts, out)


def concat(parts, out):
    with tempfile.NamedTemporaryFile("w", suffix=".txt", delete=False) as f:
        for p in parts:
            f.write(f"file '{p}'\n")
        listing = f.name
    try:
        run(["ffmpeg", "-y", "-v", "error", "-f", "concat", "-safe", "0", "-i", listing, "-c", "copy", out])
    finally:
        os.remove(listing)


def gemini_generate(key, model, parts):
    r = requests.post(f"{GEMINI}/models/{model}:generateContent", headers={"x-goog-api-key": key},
                      json={"contents": [{"role": "user", "parts": parts}], "generationConfig": {"temperature": 0.2}}, timeout=900)
    return r.status_code, r.json() if r.headers.get("content-type", "").startswith("application/json") else {"text": r.text}


def gemini_upload(key, path, mime):
    size = os.path.getsize(path)
    start = requests.post(f"{GEMINI_UPLOAD}/files", headers={
        "x-goog-api-key": key, "X-Goog-Upload-Protocol": "resumable", "X-Goog-Upload-Command": "start",
        "X-Goog-Upload-Header-Content-Length": str(size), "X-Goog-Upload-Header-Content-Type": mime},
        json={"file": {"display_name": "reel"}}, timeout=600)
    start.raise_for_status()
    url = start.headers.get("X-Goog-Upload-URL") or start.headers.get("x-goog-upload-url")
    with open(path, "rb") as f:
        r = requests.post(url, headers={"Content-Length": str(size), "X-Goog-Upload-Offset": "0",
                                        "X-Goog-Upload-Command": "upload, finalize"}, data=f.read(), timeout=900)
    r.raise_for_status()
    info = r.json()["file"]
    for _ in range(150):
        if info.get("state") == "ACTIVE":
            return info
        if info.get("state") == "FAILED":
            raise SystemExit("Gemini Files API: FAILED")
        time.sleep(2)
        info = requests.get(f"{GEMINI}/{info['name']}", headers={"x-goog-api-key": key}, timeout=120).json()
    raise SystemExit("Gemini Files API: never ACTIVE")


def text_of(response):
    return "\n".join(p.get("text", "") for c in response.get("candidates", []) for p in c.get("content", {}).get("parts", []))


def pegasus(key, path, prompt):
    send, note = path, None
    if os.path.getsize(path) > PEGASUS_RAW_LIMIT:
        dur = float(subprocess.run(["ffprobe", "-v", "error", "-show_entries", "format=duration", "-of", "csv=p=0", path],
                                   capture_output=True, text=True).stdout.strip())
        kbps = max(300, int((PEGASUS_RAW_LIMIT - 1_500_000) * 8 / 1000 / dur) - 96)
        send = path[:-4] + "_pegasus.mp4"
        run(["ffmpeg", "-y", "-v", "error", "-i", path, "-c:v", "libx264", "-b:v", f"{kbps}k", "-c:a", "aac", "-b:a", "96k", send])
        note = f"re-encoded to {kbps} kbps for Pegasus's 21 MB limit: fine-texture judgments are compromised"
    body = {"model_name": "pegasus1.5", "video": {"type": "base64_string",
                                                  "base64_string": base64.b64encode(open(send, "rb").read()).decode()},
            "prompt": prompt, "temperature": 0.2, "stream": False, "max_tokens": 4096}
    for attempt in (1, 2):
        r = requests.post(PEGASUS, json=body, headers={"x-api-key": key}, timeout=900)
        data = r.json() if r.headers.get("content-type", "").startswith("application/json") else {"text": r.text}
        answer = (data.get("data") or "").strip() if isinstance(data, dict) else ""
        # An empty or garbage answer is a failed call, never a verdict (seen 2026-09-07): ask once more.
        if r.status_code < 400 and len(answer) > 40 and answer.count("!") < len(answer) // 4:
            break
    return r.status_code, data, note, attempt


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("kind", choices=["stills", "videos", "models"])
    ap.add_argument("--name")
    ap.add_argument("--out")
    ap.add_argument("--candidate", action="append", default=[], help="name=path (the name stays in the private key)")
    ap.add_argument("--focus", choices=sorted(FOCUS))
    ap.add_argument("--context", default="", help="neutral context for the reviewer (what is shown, never which is new)")
    ap.add_argument("--gemini-model", default=DEFAULT_GEMINI)
    ap.add_argument("--seed", type=int)
    ap.add_argument("--skip", choices=["gemini", "pegasus"], action="append", default=[])
    ap.add_argument("--reference", help="an image shown first as R: a reference (e.g. the intended identity), never a candidate")
    a = ap.parse_args()
    gkey, pkey = keys()
    if a.kind == "models":
        r = requests.get(f"{GEMINI}/models", params={"pageSize": 1000}, headers={"x-goog-api-key": gkey}, timeout=60)
        print("\n".join(sorted(m["name"].split("/")[-1] for m in r.json().get("models", []) if "flash" in m["name"])))
        return
    cands = [c.split("=", 1) for c in a.candidate]
    seed = a.seed if a.seed is not None else int(hashlib.sha256(a.name.encode()).hexdigest()[:8], 16)
    random.Random(seed).shuffle(cands)
    labels = list(string.ascii_uppercase[:len(cands)])
    out = os.path.abspath(a.out)
    inputs = os.path.join(out, "inputs")
    os.makedirs(inputs, exist_ok=True)
    key = {"name": a.name, "seed": seed, "labels": {lab: {"candidate": n, "source": os.path.abspath(p)} for lab, (n, p) in zip(labels, cands)},
           "note": "PRIVATE KEY - never sent to a reviewer"}
    json.dump(key, open(os.path.join(out, "key.json"), "w"), indent=2)
    prompt = question(a.kind, labels, a.focus, a.context)
    open(os.path.join(out, "prompt.md"), "w", encoding="utf-8").write(prompt + "\n")
    record = {"name": a.name, "kind": a.kind, "labels": labels, "gemini_model": a.gemini_model, "started": time.strftime("%Y-%m-%dT%H:%M:%S")}
    previous = os.path.join(out, "run.json")
    if a.skip and os.path.exists(previous):
        # A skipped reviewer's earlier result stays on record (a re-run of one reviewer only).
        record.update({k: v for k, v in json.load(open(previous)).items() if k in a.skip})

    ref = labelled(a.reference, "R", os.path.join(inputs, "R.jpg")) if a.reference else None
    if a.kind == "stills":
        shown = [(lab, labelled(p, lab, os.path.join(inputs, f"{lab}.jpg"))) for lab, (_, p) in zip(labels, cands)]
        if ref:
            shown = [("R", ref)] + shown
        reel = os.path.join(inputs, "reel.mp4")
        reel_from_stills(shown, reel)
        if "gemini" not in a.skip and gkey:
            parts = []
            for lab, img in shown:
                parts += [{"text": "Reference R (the intended identity; not a candidate):" if lab == "R" else f"Candidate {lab}:"},
                          {"inline_data": {"mime_type": "image/jpeg",
                                                                          "data": base64.b64encode(open(img, "rb").read()).decode()}}]
            status, resp = gemini_generate(gkey, a.gemini_model, parts + [{"text": prompt}])
            json.dump(resp, open(os.path.join(out, "gemini_raw.json"), "w"), indent=2)
            open(os.path.join(out, "gemini.md"), "w", encoding="utf-8").write(text_of(resp) or f"(HTTP {status}, no text)")
            record["gemini"] = {"status": status, "mode": "inline images, one request"}
        pegasus_prompt = prompt + f"\nThe video is a slideshow: each candidate image is shown for 5 seconds, its letter in the top-left corner."
    else:
        shown = []
        for lab, (_, p) in zip(labels, cands):
            dst = os.path.join(inputs, f"{lab}{os.path.splitext(p)[1]}")
            shutil.copyfile(p, dst)
            shown.append((lab, dst))
        reel = os.path.join(inputs, "reel.mp4")
        reel_from_clips(shown, reel)
        if "gemini" not in a.skip and gkey:
            info = gemini_upload(gkey, reel, "video/mp4")
            reel_prompt = prompt + "\nThe video is one reel: each candidate clip is introduced by a black card showing its letter."
            head = []
            if ref:
                head = [{"text": "Reference R (the intended identity; not a candidate):"},
                        {"inline_data": {"mime_type": "image/jpeg", "data": base64.b64encode(open(ref, "rb").read()).decode()}}]
            status, resp = gemini_generate(gkey, a.gemini_model, head + [{"file_data": {"mime_type": "video/mp4", "file_uri": info["uri"]}},
                                                                          {"text": reel_prompt}])
            json.dump(resp, open(os.path.join(out, "gemini_raw.json"), "w"), indent=2)
            open(os.path.join(out, "gemini.md"), "w", encoding="utf-8").write(text_of(resp) or f"(HTTP {status}, no text)")
            record["gemini"] = {"status": status, "mode": "true video watch (Files API)"}
        pegasus_prompt = prompt + "\nThe video is one reel: each candidate clip is introduced by a black card showing its letter."
    if "pegasus" not in a.skip and pkey:
        status, resp, note, attempts = pegasus(pkey, reel, pegasus_prompt)
        json.dump(resp, open(os.path.join(out, "pegasus_raw.json"), "w"), indent=2)
        answer = resp.get("data") if isinstance(resp, dict) else None
        open(os.path.join(out, "pegasus.md"), "w", encoding="utf-8").write((answer or f"(HTTP {status}, no text)") + "\n")
        record["pegasus"] = {"status": status, "mode": "true video watch" + (" of a slideshow of the stills" if a.kind == "stills" else ""),
                             "attempts": attempts, "transcoded": note}
    elif "pegasus" not in a.skip:
        record["pegasus"] = {"status": None, "mode": "not run: no Twelve Labs key"}
    if "gemini" not in a.skip and not gkey:
        record["gemini"] = {"status": None, "mode": "not run: no Gemini key"}
    record["finished"] = time.strftime("%Y-%m-%dT%H:%M:%S")
    json.dump(record, open(os.path.join(out, "run.json"), "w"), indent=2)
    print(json.dumps(record))


if __name__ == "__main__":
    main()
