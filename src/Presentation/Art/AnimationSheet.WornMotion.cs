using Godot;
using System.Text.Json;
using UNNAMED.Presentation.Player;
using UNNAMED.Presentation.Settings;
using UNNAMED.Presentation.Ui.Models;
using UNNAMED.Domain.Character;
using UNNAMED.World.Runtime;

namespace UNNAMED.Presentation.Art;

public partial class AnimationSheet
{
    private SkinnedFigure? _wornMotionFigure;
    private CameraRig? _wornMotionCamera;
    private string[] _wornMotionGarments = Array.Empty<string>();
    private int _wornMotionItem;
    private ulong _wornMotionStart;
    private ulong _wornMotionLastFrame;
    private readonly List<object> _wornMotionFrames = new();
    private int _wornMotionFrame;
    private bool _wornMotionLoaded;
    private bool _wornMotionCapturePending;
    private bool _wornWindowRecording;
    private bool _wornRecordingStarted;
    private bool _wornRecordingDone;
    private Label? _wornMotionCaption;
    private bool _wornUsesItemBinding;
    private static readonly (string State, double Seconds)[] WornMotionStates =
        { ("idle", 1), ("walk", 3), ("run", 3), ("crouch", 3), ("attack", 3.3) };
    private IEnumerable<(string State, double Seconds)> CurrentWornMotionStates =>
        WornMotionStates.Append(("unequipped", 1.5));

    private void AddWornMotion()
    {
        _wornMotionFigure = SkinnedFigure.Create(_art, _bindings, _profilePerson, _gearBody, people: "people.veth")
            ?? throw new InvalidDataException("Native movement review body missing");
        AddChild(_wornMotionFigure);
        ApplyWornReviewLook(_wornMotionFigure);
        _wornMotionGarments = _gearItems![7..].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (_wornMotionGarments.Length == 0) throw new InvalidDataException("Native movement review has no garments");
        _camera.Fov = new CameraPrefs().Fov;
        _camera.Current = false;
        _wornMotionCamera = new CameraRig();
        AddChild(_wornMotionCamera);
        _wornMotionCamera.Camera.Current = true;
        _wornWindowRecording = OS.GetEnvironment("OTHERREACH_NATIVE_WEAR_RECORDING") == "1";
        if (_wornWindowRecording)
        {
            DisplayServer.WindowSetTitle("Otherreach P2 armour " + _gearBody);
            var canvas = new CanvasLayer();
            _wornMotionCaption = new Label { Position = new Vector2(24, 24) };
            _wornMotionCaption.AddThemeFontSizeOverride("font_size", 24);
            canvas.AddChild(_wornMotionCaption);
            AddChild(canvas);
        }
        LoadWornMotionItem();
    }

    private void LoadWornMotionItem()
    {
        var figure = _wornMotionFigure!;
        string selected = _wornMotionGarments[_wornMotionItem];
        string selection = selected.Contains('@') ? selected.Split('@', 2)[1] : selected;
        _wornUsesItemBinding = selected.Contains('=');
        if (_wornUsesItemBinding)
        {
            WornOutfit outfit = default;
            var slots = new HashSet<string>(StringComparer.Ordinal);
            foreach (string piece in selection.Split('+'))
            {
                string[] parts = piece.Split('=', 2);
                if (parts.Length != 2 || !parts[1].StartsWith("item.armor.", StringComparison.Ordinal) || !slots.Add(parts[0]))
                    throw new InvalidDataException("Normal worn review requires unique slots and actual armour item IDs");
                outfit = parts[0] switch
                {
                    "head" => outfit with { Head = parts[1] },
                    "chest" => outfit with { Chest = parts[1] },
                    "feet" => outfit with { Feet = parts[1] },
                    "hands" => outfit with { Hands = parts[1] },
                    "legs" => outfit with { Legs = parts[1] },
                    _ => throw new InvalidDataException("Unknown armour review slot: " + parts[0])
                };
            }
            figure.WearEquipment(default);
            figure.WearEquipment(outfit);
        }
        else figure.ReviewNativeGarment(selection);
        figure.Position = Vector3.Zero;
        figure.Hold(null);
        figure.SetPosture(false, false);
        figure.SetStance(CombatStance.AtRest);
        _wornMotionStart = Time.GetTicksUsec();
        _wornMotionLoaded = false;
        _wornMotionFrame = 0;
        _wornMotionFrames.Clear();
        Directory.CreateDirectory(Path.Combine(_directory, _wornMotionGarments[_wornMotionItem].Split('@', 2)[0]));
    }

