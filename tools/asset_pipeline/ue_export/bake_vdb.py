"""Bake a Fab VDB fire (FireFX_VDB Blueprint: HeterogeneousVolumeComponent over an animated Sparse Volume Texture) to
one linear-HDR image per volume frame, for flipbook building outside Unreal (compose_flipbook.py).

Unreal Editor Python, UE 5.8, run in the FULL editor (scene captures need a renderer; GPU job - take gpu.lock):
  set VDB_BAKE=<config.json>
  UnrealEditor.exe <ExportHost.uproject> -ExecutePythonScript=<this file> -nosplash -nop4 -unattended
--info (commandlet, -NullRHI) prints each Blueprint's component settings and frame range instead of rendering.

config: {"out": dir, "res": 2048, "elevation": 12, "items": [{"bp": "/Game/FireFX_VDB/Blueprints/BP_FirePlace_1_VDB",
         "name": "fireplace_1", "yaw": 0, "max_frames": 126}], "debug": false}
Writes <out>/<name>/f####.exr (SceneColorHDR: linear, before exposure and tonemapping, black background) and
<out>/<name>/_bake.json. Per item: the volume is shown at frame 30 until the capture is lit (shaders compiled, pages
streamed), framed wide (compose_flipbook.py crops); then each frame is set and exported once it has streamed in (Bake).
"""
import json
import math
import os
import sys
import time
import traceback

import unreal

CFG = json.load(open(os.environ["VDB_BAKE"], encoding="utf-8"))
actors = unreal.get_editor_subsystem(unreal.EditorActorSubsystem)
WARM_FRAME = 30
WARM_LIT_TICKS = 10
PROBE_PX = 64
FRAME_MAX_S = 6.0
WARM_MAX_TICKS = 9000


def describe(item):
    cls = unreal.EditorAssetLibrary.load_blueprint_class(item["bp"])
    # "yaw" turns the volume on the spot (90 shows a log fire's long side to the camera, which looks along +X).
    actor = actors.spawn_actor_from_class(cls, unreal.Vector(0, 0, 0), unreal.Rotator(0, 0, item.get("yaw", 0)))
    vol = actor.get_components_by_class(unreal.HeterogeneousVolumeComponent)[0]
    vr = vol.get_editor_property("volume_resolution")
    info = {"bp": item["bp"], "yaw": item.get("yaw", 0), "frame_rate": vol.get_editor_property("frame_rate"),
            "start_frame": vol.get_editor_property("start_frame"), "end_frame": vol.get_editor_property("end_frame"),
            "looping": vol.get_editor_property("looping"), "volume_resolution": [vr.x, vr.y, vr.z],
            "material": vol.get_material(0).get_path_name() if vol.get_material(0) else None}
    return actor, vol, info


def native_volume(item):
    """The Blueprint's fire on a plain, Movable HeterogeneousVolume actor (no construction script to re-run when a
    property is set). Material and frame settings come from the Blueprint, which is then removed."""
    bp_actor, bp_vol, info = describe(item)
    material = bp_vol.get_material(0)
    settings = {k: bp_vol.get_editor_property(k) for k in ("frame_rate", "start_frame", "end_frame", "looping")}
    bp_actor.destroy_actor()
    actor = actors.spawn_actor_from_class(unreal.HeterogeneousVolume, unreal.Vector(0, 0, 0), unreal.Rotator(0, 0, item.get("yaw", 0)))
    vol = actor.get_components_by_class(unreal.HeterogeneousVolumeComponent)[0]
    vol.set_mobility(unreal.ComponentMobility.MOVABLE)
    vol.set_material(0, material)
    for k, v in settings.items():
        vol.set_editor_property(k, v)
    return actor, vol, info


