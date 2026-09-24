"""Repair every remaining PowerShell backtick escape in the audit document.

A backtick inside a double-quoted PowerShell string starts an escape sequence. Passing document text
through `python -c` in that context silently replaced `` `a `` with BEL, `` `b `` with backspace and
`` `f `` with form feed, each time consuming the letter that followed. The visible damage was words
losing a leading character inside a markdown code span - `assets/concepts/` became `ssets/concepts/`,
`forge_shed` became `orge_shed` - which is subtle enough to read past.

This maps each escape back to the character it ate and restores it. It also scans for any other control
byte that should not appear in a Markdown document, so the fix is complete rather than a patch on the
two instances that happened to be visible.
"""
import io

PATH = r"W:\UNNAMED\docs\ASSET_LIBRARY_FORENSIC_AUDIT_2026-09-24.md"

# PowerShell backtick escapes that produce a control byte, mapped back to the source characters.
# The backtick itself was consumed along with the following letter.
ESCAPES = {
    0x00: "`0",
    0x07: "`a",
    0x08: "`b",
    0x09: "`t",
    0x0A: "`n",
    0x0B: "`v",
    0x0C: "`f",
    0x0D: "`r",
}

with io.open(PATH, "rb") as handle:
    raw = handle.read()

found = {}
for index, byte in enumerate(raw):
    if byte in ESCAPES:
        found.setdefault(byte, []).append(index)

print("  control bytes found:")
for byte, positions in sorted(found.items()):
    context = raw[max(0, positions[0] - 50):positions[0] + 30].decode("utf-8", "replace")
    print(f"    {hex(byte)} x{len(positions)}  ...{context}...")

text = raw.decode("utf-8")
for byte, replacement in ESCAPES.items():
    if byte in found:
        text = text.replace(chr(byte), replacement)
        print(f"  restored {len(found[byte])} instance(s) of {hex(byte)} -> {replacement!r}")

# Newlines are legitimate; anything else below 0x20 is not.
remaining = sorted({b for b in text.encode("utf-8") if b < 0x20 and b not in (0x09, 0x0A)})
print(f"  remaining control bytes: {[hex(b) for b in remaining] or 'none'}")

with io.open(PATH, "w", encoding="utf-8", newline="\n") as handle:
    handle.write(text)

with io.open(PATH, "rb") as handle:
    after = handle.read().decode("utf-8")
print(f"  lines: {len(after.splitlines())}")
for needle in ("`assets/concepts/`", "`forge_shed`", "`building_smithy`", "`prop_blocked_shaft`"):
    print(f"  {'OK ' if needle in after else 'CHECK'}  {needle}")
