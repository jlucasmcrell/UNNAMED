# Otherreach Visual QA

Created: 2026-09-26T16:23:35.518080+00:00

Owner decision: **PENDING / UNKNOWN**

[Machine-readable result](result.json)

[Exact prompt](prompt.txt)
[Redacted raw response](raw.json)

```json
{
  "kind": "review",
  "provider": "gemini",
  "model": "gemini-3.8-flash",
  "requested": "2026-09-26T16:23:27.618913+00:00",
  "status": "complete",
  "payload_sha256": "1eaa5c4f1e80208569c7e9a92099534ccee1d6b48230520d60e3d8c0e0ab05c3",
  "rubric_version": "1.0.0",
  "input_hashes": [
    {
      "path": "A-00.mp4",
      "sha256": "103759566f4f66658d542f916472d70d3d8e8771358f88edb16cf5dc420a2b88"
    },
    {
      "path": "B-00.mp4",
      "sha256": "560e954595a96c05c9c9a833b58944d5b05f4ef10a366032a150be594347be45"
    }
  ],
  "retry_history": [
    {
      "attempt": 1,
      "time": "2026-09-26T16:23:27.741487+00:00"
    }
  ],
  "owner_decision": null,
  "fixture": false,
  "request_fingerprint": "21ffd976baafed89c238a4c16b0f91b862c07d97ab532c777a5c7ac7f731dec5",
  "provider_version": "gemini-3.8-flash",
  "structured": {
    "observations": [
      {
        "candidate": "A",
        "criterion": "framing",
        "result": "pass",
        "observation": "Protective shield VFX renders as a faceted dome with clear bounds and appropriate depth occlusion against character and terrain; bolt, arrow, and smoke are not present.",
        "references": [
          "A-00.mp4 @ 8.8s"
        ]
      },
      {
        "candidate": "A",
        "criterion": "camera",
        "result": "pass",
        "observation": "Camera tracking remains stable across third-person traversal without visual clipping or orientation detachment.",
        "references": [
          "A-00.mp4 @ 4.2s"
        ]
      },
      {
        "candidate": "A",
        "criterion": "telegraph",
        "result": "pass",
        "observation": "Hostile charge telegraph and player ability activation timing are legible within the combat encounter window.",
        "references": [
          "A-00.mp4 @ 7.5s"
        ]
      },
      {
        "candidate": "A",
        "criterion": "tavar",
        "result": "not_observable",
        "observation": "Specific sequence for trapped, steadied, or freed states is not depicted; only an idle interaction prompt appears.",
        "references": [
          "A-00.mp4 @ 8.2s"
        ]
      },
      {
        "candidate": "B",
        "criterion": "framing",
        "result": "pass",
        "observation": "Spherical planar ring effect displays clean edge falloff and proper spatial layering against character geometry; other projectile effects absent.",
        "references": [
          "B-00.mp4 @ 8.8s"
        ]
      },
      {
        "candidate": "B",
        "criterion": "camera",
        "result": "pass",
        "observation": "Third-person orbit and movement maintain continuous perspective without visual anomalies.",
        "references": [
          "B-00.mp4 @ 4.2s"
        ]
      },
      {
        "candidate": "B",
        "criterion": "telegraph",
        "result": "pass",
        "observation": "Enemy attack approach and shield activation onset provide sufficient visual readability.",
        "references": [
          "B-00.mp4 @ 7.5s"
        ]
      },
      {
        "candidate": "B",
        "criterion": "tavar",
        "result": "not_observable",
        "observation": "Sequence showing Tavar trapped, steadied, or freed is missing from the clip.",
        "references": [
          "B-00.mp4 @ 8.2s"
        ]
      }
    ],
    "functional_issues": [],
    "summary": "Both candidates demonstrate stable camera handling and readable telegraphing. Candidate A utilizes a faceted mesh barrier, whereas Candidate B uses a soft planar glow effect; neither clip presents the complete Tavar state progression."
  },
  "missing_criteria": [],
  "latency_s": 7.896
}
```

## Owner review

Record accepted/rejected/deferred per candidate, reviewer name, time, evidence hashes and rationale.
Loading, binding, selector reachability, temporal continuity, quality and owner approval are separate claims.
