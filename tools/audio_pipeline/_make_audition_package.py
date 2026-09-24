"""Build the owner's V1-versus-V2 listening package.

The brief's requirement is that the owner must be able to listen, quickly, and not open 519 files one
at a time. So this produces per-family folders holding V1 and all three V2 candidates for every id,
plus a single HTML index that plays them in place, and it names the provisional pick so the owner can
start from a working set and override rather than assemble one.

Spectrograms are deliberately not the interface. They were the right tool for finding defects without
ears and they are the wrong tool for judging whether a boar sounds like a boar.

Usage:
    python _make_audition_package.py --apply
"""
import argparse
import io
import json
import os
import shutil

ASSETS = r"W:\UNNAMED\assets"
SPEC = os.path.join(ASSETS, "manifests", "audio_spec_v2.json")
QA = os.path.join(ASSETS, "manifests", "audio_qa_v2.json")
V1_DIR = os.path.join(ASSETS, "audio", "v1_stable_audio_open", "delivered")
CANDIDATES = os.path.join(ASSETS, "audio", "v2_candidates")
OUT = os.path.join(ASSETS, "review", "audio_v2")

# Every label a round can produce: a/b/c then d/e/f then g/h/i. The page shows whichever exist.
ALL_LABELS = tuple(chr(ord("a") + index) for index in range(9))

# Family folders, as the brief lists them.
FAMILY_OF_CATEGORY = {
    "player": "player",
    "weapon": "weapons",
    "creature": "creatures",
    "magic": "magic",
    "crafting": "crafting",
    "interaction": "interaction",
    "ui": "ui",
    "ambience": "ambience",
}


def find_v1(audio_id):
    for suffix in (".wav", ".flac"):
        path = os.path.join(V1_DIR, audio_id + suffix)
        if os.path.exists(path):
            return path
    return None


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--apply", action="store_true")
    args = parser.parse_args()

    with io.open(SPEC, encoding="utf-8") as handle:
        spec = json.load(handle)
    qa = {}
    if os.path.exists(QA):
        with io.open(QA, encoding="utf-8") as handle:
            qa = json.load(handle).get("sounds", {})

    if not args.apply:
        print(f"  ids: {len(spec['sounds'])}  qa entries: {len(qa)}")
        print("  (audit only; pass --apply to build the package)")
        return 0

    for family in sorted(set(FAMILY_OF_CATEGORY.values())):
        os.makedirs(os.path.join(OUT, family), exist_ok=True)

    rows = []
    copied = 0
    for entry in spec["sounds"]:
        audio_id = entry["audio_id"]
        family = FAMILY_OF_CATEGORY.get(entry["category"], "other")
        folder = os.path.join(OUT, family, audio_id)
        os.makedirs(folder, exist_ok=True)

        files = {}
        v1 = find_v1(audio_id)
        if v1:
            target = os.path.join(folder, "V1" + os.path.splitext(v1)[1])
            shutil.copy2(v1, target)
            files["V1"] = os.path.basename(target)
            copied += 1

        # Discover whatever rounds exist on disk. A re-render adds d/e/f, then g/h/i, and a fixed
        # a/b/c list silently drops them - the new candidates render, pass QA, and never appear on the
        # page the owner actually listens from.
        for label in ALL_LABELS:
            source = os.path.join(CANDIDATES, audio_id, f"candidate_{label}.flac")
            if os.path.exists(source):
                target = os.path.join(folder, f"V2-{label.upper()}.flac")
                shutil.copy2(source, target)
                files[f"V2-{label.upper()}"] = os.path.basename(target)
                copied += 1

        record = qa.get(audio_id, {})
        rows.append({
            "audio_id": audio_id,
            "family": family,
            "group": entry["group"],
            "seconds": entry["seconds"],
            "prompt": entry["prompt"],
            "prompt_v1": entry.get("prompt_v1"),
            "source": entry.get("source", "replacement"),
            "files": files,
            "provisional": record.get("provisional"),
            "survivors": record.get("survivors", []),
            "relative": os.path.join(family, audio_id).replace(os.sep, "/"),
        })

    write_index(rows)
    write_metadata(rows)
    print(f"  ids packaged : {len(rows)}")
    print(f"  audio copied : {copied}")
    print(f"  index        : {os.path.join(OUT, 'index.html')}")
    return 0


