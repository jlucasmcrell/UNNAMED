# Independent reference for worldgen 2 (M2b), written from the contract: semantic channels
# Random(seed, cell, subsystem, key, sample); node keys node.<cell>.<rule>.<NN>; slot keys
# <cell>.<pop id>.<NN>; cell baseline digest over outputs; probe digest over 4 fixed cells;
# fingerprint over identity + profile digest + probe digest.
import hashlib
import struct

MASK = (1 << 64) - 1
CELL_CM = 100 * 100


def canonical(*fields):
    h = hashlib.sha256()
    for f in fields:
        if isinstance(f, bool):
            raise TypeError("bool")
        if isinstance(f, str):
            b = f.encode("utf-8")
            h.update(struct.pack(">i", len(b)))
            h.update(b)
        else:
            v = f if f < (1 << 63) else f - (1 << 64)
            h.update(struct.pack(">q", v))
    return h.digest()


def digest(*fields):
    return "sha256:" + canonical(*fields).hex()


def channel(world_seed, cell, subsystem, key):
    return int.from_bytes(canonical("unnamed.rng/2", world_seed, cell, subsystem, key)[:8], "big")


def raw(seed, sample, attempt):
    counter = (sample << 32) | attempt
    z = (seed + (counter + 1) * 0x9E3779B97F4A7C15) & MASK
    z = ((z ^ (z >> 30)) * 0xBF58476D1CE4E5B9) & MASK
    z = ((z ^ (z >> 27)) * 0x94D049BB133111EB) & MASK
    return z ^ (z >> 31)


def rint(seed, sample, lo, hi):
    rng = hi - lo
    threshold = ((1 << 64) - rng) % rng
    attempt = 0
    while True:
        r = raw(seed, sample, attempt)
        if r >= threshold:
            return lo + r % rng
        attempt += 1


def fmt_axis(v):
    return f"neg{-v}" if v < 0 else str(v)


def cell_key(rx, rz, cx, cz):
    return f"r_{fmt_axis(rx)}_{fmt_axis(rz)}:c_{cx:02d}_{cz:02d}"


NODES = [("iron_vein", "resource.ore.iron_vein", 2, 4), ("silverleaf", "resource.herb.silverleaf", 0, 3)]
POPS = [("wolves", "creature.beast.wolf_grey", 5, 2, 7), ("deer", "creature.beast.deer", 3, 1, 4)]
TERRAIN = (12000, 800, 11)


def generate(seed, rx, rz, cx, cz):
    key = cell_key(rx, rz, cx, cz)
    region = f"r_{fmt_axis(rx)}_{fmt_axis(rz)}"
    base, amp, n = TERRAIN
    heights = channel(seed, key, "terrain", "height")
    terrain_fields = ["unnamed.terrain/v2", n] + [base + rint(heights, i, -amp, amp + 1) for i in range(n * n)]
    terrain_hash = digest(*terrain_fields)

    nodes = []
    for name, defid, lo, hi in NODES:
        count = rint(channel(seed, key, "resources", name + "/count"), 0, lo, hi + 1)
        for o in range(count):
            place = channel(seed, key, "resources", f"{name}/{o:02d}")
            nodes.append((f"node.{key}.{name}.{o:02d}", defid, rint(place, 0, 0, CELL_CM), rint(place, 1, 0, CELL_CM)))

    pops = []
    for name, family, target, pmin, pmax in POPS:
        pop_id = f"pop.{region}.c_{cx:02d}_{cz:02d}.{name}"
        slots = []
        for o in range(target):
            place = channel(seed, key, "wildlife", f"{name}/{o:02d}")
            slots.append((f"{key}.{pop_id}.{o:02d}", o, rint(place, 0, 0, CELL_CM), rint(place, 1, 0, CELL_CM)))
        pops.append((pop_id, family, target, pmin, pmax, slots))

    fields = ["unnamed.cell-baseline/v1", key, terrain_hash, len(nodes)]
    for k, d, x, z in nodes:
        fields += [k, d, x, z]
    fields.append(len(pops))
    for pop_id, family, target, pmin, pmax, slots in pops:
        fields += [pop_id, family, target, pmin, pmax, len(slots)]
        for sk, o, x, z in slots:
            fields += [sk, o, x, z]
    return digest(*fields), nodes, pops


def profile_digest():
    fields = ["unnamed.generation-profile/v2", len(NODES)]
    for name, d, lo, hi in NODES:
        fields += [name, d, lo, hi]
    fields.append(len(POPS))
    for name, fam, t, lo, hi in POPS:
        fields += [name, fam, t, lo, hi]
    fields += list(TERRAIN)
    return digest(*fields)


PROBE_SEED = 0x0DDB1A5E5BAD5EED
PROBES = [(0, 0, 0, 0), (0, 0, 19, 19), (-1, -1, 10, 10), (7, -3, 3, 17)]
probe = digest("unnamed.worldgen-probes/v1", len(PROBES), *[generate(PROBE_SEED, *p)[0] for p in PROBES])
fingerprint = digest("unnamed.worldgen-fingerprint/v1", "unnamed.worldgen.cell-baseline", 2, 2, profile_digest(), probe)

SEED = 0x5C1A9E7B4D2F0083
region = digest("unnamed.region-digest/v2", "r_0_0", *[generate(SEED, 0, 0, cx, cz)[0] for cx in range(20) for cz in range(20)])

home_digest, home_nodes, _ = generate(SEED, 0, 0, 7, 11)
print("profile digest :", profile_digest())
print("probe digest   :", probe)
print("fingerprint    :", fingerprint)
print("region r_0_0   :", region)
print("home digest    :", home_digest)
print("home nodes     :", home_nodes)

# RNG channel vectors pinned by StableRandomTests.
iron = channel(SEED, "r_0_0:c_07_11", "resources", "iron_vein/00")
print("iron_vein/00 UInt64 samples 0..2:", [raw(iron, i, 0) for i in range(3)])
print("iron_vein/00 Int(sample, 0, 10000), samples 0..1:", [rint(iron, i, 0, 10000) for i in range(2)])
heights = channel(SEED, "r_0_0:c_07_11", "terrain", "height")
print("terrain/height Int(i, -800, 801), i 0..4:", [rint(heights, i, -800, 801) for i in range(5)])

# Schema 3: the appearance seed a character gets when nothing chose one (PlayerRecord.DerivedAppearanceSeed).
FIXTURE_PLAYER = "chr_01HF7YAT00041061050R3GG28A"
seed_bytes = canonical("unnamed.appearance-seed/v1", FIXTURE_PLAYER)[:8]
print("appearance seed of", FIXTURE_PLAYER, ":", "0x%016X" % int.from_bytes(seed_bytes, "big"))
