# ASTRAL — SECOND GENERATION HOST

**Status: operational.** ASTRAL runs ComfyUI 0.35.0 and now shares concept rendering with
RAZER, roughly doubling 2D throughput. It also has the full Trellis2/Pixal3D node set, so it
can take 3D work.

---

## The machine

| | ASTRAL | BEAST | RAZER |
|---|---|---|---|
| GPU | **RTX 5090, 34.2 GB** | RTX 3090, 24 GB | RTX 4070 Ti, 12.9 GB |
| CPU | Ryzen 9 9950X3D, 16-core | Ryzen 9 5950X | — |
| RAM | **100 GB** | 64 GB | — |
| ComfyUI | 0.35.0 | 0.34.0 | 0.33.0 |

The RAM matters most. Host-memory exhaustion was the single most costly fault of the overnight
run — ComfyUI committed 244 GB against a 240 GB limit on BEAST and thrashed at 191 GB, forcing
guard restarts that lost assets. ASTRAL has 100 GB of real RAM against BEAST's 64.

## Access

ASTRAL is not directly reachable from BEAST: SSH (22), WinRM (5985) and its ComfyUI port were
all closed, and its `C$`/`G$`/`admin$` shares are denied. It is now reachable over SSH, and
ComfyUI is reached through a tunnel.

### SSH

Key-based, one-way from BEAST. The public key is
`~/.ssh/id_ed25519_astral.pub` on BEAST, and the matching line lives in
`C:\ProgramData\ssh\administrators_authorized_keys` on ASTRAL.

**The non-obvious part:** `jluca` is an **administrator** on ASTRAL, and Windows OpenSSH
**ignores `~/.ssh/authorized_keys` entirely** for administrator accounts. It reads only
`C:\ProgramData\ssh\administrators_authorized_keys`, and that file must be given restrictive
ACLs or sshd refuses to read it:

```powershell
Set-Content -Path "C:\ProgramData\ssh\administrators_authorized_keys" -Value "ssh-ed25519 AAAA... comment"
icacls "C:\ProgramData\ssh\administrators_authorized_keys" /inheritance:r /grant "Administrators:F" /grant "SYSTEM:F"
Restart-Service sshd
```

Without the `icacls` line the key is present, correct and still refused.

### Running remote commands

Use `_remote.py`. Nested quoting through `ssh -> cmd -> powershell -Command` breaks on
anything non-trivial: the first `$_` inside double quotes collapses and `[Math]::Min(...)` gets
parsed by the wrong layer, producing syntax errors in code that was never written. The tool
base64-encodes PowerShell as UTF-16LE for `-EncodedCommand`, which passes every layer
untouched.

```
python _remote.py --host astral "Get-Date"
python _remote.py --host astral --file probe.ps1
python _remote.py --host astral "nvidia-smi --query-gpu=name --format=csv,noheader" --raw
```

ASTRAL's default shell is `cmd`, so use `--raw` for plain executables and the wrapper for
PowerShell.

### ComfyUI through an SSH tunnel

ASTRAL's ComfyUI binds to localhost only, which is why every port probe from BEAST failed
while the server was running fine. Reach it by forwarding:

```
ssh -N -L 18190:127.0.0.1:8190 astral
```

Then point the pipeline at `http://127.0.0.1:18190`. Verified:

```
device    : cuda:0 NVIDIA GeForce RTX 5090
vram      : 34.2 GB total, 30.6 GB free
comfy     : 0.35.0
argv      : main.py --windows-standalone-build --port 8190
            --disable-dynamic-vram --use-sage-attention
```

## Absolute paths

```
ComfyUI install   W:\ComfyUI_LTX25\ComfyUI          (ASTRAL's G:\ComfyUI_LTX25\ComfyUI)
launcher          W:\ComfyUI_LTX25\run_LTX25_port8190.bat
output            W:\ComfyUI_LTX25\ComfyUI\output
log               W:\ComfyUI_LTX25\ComfyUI\user\comfyui_8190.log
models            X:\ (= \\astral\models), via ASTRAL's own extra_model_paths.yaml
```

`W:` is ASTRAL's `G:` — the project tree is physically on ASTRAL. Models are **not** copied;
ASTRAL's ComfyUI references its own model tree, which already held Z-Image, the text encoder
and the VAE.

## Model coverage

| Need | ASTRAL has |
|---|---|
| Concept generation | `z_image_turbo_bf16.safetensors`, `thmUNCZImageTE_v10.safetensors` (lumina2), `ultrafluxVAEImproved_v10.safetensors` |
| 3D generation | **all 10 Trellis2 / Pixal3D nodes** — `Trellis2ShapeStage`, `Trellis2TextureStage`, `Trellis2UpsampleStage`, `VaeDecodeShapeTrellis`, `VaeDecodeStructureTrellis2`, `VaeDecodeTextureTrellis`, `Pixal3DConditioning`, `Pixal3DMultiViewConditioning`, `Trellis2Conditioning`, `EmptyTrellis2LatentStructure` |
| Z-Image tooling | 15 `z_image*` nodes including `TextEncodeZImageOmni`, `ZImageTurboLoraStack` |

## A host-capability bug this exposed

The first render on ASTRAL was rejected:

```
* Z_ImageIntegratedKSampler 5:
  - Value not in list: sampler_name: 'euler_flow' not in (list of length 123)
```

`euler_flow` comes from the `ComfyUI-ZImageTurbo-FlowSampler` custom node, which RAZER and
BEAST both have and ASTRAL does not. The pipeline had a per-machine sampler constant, so
adding a host meant editing code.

**Fixed by asking the server what it accepts.** `_make_concepts.py` now reads
`/object_info`, resolves the sampler against the server's declared list, falls back to the
same family first, and **prints the substitution** rather than making it silently — a changed
sampler changes the output, so it belongs in the run log:

```
sampler: 'euler_flow' is not offered by this server; used 'euler' of 123 available
```

This removes the whole class of "works on one host, fails on another" faults for node inputs.

## Measured throughput

| Host | Per concept |
|---|---|
| ASTRAL (5090) | **92 s** |
| RAZER (4070 Ti) | 125 s |

ASTRAL is ~26% faster on the same work.

## Parallel operation

Two independent `_render_queue.py` processes, one per host, disjoint request files:

| Host | Queue |
|---|---|
| RAZER | `wave0_magic_implements`, `wave0_travel`, `overnight_materials` |
| ASTRAL | `wave0_animals_mounts`, `wave0_containers`, `overnight_icons` |

BEAST's 3D build queue is untouched — three machines, three independent jobs.

## Verified

```
RAZER   running=1 pending=0
ASTRAL  running=1 pending=0
BEAST   GPU 100%
newest concepts arriving from ASTRAL: animal_pack_goat, mount_riding_horse, ...
```

## Still to do

- **Watch ASTRAL's RAM.** It reported only 12.9 GB free of 100 GB with other workloads
  running. Fine for concepts; test before committing heavy 3D to it.
- **3D on ASTRAL is untested.** The nodes and models are present but no Trellis2 build has run
  there. Worth proving with one asset before moving bulk work.
- **The tunnel is a foreground process.** If it drops, the ASTRAL queue fails. A supervised
  tunnel would be more robust for long runs.
