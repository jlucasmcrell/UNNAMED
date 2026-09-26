# Otherreach Visual QA

Created: 2026-09-26T16:23:43.508839+00:00

Owner decision: **PENDING / UNKNOWN**

[Machine-readable result](result.json)

[Exact prompt](prompt.txt)
[Redacted raw response](raw.json)

```json
{
  "kind": "review",
  "provider": "gemini",
  "model": "gemini-3.8-flash",
  "requested": "2026-09-26T16:23:35.653076+00:00",
  "status": "complete",
  "payload_sha256": "357aa59c5303c1de37552bbd16c74426addc4b033391f14a229268b284cb09a1",
  "rubric_version": "1.0.0",
  "input_hashes": [
    {
      "path": "A-00.mp4",
      "sha256": "b1a456918d02f2956a3ff924d98f601e1b9ff7409a43f6748aeccd2ec915e2de"
    },
    {
      "path": "B-00.mp4",
      "sha256": "ee5ac8235de8513cffc4369c3b2b3c15db7bd962391602c04b65c614fb833700"
    }
  ],
  "retry_history": [
    {
      "attempt": 1,
      "time": "2026-09-26T16:23:35.734414+00:00"
    }
  ],
  "owner_decision": null,
  "fixture": false,
  "request_fingerprint": "78edbb33f37a10c54a16c958afa47e72bbacf3ae1e6266f33539e789f6b6dc2b",
  "provider_version": "gemini-3.8-flash",
  "structured": {
    "observations": [
      {
        "candidate": "A",
        "criterion": "framing",
        "result": "not_observable",
        "observation": "Specified VFX elements (Mending, bolt, arrow, smoke) are not present in the sequence.",
        "references": [
          "A-00.mp4 @ 1.5s"
        ]
      },
      {
        "candidate": "A",
        "criterion": "camera",
        "result": "pass",
        "observation": "Third-person camera maintains stable orientation and tracking throughout character movement.",
        "references": [
          "A-00.mp4 @ 2.0s"
        ]
      },
      {
        "candidate": "A",
        "criterion": "telegraph",
        "result": "not_observable",
        "observation": "Combat timing, telegraphing, and recovery mechanics are absent.",
        "references": [
          "A-00.mp4 @ 0.5s"
        ]
      },
      {
        "candidate": "A",
        "criterion": "tavar",
        "result": "not_observable",
        "observation": "Tavar trapped, steadied, or freed sequence is not displayed.",
        "references": [
          "A-00.mp4 @ 3.0s"
        ]
      },
      {
        "candidate": "B",
        "criterion": "framing",
        "result": "not_observable",
        "observation": "Required VFX targets are not depicted in the video.",
        "references": [
          "B-00.mp4 @ 1.5s"
        ]
      },
      {
        "candidate": "B",
        "criterion": "camera",
        "result": "pass",
        "observation": "Continuous third-person perspective tracks motion without clipping or orientation errors.",
        "references": [
          "B-00.mp4 @ 2.0s"
        ]
      },
      {
        "candidate": "B",
        "criterion": "telegraph",
        "result": "not_observable",
        "observation": "No combat sequences are featured in the footage.",
        "references": [
          "B-00.mp4 @ 0.5s"
        ]
      },
      {
        "candidate": "B",
        "criterion": "tavar",
        "result": "not_observable",
        "observation": "Tavar entity and related narrative states are not present.",
        "references": [
          "B-00.mp4 @ 3.0s"
        ]
      }
    ],
    "functional_issues": [],
    "summary": "Both candidates demonstrate equivalent stable third-person camera behavior during environment interaction, but lack evidence for combat telegraphs, specific VFX framing, and Tavar sequences."
  },
  "missing_criteria": [],
  "latency_s": 7.853
}
```

## Owner review

Record accepted/rejected/deferred per candidate, reviewer name, time, evidence hashes and rationale.
Loading, binding, selector reachability, temporal continuity, quality and owner approval are separate claims.
