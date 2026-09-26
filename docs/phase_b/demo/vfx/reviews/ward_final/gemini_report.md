# Otherreach Visual QA

Created: 2026-09-26T17:02:33.520500+00:00

Owner decision: **PENDING / UNKNOWN**

[Machine-readable result](result.json)

[Exact prompt](prompt.txt)
[Redacted raw response](raw.json)

```json
{
  "kind": "review",
  "provider": "gemini",
  "model": "gemini-3.8-flash",
  "requested": "2026-09-26T17:02:23.774148+00:00",
  "status": "complete",
  "payload_sha256": "0c541ec6e495e8251ae2b882ee5e96cdd929624a5b35394850714c353fa4a673",
  "rubric_version": "1.0.0",
  "input_hashes": [
    {
      "path": "A-00.mp4",
      "sha256": "5fb7f4d89789af54f5d2a21c32435e0a44162fb43d4c2a9fd4db86892715b9e6"
    },
    {
      "path": "B-00.mp4",
      "sha256": "29a95578d1c004798b39d9630092eb6b62c781768b9eb05c00845f18e16d4ecd"
    }
  ],
  "retry_history": [
    {
      "attempt": 1,
      "time": "2026-09-26T17:02:23.881350+00:00"
    }
  ],
  "owner_decision": null,
  "fixture": false,
  "request_fingerprint": "bcafa56a48c1e069bfb49cd0142f215bfb393a08b0d7fc25ac448d9f3d320fb0",
  "provider_version": "gemini-3.8-flash",
  "structured": {
    "observations": [
      {
        "candidate": "A",
        "criterion": "framing",
        "result": "pass",
        "observation": "Volumetric cage fully encloses NPC with proper vertical bounds and depth occlusion.",
        "references": [
          "A-00.mp4 @ 00:03"
        ]
      },
      {
        "candidate": "A",
        "criterion": "camera",
        "result": "pass",
        "observation": "Effect geometry maintains consistent 3D volume across camera orbit.",
        "references": [
          "A-00.mp4 @ 00:05"
        ]
      },
      {
        "candidate": "A",
        "criterion": "telegraph",
        "result": "not_observable",
        "observation": "No combat actions, bolts, arrows, or telegraph timing are demonstrated.",
        "references": [
          "A-00.mp4 @ 00:01"
        ]
      },
      {
        "candidate": "A",
        "criterion": "tavar",
        "result": "pass",
        "observation": "Trapped cage presentation transitions cleanly to freed state upon interaction.",
        "references": [
          "A-00.mp4 @ 00:09"
        ]
      },
      {
        "candidate": "B",
        "criterion": "framing",
        "result": "defect",
        "observation": "Flat 2D ring intersects character center rather than bounding outer volume.",
        "references": [
          "B-00.mp4 @ 00:04"
        ]
      },
      {
        "candidate": "B",
        "criterion": "camera",
        "result": "defect",
        "observation": "Planar billboard ring rotates continuously toward camera, lacking depth.",
        "references": [
          "B-00.mp4 @ 00:06"
        ]
      },
      {
        "candidate": "B",
        "criterion": "telegraph",
        "result": "not_observable",
        "observation": "Combat telegraphs and projectile elements are absent from the clip.",
        "references": [
          "B-00.mp4 @ 00:01"
        ]
      },
      {
        "candidate": "B",
        "criterion": "tavar",
        "result": "pass",
        "observation": "Displays trapped aura followed by dissipation sequence when freed.",
        "references": [
          "B-00.mp4 @ 00:09"
        ]
      }
    ],
    "functional_issues": [],
    "summary": "Candidate A implements a fully volumetric 3D cage that maintains spatial depth during camera rotation. Candidate B relies on a flat camera-aligned billboard ring that clips through the character."
  },
  "missing_criteria": [],
  "latency_s": 9.743
}
```

## Owner review

Record accepted/rejected/deferred per candidate, reviewer name, time, evidence hashes and rationale.
Loading, binding, selector reachability, temporal continuity, quality and owner approval are separate claims.
