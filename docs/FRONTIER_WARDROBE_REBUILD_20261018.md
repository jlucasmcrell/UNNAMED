# Frontier wardrobe rebuild — in progress

The owner ruling of 2026-10-18 fixes approved MetaHuman anatomy. Stature uses
uniform whole-body scale; only restrained native Creator muscularity is permitted
for Kal/Ondrek. No torso/waist/shoulder/limb reshaping, and no further warping of
the rejected old garments. Clothing must be constructed to fit the bodies.

The earlier `outfit_v8_20261018` fits are rejected experiments. They are not
accepted art and are not enabled on shared branches. A/B remain at `d43b6dbf`;
the separately assigned perf/sky lane owns frontier work while this lane handles C.

## Rejected bespoke construction

Private root: `D:\Otherreach_Scratch\frontier_rebuild_20261018`.
New GarmentCode MIT patterns were drafted from measurements of each approved
assembly and draped against that unchanged assembly. Input hashes, design
parameters, flat panels, simulation scenes and output provenance are retained.
No old garment mesh entered this construction. Existing Poly Haven CC0 fabric
scans supply the materials. All current candidates are UNACCEPTED until motion,
layering, reduced LOD and native visual checks pass. Layered02 failed the motion
gate (worst idle/walk/run skin faults 24/95/91, holes 444/462/463, layer faults
1510/926/947). The bespoke drape/fit approach is retired as the production path
under the owner's subsequent standard-pipeline ruling.

## Native MetaHuman control

The active private root is `D:\Otherreach_Scratch\frontier_native_20261018`.
Exact copies of the approved Creator characters are inputs; no body sculpt,
conform, measurement or cloud request is used. Native outfit preview and export
leave the source body's geometry digest unchanged. Native body/head removal
settings explicitly use zero shrink distance.

The supplied Epic default outfit successfully produced a fitted clothing
skeletal mesh. Full optimized character assembly subsequently failed in the
installed face ControlRig link migration. That failure remains recorded; only
the independently produced clothing mesh and reference body were exported.
This is a native pipeline control, not the finished frontier wardrobe.

A direct FBX/glTF control retains all 341 body bones and the face bones, with
1,184 combined native bones. No bone pruning, ancestor-weight merge or custom
corrective fit is applied. Godot imports the body, face and clothing on that
skeleton; its built-in retargeter runs the accepted jog in a headless control.
The export uses eight influences per vertex, so native weight fidelity and
helper/corrective behavior still need measurement. GPU motion review, full
wardrobe construction, masking, all four offered body fits and LOD acceptance
remain pending. The installed bodies and accepted run are unchanged.

An earlier 395-bone body-only rest control also loads in the actual game sheet
harness, with 36 headless phases and zero splay. Its first launch loaded no
model because the private root lacked a catalogue manifest; that launch was a
failure. The staging root was corrected before the successful rerun. Sheet
setup exceptions now terminate with a nonzero exit status rather than later
reporting an empty successful sheet. Headless runs never claim rendered images.

The playtest scope follows `ARMOUR_TIERS_DESIGN.md` sections 3–5: wool underlayer,
standing cap, sealed vest/boots, gloves and cloak; plus a segmented lamework jack,
open-faced lame coif and lame sabatons. Retain 3.5 mm inter-layer clearance and
trousers inside boots. Lames ride individual bones; joint-spanning protection
requires separate articulated segments, never a stretched rigid plate.

Owned armour references read and inventoried:

- Iron Armor Set, Alejandro CG for The Key of the World project, submitted by
  rogerdv; CC-BY 3.0, <https://opengameart.org/content/iron-armor-set>.
- Knight, piacenti; CC-BY 3.0, <https://opengameart.org/content/knight-2>.

The first source is a low-resolution continuous breastplate, not a finished
lamework jack. It needs new segmented construction. Neither source has been
installed as accepted art. No source enters a generative model.

## Content and engine limits

Three items and recipes use existing chest/head/feet slots and smithing at the
anvil. Each consumes the corresponding starting hide piece, which remains part
of the finished visual item. Kera teaches all three through one conversation
choice after her initial forge lesson; her daily routine does not constrain it.
Names are the owner-accepted playtest design names. Current numeric balance is
provisional. Nothing changes the starting kit, save schema or authoritative code.

WP3b has one `ItemWear.Points` value per item instance. It cannot record or repair
individual lames. The three pieces use normal whole-item repair with an iron
ingot; no per-lame state or behaviour is claimed. Wear rates are global inventory
tuning, not per-item fields. Tier-specific noise/mobility modifiers and the Kal
wing-channel comparison are also not implemented by these item definitions.

## Validation checkpoints

- Engine lint: 426 definitions, zero errors after adding the three items/recipes.
- Focused content rerun: 36 pass, zero failures after extending the explicit
  recipe-catalogue pin for the three new recipes.
- Latest completed full serial invocation: 1791 pass, three stale recipe/catalogue
  expectation failures, three existing long skips. All three expectations are
  corrected and pass individually (combined coverage 1794 passes). A clean full
  invocation is running; full-suite pass is not yet claimed. The prior AsyncSave
  cleanup case passed unchanged in the latest serial invocation.
- Three private mask-boundary regressions pass: drawn cut-edge endpoints,
  midpoint-only hole, and UV-seam welding/natural boundaries. This checks the
  diagnostic, not garment acceptance.
- Fresh female first base-two motion gate FAIL: idle/walk/run worst skin
  21/52/73, gaps 15/20/19, inner-layer faults 14/59/91. These body-only drapes
  are rejected as a finished outfit. Revision `layered_02` shortens the newly
  drafted ankle hems and drapes successive pieces over their new lower layers.
- The first cap rest pilot failed face/crown placement. Corrected new topology
  was inspected in side view and leaves the eyes visible; the coif front is
  still an undetailed construction candidate. These are rest views, not motion
  or art acceptance.

Optional `PersonArt.equipment_outfits` now maps equipped item IDs to modular
garments, over the permanent `outfit` underlayer. Main and the creator pass all
six worn armour slots. Definitions without the new map keep their legacy
variant behaviour. Changing equipment removes only owned garment meshes/springs,
resets body masks, and dresses the new pieces without replacing the body or its
current pose. No live binding enables this until its art passes.

Release build succeeds; 245 Presentation tests and 23 Architecture tests pass.
The native headless `--anim-sheet <dir> --anim-equipment-probe` checks seven
selections: base, legs, cap, coif over cap, unequip, re-equip and unchanged repeat.
All seven pass using private unaccepted diagnostic assets. It compares actual
garment asset IDs (not the number of imported LOD meshes), proves current bone
poses remain identical, and checks the body mask changes on equip and returns
to its original value on unequip. Headless reports contain checks, not images.
GPU/native visual and complete wardrobe acceptance remain pending.

No zero-clipping claim, shared C integration, performance acceptance or release
candidate is ready yet.
