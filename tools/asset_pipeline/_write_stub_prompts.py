"""Generate sacrificial-stub concept prompts for hard weapon components.

Regenerates only the components a stub can rescue, using the standardized convention from
`_stub_spec.py`: present the component attached to a plain undecorated stub of its
neighbour, so the generator sees a whole object, then cut the stub in Blender.

Wearable armour is deliberately excluded - the armour bakeoff decides that method.

Usage:
    python _write_stub_prompts.py
"""
import json
import os

OUT = r"W:\UNNAMED\assets\requests\_stub_prompts.json"

# The stub must be BOUNDED, not merely described as plain. Probing the first stub batch
# showed the generator rendering the "plain stub" as most of the frame - a focus crystal came
# out of a 0.92 m mesh, a grip out of 0.91 m - so the plain section was a full weapon length
# rather than a stub. "Plain and undecorated" was not enough; the proportion has to be stated,
# and restated as a fraction of the image, because that is the form the model responds to.
STUB_PHRASE = ("{neighbour} which is completely plain undecorated bare material with no "
               "detail no wrapping no decoration of any kind, of uniform thickness "
               "throughout, and this plain section is SHORT - it is only about one quarter "
               "of the total height of the image and the detailed part fills the rest, the "
               "plain section begins below the detailed part and runs down to the bottom "
               "edge of the frame")

# Component-specific description of the detailed part, plus which neighbour it stubs into.
COMPONENTS = [
    {
        "id": "weaponcomp_mace_head_flanged_a",
        "neighbour": "a short straight ash haft",
        "detail": ("a heavy steel mace head with six radiating vertical flanges around a "
                   "central boss, the head mounted on top of the haft"),
        "framing": "side view, the mace shown whole and complete",
    },
    {
        "id": "weaponcomp_pommel_counterweight_a",
        "neighbour": "a short straight ash haft",
        "detail": ("a squat faceted pear-shaped iron counterweight, the weight fitted on the "
                   "bottom end of the haft"),
        "framing": "side view, the mace shown whole and complete",
    },
    {
        "id": "weaponcomp_grip_standard_a",
        "neighbour": "a plain straight steel blade tang",
        "detail": ("a short cylindrical grip bound in dark brown leather cord with a subtle "
                   "waist in the middle where a hand closes, the grip fitted around the base "
                   "of the blade with plain iron collar rings at each end"),
        "framing": "side view, a complete sword shown whole",
    },
    {
        "id": "weaponcomp_grip_vaskaal_a",
        "neighbour": "a plain straight steel blade tang",
        "detail": ("a slim elongated alien grip of dark seamless matte unfamiliar material "
                   "with three deep finger channels along one side and two opposing thumb "
                   "channels on the other, clearly not shaped for a human hand, the grip "
                   "fitted around the base of the blade"),
        "framing": "side view, a complete weapon shown whole",
    },
    {
        "id": "weaponcomp_blade_arming_sword_a",
        "neighbour": "a short plain leather-wrapped grip",
        "detail": ("a straight double-edged steel arming sword blade with a central fuller "
                   "running two thirds of its length and a plain crossguard, the blade "
                   "mounted above the grip"),
        "framing": "front view, a complete sword shown whole",
    },
    {
        "id": "weaponcomp_shield_heater_a",
        "neighbour": "a plain flat unpainted wooden board behind it",
        "detail": ("a flat-topped curved-bottom heater shield of bare unpainted wooden planks "
                   "with a plain iron rim and a simple central iron boss, its reverse showing "
                   "two plain leather arm straps"),
        "framing": "three-quarter view showing both the face and the strapped reverse",
    },
    {
        "id": "magiccomp_focus_crystal_a",
        "neighbour": "a plain straight undecorated wooden staff",
        "detail": ("a faceted pale blue-white crystal roughly the size of a fist held in a "
                   "plain brass claw mount, the mount fixed to the top end of the staff, the "
                   "crystal glowing faintly from within"),
        "framing": "three-quarter view, the staff shown whole and complete",
    },
    {
        "id": "weaponcomp_haft_short_a",
        "neighbour": "a plain undecorated steel mace head",
        "detail": ("a short straight round ash haft with plain iron ferrules near each end "
                   "and a slightly tapered tip, the plain haft running down from the head"),
        "framing": "side view, the complete mace shown whole",
    },
    {
        "id": "weaponcomp_haft_long_a",
        "neighbour": "a plain undecorated steel spearhead",
        "detail": ("a long straight round ash polearm haft with plain iron ferrules at "
                   "intervals and a leather grip wrap at the lower third, the haft running "
                   "down from the head"),
        "framing": "side view, the complete polearm shown whole",
    },
]


def main():
    prompts = []
    for component in COMPONENTS:
        prompt = (
            f"game asset concept art of {component['detail']}, the whole weapon shown "
            f"complete, with {STUB_PHRASE.format(neighbour=component['neighbour'])}, "
            f"the plain section being roughly one third the length of the detailed part "
            f"above it, isolated on a plain flat light grey background, "
            f"{component['framing']}"
        )
        prompts.append({
            "id": component["id"],
            "prompt": prompt,
            "stub_convention": "sacrificial_stub_v1",
            "neighbour": component["neighbour"],
        })

    with open(OUT, "w", encoding="utf-8") as handle:
        json.dump(prompts, handle, indent=2)
    print(f"  wrote {len(prompts)} stub concepts to {OUT}")
    for item in prompts:
        print(f"    {item['id']}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