def write_metadata(rows):
    with io.open(os.path.join(OUT, "audition_index.json"), "w", encoding="utf-8") as handle:
        json.dump({
            "comment": [
                "Owner audition index for the Stable Audio 3 pass.",
                "",
                "For each id: the V1 original, three V2 candidates, and the candidate an automated",
                "non-aesthetic rule picked as provisional. The owner may replace any provisional",
                "pick; nothing here is approved.",
            ],
            "human_auditioned": False,
            "ids": rows,
        }, handle, indent=2)
        handle.write("\n")


def write_index(rows):
    """The listening page: pick a preference per id, then export a report.

    Three things it has to get right, because the owner will spend an hour in it.

    Picks must survive a reload. 228 ids is not a sitting, and losing the work halfway through the
    list would make the tool worse than paper. State goes to localStorage on every change.

    V1 has to be selectable. The brief says a provisional pick may be replaced, and for some ids the
    right replacement is the old sound. Offering only "which V2" would quietly force a change
    everywhere.

    Undecided must stay distinguishable from decided. If every id started on its provisional pick,
    "I listened and agreed" and "I never reached it" would be the same value in the report, and the
    report would claim 228 auditions that never happened.
    """
    import html as html_module

    ids_payload = [{"id": r["audio_id"], "files": r["files"], "provisional": r["provisional"]}
                   for r in rows]

    by_family = {}
    for row in rows:
        by_family.setdefault(row["family"], []).append(row)

    head = """<!doctype html><html><head><meta charset="utf-8">
<title>Otherreach Phase-1 audio - V1 vs V2 audition</title>
<style>
 :root{--bg:#12131a;--panel:#1b1d27;--line:#262838;--fg:#e6e6ee;--dim:#8b8b9c;
       --gold:#ffd88a;--green:#8ee08a;--blue:#9fd0ff;--red:#ff8f8f}
 *{box-sizing:border-box}
 body{background:var(--bg);color:var(--fg);font:13px/1.5 system-ui,Segoe UI,sans-serif;margin:0}
 header{position:sticky;top:0;z-index:20;background:#0d0e14;border-bottom:1px solid var(--line);
        padding:10px 18px;display:flex;gap:16px;align-items:center;flex-wrap:wrap}
 h1{font-size:15px;margin:0;font-weight:600}
 h2{font-size:14px;margin:26px 18px 8px;color:var(--blue)}
 .grow{flex:1}
 .prog{font-variant-numeric:tabular-nums;color:var(--dim)}
 .prog b{color:var(--fg)}
 button{background:var(--panel);color:var(--fg);border:1px solid var(--line);border-radius:6px;
        padding:5px 11px;font:inherit;cursor:pointer}
 button:hover{border-color:#3d4160;background:#222533}
 button.primary{background:#26405c;border-color:#39628c}
 .note{color:var(--dim);max-width:1000px;margin:12px 18px}
 .id{border-top:1px solid var(--line);padding:10px 18px}
 .id.decided{background:#161a20}
 .id.kept-v1{background:#1d1a16}
 .id.rejected-all{background:#231618}
 .none{display:flex;gap:5px;align-items:center;font-size:11px;color:var(--red);
       background:var(--panel);border:1px solid var(--line);border-radius:6px;padding:5px 10px;
       cursor:pointer}
 .none.picked{border-color:var(--red);box-shadow:0 0 0 1px var(--red) inset}
 .name{font-weight:600;color:var(--gold);font-size:13px}
 .meta{color:var(--dim);font-size:11px;margin:1px 0 4px}
 .prompt{color:#b9b9c8;font-size:11px;margin:3px 0 6px;max-width:1100px}
 .prompt b{color:#d8d8e6;font-weight:600}
 .row{display:flex;flex-wrap:wrap;gap:10px;align-items:flex-start}
 .cell{background:var(--panel);border:1px solid var(--line);border-radius:6px;padding:6px 8px}
 .cell.picked{border-color:var(--green);box-shadow:0 0 0 1px var(--green) inset}
 .cell.picked.prov{border-color:var(--green)}
 label.cell{cursor:pointer}
 .cell b{display:block;font-size:11px;color:#c9c9d8;margin-bottom:3px}
 .cell .tag{font-weight:600}
 .v1 .tag{color:#9fb0c8}.prov .tag{color:var(--green)}
 audio{height:30px;width:250px;display:block}
 .pick{display:flex;gap:5px;align-items:center;margin-top:5px;font-size:11px;color:var(--dim)}
 input[type=radio]{accent-color:#8ee08a;cursor:pointer}
 .acts{display:flex;gap:8px;align-items:center;margin-top:7px;flex-wrap:wrap}
 input.notein{background:#0f1016;border:1px solid var(--line);color:var(--fg);border-radius:5px;
              padding:4px 8px;font:inherit;width:340px}
 .status{font-size:11px;color:var(--dim)}
 #panel{position:fixed;inset:0;background:rgba(6,7,10,.92);z-index:50;padding:28px;
        overflow:auto;display:none}
 #panel.on{display:block}
 #panel .box{max-width:1000px;margin:0 auto}
 textarea{width:100%;height:340px;background:#0f1016;color:#cfe6cf;border:1px solid var(--line);
          border-radius:8px;padding:12px;font:12px/1.45 ui-monospace,Consolas,monospace}
 .hint{color:var(--dim);font-size:11px;margin:6px 0 10px}
 kbd{background:#222533;border:1px solid var(--line);border-radius:4px;padding:0 5px;font-size:11px}
</style></head><body>
<header>
  <h1>Otherreach Phase-1 audio &mdash; V1 vs V2</h1>
  <span class="prog" id="prog"></span>
  <span class="grow"></span>
  <button onclick="jumpNext()">Next undecided</button>
  <button class="primary" onclick="openReport()">Export report</button>
  <button onclick="resetAll()">Reset</button>
</header>
<div class="note">
  V1 is Stable Audio Open 1.0. V2 is Stable Audio 3 Small SFX. Pick the one you prefer per sound &mdash;
  <b>V1 is a valid choice</b>, and for some sounds it may be the right one. If none of them work, pick
  <b>none of these</b> and it is queued for a re-render with fresh seeds instead of being forced.
  Your picks save automatically in this browser. <b>No machine has listened to any of this</b>: technical
  QA checked duration, level, clipping, silence, channels and loop seams, and nothing else. Whether a
  sound is convincing is yours.
  <div class="hint" style="margin-top:8px">
    <kbd>1</kbd> <kbd>2</kbd> <kbd>3</kbd> <kbd>4</kbd> pick V1/A/B/C for the focused sound &nbsp;&middot;&nbsp;
    <kbd>0</kbd> none of these &nbsp;&middot;&nbsp;
    <kbd>Space</kbd> play all in a row &nbsp;&middot;&nbsp; <kbd>n</kbd> next undecided
  </div>
</div>
"""

    parts = [head]
    for family in sorted(by_family):
        parts.append(f"<h2>{family} ({len(by_family[family])})</h2>")
        for row in by_family[family]:
            audio_id = row["audio_id"]
            escaped = html_module.escape(audio_id, quote=True)
            parts.append(f'<div class="id" id="card-{escaped}" data-id="{escaped}">')
            parts.append(f'<div class="name">{html_module.escape(audio_id)}</div>')
            parts.append(f'<div class="meta">{row["group"]} &middot; {row["seconds"]}s &middot; '
                         f'{row["source"]}</div>')
            if row.get("prompt_v1"):
                parts.append(f'<div class="prompt"><b>V1 prompt:</b> '
                             f'{html_module.escape(row["prompt_v1"])}</div>')
            parts.append(f'<div class="prompt"><b>V2 prompt:</b> '
                         f'{html_module.escape(row["prompt"])}</div>')
            parts.append('<div class="row">')

            order = [k for k in ("V1", "V2-A", "V2-B", "V2-C", "V2-D", "V2-E", "V2-F",
                                 "V2-G", "V2-H", "V2-I") if k in row["files"]]
            for key in order:
                name = row["files"][key]
                provisional = (key != "V1" and row["provisional"]
                               and key == f'V2-{row["provisional"].upper()}')
                css = "cell"
                if key == "V1":
                    css += " v1"
                if provisional:
                    css += " prov"
                tag = key
                if key == "V1":
                    tag += ' <span class="tag">(original)</span>'
                elif provisional:
                    tag += ' <span class="tag">provisional</span>'
                parts.append(f'<div class="{css}" data-key="{key}">')
                parts.append(f'<b>{tag}</b>')
                # `controls` is what makes the player visible. It was dropped when this markup was
                # rewritten for the interactive version, which left 857 audio elements present in the
                # DOM and not one of them playable - the page looked as though the players had gone.
                parts.append(f'<audio controls preload="none" '
                             f'src="{row["relative"]}/{name}"></audio>')
                parts.append(f'<div class="pick"><input type="radio" name="pick-{escaped}" '
                             f'value="{key}" onchange="pick(\'{escaped}\',this.value)">'
                             f'<span>prefer this</span></div>')
                parts.append("</div>")

            parts.append("</div>")
            parts.append('<div class="acts">'
                         f'<button onclick="playRow(\'{escaped}\')">Play all in order</button>'
                         f'<label class="none"><input type="radio" name="pick-{escaped}" '
                         f'value="NONE" onchange="pick(\'{escaped}\',this.value)">'
                         f'<span>none of these &mdash; re-render</span></label>'
                         f'<span class="status" id="st-{escaped}"></span>'
                         f'<input class="notein" placeholder="note for this sound (optional)" '
                         f'oninput="note(\'{escaped}\',this.value)"></div>')
            parts.append("</div>")

    parts.append('<div id="panel"><div class="box">'
                 '<h1 style="font-size:16px">Audition report</h1>'
                 '<div class="hint">Copy this, or download it, and give it back. '
                 'Undecided sounds are listed as undecided and are not claimed as auditions.</div>'
                 '<div style="margin-bottom:10px">'
                 '<button class="primary" onclick="copyReport()">Copy</button> '
                 '<button onclick="downloadReport()">Download JSON</button> '
                 '<button onclick="closeReport()">Close</button>'
                 '<span class="status" id="copystat"></span></div>'
                 '<textarea id="reporttext" readonly></textarea>'
                 '</div></div>')

    parts.append("<script>\nconst IDS = " + json.dumps(ids_payload) + ";\n")
    parts.append(r"""
const KEY = 'otherreach.audio.v2.audition.v1';
let state = load();

function load(){ try { return JSON.parse(localStorage.getItem(KEY)) || {picks:{},notes:{}}; }
                 catch(e){ return {picks:{},notes:{}}; } }
function save(){ localStorage.setItem(KEY, JSON.stringify(state)); }
// getElementById takes the raw id. Every id here contains dots, and passing CSS.escape() through it
// yields 'sfx\.ui\.select', which matches nothing - the cards would render but the status line would
// silently never update and the page would look like it had lost the picks.
function card(id){ return document.getElementById('card-'+id); }

function pick(id, value){
  state.picks[id] = value;
  save(); render(id);
}
function note(id, value){
  if (value.trim()) { state.notes[id] = value.trim(); } else { delete state.notes[id]; }
  save();
}
function render(id){
  const c = card(id); if (!c) return;
  const chosen = state.picks[id];
  c.classList.toggle('decided', !!chosen);
  c.classList.toggle('kept-v1', chosen === 'V1');
  c.classList.toggle('rejected-all', chosen === 'NONE');
  c.querySelectorAll('.cell').forEach(cell => {
    const on = cell.dataset.key === chosen;
    cell.classList.toggle('picked', on);
    const radio = cell.querySelector('input[type=radio]');
    if (radio) radio.checked = on;
  });
  const none = c.querySelector('.none');
  if (none) none.classList.toggle('picked', chosen === 'NONE');
  const st = document.getElementById('st-'+id);
  if (st) {
    st.textContent = chosen === 'NONE'
      ? 'you rejected all of these - queued for re-render'
      : (chosen ? ('your pick: ' + chosen) : 'not decided yet');
  }
}
function renderAll(){
  IDS.forEach(r => render(r.id));
  const decided = IDS.filter(r => state.picks[r.id]).length;
  const reject = IDS.filter(r => state.picks[r.id] === 'NONE').length;
  document.getElementById('prog').innerHTML =
    '<b>' + decided + '</b> / ' + IDS.length + ' decided' +
    (reject ? ' &middot; <b style="color:#ff8f8f">' + reject + '</b> to re-render' : '');
}
function playRow(id){
  const c = card(id); if (!c) return;
  const audios = Array.from(c.querySelectorAll('audio'));
  let i = 0;
  const next = () => {
    if (i >= audios.length) { return; }
    const a = audios[i++];
    a.currentTime = 0; a.play();
    a.onended = next;
  };
  audios.forEach(a => { a.pause(); a.onended = null; });
  next();
}
function focusFirstUndecided(from){
  const list = IDS.map(r => r.id);
  const start = from === undefined ? 0 : from + 1;
  for (let i = start; i < list.length; i++) {
    if (!state.picks[list[i]]) {
      const c = card(list[i]);
      c.scrollIntoView({block:'center'}); c.focus();
      window.__focus = i;
      return;
    }
  }
}
function jumpNext(){ focusFirstUndecided(window.__focus); }
function openReport(){
  document.getElementById('reporttext').value = buildReport();
  document.getElementById('panel').classList.add('on');
}
function closeReport(){ document.getElementById('panel').classList.remove('on'); }
function buildReport(){
  const picks = {}, notes = {}, undecided = [], needsRerender = [];
  IDS.forEach(r => {
    const p = state.picks[r.id];
    if (!p) { undecided.push(r.id); return; }
    if (p === 'NONE') { needsRerender.push(r.id); return; }
    picks[r.id] = p;
  });
  Object.keys(state.notes).forEach(k => { notes[k] = state.notes[k]; });
  const counts = {};
  Object.values(picks).forEach(v => { counts[v] = (counts[v]||0)+1; });
  return JSON.stringify({
    kind: 'otherreach.audio.v2.audition-report',
    version: 2,
    generated: new Date().toISOString(),
    human_auditioned: true,
    total_ids: IDS.length,
    decided: Object.keys(picks).length + needsRerender.length,
    undecided_count: undecided.length,
    counts_by_selection: counts,
    kept_v1: Object.keys(picks).filter(k => picks[k] === 'V1'),
    selections: picks,
    // Rejected outright rather than held back for later. Distinct from undecided: these were heard
    // and none of the available takes was usable, so they need new renders rather than a decision.
    needs_rerender: needsRerender,
    needs_rerender_count: needsRerender.length,
    notes: notes,
    undecided: undecided
  }, null, 2);
}
function copyReport(){
  const t = document.getElementById('reporttext');
  t.select(); navigator.clipboard.writeText(t.value);
  document.getElementById('copystat').textContent = 'copied';
}
function downloadReport(){
  const blob = new Blob([buildReport()], {type:'application/json'});
  const a = document.createElement('a');
  a.href = URL.createObjectURL(blob);
  a.download = 'otherreach_audio_v2_audition_report.json';
  a.click();
}
function resetAll(){
  if (!confirm('Clear every pick and note? This cannot be undone.')) return;
  state = {picks:{}, notes:{}}; save(); renderAll();
  document.querySelectorAll('input.notein').forEach(i => i.value = '');
}
document.addEventListener('keydown', e => {
  if (e.target.tagName === 'INPUT' || e.target.tagName === 'TEXTAREA') return;
  const c = document.activeElement && document.activeElement.closest
            ? document.activeElement.closest('.id') : null;
  if (e.key === 'n') { jumpNext(); return; }
  if (!c) return;
  const id = c.dataset.id;
  if (e.key === ' ') { e.preventDefault(); playRow(id); return; }
  const map = {'1':'V1','2':'V2-A','3':'V2-B','4':'V2-C',
               '5':'V2-D','6':'V2-E','7':'V2-F','0':'NONE'};
  if (map[e.key]) { pick(id, map[e.key]); }
});
renderAll();
""")
    parts.append("</script></body></html>")

    with io.open(os.path.join(OUT, "index.html"), "w", encoding="utf-8") as handle:
        handle.write("\n".join(parts))

    # Assert the page is actually usable before declaring success. An <audio> without `controls` is
    # present in the DOM, counts in every structural check, and cannot be played - so a page that
    # looks complete can hand the owner 228 sounds they have no way to hear.
    import re as _re
    players = _re.findall(r"<audio[^>]*>", "\n".join(parts))
    without_controls = [p for p in players if "controls" not in p]
    print(f"  players      : {len(players)}  without controls: {len(without_controls)}")
    if without_controls or not players:
        raise SystemExit("refusing to ship an audition page with unplayable audio elements")
    if len(players) != sum(len(r["files"]) for r in rows):
        raise SystemExit(f"expected {sum(len(r['files']) for r in rows)} players, found {len(players)}")


if __name__ == "__main__":
    raise SystemExit(main())
