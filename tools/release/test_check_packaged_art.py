import tempfile
from pathlib import Path
from check_packaged_art import problems

with tempfile.TemporaryDirectory() as directory:
    path = Path(directory) / "game.log"
    for text, bad in [
        ("502 assets drawn; 0 withheld or unavailable (greybox stands in)", False),
        ("502 assets drawn; 24 withheld or unavailable (greybox stands in)", True),
        ("WARNING: UNNAMED art: chicken: no rigged file - drawing greybox", True),
        ("binding npc.renn resolves to a rig placeholder", True),
    ]:
        path.write_text(text)
        assert bool(problems(path)) == bad, text
    path = Path(directory) / "art_coverage.json"
    path.write_text('[{"outcome":"fallback","allowed":"old fixture"}]')
    assert problems(path), "Public gate must reject even an allowlisted placeholder"
print("PACKAGED_ART_GATE_CHECK_PASS")