    private void WornMotionFrame(double delta)
    {
        var figure = _wornMotionFigure!;
        if (_wornRecordingDone)
        {
            if (File.Exists(Path.Combine(_directory, "recording.stopped"))) CaptureQuit.At(this, 0);
            return;
        }
        ulong now = Time.GetTicksUsec();
        double elapsed = (now - _wornMotionStart) / 1_000_000.0;
        if (!_wornMotionLoaded && elapsed >= 3)
        {
            _wornMotionStart = now;
            _wornMotionLastFrame = 0;
            elapsed = 0;
            if (_wornWindowRecording && !_wornRecordingStarted)
            {
                if (!File.Exists(Path.Combine(_directory, "native.ready")))
                {
                    File.WriteAllText(Path.Combine(_directory, "native_window.json"), JsonSerializer.Serialize(new
                    {
                        title = GetTree().Root.Title,
                        window_handle = DisplayServer.WindowGetNativeHandle(DisplayServer.HandleType.WindowHandle),
                        process_id = System.Environment.ProcessId
                    }));
                    File.WriteAllText(Path.Combine(_directory, "native.ready"), DateTimeOffset.UtcNow.ToString("O"));
                }
                if (File.Exists(Path.Combine(_directory, "recording.started")))
                {
                    _wornRecordingStarted = true;
                    _wornMotionLoaded = true;
                }
            }
            else _wornMotionLoaded = true;
        }
        double inState = elapsed;
        string state = "idle";
        bool done = _wornMotionLoaded;
        foreach (var phase in CurrentWornMotionStates)
        {
            if (inState < phase.Seconds) { state = phase.State; done = false; break; }
            inState -= phase.Seconds;
        }
        string garment = _wornMotionGarments[_wornMotionItem].Split('@', 2)[0];
        if (done)
        {
            if (_wornMotionCapturePending) return;
            string folder = Path.Combine(_directory, garment);
            File.WriteAllText(Path.Combine(folder, "FRAME_CLOCKS.json"), JsonSerializer.Serialize(new
            {
                body = figure.ModelId, garment, selection = _wornMotionGarments[_wornMotionItem], frames = _wornMotionFrames, end_monotonic_us = now,
                camera_distance_m = 3.5, camera_fov_degrees = _camera.Fov,
                method = _wornWindowRecording
                    ? "Primary movement evidence is the continuous Windows Graphics Capture recording of the actual game window; these are animation telemetry samples, not reconstructed video frames. Native gameplay clips, ordinary CameraRig on a lit QA floor. Not a world-route or performance gate."
                    : "Native game figure and unchanged gameplay clips, real-time on a lit QA floor. In-place loading/warmup omitted between item segments. No held poses, speed changes or interpolation. Supporting frame capture; not the primary movement gate."
            }, new JsonSerializerOptions { WriteIndented = true }));
            if (++_wornMotionItem >= _wornMotionGarments.Length)
            {
                if (_wornWindowRecording)
                {
                    _wornRecordingDone = true;
                    File.WriteAllText(Path.Combine(_directory, "recording.finished"), DateTimeOffset.UtcNow.ToString("O"));
                    if (_wornMotionCaption is not null) _wornMotionCaption.Text = "Native movement batch complete";
                    return;
                }
                CaptureQuit.At(this, 0);
                return;
            }
            LoadWornMotionItem();
            return;
        }
        if (!_wornMotionLoaded) state = "idle";
        if (state == "unequipped")
        {
            if (_wornUsesItemBinding) figure.WearEquipment(default);
            else figure.ReviewNativeGarment("");
        }
        float speed = state == "walk" ? 1.4f : state == "run" ? 3.2f : state == "crouch" && inState > .5 ? .8f : 0;
        figure.SetPosture(state == "crouch", false);
        if (state == "attack")
        {
            figure.Hold("item.weapon.engraved_road_sword");
            float cycle = (float)(inState % 1.1);
            var stance = cycle < .25f ? new CombatStance(CombatPhase.Windup, cycle / .25f, Held.Sword, false)
                : cycle < .55f ? new CombatStance(CombatPhase.Active, (cycle - .25f) / .3f, Held.Sword, false)
                : new CombatStance(CombatPhase.Recovery, (cycle - .55f) / .55f, Held.Sword, false);
            figure.SetStance(stance);
        }
        else figure.SetStance(CombatStance.AtRest);
        var direction = Vector3.Back; // The unchanged native bodies face +Z.
        figure.SetMovementDirection(direction);
        figure.Pose(figure.Position + direction * speed * (float)delta, 0, speed, delta);
        _wornMotionCamera!.Yaw = state switch
        {
            "run" => Mathf.DegToRad(220),
            "crouch" => Mathf.DegToRad(140),
            "attack" => Mathf.DegToRad(40),
            _ => Mathf.DegToRad(15)
        };
        _wornMotionCamera.Crouch = figure.Crouch;
        _wornMotionCamera.Follow(figure.Position, delta);
        if (_wornMotionCaption is not null) _wornMotionCaption.Text = garment + "\n" + figure.ModelId + " / " + state;
        if (_wornWindowRecording)
        {
            if (_wornMotionLoaded) _wornMotionFrames.Add(new { process_frame = Engine.GetProcessFrames(), monotonic_us = now,
                elapsed_s = elapsed, state, clip = figure.ClipState, x = figure.Position.X, y = figure.Position.Y, z = figure.Position.Z,
                creator_outfit_visible = figure.Skeleton.GetChildren().OfType<MeshInstance3D>()
                    .Where(m => m.GetMeta("creator_part_kind", "").AsString() == "Outfit" && m.Visible)
                    .Select(m => m.Name.ToString()).ToArray(),
                replaces_creator_sweater = figure.Skeleton.GetChildren().OfType<MeshInstance3D>()
                    .Any(m => m.GetMeta("native_equipment_replaces_sweater", false).AsBool()) });
            return;
        }
        if (!_wornMotionLoaded || _wornMotionCapturePending || now - _wornMotionLastFrame < 100_000) return;
        _wornMotionLastFrame = now;
        if (DisplayServer.GetName() == "headless") return;
        SaveWornMotionFrame(garment, state, figure.ClipState, figure.Position);
    }

