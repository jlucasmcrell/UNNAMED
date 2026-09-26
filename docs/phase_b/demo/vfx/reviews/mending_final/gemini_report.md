# Otherreach Visual QA

Created: 2026-09-26T17:02:23.642724+00:00

Owner decision: **PENDING / UNKNOWN**

[Machine-readable result](result.json)

[Exact prompt](prompt.txt)
[Redacted raw response](raw.json)

```json
{
  "kind": "review",
  "provider": "gemini",
  "model": "gemini-3.8-flash",
  "requested": "2026-09-26T17:02:12.801197+00:00",
  "status": "complete",
  "payload_sha256": "3a379802cae2f6162d40a2487c2e8f56c3addd6630c044afd493b76595d29597",
  "rubric_version": "1.0.0",
  "input_hashes": [
    {
      "path": "A-00.mp4",
      "sha256": "8ad2fee2e919b4a9e79bc0e397fea8b462406b303e01539a4c7ad97c979f53cc"
    },
    {
      "path": "B-00.mp4",
      "sha256": "86831c4acc1e7b4cb068f2d0d5d0b1d5a451998bb7a49ad8807271b213928e37"
    }
  ],
  "retry_history": [
    {
      "attempt": 1,
      "time": "2026-09-26T17:02:12.891677+00:00"
    }
  ],
  "owner_decision": null,
  "fixture": false,
  "request_fingerprint": "e62a3fe50d83322f65b62206696db58a8594c5fb48d2a0a77f326c0570ee4877",
  "provider_version": "gemini-3.8-flash",
  "structured": {
    "observations": [
      {
        "candidate": "A",
        "criterion": "framing",
        "result": "pass",
        "observation": "Mending ribbons frame clearly across bottom screen space; bolt, arrow, and smoke are absent.",
        "references": [
          "A-00.mp4 @ 2.0s"
        ]
      },
      {
        "candidate": "A",
        "criterion": "camera",
        "result": "pass",
        "observation": "Continuous camera transition from tight wall occlusion to third-person movement remains stable.",
        "references": [
          "A-00.mp4 @ 4.5s"
        ]
      },
      {
        "candidate": "A",
        "criterion": "telegraph",
        "result": "not_observable",
        "observation": "No combat encounters or attack telegraph sequences occur in the clip.",
        "references": [
          "A-00.mp4 @ 1.0s"
        ]
      },
      {
        "candidate": "A",
        "criterion": "tavar",
        "result": "not_observable",
        "observation": "Tavar is already following; trapped, steadied, or freed sequences are not shown.",
        "references": [
          "A-00.mp4 @ 6.5s"
        ]
      },
      {
        "candidate": "B",
        "criterion": "framing",
        "result": "pass",
        "observation": "Mending visual effects maintain clean lower boundary framing; bolt, arrow, and smoke are unobserved.",
        "references": [
          "B-00.mp4 @ 2.0s"
        ]
      },
      {
        "candidate": "B",
        "criterion": "camera",
        "result": "pass",
        "observation": "Camera motion smoothly tracks character movement and rotation around architecture.",
        "references": [
          "B-00.mp4 @ 4.5s"
        ]
      },
      {
        "candidate": "B",
        "criterion": "telegraph",
        "result": "not_observable",
        "observation": "Clip lacks active hostile combat, telegraphing, and recovery phases.",
        "references": [
          "B-00.mp4 @ 1.0s"
        ]
      },
      {
        "candidate": "B",
        "criterion": "tavar",
        "result": "not_observable",
        "observation": "Trapped, steadied, or freed states are absent as Tavar is already in following status.",
        "references": [
          "B-00.mp4 @ 6.5s"
        ]
      }
    ],
    "functional_issues": [],
    "summary": "Both candidates demonstrate equivalent Mending framing and smooth camera tracking during movement, but both lack evidence for combat telegraphing and Tavar quest progression sequences."
  },
  "missing_criteria": [],
  "latency_s": 10.838
}
```

## Owner review

Record accepted/rejected/deferred per candidate, reviewer name, time, evidence hashes and rationale.
Loading, binding, selector reachability, temporal continuity, quality and owner approval are separate claims.
