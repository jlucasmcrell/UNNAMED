"""WP17 step 4: the clipping gate across every build (plan 2.18 step 5) - mh_gate.py on each body in its baked outfit over every game
clip at 12 phases, run headless in parallel (CPU only: no GPU turn), then one table: INTERSECTIONS_<date>.txt and .json.

python gate_builds.py --sex m,f --builds very_lean,lean,average,heavy,very_heavy --out <dir> [--jobs 5] [--report-only]

Two verdicts per body:
  outfit  the plan's absolute rule: no body region through the cloth worn over it ("own" skin), no hole, no hair through headwear in
          any pose - the outfit's own faults (the crotch in a run or crouch, the armpits, the shirt under the jerkin's V, the cap brim)
          fail it on the Average body too, so it is reported, and is the garments' to fix;
  build   what the build itself adds, against its own Average body in the same outfit: per clip no more own skin, gap, hair or
          layer-through-layer vertices than the Average's worst + 10% (+5 vertices of mesh noise). An extreme build ships only if it
          passes this.
Hand and forearm contact with the cloth (the clip pressing a hand into a thigh) is counted and shown, as clip QA, not a verdict.
"""
import argparse
import datetime
import json
import os
import subprocess
from concurrent.futures import ThreadPoolExecutor

HERE = os.path.dirname(os.path.abspath(__file__))
MH = r"C:\Otherreach_Recovery\asset_work\metahuman"
BLENDER = r"C:\Program Files\Blender Foundation\Blender 5.2\blender.exe"
OUTFIT = ("shirt", "trousers", "boots", "jerkin", "hide_cap")

ap = argparse.ArgumentParser()
ap.add_argument("--sex", default="m,f")
ap.add_argument("--builds", default="very_lean,lean,average,heavy,very_heavy")
ap.add_argument("--out", required=True)
ap.add_argument("--jobs", type=int, default=5)
ap.add_argument("--report-only", action="store_true")
ap.add_argument("--fix", action="store_true", help="mh_gate.py --fix on every body: motion clearance written into the baked garments")
ap.add_argument("--clips", default=os.path.join(MH, "work", "clips_veth", "animation", "ready", "player"))
a = ap.parse_args()
os.makedirs(a.out, exist_ok=True)
clips = sorted(os.path.join(a.clips, f) for f in os.listdir(a.clips) if f.endswith(".glb"))


def asset(sex, build):
    return f"mh_veth_{sex}" if build == "average" else f"mh_veth_{sex}_{build}"


SPLAY = {b["id"]: b["arm_splay_deg"] for b in json.load(open(os.path.join(HERE, "builds.json")))["builds"]}


def gate(sex, build):
    body = asset(sex, build)
    out = os.path.join(a.out, f"gate_{body}.json")
    garments = ",".join(os.path.join(MH, "work", "drape", "rigged", f"player_{g}_{sex}__{body}", f"player_{g}_{sex}__{body}_rigged.glb")
                        for g in OUTFIT)
    cmd = [BLENDER, "-b", "--factory-startup", "--python", os.path.join(HERE, "mh_gate.py"), "--",
           "--body", os.path.join(MH, "work", "assembled", body, f"{body}_rigged.glb"), "--garments", garments,
           "--clips", ",".join(clips), "--arm-splay-deg", str(SPLAY[build][sex]), "--out", out] + (["--fix"] if a.fix else [])
    log = subprocess.run(cmd, capture_output=True, text=True).stdout
    line = next((x for x in log.splitlines() if x.startswith("MHGATE")), "MHGATE ERROR " + log[-400:])
    print(body, line[:160], flush=True)
    return out


pairs = [(s, b) for s in a.sex.split(",") for b in a.builds.split(",")]
if not a.report_only:
    with ThreadPoolExecutor(a.jobs) as pool:
        list(pool.map(lambda p: gate(*p), pairs))


def short(name, sex, body):
    return name.replace(f"__{body}", "").replace(f"player_", "").replace(f"_{sex}", "")


