"""Undo the over-broad replace in _repair_control_bytes.py.

That script mapped control byte 0x0A back to the source characters that produce it. But 0x0A in a
Markdown file is a newline, not an escaped backtick-n, so it converted all 646 line breaks into the
literal two characters `` `n `` and collapsed the document to a single line.

The correct rule is that a control byte should only be mapped back when it appears where a backtick
escape could plausibly have been written - inside the prose, not as line structure. Line breaks are
line structure.

This restores every `` `n `` to a newline. The document contains no legitimate `` `n `` sequence, since
it is prose and not code containing a backtick-n.
"""
import io

PATH = r"W:\UNNAMED\docs\ASSET_LIBRARY_FORENSIC_AUDIT_2026-09-24.md"

with io.open(PATH, "rb") as handle:
    text = handle.read().decode("utf-8")

before_lines = len(text.splitlines())
count = text.count("`n")
print(f"  lines before: {before_lines}")
print(f"  literal `n occurrences: {count}")

text = text.replace("`n", "\n")

# Any other backtick escape restored by the previous script is legitimate and stays: `a, `b and `f
# were real eaten characters inside markdown code spans.
with io.open(PATH, "w", encoding="utf-8", newline="\n") as handle:
    handle.write(text)

with io.open(PATH, "rb") as handle:
    raw = handle.read()
after = raw.decode("utf-8")
print(f"  lines after : {len(after.splitlines())}")
print(f"  valid UTF-8 : yes")
print(f"  control bytes: {sorted({hex(b) for b in raw if b < 0x20 and b not in (0x0A,)}) or 'none'}")
print()
print("  === first 5 lines ===")
for line in after.splitlines()[:5]:
    print(f"   |{line[:100]}")
print("  === headings ===")
for line in after.splitlines():
    if line.startswith("## "):
        print(f"   {line}")
