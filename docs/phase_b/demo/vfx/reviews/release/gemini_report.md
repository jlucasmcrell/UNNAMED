# Otherreach Visual QA

Created: 2026-09-26T16:23:58.066175+00:00

Owner decision: **PENDING / UNKNOWN**

[Machine-readable result](result.json)

[Exact prompt](prompt.txt)
[Redacted raw response](raw.json)

```json
{
  "kind": "review",
  "provider": "gemini",
  "model": "gemini-3.8-flash",
  "requested": "2026-09-26T16:23:43.734004+00:00",
  "status": "complete",
  "payload_sha256": "8425cb6c0141651329362b81af74b373c6433a7742a18fd7e3c84d76f46494bf",
  "rubric_version": "1.0.0",
  "input_hashes": [
    {
      "path": "A-00.mp4",
      "sha256": "bf22edac488fdf03baa5950b40b538934d3044ad29138f4cc2d20a85a96ff18a"
    },
    {
      "path": "B-00.mp4",
      "sha256": "3606c5cc19b9c8e8559ecc40c7f762a6e6b545c5be1c230c5e8c271c651c1305"
    }
  ],
  "retry_history": [
    {
      "attempt": 1,
      "time": "2026-09-26T16:23:43.910945+00:00"
    }
  ],
  "owner_decision": null,
  "fixture": false,
  "request_fingerprint": "c16ffbfee679cd5595641beac0a61a48a92689f4abe1e5df7c758da846a80672",
  "provider_version": "gemini-3.8-flash",
  "structured": {
    "observations": [
      {
        "candidate": "A",
        "criterion": "framing",
        "result": "not_observable",
        "observation": "Ground ring effect frames correctly around the monolith base, but required bolt, arrow, and smoke sequences are absent.",
        "references": [
          "A-00.mp4 @ 8.5s"
        ]
      },
      {
        "candidate": "A",
        "criterion": "camera",
        "result": "pass",
        "observation": "Continuous third-person camera rotation shows proper world-space alignment without billboard clipping.",
        "references": [
          "A-00.mp4 @ 10.0s"
        ]
      },
      {
        "candidate": "A",
        "criterion": "telegraph",
        "result": "defect",
        "observation": "Combat exchanges lack clear onset windups and distinct recovery cues, limiting visual readability.",
        "references": [
          "A-00.mp4 @ 2.5s"
        ]
      },
      {
        "candidate": "A",
        "criterion": "tavar",
        "result": "not_observable",
        "observation": "Specific presentation sequences for Tavar trapped, steadied, or freed are missing from the clip.",
        "references": [
          "A-00.mp4 @ 11.0s"
        ]
      },
      {
        "candidate": "B",
        "criterion": "framing",
        "result": "not_observable",
        "observation": "Monolith ground ring aligns with the terrain, but bolt, arrow, and smoke effects are not present.",
        "references": [
          "B-00.mp4 @ 8.5s"
        ]
      },
      {
        "candidate": "B",
        "criterion": "camera",
        "result": "pass",
        "observation": "Third-person orbit maintains stable depth and orientation across character and world movement.",
        "references": [
          "B-00.mp4 @ 10.0s"
        ]
      },
      {
        "candidate": "B",
        "criterion": "telegraph",
        "result": "defect",
        "observation": "Combat lacks distinct visual telegraphs and readable recovery windows during weapon strikes.",
        "references": [
          "B-00.mp4 @ 2.5s"
        ]
      },
      {
        "candidate": "B",
        "criterion": "tavar",
        "result": "not_observable",
        "observation": "Dedicated Tavar trapped/freed sequences cannot be evaluated as they are not shown.",
        "references": [
          "B-00.mp4 @ 11.0s"
        ]
      }
    ],
    "functional_issues": [],
    "summary": "Both candidates demonstrate stable world-space camera integration for the ground ring effect, but both exhibit poor combat telegraph readability and omit the required projectile, smoke, and Tavar-specific visual sequences."
  },
  "missing_criteria": [],
  "latency_s": 14.328
}
```

## Owner review

Record accepted/rejected/deferred per candidate, reviewer name, time, evidence hashes and rationale.
Loading, binding, selector reachability, temporal continuity, quality and owner approval are separate claims.
