# Sedge Levels Gate 5 — package and memory totals

Owner 13:35 clarification applies: retain the usual texture cache/compression, mipmaps, existing LOD/distance ladders, far impostors and streaming. No new character-saving measures, HumGen texture downscaling, special character trimming or performance tuning were introduced.

Candidate: Gate-4 source `629356f1`, game24 export, primary07 and the same fallback/approved body payload used by native23. This report is separate from pending Gate-4 visual acceptance and current integration reconciliation.

| Deployed root | Bytes |
|---|---:|
| game24 | 304,567,947 |
| primary07 | 2,996,203,161 |
| fallback_primary | 27,151,142,966 |
| humgen_characters | 88,503,020 |
| content | 5,889,061 |
| **Total** | **30,546,306,155** |

Total is **30.546 GB / 28.448 GiB**, uncompressed. Each deployed root is counted once, including both primary and fallback roots. No byte hashes or duplicate-removal pass was performed. This exceeds a decimal 30 GB limit uncompressed; an itch upload archive and the newer integration package have not been measured, so upload-cap acceptance is not claimed.

Existing full-resolution cache preflight: 8,838 image references, 2,568 at least 2K, zero failures. No additional stripping, compression experiment, downscaling or LOD changes were made for this gate.

## Memory measurement

**VRAM total unavailable; 12 GB acceptance is unproven.** Both bounded external-script probes timed out after native scene setup logging, without producing a renderer-memory sample. The second used the accepted fixed-view launch settings with image capture delayed beyond its timeout; neither recorded video or produced a proof image. This does not establish a production runtime regression: the external measurement entry point was different from the successful native23 proof. No additional runs were attempted under the owner's minimum-testing rule.

Original diagnostics remain in `C:/Otherreach_Farm/results/sedge_gate5_totals24` and `sedge_gate5_totals24b`. The farm is idle. Use the successful Gate-4 native23 proof for visual review; the final game24 camera-only roof colliders remain compiled but not normally recaptured. Gate 5 is **PARTIAL**, pending a valid renderer-memory total and eventual integrated shipping package; no package-cap or 12 GB hardware acceptance is claimed. No source quality was lowered to obtain these numbers.
