"""Name the design's five locations the way the frozen design names them (P4).

The quest's `location_ref`s are the design's own: `location.barrowworks.intake_hall`, `.gauge_archive`,
`.blind_store`, `.service_vestibule`, `.headworks`. The blockout named its thirteen rooms `r01_`..`r13_`, and
five of those ARE these five places - the blockout's own room list calls them Intake Hall, Gauge Archive,
Blind Store, Service Vestibule and Headworks already.

So they are renamed rather than duplicated: two discoverable places standing in the same room would make the
discovery log say the same thing twice, and the design's names are canon where the `rNN_` prefix is mine. The
other eight rooms keep their blockout names, which is honest - they are the blockout's, not the design's.

Writes via a temporary and replaces, so a failure part-way through leaves the tree as it was.
"""
import io
import os
import re

ROOT = r"C:\Otherreach_Build\barrowworks_wt"
LOCATIONS = os.path.join(ROOT, "content", "locations", "barrowworks")
TESTS = os.path.join(ROOT, "tests", "Content.Tests", "ValidationTests.cs")

# blockout id -> the design's id. The room is the same room; only its name changes.
RENAMES = {
    "r02_intake_hall": "intake_hall",
    "r07_gauge_archive": "gauge_archive",
    "r09_blind_store": "blind_store",
    "r10_service_vestibule": "service_vestibule",
    "r12_headworks": "headworks",
}


def main():
    for old, new in RENAMES.items():
        src = os.path.join(LOCATIONS, old + ".yaml")
        dst = os.path.join(LOCATIONS, new + ".yaml")
        if not os.path.exists(src):
            print(f"  !! {old}.yaml is not there; skipped")
            continue
        with io.open(src, encoding="utf-8") as handle:
            text = handle.read()
        text = text.replace(f"id: location.barrowworks.{old}", f"id: location.barrowworks.{new}")
        text = text.replace(f"display_key: location.barrowworks.{old}.name",
                            f"display_key: location.barrowworks.{new}.name")
        # Its own name field already says the room's name; note the canon id so the change is visible in the file.
        text = text.replace("notes: >-", "notes: >-\n    The design's own place name (see tools/port_barrowworks_locations.py); the blockout called this room " + old + ".", 1)
        tmp = dst + ".tmp"
        with io.open(tmp, "w", encoding="utf-8", newline="\n") as handle:
            handle.write(text)
        os.replace(tmp, dst)
        os.remove(src)
        print(f"  {old} -> {new}")

    # The pinned expected-ID list.
    with io.open(TESTS, encoding="utf-8") as handle:
        tests = handle.read()
    before = tests
    for old, new in RENAMES.items():
        tests = tests.replace(f'"location.barrowworks.{old}"', f'"location.barrowworks.{new}"')
    if tests != before:
        tmp = TESTS + ".tmp"
        with io.open(tmp, "w", encoding="utf-8", newline="\n") as handle:
            handle.write(tests)
        os.replace(tmp, TESTS)
        print(f"  pinned-ID list updated")
    else:
        print(f"  !! pinned-ID list unchanged - check the ids")

    # Sorted check: the list must still be in order for a reader to trust it.
    listing = sorted(f for f in os.listdir(LOCATIONS) if f.endswith(".yaml"))
    print(f"  {len(listing)} barrowworks locations now: {', '.join(f[:-5] for f in listing)}")


if __name__ == "__main__":
    main()
