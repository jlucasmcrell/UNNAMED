"""Generate three Stable Audio 3 Small SFX candidates for every Phase-1 audio id.

V1 rendered one candidate per id, which was the right call when one id meant one 125-second render and
the failures turned out to be systematic pipeline bugs rather than unlucky seeds. V2 renders three,
because SA3 is fast enough that the cost is small and because the owner explicitly wants a listening
choice rather than a single take: the brief's own rule is that technical QA must not pick a winner on
aesthetic grounds, so the honest output of an automated pass is a small set of technically valid
candidates with one marked as provisional.

Candidates are named `candidate_a`, `candidate_b`, `candidate_c` under the id's own directory, so a
later selection step never has to guess which file came from which seed.

Resumable: the state file records each completed candidate, and a re-run skips work whose output file
already exists. That matters because this is a ~500-render batch and a crash at 400 should not mean
starting again.

Usage:
    python _generate_sa3.py --audit
    python _generate_sa3.py --apply
    python _generate_sa3.py --apply --limit 6
    python _generate_sa3.py --apply --ids sfx.ui.confirm sfx.ui.back
"""
import argparse
import io
import json
import os
import shutil
import time
import urllib.error
import urllib.request

SERVER = os.environ.get("UNNAMED_AUDIO_SERVER", "http://127.0.0.1:8188")
COMFY_OUTPUT = os.environ.get("UNNAMED_AUDIO_COMFY_OUTPUT", r"C:\Users\jluca\ComfyUI\output")
ASSETS = r"W:\UNNAMED\assets"
SPEC = os.path.join(ASSETS, "manifests", "audio_spec_v2.json")
CANDIDATES = os.path.join(ASSETS, "audio", "v2_candidates")
STATE = os.path.join(ASSETS, "sa3_generate_state.json")
LOG = os.path.join(ASSETS, "sa3_generate.log")

CHECKPOINT = "stable_audio_3_small_sfx.safetensors"
TEXT_ENCODER = "t5gemma_b_b_ul2.safetensors"

# SA3's own settings from the official blueprint. Not V1's 32 steps / cfg 7.0 / euler.
STEPS = 8
CFG = 1.0
SAMPLER = "lcm"
SCHEDULER = "simple"

# No negative prompt. The probe established with a determinism control that changing it alters the
# decoded audio by exactly 0.000000 at cfg 1.0, so supplying one would only record a setting that has
# no effect. The negative conditioner is still wired because the graph requires the input.
NEGATIVE = ""

# EmptyLatentAudio's own floor is 1.0 s, so a 0.3 s UI tick cannot be requested directly. Render with
# room around the event and trim afterwards. The multiplier gives the model context on either side,
# which a bare 0.3 s render would not have.
#
# Loops are the exception and need a different rule. A 20 s ambience bed cannot be produced by
# padding a short render - the crossfade needs material beyond the loop length, and a bed that came
# back at 12 s simply is not the asset that was asked for. This was the cause of the only genuine
# duration failure in the first batch: GEN_MAX clipped all four beds, and the trim then reported a
# 9.1 s error rather than a wrong-length bed.
GEN_MIN = 2.0
GEN_MAX = 40.0
GEN_PADDING = 2.5
LOOP_HEADROOM_S = 3.0



def labels_for_round(number):
    """Three candidate labels per round: a/b/c, then d/e/f, then g/h/i.

    A re-render after the owner rejects every take must not overwrite the rejected candidates. They
    are the evidence for why the sound needs another pass, and the owner may want to compare the new
    round against them. New labels also mean the audition page picks the new candidates up with no
    change to its own logic, and a report naming V2-D is unambiguous about which round it came from.
    """
    if not 1 <= number <= 3:
        raise SystemExit(f"round must be 1..3 (labels a-i are defined); got {number}")
    base = 3 * (number - 1)
    return tuple(chr(ord("a") + base + index) for index in range(3))

# Fixed, documented seed derivation. A candidate's seed is reproducible from the id and the label
# alone, so a regeneration of one candidate does not need the state file to know what seed it used.
SEED_BASE = {"a": 1013904223, "b": 1664525, "c": 22695477,
             "d": 69069, "e": 134775813, "f": 1103515245,
             "g": 214013, "h": 2531011, "i": 2147483647}


def get(path, timeout=90):
    return json.loads(urllib.request.urlopen(SERVER + path, timeout=timeout).read())


def post(path, payload, timeout=180):
    request = urllib.request.Request(SERVER + path, data=json.dumps(payload).encode(),
                                     headers={"Content-Type": "application/json"})
    return json.loads(urllib.request.urlopen(request, timeout=timeout).read())


def seed_for(audio_id, label):
    digest = 0
    for char in audio_id:
        digest = (digest * 131 + ord(char)) & 0xFFFFFFFF
    return (digest ^ SEED_BASE[label]) & 0x7FFFFFFF


