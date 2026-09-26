# Otherreach Visual QA

Created: 2026-09-26T16:23:27.486316+00:00

Owner decision: **PENDING / UNKNOWN**

[Machine-readable result](result.json)

[Exact prompt](prompt.txt)
[Redacted raw response](raw.json)

```json
{
  "kind": "review",
  "provider": "gemini",
  "model": "gemini-3.8-flash",
  "requested": "2026-09-26T16:23:18.612230+00:00",
  "status": "complete",
  "payload_sha256": "ab28829ecf5021cb141eb7b95d641fb48694cab5339f6af70263622a3068d384",
  "rubric_version": "1.0.0",
  "input_hashes": [
    {
      "path": "A-00.mp4",
      "sha256": "4e0f8eda4b060f5d6fedc1900193faa0fae71cc0aff0253aa99e1b2e520e17fa"
    },
    {
      "path": "B-00.mp4",
      "sha256": "86831c4acc1e7b4cb068f2d0d5d0b1d5a451998bb7a49ad8807271b213928e37"
    }
  ],
  "retry_history": [
    {
      "attempt": 1,
      "time": "2026-09-26T16:23:18.702289+00:00"
    }
  ],
  "owner_decision": null,
  "fixture": false,
  "request_fingerprint": "53b12827959054a8864c7f3693fef882a11826a6d5d86c162d7c6948d61ae6be",
  "provider_version": "gemini-3.8-flash",
  "structured": {
    "observations": [
      {
        "candidate": "A",
        "criterion": "framing",
        "result": "defect",
        "observation": "Mending ribbon renders over the foreground wooden pillar due to depth occlusion sorting failure.",
        "references": [
          "A-00.mp4 @ 00:01"
        ]
      },
      {
        "candidate": "A",
        "criterion": "camera",
        "result": "pass",
        "observation": "Particle ribbon maintains spatial coherence relative to the character throughout third-person camera motion.",
        "references": [
          "A-00.mp4 @ 00:06"
        ]
      },
      {
        "candidate": "A",
        "criterion": "telegraph",
        "result": "not_observable",
        "observation": "No combat attacks or telegraph windows are shown during the clip.",
        "references": [
          "A-00.mp4 @ 00:00"
        ]
      },
      {
        "candidate": "A",
        "criterion": "tavar",
        "result": "not_observable",
        "observation": "Tavar trapped/steadied/freed interaction sequence is absent from the footage.",
        "references": [
          "A-00.mp4 @ 00:03"
        ]
      },
      {
        "candidate": "B",
        "criterion": "framing",
        "result": "pass",
        "observation": "Mending ribbon correctly depth-occludes behind the foreground wooden pillar.",
        "references": [
          "B-00.mp4 @ 00:01"
        ]
      },
      {
        "candidate": "B",
        "criterion": "camera",
        "result": "pass",
        "observation": "Ribbon effect tracks character position accurately during third-person camera transition.",
        "references": [
          "B-00.mp4 @ 00:06"
        ]
      },
      {
        "candidate": "B",
        "criterion": "telegraph",
        "result": "not_observable",
        "observation": "No combat encounters or attack telegraphs are depicted.",
        "references": [
          "B-00.mp4 @ 00:00"
        ]
      },
      {
        "candidate": "B",
        "criterion": "tavar",
        "result": "not_observable",
        "observation": "Trapped, steadied, or freed sequences for Tavar are not present in the capture.",
        "references": [
          "B-00.mp4 @ 00:03"
        ]
      }
    ],
    "functional_issues": [],
    "summary": "Candidate B correctly handles depth occlusion for the Mending VFX behind foreground geometry, whereas Candidate A exhibits foreground sorting errors. Combat telegraphs and Tavar quest sequences are not observable in either clip."
  },
  "missing_criteria": [],
  "latency_s": 8.871
}
```

## Owner review

Record accepted/rejected/deferred per candidate, reviewer name, time, evidence hashes and rationale.
Loading, binding, selector reachability, temporal continuity, quality and owner approval are separate claims.
