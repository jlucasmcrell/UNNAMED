"""Extract the sampler settings and prompts from a ComfyUI PNG's embedded metadata."""
import json
import struct
import sys

path = sys.argv[1]
with open(path, "rb") as handle:
    data = handle.read()

offset = 8
prompt_graph = None
while offset < len(data):
    length = struct.unpack(">I", data[offset:offset + 4])[0]
    chunk_type = data[offset + 4:offset + 8].decode("latin-1")
    chunk = data[offset + 8:offset + 8 + length]
    if chunk_type == "tEXt":
        keyword, _, value = chunk.partition(b"\x00")
        if keyword == b"prompt":
            prompt_graph = json.loads(value.decode("utf-8", "replace"))
    offset += 12 + length
    if chunk_type == "IEND":
        break

if not prompt_graph:
    print("no prompt metadata")
    raise SystemExit(1)

SAMPLER_KEYS = ("positive_prompt", "negative_prompt", "steps", "cfg", "sampler_name",
                "scheduler", "denoise", "width", "height", "seed", "batch_size",
                "prompt_template", "enable_prompt_enhance", "shift")
LORA_KEYS = ("lora_name", "strength", "strength_model", "strength_clip")

for node_id, node in sorted(prompt_graph.items(), key=lambda kv: int(kv[0]) if kv[0].isdigit() else 0):
    ctype = node.get("class_type", "")
    inputs = node.get("inputs", {})
    interesting = {k: v for k, v in inputs.items() if k in SAMPLER_KEYS}
    if interesting:
        print(f"=== [{node_id}] {ctype}")
        for key, value in interesting.items():
            if isinstance(value, str) and len(value) > 160:
                print(f"  {key}:")
                for line in value.split(". "):
                    print(f"      {line.strip()}")
            else:
                print(f"  {key}: {value}")
        print()
    loras = {k: v for k, v in inputs.items() if k in LORA_KEYS}
    if loras and "lora" in ctype.lower():
        print(f"=== [{node_id}] {ctype}  {loras}")
        print()