def generation_seconds(entry):
    """How long to ask the model for.

    A loop needs the loop length plus crossfade headroom, not a multiple of it: asking for 50 s to get
    a 20 s bed wastes most of the render and, with a cap, silently returns something too short.
    """
    if entry["loop"]:
        return max(GEN_MIN, min(entry["seconds"] + LOOP_HEADROOM_S, GEN_MAX))
    return max(GEN_MIN, min(entry["seconds"] * GEN_PADDING, GEN_MAX))


def graph(entry, seed, seconds, filename_prefix):
    return {
        "1": {"class_type": "CheckpointLoaderSimple", "inputs": {"ckpt_name": CHECKPOINT}},
        "2": {"class_type": "CLIPLoader",
              "inputs": {"clip_name": TEXT_ENCODER, "type": "stable_audio", "device": "default"}},
        "3": {"class_type": "CLIPTextEncode",
              "inputs": {"clip": ["2", 0], "text": entry["prompt"]}},
        "4": {"class_type": "CLIPTextEncode",
              "inputs": {"clip": ["2", 0], "text": NEGATIVE}},
        "5": {"class_type": "EmptyLatentAudio",
              "inputs": {"seconds": round(seconds, 2), "batch_size": 1}},
        "6": {"class_type": "KSampler",
              "inputs": {"model": ["1", 0], "positive": ["3", 0], "negative": ["4", 0],
                         "latent_image": ["5", 0], "seed": seed, "steps": STEPS, "cfg": CFG,
                         "sampler_name": SAMPLER, "scheduler": SCHEDULER, "denoise": 1.0}},
        "7": {"class_type": "VAEDecodeAudio", "inputs": {"samples": ["6", 0], "vae": ["1", 2]}},
        "8": {"class_type": "SaveAudioAdvanced",
              "inputs": {"audio": ["7", 0], "filename_prefix": filename_prefix, "format": "flac"}},
    }


def wait(prompt_id, budget=1200):
    started = time.time()
    delay = 2.0
    while time.time() - started < budget:
        try:
            history = get(f"/history/{prompt_id}")
        except urllib.error.HTTPError:
            history = {}
        if prompt_id in history:
            return history[prompt_id], time.time() - started
        time.sleep(delay)
        delay = min(delay * 1.35, 10.0)
    return None, time.time() - started


def output_path(outputs):
    node = outputs.get("8", {})
    for entry in node.get("audio", []) + node.get("flac", []):
        name = entry.get("filename")
        if name:
            return os.path.join(COMFY_OUTPUT, entry.get("subfolder") or "", name)
    return None


def load_state():
    if os.path.exists(STATE):
        with io.open(STATE, encoding="utf-8") as handle:
            return json.load(handle)
    return {"version": 2, "candidates": {}}


def save_state(state):
    with io.open(STATE, "w", encoding="utf-8") as handle:
        json.dump(state, handle, indent=2)
        handle.write("\n")


def log(line):
    with io.open(LOG, "a", encoding="utf-8") as handle:
        handle.write(line + "\n")


