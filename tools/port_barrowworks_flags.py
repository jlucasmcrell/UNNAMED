"""The Barrowworks' world flags, ported from the frozen M8 design (P4).

Nine flags in the source; eight here. The ninth is `fact:defeat.creature.boss.ring_custodian`, and `DefinitionId`
will not accept it - its pattern is `^[a-z0-9]+(\\.[a-z0-9_]*[a-z_][a-z0-9_]*)+(\\.[0-9]{2})?$`, which admits no
colon, and no id anywhere in the content uses one. It is translated to
`world.barrowworks.ring_custodian_defeated`, which is the shape every other flag in the game has and says the
same thing.

The design is explicit that the Custodian's defeat is read as a DURABLE FACT and not as a kill count -
"a defeat that happened before the quest was offered still counts" - so it stays a world flag rather than
becoming a `kill_creature` objective. That intent survives the rename; the name does not survive as written.
"""
import io
import os

ROOT = r"C:\Otherreach_Build\barrowworks_wt"
OUT = os.path.join(ROOT, "content", "world_flags", "barrowworks")

# id -> (why it exists, what sets it)
FLAGS = {
    "withdrawal_notice_read": ("Whether the withdrawal notice in the intake hall has been read.",
                               "Reading the notice (S-22). The one reading the design puts in the first room a body walks into, so it can be satisfied by walking in."),
    "fitting_note_read": ("Whether the fitting note in the gauge archive has been read.",
                          "Reading the stand in the gauge archive (S-22). K2 of the design's readings."),
    "diagram_rack_read": ("Whether the diagram rack in the gauge archive has been read.",
                          "Reading the rack in the gauge archive (S-22). P of the design's readings."),
    "gate_g3_open": ("Whether the headworks door beyond the measuring court stands open.",
                     "Interacting with the door once a fitting wedge is held (S-22); persisted in its cell's delta."),
    "headworks_preserved": ("Whether Kera was told to leave the mechanism whole.",
                            "Answering the design's choice - preserve (S-22). One of the two endings; either completes the quest."),
    "headworks_dismantled": ("Whether Kera was told to break the mechanism up for what it is made of.",
                             "Answering the design's choice - dismantle (S-22). The other ending, and neither is a failure."),
    "blind_store_read": ("Whether the blind store has been read.",
                         "Reading the store in R09 (S-22). The design's optional report - no objective requires it, and a player who never finds it is never blocked."),
    "ring_custodian_defeated": ("Whether the Ring Custodian has been put down.",
                                "The encounter that owns the creature (M8 I4) writes it on the defeat, as a durable fact rather than a kill count: a defeat that happened before the quest was offered still counts, and the quest cannot be stranded by doing things out of order. Ported from the source's `fact:defeat.creature.boss.ring_custodian`, renamed because a DefinitionId admits no colon."),
}

TEMPLATE = """id: world.barrowworks.{key}
kind: world_flag
schema: 1
display_key: world.barrowworks.{key}.name
tags: [world_flag]
notes: {notes}
type: bool
default: 0
owning_system: S-22
"""


def main():
    os.makedirs(OUT, exist_ok=True)
    for key, (notes, _) in FLAGS.items():
        path = os.path.join(OUT, key + ".yaml")
        tmp = path + ".tmp"
        with io.open(tmp, "w", encoding="utf-8", newline="\n") as handle:
            handle.write(TEMPLATE.format(key=key, notes=notes))
        os.replace(tmp, path)
        print(f"  {key}")
    print(f"  {len(FLAGS)} flags written to content/world_flags/barrowworks/")


if __name__ == "__main__":
    main()
