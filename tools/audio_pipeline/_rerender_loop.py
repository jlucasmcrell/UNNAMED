"""One command that turns an audition report into a fresh round of candidates and a rebuilt page.

The problem this solves: rendering is a separate step from listening, the audition page is a static
file that cannot call ComfyUI, and the first version of the re-render path needed a person to read the
report, decide what to change, run three commands in order and rebuild the page. For a pass the owner
does hundreds of times, that means the pipeline lead is in the loop for every rejection, which is the
wrong shape.

What it does, in order:
  1. applies the report, so picks and rejections reach the manifest before anything else runs
  2. for each rejected sound, takes the next untried prompt from the mechanical ladder
  3. renders a new round for exactly those sounds
  4. processes and QA-checks the new candidates
  5. rebuilds the audition page

Every step is the existing tool, invoked rather than reimplemented, so there is one implementation of
each behaviour and a bug fixed in one place stays fixed everywhere.

A sound whose ladder is exhausted is reported rather than re-rendered again on the same prompt. At that
point the question is what the sound should be, not how to reword it, and that is a person's call.

Usage:
    python _rerender_loop.py --report <report.json>            # audit: show what it would do
    python _rerender_loop.py --report <report.json> --apply
"""
import argparse
import io
import json
import os
import subprocess
import sys

TOOL_DIR = os.path.dirname(os.path.abspath(__file__))
ASSETS = r"W:\UNNAMED\assets"
VARIANTS = os.path.join(ASSETS, "manifests", "audio_prompt_variants.json")
STATE = os.path.join(ASSETS, "audio_rerender_state.json")
OVERRIDES = os.path.join(ASSETS, "manifests", "audio_prompt_overrides_auto.json")

# Round 1 is the original spec prompt, rounds 2 and 3 are ladder steps 1 and 2, round 4 is ladder
# step 3. `labels_for_round` in the generator gives a/b/c, d/e/f, g/h/i for rounds 1-3, so a fourth
# round would collide - the ladder is sized to fit inside the labels that exist.
FIRST_LADDER_ROUND = 2
LAST_LADDER_ROUND = 4


def run(args, label):
    result = subprocess.run([sys.executable] + args, cwd=TOOL_DIR,
                            capture_output=True, text=True, timeout=7200)
    if result.returncode != 0:
        print(f"  {label} FAILED")
        for line in (result.stdout or "").splitlines()[-6:]:
            print(f"     {line}")
        for line in (result.stderr or "").splitlines()[-4:]:
            print(f"     {line}")
        return None
    return result.stdout


def load_state():
    if os.path.exists(STATE):
        with io.open(STATE, encoding="utf-8") as handle:
            return json.load(handle)
    return {"rounds": {}}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--report", required=True)
    parser.add_argument("--apply", action="store_true")
    args = parser.parse_args()

    with io.open(args.report, encoding="utf-8") as handle:
        report = json.load(handle)
    if report.get("kind") != "otherreach.audio.v2.audition-report":
        print(f"  not an audition report (kind={report.get('kind')!r})")
        return 1
    rejected = list(report.get("needs_rerender", []))
    if not rejected:
        print("  the report rejects nothing, so there is nothing to re-render")
        print("  (applying it anyway, in case picks changed)")
        if args.apply:
            run(["_apply_audition_report.py", "--report", args.report, "--apply"], "apply")
        return 0

    with io.open(VARIANTS, encoding="utf-8") as handle:
        ladder = json.load(handle)["ladder"]
    state = load_state()

    plan = {}
    exhausted = []
    for audio_id in rejected:
        current = state["rounds"].get(audio_id, 1)
        target = max(FIRST_LADDER_ROUND, current + 1)
        if target > LAST_LADDER_ROUND or audio_id not in ladder:
            exhausted.append((audio_id, current))
            continue
        step = target - FIRST_LADDER_ROUND          # 0-based index into the ladder
        if step >= len(ladder[audio_id]):
            exhausted.append((audio_id, current))
            continue
        plan[audio_id] = {"round": target, "prompt": ladder[audio_id][step]}

    print(f"  report        : {os.path.basename(args.report)}")
    print(f"  rejected      : {len(rejected)}")
    print(f"  will re-render: {len(plan)}")
    for audio_id, item in sorted(plan.items()):
        print(f"    {audio_id:<44} round {item['round']}")
    if exhausted:
        print(f"  ladder exhausted: {len(exhausted)} - these need a person, not another wording")
        for audio_id, round_number in exhausted:
            print(f"    {audio_id:<44} (already at round {round_number})")

    if not plan:
        print("\n  nothing left to try mechanically.")
        if args.apply:
            run(["_apply_audition_report.py", "--report", args.report, "--apply"], "apply")
        return 0

    if not args.apply:
        print("\n  (audit only; pass --apply to run the whole loop)")
        return 0

    print()
    if run(["_apply_audition_report.py", "--report", args.report, "--apply"], "apply") is None:
        return 1
    print("  applied the report")

    # One overrides file per invocation, so a ladder step can never leak onto a sound it was not
    # chosen for.
    with io.open(OVERRIDES, "w", encoding="utf-8") as handle:
        json.dump({k: v["prompt"] for k, v in plan.items()}, handle, indent=2)

    rounds = sorted({item["round"] for item in plan.values()})
    for round_number in rounds:
        ids = [k for k, v in plan.items() if v["round"] == round_number]
        print(f"  rendering round {round_number} for {len(ids)} sound(s)...")
        if run(["_generate_sa3.py", "--round", str(round_number), "--ids"] + ids
               + ["--prompt-overrides", OVERRIDES, "--apply"], f"round {round_number}") is None:
            return 1

    all_ids = sorted(plan)
    if run(["_process_sa3.py", "--apply", "--only"] + all_ids, "process") is None:
        return 1
    print("  processed and QA-checked")

    if run(["_make_audition_package.py", "--apply"], "rebuild page") is None:
        return 1
    print("  rebuilt the audition page")

    for audio_id, item in plan.items():
        state["rounds"][audio_id] = item["round"]
    with io.open(STATE, "w", encoding="utf-8") as handle:
        json.dump(state, handle, indent=2)
        handle.write("\n")

    print()
    print(f"  done. Reload assets/review/audio_v2/index.html")
    print(f"  the re-rendered sounds now carry round {sorted(rounds)} candidates alongside the old ones")
    return 0


if __name__ == "__main__":
    sys.exit(main())