class Bake:
    """One state per editor tick. A 64 px probe capture with the same framing tells when the volume has drawn and when a
    newly set frame has streamed in (the component requests it on its own tick; it arrives after a few hundred ms, and
    until then the old frame is drawn): the full-size capture is exported once the probe has changed and held still for
    two ticks (at most FRAME_MAX_S; such frames are listed in _bake.json slow_frames)."""

    def __init__(self):
        # -ExecutePythonScript closes the editor once the script returns unless it is kept alive; finish() ends it.
        unreal.EditorPythonScripting.set_keep_python_script_alive(True)
        unreal.EditorLoadingAndSavingUtils.new_blank_map(False)
        self.world = unreal.get_editor_subsystem(unreal.UnrealEditorSubsystem).get_editor_world()
        # Ray-march jitter and stochastic filtering change every capture (a main view's TAA averages them away; a single
        # capture keeps the noise) - off, so a frame renders clean and the same frame renders bit-identical.
        for cmd in ("r.HeterogeneousVolumes.Jitter 0", "r.HeterogeneousVolumes.StochasticFiltering 0",
                    "r.SparseVolumeTexture.Streaming.ForceBlockingRequests 1"):
            unreal.SystemLibrary.execute_console_command(self.world, cmd)
        self.items = list(CFG["items"])
        self.res = CFG.get("res", 2048)
        self.cap, self.rt = self.capture(self.res)
        self.probe_cap, self.probe_rt = self.capture(PROBE_PX)
        self.actor = None
        self.frames = []
        self.index = 0
        self.handle = unreal.register_slate_post_tick_callback(self.tick)

    def capture(self, px):
        rt = unreal.RenderingLibrary.create_render_target2d(self.world, px, px, unreal.TextureRenderTargetFormat.RTF_RGBA16F,
                                                            unreal.LinearColor(0, 0, 0, 1), False)
        cap = actors.spawn_actor_from_class(unreal.SceneCapture2D, unreal.Vector(0, 0, 0), unreal.Rotator(0, 0, 0)).capture_component2d
        cap.set_editor_property("projection_type", unreal.CameraProjectionMode.ORTHOGRAPHIC)
        cap.set_editor_property("capture_source", unreal.SceneCaptureSource.SCS_SCENE_COLOR_HDR)
        cap.set_editor_property("texture_target", rt)
        cap.set_editor_property("capture_every_frame", False)
        cap.set_editor_property("capture_on_movement", False)
        cap.set_editor_property("always_persist_rendering_state", True)
        return cap, rt

    def aim(self, centre_y, centre_z, width):
        elev = math.radians(CFG.get("elevation", 12))
        dist = 4 * width
        for cap in (self.cap, self.probe_cap):
            # Looking along +X, raised by the elevation angle.
            cap.set_world_location_and_rotation(unreal.Vector(-dist * math.cos(elev), centre_y, centre_z + dist * math.sin(elev)),
                                                unreal.Rotator(0, -CFG.get("elevation", 12), 0), False, False)
            cap.set_editor_property("ortho_width", width)

    def probe(self):
        """The probe capture as 64 block sums (8 x 8 px each)."""
        self.probe_cap.capture_scene()
        px = unreal.RenderingLibrary.read_render_target_raw(self.world, self.probe_rt, False)
        blocks = [0.0] * 64
        for i, c in enumerate(px):
            blocks[(i // PROBE_PX // 8) * 8 + (i % PROBE_PX) // 8] += c.r + c.g + c.b
        return blocks

    def start(self, item):
        if self.actor:
            self.actor.destroy_actor()
        self.item = item
        self.actor, self.vol, self.info = native_volume(item)
        param = self.vol.get_material(0).get_editor_property("sparse_volume_texture_parameter_values")[0]
        self.svt_name = param.get_editor_property("parameter_info").get_editor_property("name")
        self.svt = param.get_editor_property("parameter_value")
        self.vol.set_playing(False)
        self.vol.set_editor_property("issue_blocking_requests", True)
        self.vol.set_editor_property("step_factor", CFG.get("step_factor", 0.5))
        vr = self.info["volume_resolution"]
        # Wide: a volume's frames carry their own offsets (FirePlace_1 sits 40 cm off its actor), and the actor's
        # bounds read a default 100 cm until it has streamed. compose_flipbook.py crops to what is lit.
        self.width = self.item.get("ortho_cm", 2.6 * max(vr))
        self.aim(0, vr[2] / 2, self.width)
        self.warm = self.lit = 0
        self.frames = []
        self.dir = os.path.join(CFG["out"], item["name"])
        os.makedirs(self.dir, exist_ok=True)

    def framed(self):
        """The volume has drawn: list its frames."""
        info = self.info
        first = int(info["start_frame"])
        last = int(info["end_frame"]) if info["end_frame"] > info["start_frame"] else first + self.item.get("max_frames", 126)
        self.frames = list(range(first, last, self.item.get("step", 1)))[: self.item.get("max_frames", 126)]
        info.update({"ortho_width_cm": self.width, "centre_z_cm": info["volume_resolution"][2] / 2,
                     "elevation_deg": CFG.get("elevation", 12), "res": self.res, "frames": self.frames,
                     "step": self.item.get("step", 1), "slow_frames": []})
        unreal.log(f"bake_vdb: {self.item['name']} {len(self.frames)} frames {json.dumps(info)}")
        self.index = 0
        self.set_at = None

    def tick(self, dt):
        # An exception here would leave the editor open (and the GPU lock held) until the runner's timeout.
        try:
            self.step()
            self.unpin()
        except Exception:
            unreal.log_error("bake_vdb: failed\n" + traceback.format_exc())
            self.finish()

    def unpin(self):
        """Give the volume's material back its animated texture after this tick's captures. UE 5.8 editor builds only:
        the component's TickComponent sets its dynamic material's texture to the requested FRAME object, and on the next
        tick its WITH_EDITOR "material changed" check reads that frame (1 frame long) and pins Start/End/Frame to 0, so
        SetFrame never shows another frame. Restoring the animated texture keeps that check quiet."""
        if self.actor is None:
            return
        mid = self.vol.get_material(0)
        if isinstance(mid, unreal.MaterialInstanceDynamic):
            mid.set_sparse_volume_texture_parameter_value(self.svt_name, self.svt)

    def finish(self):
        unreal.unregister_slate_post_tick_callback(self.handle)
        unreal.EditorPythonScripting.set_keep_python_script_alive(False)

    def step(self):
        if self.actor is None or (self.frames and self.index >= len(self.frames)):
            if self.actor is not None:
                json.dump(self.info, open(os.path.join(self.dir, "_bake.json"), "w"), indent=1)
            if not self.items:
                unreal.log("bake_vdb: done")
                self.finish()
                return
            self.start(self.items.pop(0))
            return
        if not self.frames:
            # Shaders compile and the volume's pages stream before anything is drawn.
            self.vol.set_frame(float(WARM_FRAME))
            self.warm += 1
            self.lit = self.lit + 1 if sum(self.probe()) > 1e-3 else 0
            if self.warm % 300 == 0:
                unreal.log(f"bake_vdb: warming {self.warm} ticks, lit {self.lit}")
            if self.warm > WARM_MAX_TICKS:
                raise RuntimeError("the volume never drew anything in the capture")
            if self.lit >= WARM_LIT_TICKS:
                self.exported = self.probe()
                self.framed()
            return
        frame = self.frames[self.index]
        if self.set_at is None:
            self.vol.set_frame(float(frame))
            self.set_at, self.last, self.still = time.time(), None, 0
            return
        blocks = self.probe()
        # The render is deterministic: an unchanged frame reads back bit for bit the same, so any difference is the new
        # frame (consecutive VDB frames differ by well under 1 % of the light).
        moved = blocks != self.exported
        same = blocks == self.last
        if CFG.get("debug") and self.index < 3:
            d_last = sum(abs(a - b) for a, b in zip(blocks, self.last)) if self.last else -1
            d_exp = sum(abs(a - b) for a, b in zip(blocks, self.exported))
            unreal.log(f"bake_vdb debug: frame {frame} t {time.time() - self.set_at:.2f} sum {sum(blocks):.5f} "
                       f"d_last {d_last:.6g} d_exported {d_exp:.6g} component frame {self.vol.get_editor_property('frame')} "
                       f"playing {self.vol.get_editor_property('playing')} mobility {self.vol.get_editor_property('mobility')}")
        self.still = self.still + 1 if same else 0
        self.last = blocks
        slow = time.time() - self.set_at > FRAME_MAX_S
        # Two unchanged probes in a row: a frame can stream in at a coarse mip first and sharpen a tick later.
        if not (moved and self.still >= 2) and not slow:
            return
        if slow:
            self.info["slow_frames"].append(frame)
            if len(self.info["slow_frames"]) == 3 == self.index + 1:
                raise RuntimeError(f"{self.item['name']}: the first 3 frames never changed the picture - frames are not being set")
        self.cap.capture_scene()
        unreal.RenderingLibrary.export_render_target(self.world, self.rt, self.dir, f"f{frame:04d}.exr")
        self.exported = blocks
        self.index += 1
        self.set_at = None

if "--info" in sys.argv:
    unreal.EditorLoadingAndSavingUtils.new_blank_map(False)
    for it in CFG["items"]:
        unreal.log("bake_vdb info: " + json.dumps(describe(it)[2]))
else:
    try:
        BAKE = Bake()
    except Exception:
        unreal.log_error("bake_vdb: failed\n" + traceback.format_exc())
        unreal.EditorPythonScripting.set_keep_python_script_alive(False)