def summary(sex, build):
    body = asset(sex, build)
    d = json.load(open(os.path.join(a.out, f"gate_{body}.json")))
    out = {}
    for state, c in d["clips"].items():
        layers = {}
        for p in c["phases"]:
            for pair, n in p["layers"].items():
                layers[short(pair, sex, body)] = max(layers.get(short(pair, sex, body), 0), n)
        out[state] = {"own": c["worst_skin_own"], "contact": c["worst_skin_contact"], "gaps": c["worst_gaps"], "hair": c["worst_hair"],
                      "layers": layers, "own_by": sorted({short(k, sex, body) for p in c["phases"] for k in p["skin_by"]
                                                          if p["skin_own"]})}
    return out


table, verdict, meta = {}, {}, {}
for sex in a.sex.split(","):
    base = summary(sex, "average")
    for build in a.builds.split(","):
        s = summary(sex, build)
        table[asset(sex, build)] = s
        meta[asset(sex, build)] = SPLAY[build][sex]
        faults = []
        for state, c in s.items():
            b = base[state]
            allow = lambda x: int(x * 1.1) + 5
            if c["own"] > allow(b["own"]):
                faults.append(f"{state}: own skin {c['own']} > Average {b['own']}")
            if c["gaps"] > allow(b["gaps"]):
                faults.append(f"{state}: gaps {c['gaps']} > Average {b['gaps']}")
            if c["hair"] > allow(b["hair"]):
                faults.append(f"{state}: hair {c['hair']} > Average {b['hair']}")
            for pair, n in c["layers"].items():
                if n > allow(b["layers"].get(pair, 0)):
                    faults.append(f"{state}: {pair} {n} > Average {b['layers'].get(pair, 0)}")
        outfit = [f"{state}: own {c['own']}, gaps {c['gaps']}, hair {c['hair']}" for state, c in s.items() if c["own"] or c["gaps"] or c["hair"]]
        verdict[asset(sex, build)] = {"pass": not faults, "faults": faults, "outfit_pass": not outfit, "outfit_faults": outfit}

stamp = datetime.date.today().strftime("%Y%m%d")
json.dump({"table": table, "verdict": verdict, "arm_splay_deg": meta}, open(os.path.join(a.out, f"INTERSECTIONS_{stamp}.json"), "w"), indent=1)
lines = [f"WP17 clipping gate {stamp}: {len(clips)} clips x 12 phases, outfit {'+'.join(OUTFIT)}, worst pose per clip, each body at its arm splay",
         "own = body through the cloth worn over it; contact = hand/forearm pressed into cloth by the clip (clip QA); gaps = hidden skin no",
         "garment encloses; layers = lower garment through a higher one. build verdict: each measure within the same sex's Average +10% +5;",
         "outfit verdict: the absolute rule (own, gaps and hair all 0)", ""]
for body, s in table.items():
    v = verdict[body]
    lines.append(f"{body} (arm splay {meta[body]} deg): build {'PASS' if v['pass'] else 'FAIL'}, outfit {'PASS' if v['outfit_pass'] else 'FAIL'}")
    for state, c in s.items():
        lines.append(f"  {state:18} own {c['own']:4} contact {c['contact']:4} gaps {c['gaps']:4} hair {c['hair']:3}  "
                     + " ".join(f"{k} {n}" for k, n in sorted(c["layers"].items())))
    lines += [f"    ! {f}" for f in v["faults"][:12]] + ([f"    ! ... {len(v['faults']) - 12} more"] if len(v["faults"]) > 12 else [])
open(os.path.join(a.out, f"INTERSECTIONS_{stamp}.txt"), "w").write("\n".join(lines) + "\n")
print("\n".join(f"{b}: build {'PASS' if v['pass'] else 'FAIL'} ({len(v['faults'])} faults), outfit {'PASS' if v['outfit_pass'] else 'FAIL'}"
                for b, v in verdict.items()))
