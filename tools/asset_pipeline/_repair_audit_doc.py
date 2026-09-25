"""Repair the audit document after a PowerShell backtick escape corrupted it.

Cause: the fix was passed through `python -c` inside a double-quoted PowerShell string. PowerShell
treats a backtick as an escape character, so the sequence backtick-b in the inserted text became a
backspace (0x08) and consumed the following character. The executive summary lost part of two lines
and gained an insertion belonging in Part 7.

This restores the executive-summary wording and places the correction in the right section, using a
file rather than `-c` so no shell can reinterpret the content.
"""
import io

PATH = r"W:\UNNAMED\docs\ASSET_LIBRARY_FORENSIC_AUDIT_2026-09-24.md"

with io.open(PATH, "rb") as handle:
    raw = handle.read()

report = {
    "utf8_em_dash": raw.count(b"\xe2\x80\x94"),
    "cp1252_0x97": raw.count(b"\x97"),
    "cp1252_0x92": raw.count(b"\x92"),
    "backspace_0x08": raw.count(b"\x08"),
}
print(f"  before: {report}")

text = raw.decode("utf-8", errors="replace")

# 1. Remove the wrongly-placed block from the executive summary. It was inserted where a sentence
#    about the visual gap belonged, and the backspace escape ate a character from three of its lines.
start = text.find("**Already discarded by the pipeline itself**")
if start >= 0:
    end = text.find("`prop_blocked_shaft` is a shattered pile")
    if end > start:
        text = text[:start] + text[end:]
        print("  removed the misplaced block")

# 2. Restore the sentence it displaced.
broken = "**Is the owner's impression substantially correct? Yes on the visual gap, no on the diagnosis.**\n\n\n"
if broken in text:
    text = text.replace(
        broken,
        "**Is the owner's impression substantially correct? Yes on the visual gap, no on the "
        "diagnosis.**\n\nThe gap is real and in places severe \u2014 `building_smithy` is a "
        "catastrophic crumpled mass, and\n",
        1,
    )
    print("  restored the displaced sentence")

# 3. Put the correction where it belongs: in the acceptance list, replacing the discard line.
anchor = "- `building_smithy` \u2014 failed; a crumpled sheet\n"
if anchor in text:
    text = text.replace(
        anchor,
        "**Already discarded by the pipeline itself** \u2014 no action needed, and it is the model to "
        "copy.\n"
        "`building_smithy` and `building_lodge` were both detected as failures and archived to\n"
        "`assets/_superseded/reconstruction_failed/`. Their review renders remain in `bible_batch`,\n"
        "which is how they appear to a reader as current assets. **They are not.**\n",
        1,
    )
    print("  corrected the acceptance list")
else:
    print("  WARNING: acceptance-list anchor not found")

# 4. Normalise the stray CP1252 punctuation. The file had both encodings mixed, which is why an
#    em-dash search kept failing.
text = text.replace("\x97", "\u2014").replace("\x92", "\u2019")
text = text.replace("\x08", "")

with io.open(PATH, "w", encoding="utf-8", newline="\n") as handle:
    handle.write(text)

with io.open(PATH, "rb") as handle:
    after = handle.read()
print(f"  after:  utf8_em_dash={after.count(chr(0xe2).encode()+chr(0x80).encode()+chr(0x94).encode())} "
      f"cp1252_0x97={after.count(bytes([0x97]))} backspace={after.count(bytes([8]))}")
print(f"  lines: {len(text.splitlines())}")
