# Otherreach Visual QA

Created: 2026-09-26T16:23:18.479356+00:00

Owner decision: **PENDING / UNKNOWN**

[Machine-readable result](result.json)

[Exact prompt](prompt.txt)
[Redacted raw response](raw.json)

```json
{
  "kind": "review",
  "provider": "gemini",
  "model": "gemini-3.8-flash",
  "requested": "2026-09-26T16:23:08.478676+00:00",
  "status": "complete",
  "payload_sha256": "8d684abe17f59b20bfc9c4a0babe46edfd3a68c169ab52e8779513bf47efc2f8",
  "rubric_version": "1.0.0",
  "input_hashes": [
    {
      "path": "A-00.mp4",
      "sha256": "b24765839b5476d17966c1ec154082b5283193ad909c5e554e65872d0de110bf"
    },
    {
      "path": "B-00.mp4",
      "sha256": "29a95578d1c004798b39d9630092eb6b62c781768b9eb05c00845f18e16d4ecd"
    }
  ],
  "retry_history": [
    {
      "attempt": 1,
      "time": "2026-09-26T16:23:08.588583+00:00"
    }
  ],
  "owner_decision": null,
  "fixture": false,
  "request_fingerprint": "63d3bd16d0b28d872bd27baa7c8552b9bff412de4930fd0f9106635908310d37",
  "provider_version": "gemini-3.8-flash",
  "structured": {
    "observations": [
      {
        "candidate": "A",
        "criterion": "framing",
        "result": "pass",
        "observation": "Wireframe enclosure fully covers character volume with well-defined boundaries and depth occlusion.",
        "references": [
          "A-00.mp4 @ 3.0s"
        ]
      },
      {
        "candidate": "A",
        "criterion": "camera",
        "result": "pass",
        "observation": "Enclosure maintains stable world-space perspective and geometry during camera orbit.",
        "references": [
          "A-00.mp4 @ 5.0s"
        ]
      },
      {
        "candidate": "A",
        "criterion": "telegraph",
        "result": "not_observable",
        "observation": "Combat telegraphs, onset, and recovery sequences are not shown.",
        "references": [
          "A-00.mp4 @ 0.0s"
        ]
      },
      {
        "candidate": "A",
        "criterion": "tavar",
        "result": "pass",
        "observation": "Trapped and freed states are clearly depicted by the presence and dissolution of the cage.",
        "references": [
          "A-00.mp4 @ 10.0s"
        ]
      },
      {
        "candidate": "B",
        "criterion": "framing",
        "result": "defect",
        "observation": "Planar ring lacks volumetric boundaries and intersects character geometry flatly.",
        "references": [
          "B-00.mp4 @ 3.0s"
        ]
      },
      {
        "candidate": "B",
        "criterion": "camera",
        "result": "defect",
        "observation": "Effect billboard tracks camera orientation rather than anchoring as a 3D volume.",
        "references": [
          "B-00.mp4 @ 5.0s"
        ]
      },
      {
        "candidate": "B",
        "criterion": "telegraph",
        "result": "not_observable",
        "observation": "No combat readability or telegraphing animations are present in the footage.",
        "references": [
          "B-00.mp4 @ 0.0s"
        ]
      },
      {
        "candidate": "B",
        "criterion": "tavar",
        "result": "pass",
        "observation": "Transition from trapped to freed state is presented via effect disappearance.",
        "references": [
          "B-00.mp4 @ 10.0s"
        ]
      }
    ],
    "functional_issues": [],
    "summary": "Candidate A provides an enclosing 3D volume with stable depth and camera independence, whereas Candidate B uses a flat camera-aligned billboard that clips through the character."
  },
  "missing_criteria": [],
  "latency_s": 9.997
}
```

## Owner review

Record accepted/rejected/deferred per candidate, reviewer name, time, evidence hashes and rationale.
Loading, binding, selector reachability, temporal continuity, quality and owner approval are separate claims.
