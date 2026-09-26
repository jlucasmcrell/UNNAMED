| run | segment | avg FPS | 1% low | 0.1% low | GPU p50 ms | GPU p99 ms | CPU p99 ms | <=16.7 ms | hitches >33 ms | VRAM MB | RAM MB |
|---|---|---|---|---|---|---|---|---|---|---|---|
| baseline_phaseA | obstruction | 259 | 184 | 124 | 3.13 | 4.82 | 1.37 | 100.00 | 0 | 5792 | 1982 |
| baseline_phaseA | third_person | 254 | 176 | 118 | 3.19 | 4.82 | 1.35 | 99.99 | 0 | 5792 | 1982 |
| baseline_phaseA | first_person | 245 | 153 | 91 | 3.32 | 5.15 | 1.37 | 99.98 | 0 | 5792 | 1998 |
| tier_low | obstruction | 332 | 193 | 113 | 1.98 | 2.27 | 1.59 | 100.00 | 0 | 2278 | 1701 |
| tier_low | third_person | 339 | 193 | 101 | 1.91 | 2.27 | 1.60 | 99.99 | 1 | 2278 | 1675 |
| tier_low | first_person | 357 | 214 | 119 | 1.85 | 2.30 | 1.53 | 100.00 | 0 | 2278 | 1686 |
| tier_medium | obstruction | 252 | 173 | 113 | 3.01 | 3.86 | 2.01 | 100.00 | 0 | 2969 | 2914 |
| tier_medium | third_person | 253 | 162 | 85 | 2.99 | 3.66 | 2.02 | 99.99 | 1 | 2969 | 2805 |
| tier_medium | first_person | 248 | 155 | 91 | 3.06 | 4.09 | 2.31 | 99.99 | 0 | 2969 | 2817 |
| tier_high | obstruction | 225 | 129 | 62 | 3.50 | 4.47 | 1.78 | 99.99 | 1 | 3174 | 2924 |
| tier_high | third_person | 222 | 148 | 92 | 3.60 | 4.50 | 1.78 | 99.99 | 0 | 3174 | 2937 |
| tier_high | first_person | 204 | 124 | 58 | 3.99 | 5.43 | 1.91 | 99.97 | 2 | 3174 | 2941 |
| tier_ultra | obstruction | 145 | 105 | 74 | 6.42 | 7.82 | 2.50 | 100.00 | 0 | 3714 | 2916 |
| tier_ultra | third_person | 142 | 108 | 80 | 6.63 | 7.89 | 2.37 | 100.00 | 0 | 3666 | 2885 |
| tier_ultra | first_person | 126 | 95 | 62 | 7.36 | 8.97 | 2.75 | 99.98 | 1 | 3666 | 2894 |

The tier rows above were measured before the production characters joined the tier presets (they draw the Phase-A people).

## The production characters at High (same build, `tier=high` against `tier=high,people=phase_a`)

| run | segment | avg FPS | 1% low | 0.1% low | GPU p50 ms | GPU p99 ms | CPU p99 ms | <=16.7 ms | hitches >33 ms | VRAM MB | RAM MB |
|---|---|---|---|---|---|---|---|---|---|---|---|
| tier_high_phaseachars_mem | obstruction | 242 | 157 | 109 | 3.29 | 4.26 | 2.00 | 100.00 | 0 | 3035 | 2870 |
| tier_high_phaseachars_mem | third_person | 238 | 159 | 111 | 3.38 | 4.23 | 1.96 | 99.99 | 0 | 3035 | 2858 |
| tier_high_phaseachars_mem | first_person | 220 | 142 | 87 | 3.67 | 5.31 | 2.19 | 99.99 | 0 | 3035 | 2872 |
| tier_high_prodchars_mem | obstruction | 232 | 156 | 112 | 3.41 | 4.37 | 2.25 | 100.00 | 0 | 3298 | 3296 |
| tier_high_prodchars_mem | third_person | 230 | 149 | 90 | 3.50 | 4.35 | 2.19 | 99.99 | 0 | 3298 | 3367 |
| tier_high_prodchars_mem | first_person | 216 | 137 | 80 | 3.77 | 5.30 | 2.31 | 99.99 | 0 | 3298 | 3372 |

Five production characters (4096 atlases, 37-58k LOD0 triangles, LOD1/LOD2 by distance) cost 4-10 fps on the route
(GPU p50 and p99 +0.1 ms, no new hitches), +263 MB VRAM and +446 MB median working set (3,294 vs 2,848 MB; Godot's own
static memory differs by 10 MB). Before the loader released what it no longer needed (`ArtLibrary.LoadScene` disposing the glTF
state and the uncompressed uploads the cache replaced; `TextureCache.Load` disposing its DDS images) the difference was +638 MB.