def candidate_path(audio_id, label):
    return os.path.join(CANDIDATES, audio_id, f"candidate_{label}.flac")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--audit", action="store_true")
    parser.add_argument("--apply", action="store_true")
    parser.add_argument("--limit", type=int, default=None)
    parser.add_argument("--ids", nargs="*", default=None)
    parser.add_argument("--round", type=int, default=1,
                        help="1 = candidates a/b/c, 2 = d/e/f, 3 = g/h/i")
    parser.add_argument("--from-report", default=None,
                        help="an audition report; render this round for every id it marks "
                             "needs_rerender, so the rejected list does not have to be retyped")
    parser.add_argument("--prompt-overrides", default=None,
                        help="JSON mapping audio_id to a rewritten prompt. A rejected sound needs "
                             "different wording, not different seeds: see _seed_vs_prompt_probe.py")
    args = parser.parse_args()
    args = parser.parse_args()

    with io.open(SPEC, encoding="utf-8") as handle:
        spec = json.load(handle)

    labels = labels_for_round(args.round)
    entries = spec["sounds"]

    # A rejection means the sound's character is wrong, and the probe showed a new seed reproduces the
    # same character: a rewritten prompt is the only lever that moves it. Overrides replace the prompt
    # for this round while leaving the spec's own prompt untouched, so the record of what was
    # originally asked for survives.
    if args.prompt_overrides:
        with io.open(args.prompt_overrides, encoding="utf-8") as handle:
            overrides = json.load(handle)
        # Keys starting with an underscore are notes for a human, not ids. Without this the file's own
        # explanatory comment is rejected as an unknown audio id, which reads as a broken override
        # rather than a comment.
        overrides = {k: v for k, v in overrides.items() if not k.startswith("_")}
        unknown = sorted(set(overrides) - {e["audio_id"] for e in entries})
        if unknown:
            print(f"  override names ids not in the spec: {unknown[:5]}")
            return 1
        by_id = {e["audio_id"]: e for e in entries}
        for audio_id, prompt in overrides.items():
            by_id[audio_id] = dict(by_id[audio_id], prompt=prompt)
        entries = [by_id[e["audio_id"]] for e in entries]
        print(f"  prompt overrides applied to {len(overrides)} id(s)")

    if args.from_report:
        with io.open(args.from_report, encoding="utf-8") as handle:
            report = json.load(handle)
        if report.get("kind") != "otherreach.audio.v2.audition-report":
            print(f"  not an audition report (kind={report.get('kind')!r})")
            return 1
        wanted = set(report.get("needs_rerender", []))
        known = {e["audio_id"] for e in entries}
        unknown = sorted(wanted - known)
        if unknown:
            print(f"  report names ids not in the spec: {unknown[:5]}")
            return 1
        if not wanted:
            print("  the report marks nothing for re-render; nothing to do")
            return 0
        entries = [e for e in entries if e["audio_id"] in wanted]
        print(f"  from report: {len(entries)} ids the owner rejected outright")

    if args.ids:
        wanted = set(args.ids)
        entries = [e for e in entries if e["audio_id"] in wanted]
        missing = wanted - {e["audio_id"] for e in entries}
        if missing:
            print(f"  unknown ids: {sorted(missing)}")
            return 1
    if args.limit:
        entries = entries[:args.limit]

    state = load_state()
    total = len(entries) * len(labels)
    outstanding = 0
    for entry in entries:
        for label in labels:
            key = f"{entry['audio_id']}#{label}"
            if not (os.path.exists(candidate_path(entry["audio_id"], label))
                    and state["candidates"].get(key, {}).get("ok")):
                outstanding += 1

    print(f"  ids        : {len(entries)}")
    print(f"  candidates : {total}  ({len(labels)} per id)")
    print(f"  outstanding: {outstanding}")
    print(f"  round      : {args.round}  ({chr(47).join(labels)})")
    print(f"  settings   : {STEPS} steps, cfg {CFG}, {SAMPLER}/{SCHEDULER}, negative {'(none)' if not NEGATIVE else NEGATIVE!r}")
    print(f"  output     : {CANDIDATES}")

    if not args.apply:
        print("\n  (audit only; pass --apply to render)")
        return 0

    print()
    started = time.time()
    done = failed = skipped = 0
    for index, entry in enumerate(entries, 1):
        audio_id = entry["audio_id"]
        for label in labels:
            key = f"{audio_id}#{label}"
            target = candidate_path(audio_id, label)
            if os.path.exists(target) and state["candidates"].get(key, {}).get("ok"):
                skipped += 1
                continue
            seed = seed_for(audio_id, label)
            seconds = generation_seconds(entry)
            prefix = f"sa3v2/{audio_id.replace('.', '_')}_{label}"
            try:
                queued = post("/prompt", {"prompt": graph(entry, seed, seconds, prefix),
                                          "client_id": "sa3-v2"})
            except urllib.error.HTTPError as error:
                body = error.read().decode("utf-8", "replace")
                print(f"  {audio_id} [{label}] REJECTED: {body[:220]}")
                log(f"{audio_id}#{label} HTTP {error.code} {body[:400]}")
                failed += 1
                continue

            history, elapsed = wait(queued["prompt_id"])
            if history is None:
                print(f"  {audio_id} [{label}] TIMEOUT")
                log(f"{audio_id}#{label} TIMEOUT")
                failed += 1
                continue
            if history.get("status", {}).get("status_str") != "success":
                detail = json.dumps(history.get("status", {}).get("messages", [])[-2:])[:300]
                print(f"  {audio_id} [{label}] ERROR {detail}")
                log(f"{audio_id}#{label} ERROR {detail}")
                failed += 1
                continue

            source = output_path(history.get("outputs", {}))
            if not source or not os.path.exists(source):
                print(f"  {audio_id} [{label}] no output file")
                log(f"{audio_id}#{label} no output")
                failed += 1
                continue

            os.makedirs(os.path.dirname(target), exist_ok=True)
            shutil.copy2(source, target)
            state["candidates"][key] = {
                "audio_id": audio_id,
                "candidate": label,
                "seed": seed,
                "requested_seconds": round(seconds, 2),
                "model": CHECKPOINT,
                "steps": STEPS, "cfg": CFG, "sampler": SAMPLER, "scheduler": SCHEDULER,
                "negative_prompt": NEGATIVE or None,
                "prompt": entry["prompt"],
                "prompt_id": queued["prompt_id"],
                "seconds_to_render": round(elapsed, 1),
                "ok": True,
            }
            done += 1
            save_state(state)

        if index % 5 == 0 or index == len(entries):
            elapsed_total = time.time() - started
            print(f"  {index}/{len(entries)} ids  rendered {done}  skipped {skipped}  failed {failed}"
                  f"  {elapsed_total/60:.1f} min")

    save_state(state)
    print()
    print(f"  rendered {done}, skipped {skipped}, failed {failed}")
    print(f"  wall clock {(time.time()-started)/60:.1f} min")
    print(f"  state {STATE}")
    return 0 if not failed else 1


if __name__ == "__main__":
    raise SystemExit(main())
