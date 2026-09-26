# Otherreach Visual QA

Created: 2026-09-26T16:28:24.272795+00:00

Owner decision: **PENDING / UNKNOWN**

[Machine-readable result](result.json)

[Exact prompt](prompt.txt)
[Redacted raw response](raw.json)

```json
{
  "kind": "review",
  "provider": "gemini",
  "model": "gemini-3.8-flash",
  "requested": "2026-09-26T16:28:09.776472+00:00",
  "status": "complete",
  "payload_sha256": "91747bc57ce9004a6590c75a6e9d1cb44ffad6d10d3522566f7e75d31d511e5c",
  "rubric_version": "1.0.0",
  "input_hashes": [
    {
      "path": "A-00.png",
      "sha256": "a7d6cb1bc5c78f591c4322374ca6f21e691902aef9dba327142a240b29f4bb9a"
    },
    {
      "path": "B-00.png",
      "sha256": "bcd1f86fbbdcce1adc22edb599f4f16b850472d13cf253d99f4d49594972d772"
    }
  ],
  "retry_history": [
    {
      "attempt": 1,
      "time": "2026-09-26T16:28:09.907275+00:00"
    }
  ],
  "owner_decision": null,
  "fixture": false,
  "request_fingerprint": "c7a0b8301d61aeccedd38476044cd39e7dd99e97c25eae16632e3d83b7c48e54",
  "provider_version": "gemini-3.8-flash",
  "structured": {
    "observations": [
      {
        "candidate": "A",
        "criterion": "hierarchy",
        "result": "defect",
        "observation": "Text lacks backing containers, causing weak contrast and legibility issues against bright background terrain.",
        "references": [
          "A-00.png"
        ]
      },
      {
        "candidate": "A",
        "criterion": "layout",
        "result": "defect",
        "observation": "HUD elements lack unified paneling and consistent spacing, leaving prompts and logs floating loosely.",
        "references": [
          "A-00.png"
        ]
      },
      {
        "candidate": "A",
        "criterion": "context",
        "result": "pass",
        "observation": "Displays interaction prompt and combat log in appropriate contexts, despite unstyled presentation.",
        "references": [
          "A-00.png"
        ]
      },
      {
        "candidate": "B",
        "criterion": "hierarchy",
        "result": "pass",
        "observation": "Dark backing panels and clear typographic weights provide solid contrast and clear visual hierarchy.",
        "references": [
          "B-00.png"
        ]
      },
      {
        "candidate": "B",
        "criterion": "layout",
        "result": "pass",
        "observation": "Elements are neatly aligned within structured containers, with prominent contextual interaction prompts.",
        "references": [
          "B-00.png"
        ]
      },
      {
        "candidate": "B",
        "criterion": "context",
        "result": "pass",
        "observation": "Combat logs, enemy health bars, and world prompts are well-integrated into contextual frames.",
        "references": [
          "B-00.png"
        ]
      }
    ],
    "functional_issues": [],
    "summary": "Candidate B offers structured layouts with solid legibility panels, whereas Candidate A displays unstyled text that suffers from poor contrast."
  },
  "missing_criteria": [],
  "latency_s": 14.493
}
```

## Owner review

Record accepted/rejected/deferred per candidate, reviewer name, time, evidence hashes and rationale.
Loading, binding, selector reachability, temporal continuity, quality and owner approval are separate claims.