    private void ApplyWornReviewLook(SkinnedFigure figure)
    {
        string json = Godot.FileAccess.GetFileAsString("res://Art/humgen_creator.json");
        var catalogue = CreatorCatalogue.Read(json);
        var data = new HumGenCreatorData(json);
        string sex = figure.ModelId.EndsWith("female", StringComparison.Ordinal) ? "f" : "m";
        string face = catalogue.Options(CreatorChoice.Face, sex).First(o => o.Label == "Caucasian 2").Id;
        int stature = (int)MathF.Round(data.Preset(face).GetProperty("height").GetProperty("set").GetSingle() * 10);
        var look = new CharacterAppearance(stature, face, null, null, "outfit.office.new_intern", "footwear.extra_outfits_pack.pirate_boots");
        PeopleAppearance.ApplyLook(figure, "people.veth", look, _art, figure.ModelId);
    }

    private async void SaveWornMotionFrame(string garment, string state, string? clip, Vector3 position)
    {
        _wornMotionCapturePending = true;
        string file = $"{_wornMotionFrame++:D6}.png";
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        ulong captured = Time.GetTicksUsec();
        using var image = GetViewport().GetTexture().GetImage();
        if (image.SavePng(Path.Combine(_directory, garment, file)) != Error.Ok)
            throw new IOException("Native movement frame could not be saved");
        _wornMotionFrames.Add(new { file, monotonic_us = captured, elapsed_s = (captured - _wornMotionStart) / 1_000_000.0,
            state, clip, x = position.X, y = position.Y, z = position.Z });
        _wornMotionCapturePending = false;
    }
}
