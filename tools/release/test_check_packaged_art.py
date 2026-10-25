import tempfile
from pathlib import Path
from check_packaged_art import problems, main

with tempfile.TemporaryDirectory() as directory:
    path = Path(directory) / "game.log"
    for text, bad in [
        ("502 assets drawn; 0 withheld or unavailable (greybox stands in)", False),
        ("0 assets drawn from the library at boot; 0 withheld or unavailable (greybox stands in)", True),
        ("502 assets drawn; 24 withheld or unavailable (greybox stands in)", True),
        ("WARNING: UNNAMED art: chicken: no rigged file - drawing greybox", True),
        ("binding npc.renn resolves to a rig placeholder", True),
    ]:
        path.write_text(text)
        assert bool(problems(path)) == bad, text
    path = Path(directory) / "art_coverage.json"
    path.write_text('[{"outcome":"fallback","allowed":"old fixture"}]')
    assert problems(path), "Public gate must reject even an allowlisted placeholder"
    path.write_text('{"entries":[{"Outcome":"fallback","Fallbacks":1,"Allowed":"old fixture"}]}')
    assert problems(path), "Native C# coverage uses capitalised record fields"
    path.write_text('{"entries":[{"Outcome":"resolved","Fallbacks":0}]}')
    assert not problems(path)
    assert main([str(path)]) == 0
    path.write_text('{"entries":[{"Outcome":"fallback","Fallbacks":0}]}')
    assert problems(path), "A fallback is rejected even when its count is zero"
    empty = Path(directory) / "empty.stderr"
    empty.write_text("")
    assert main([str(empty)]) == 1, "Empty evidence must not pass"
    path = Path(directory) / "valid.log"
    path.write_text("502 assets drawn; 0 withheld or unavailable (greybox stands in)")
    assert main([str(path), str(empty)]) == 0, "An empty stderr is fine beside positive art evidence"
print("PACKAGED_ART_GATE_CHECK_PASS")
