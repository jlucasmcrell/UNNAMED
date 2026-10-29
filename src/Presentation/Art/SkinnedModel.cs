// UNNAMED Presentation - a rigged model from the asset library and the clips that play on it
// Godot presentation only: no gameplay state lives here (D-11)

using Godot;

namespace UNNAMED.Presentation.Art;

/// <summary>
/// A skinned model with its clips, each under the name of the state it shows (<c>idle</c>, <c>walk</c>, <c>attack</c>...): the clips
/// are retargeted onto this model's skeleton by bone name, so a shared rig's clip (the NPC set, the husk's walk) plays on every
/// body of that rig. The owner of the figure says which state is showing; timing comes from the simulation, never from a clip.
/// </summary>
public sealed partial class SkinnedModel : Node3D
{
    private readonly HashSet<string> _states = new(StringComparer.Ordinal);
    public double IdleStartPhase { get; set; }
    private bool _idlePhaseStarted;
    private Node? _locomotion;
    public IReadOnlyDictionary<string, float>? MotionPaces { get; private set; }
    public IReadOnlyDictionary<string, float[]>? MotionActionPhases { get; private set; }
    public IReadOnlyDictionary<int, Quaternion>? WeaponGrip { get; private set; }
    public bool HasFootPlantEvents { get; private set; }
    public event Action<string,string,double>? FootPlanted;
    private Godot.Collections.Dictionary _contacts = new();
    private AnimationPlayer _player = null!;
    private HumGenLods? _humgenLods;
    private HumGenCloseupTextures? _closeTextures;
    private bool? _reviewCloseTextures;
    internal void ReviewCloseTextures(bool? enabled) { _reviewCloseTextures=enabled; _closeTextures?.Update((enabled??true) && _humgenLods?.Level==0 && IsVisibleInTree()); }
    internal void ReviewAllLods(bool all) => _humgenLods?.ReviewAllLevels(all);

    /// <summary>Show or hide one of the body's own meshes so its level of detail stays exclusive (HumGen bodies choose it each band).</summary>
    public void OutfitVisible(MeshInstance3D mesh, bool shown)
    {
        if (_humgenLods is { } lods) lods.OutfitVisible(mesh, shown);
        else mesh.Visible = shown;
    }

    /// <summary>Meshes taken off and put on the body after it was built, so a HumGen body's level of detail covers them too.</summary>
    public void SwapParts(IReadOnlyCollection<MeshInstance3D> removed, IReadOnlyCollection<MeshInstance3D> added) => _humgenLods?.Swap(removed, added);
    public bool ForceLod0
    {
        set
        {
            if (_humgenLods is not { } lods || lods.CloseUp == value) return;
            lods.CloseUp = value;
            if (value) lods.Update(0);
        }
    }

    public Skeleton3D Skeleton { get; private set; } = null!;

    /// <summary>The state showing now, or null before the first.</summary>
    public string? Current { get; private set; }

    /// <summary>
    /// The rigged model <paramref name="assetId"/> with <paramref name="clips"/> (state to clip ID); null without the model. With an
    /// <paramref name="owner"/> (what it draws: <c>creature.beast.wolf_grey</c>), each state's clip is recorded in the coverage report.
    /// </summary>
    public static SkinnedModel? Create(ArtLibrary art, string assetId, IReadOnlyDictionary<string, string> clips, string? owner = null,
        IReadOnlyDictionary<string, string>? nativeStates = null)
    {
        if (art.Rigged(assetId) is not { } model)
            return null;
        if (ArtLibrary.Find<Skeleton3D>(model) is not { } skeleton)
        {
            art.Report(assetId, "its rigged file holds no skeleton", assetId);
            model.Free();
            return null;
        }
        var figure = new SkinnedModel { Name = assetId, Skeleton = skeleton };
        figure.AddChild(model);
        HumGenIdentityWeights.Apply(model, art, assetId);
        WearFrontier(art, assetId, model, skeleton);
        // THE RETURN VALUE IS THE ONLY EVIDENCE THIS FIRES, AND IT WAS BEING THROWN AWAY. CharacterMaterials matches a
        // surface material to a zone by the END of its name - _skin, _cloth, _leather, _eye and so on - and applies
        // what glTF cannot say, which for skin is subsurface scattering at 0.22 with the skin mode on and for the eye
        // is a wet cornea at roughness 0.05 with a full clearcoat. Two blind reviews of this region scored the skin
        // 3 and the eyes 2 and named exactly those two absences. A count of zero means the names do not match and the
        // spec is being read for nothing, which is not something anyone can tell by looking at the frame.
        int upgraded = CharacterMaterials.Upgrade(model, out int zoneNamed, HumanEye.Load(art, assetId),
            nativeHumGen: art.RiggedFile(assetId, "native_materials.json") is not null);
        HumGenMaterials.Apply(model, art, assetId);        HumGenMouth.Apply(model, art, assetId);
        // SAY WHICH OF THE THREE REASONS IT IS, not the one this used to assume. See CharacterMaterials.Upgrade's
        // second parameter: a single-atlas model with no zone names at all is NORMAL and was being reported as a
        // name mismatch every run, which is how the creatures came to read as broken rather than as out of scope.
        GD.Print($"UNNAMED character materials: {upgraded} surfaces upgraded on {assetId}" +
                 (upgraded > 0 ? ""
                  : zoneNamed == 0
                      ? " - this model carries no zone names at all (one atlas), so there is nothing for the zone pass to do; not a mismatch"
                      : $" - {zoneNamed} surface(s) carry zone names but none matched a kind in character_materials.json,"
                        + " so skin SSS and the eye cornea are NOT applied"));
        int levels = CharacterMaterials.ApplyLods(model);
        if (assetId.StartsWith("humgen_", StringComparison.Ordinal))
            figure._humgenLods = new HumGenLods(model);
        // Imported players (none in these files today) would fight ours over the same bones.
        foreach (var imported in model.FindChildren("*", nameof(AnimationPlayer), true, false))
            imported.QueueFree();
        // Imported clips omit constant tracks. Missing channels must return to
        // native bone rest, rather than retaining the preceding clip's root yaw.
        var player = new AnimationPlayer { Name = "Clips", Deterministic = assetId.StartsWith("humgen_", StringComparison.Ordinal) };
        figure.AddChild(player);
        player.RootNode = new NodePath("..");
        string skeletonPath = figure.GetPathTo(skeleton);
        var library = new AnimationLibrary();
        foreach (var (state, clipId) in clips)
        {
            if (IsComparisonBody(assetId)) continue;
            if (art.Clip(clipId) is not { } clip)
            {
                if (owner is not null)
                    art.Coverage.Fallback("clip", $"{owner}:{state}", $"{art.Why(clipId) ?? "no clip"}: the body holds its last pose for this state", clipId);
                continue;
            }
            bool loop = art.Info(clipId)?.Loop ?? state is "idle" or "walk" or "run" or "sprint" or "talk" or "work";
            library.AddAnimation(state, ArtLibrary.Retarget(clip, skeletonPath, skeleton, loop, art.ClipRest(clipId)));
            figure._states.Add(state);
            if (owner is not null)
                art.Coverage.Resolved("clip", $"{owner}:{state}", clipId);
        }
        if (IsComparisonBody(assetId))
            figure.ComparisonClips(assetId, library, skeletonPath, art.RiggedFile(assetId, assetId + "_rigged.glb"));
        // Finish state selection before AnimationPlayer/FootPlants cache lookups and add per-state contact method tracks.
        if (nativeStates is not null)
            foreach (var (state, native) in nativeStates) figure.Alias(library, state, native);
        player.AddAnimationLibrary("", library);
        figure._player = player;
        var plants=GD.Load<GDScript>("res://Art/FootPlants.gd").New().As<Node>();
        figure.HasFootPlantEvents=plants.Call("configure",player,Callable.From(()=>
            figure._locomotion?.Get("active").AsBool()==true
                ? figure._locomotion.Call("dominant_clip").AsString():figure.Current??"")).AsBool();
        if(figure.HasFootPlantEvents)plants.Connect("planted",Callable.From<string,string,double>((clip,side,seconds)=>figure.FootPlanted?.Invoke(clip,side,seconds)));
        else plants.Free();
        // Keep the opt-in interior's local player continuous through space changes; NPC visibility throttling remains.
        if (levels >= 4 && !(VisualOptions.Barrowworks && owner == "player"))
            figure.Throttle();
        figure.FindOutfits(model);
        figure.SetOutfit("base");
        if (owner=="player") figure._closeTextures=HumGenCloseupTextures.Create(model,art,assetId);
        return figure;
    }

    // Attach before material/LOD/face discovery: the native base (including its teeth and identity) stays intact.
    private static void WearFrontier(ArtLibrary art, string assetId, Node3D model, Skeleton3D target)
    {
        string? outfitId = assetId switch
        {
            "humgen_player_ada_f" => "humgen_frontier_ada_f",
            "humgen_player_mateo_m" => "humgen_frontier_mateo_m",
            _ => null
        };
        if (outfitId is null) return;
        var outfit = art.Rigged(outfitId) ?? throw new InvalidDataException("Default frontier outfit missing: " + outfitId);
        var source = ArtLibrary.Find<Skeleton3D>(outfit) ?? throw new InvalidDataException("Frontier skeleton missing");
        if (source.GetBoneCount() != target.GetBoneCount()) throw new InvalidDataException("Frontier bone count differs");
        for (int i = 0; i < target.GetBoneCount(); i++)
            if (source.GetBoneName(i) != target.GetBoneName(i))
                throw new InvalidDataException("Frontier native bone order differs");
        string[] originals = assetId.EndsWith("ada_f", StringComparison.Ordinal)
            ? new[] { "Chinos_Female", "Garden_Boots_Female", "Sweater_1_Female" }
            : new[] { "Skinny_Jeans_Male", "Garden_Boots_Male", "TSHIRT_Male" };
        var old = model.FindChildren("*", nameof(MeshInstance3D), true, false).OfType<MeshInstance3D>()
            .Where(mesh => originals.Any(part => mesh.Name.ToString().Contains("_" + part + "_LOD", StringComparison.Ordinal))).ToArray();
        var garments = outfit.FindChildren("*", nameof(MeshInstance3D), true, false).OfType<MeshInstance3D>().ToArray();
        if (old.Length != 12 || garments.Length != 12) throw new InvalidDataException("Expected three native four-LOD garments");
        foreach (var mesh in old) { mesh.GetParent().RemoveChild(mesh); mesh.Free(); }
        foreach (var mesh in garments)
        {
            mesh.Owner = null;
            mesh.GetParent().RemoveChild(mesh);
            target.AddChild(mesh);
            mesh.Skeleton = mesh.GetPathTo(target);
        }
        outfit.Free();
        GD.Print($"UNNAMED frontier default: {assetId}, {garments.Length} native garment LOD meshes; base body unchanged");
    }

    /// <summary>Whether this body is a comparison cast's export, which brings its own clips and no bound outfit.</summary>
    private static bool IsComparisonBody(string assetId) =>
        VisualOptions.ComparisonBodyPrefix is { } prefix && assetId.StartsWith(prefix, StringComparison.Ordinal);

    /// <summary>Where each comparison cast's offline motion libraries live, by cast id.</summary>
    private static string ComparisonMotionFolder => VisualOptions.ComparisonCast == "humgen" ? "HumGenMotion" : "MPFBMotion";

    // Every clip in these libraries was retargeted onto this exact body offline; only node paths are copied here.
    private void ComparisonClips(string assetId, AnimationLibrary library, string skeletonPath, string? bodyPath)
    {
        // Production HumGen motion stays in its installed payload, outside the public source repository.
        // The old resource fallback is for explicit reference casts only.
        bool humgen = VisualOptions.ComparisonCast == "humgen";
        string payloadRoot = humgen
            ? System.Environment.GetEnvironmentVariable("OTHERREACH_CHARACTER_ROOT") ?? System.IO.Path.Combine(
                OS.HasFeature("template") ? System.IO.Path.GetDirectoryName(OS.GetExecutablePath())! : ProjectSettings.GlobalizePath("res://"), "humgen_characters")
            : UNNAMED.Presentation.MHCharacters.MetaHumanFigure.AssetRoot;
        string externalMotion = System.IO.Path.Combine(payloadRoot, "mocap_motion", assetId + "_motion.res");
        if (humgen && !System.IO.File.Exists(externalMotion))
            throw new FileNotFoundException("HumGen exact-target motion payload is missing", externalMotion);
        var motion = GD.Load<AnimationLibrary>(System.IO.File.Exists(externalMotion)
                ? externalMotion
                : $"res://Art/{ComparisonMotionFolder}/{assetId}_motion.res")
            ?? throw new InvalidDataException("comparison motion library missing: " + assetId);
        if (bodyPath is null || motion.GetMeta("target_body_sha256").AsString() != Godot.FileAccess.GetSha256(bodyPath))
            throw new InvalidDataException("comparison motion library target body differs: " + assetId);
        if (VisualOptions.ComparisonCast == "humgen")
            Ui.Models.HumGenMotionPolicy.Validate(motion.GetAnimationList().Select(name => name.ToString()),
                motion.GetMeta("animation_sources", new Godot.Collections.Dictionary()).AsGodotDictionary()
                    .ToDictionary(p => p.Key.AsString(), p => p.Value.AsString(), StringComparer.Ordinal),
                motion.GetMeta("foot_plants_schema", 0).AsInt32(), motion.GetMeta("source_playback_schema", 0).AsInt32());
        foreach (string clip in motion.GetAnimationList())
        {
            var animation = (Animation)motion.GetAnimation(clip).Duplicate();
            for (int track = 0; track < animation.GetTrackCount(); track++)
            {
                string bone = animation.TrackGetPath(track).GetSubName(0);
                if (Skeleton.FindBone(bone) < 0) throw new InvalidDataException("comparison motion bone missing: " + bone);
                animation.TrackSetPath(track, new NodePath($"{skeletonPath}:{bone}"));
            }
            library.AddAnimation(clip, animation);
            _states.Add(clip);
        }
        MotionPaces = motion.GetMeta("motion_paces").AsGodotDictionary().ToDictionary(p => p.Key.AsString(), p => p.Value.AsSingle());
        MotionActionPhases = motion.GetMeta("action_phases", new Godot.Collections.Dictionary()).AsGodotDictionary()
            .ToDictionary(p => p.Key.AsString(), p => p.Value.AsGodotArray().Select(v => v.AsSingle()).ToArray());
        library.SetMeta("foot_plants_schema",motion.GetMeta("foot_plants_schema",0));
        library.SetMeta("foot_plants",motion.GetMeta("foot_plants",new Godot.Collections.Dictionary()));
        _contacts = motion.GetMeta("motion_phases").AsGodotDictionary();
        if (VisualOptions.ComparisonCast == "humgen")
        {
            // HumGen closes its own hand through HandGrip. MPFB's authored local
            // rotations belong to a different rest frame and must never be reused.
            GD.Print($"UNNAMED HumGen motion: {assetId}, {motion.GetAnimationList().Count} exact-target clips");
            return;
        }
        string gripId = motion.GetMeta("variant_base_rig", assetId).AsString();
        var grip = System.Text.Json.JsonDocument.Parse(Godot.FileAccess.GetFileAsString($"res://Art/MPFBHandGrips/{gripId}.json"));
        using (grip)
        {
            string expectedHash = motion.GetMeta("base_target_body_sha256", motion.GetMeta("target_body_sha256")).AsString();
            if (grip.RootElement.GetProperty("target_body_sha256").GetString() != expectedHash)
                throw new InvalidDataException("MPFB grip target body differs: " + assetId);
            var rotations = new Dictionary<int, Quaternion>();
            foreach (var digit in new[] { "thumb", "index", "middle", "ring", "pinky" })
                for (int joint = 1; joint <= 3; joint++)
                {
                    string name = $"{digit}_0{joint}_r";
                    int bone = Skeleton.FindBone(name);
                    var q = grip.RootElement.GetProperty("rotations").GetProperty(name).EnumerateArray().Select(v => v.GetSingle()).ToArray();
                    if (bone < 0 || q.Length != 4 || q.Any(v => !float.IsFinite(v))) throw new InvalidDataException("Invalid authored digit: " + name);
                    var rotation = new Quaternion(q[0], q[1], q[2], q[3]);
                    if (Math.Abs(rotation.LengthSquared() - 1) > .001f) throw new InvalidDataException("Non-unit authored digit: " + name);
                    rotations.Add(bone, rotation);
                }
            WeaponGrip = rotations;
        }
        GD.Print($"UNNAMED {VisualOptions.ComparisonCast} motion: {assetId}, {motion.GetAnimationList().Count} offline target clips; no runtime retarget");
    }

    public bool MoveBlend(Vector2 velocity, double delta, IReadOnlyDictionary<string, float> paces, float scale)
    {
        if (!IsComparisonBody(Name) || !Has("strafe_left")) return false;
        if (_locomotion is null)
        {
            _locomotion = GD.Load<GDScript>("res://Art/LocomotionBlend.gd").New().As<Node>();
            AddChild(_locomotion);
            var measured = new Godot.Collections.Dictionary();
            foreach (var (name, pace) in paces) measured[name] = pace * scale;
            if (!_locomotion.Call("configure", _player, measured, _throttled, _contacts).AsBool()) return false;
        }
        bool moving = _locomotion.Call("move", velocity, delta).AsBool();
        if (moving) Current = "locomotion_2d";
        return moving;
    }

    // Outfit variants (charstd export_character.py): a surface whose material is named "<variants>::<material>" shows only under those
    // variants - a garment, or the body under a garment only some variants wear. Everything else always shows.
    private readonly List<(MeshInstance3D Mesh, int Surface, string[] Variants, bool Whole)> _outfitParts = new();
    private string? _outfit;
    private static ShaderMaterial? _unseen;

    private void FindOutfits(Node model)
    {
        foreach (var mesh in model.FindChildren("*", nameof(MeshInstance3D), true, false).Cast<MeshInstance3D>())
        {
            if (mesh.Mesh is null)
                continue;
            var tagged = new List<(int, string[])>();
            for (int i = 0; i < mesh.Mesh.GetSurfaceCount(); i++)
            {
                string name = mesh.Mesh.SurfaceGetMaterial(i)?.ResourceName ?? string.Empty;
                int cut = name.IndexOf("::", StringComparison.Ordinal);
                if (cut > 0)
                    tagged.Add((i, name[..cut].Split(',')));
            }
            bool whole = tagged.Count == mesh.Mesh.GetSurfaceCount() && tagged.All(t => t.Item2.SequenceEqual(tagged[0].Item2));
            foreach (var (surface, variants) in tagged)
                _outfitParts.Add((mesh, surface, variants, whole));
        }
    }

    /// <summary>Show one outfit variant (unknown names show <c>base</c>); a model without variants ignores it.</summary>
    public void SetOutfit(string variant)
    {
        if (_outfitParts.Count == 0)
            return;
        if (!_outfitParts.Any(p => p.Variants.Contains(variant)))
            variant = "base";
        if (variant == _outfit)
            return;
        _outfit = variant;
        _unseen ??= new ShaderMaterial { Shader = new Shader { Code = "shader_type spatial; render_mode unshaded, cull_disabled; void fragment() { discard; }" } };
        foreach (var (mesh, surface, variants, whole) in _outfitParts)
        {
            bool shown = variants.Contains(variant);
            if (whole)
            {
                if (_humgenLods is { } lods) lods.OutfitVisible(mesh, shown);
                else mesh.Visible = shown;
            }
            else
                mesh.SetSurfaceOverrideMaterial(surface, shown ? null : _unseen);
        }
    }

    // A MetaHuman's clip and skeleton modifiers (springs, IK, look-at) step every frame within 10 m, at 30 Hz to 20 m, at 15 Hz
    // beyond, and not at all off screen: each step is the time owed since the last, so motion keeps its speed (the 13-figure budget).
    // By time, not by frame count: at a low frame rate "every 4th frame" was a few updates a second.
    private bool _throttled;
    private double _owed;
    private VisibleOnScreenNotifier3D _seen = null!;

    private void Throttle()
    {
        _throttled = true;
        _player.CallbackModeProcess = AnimationMixer.AnimationCallbackModeProcess.Manual;
        Skeleton.ModifierCallbackModeProcess = Skeleton3D.ModifierCallbackModeProcessEnum.Manual;
        _seen = new VisibleOnScreenNotifier3D { Name = "Seen", Aabb = new Aabb(new Vector3(-1, 0, -1), new Vector3(2, 2.2f, 2)) };
        AddChild(_seen);
    }

    public override void _Notification(int what) { if(what==NotificationDisabled) _closeTextures?.Update(false); }
    public override void _ExitTree() => _closeTextures?.Update(false);

    public override void _Process(double delta)
    {
        _humgenLods?.Update(GetViewport().GetCamera3D() is { } eye ? eye.GlobalPosition.DistanceTo(GlobalPosition) : 0f);
        _closeTextures?.Update((_reviewCloseTextures??true) && _humgenLods?.Level==0 && IsVisibleInTree());
        if (!_throttled)
            return;
        _owed += delta;
        if (!_seen.IsOnScreen())
            return;
        float distance = GetViewport().GetCamera3D() is { } camera ? camera.GlobalPosition.DistanceTo(GlobalPosition) : 0f;
        if (_owed < (distance > 20f ? 1.0 / 15 : distance > 10f ? 1.0 / 30 : 0))
            return;
        double step = Math.Min(_owed, 0.25);
        _owed = 0;
        if (_locomotion?.Get("active").AsBool() == true) _locomotion.Call("advance", step);
        else _player.Advance(step);
        Skeleton.Advance(step);
    }

    public bool Has(string state) => _states.Contains(state);

    /// <summary>Name an already baked exact-target take for a creature state; no retarget or bone edits.</summary>
    private void Alias(AnimationLibrary library, string state, string nativeState)
    {
        if (!Has(nativeState)) throw new InvalidDataException($"{Name}: native motion state missing: {nativeState}");
        if (state == nativeState) return;
        var take = (Animation)library.GetAnimation(nativeState).Duplicate(true);
        // Explicit creature bindings may select the native undead idle/walk over the generic humanoid state.
        if (Has(state)) library.RemoveAnimation(state);
        library.AddAnimation(state, take);
        if (MotionPaces is { } paces && paces.TryGetValue(nativeState, out float pace))
            MotionPaces = new Dictionary<string, float>(paces) { [state] = pace };
        if (_contacts.TryGetValue(nativeState, out var phase))
        {
            _contacts = _contacts.Duplicate();
            _contacts[state] = phase;
        }
        var plants = library.GetMeta("foot_plants", new Godot.Collections.Dictionary()).AsGodotDictionary();
        if (plants.TryGetValue(nativeState, out var events))
        {
            plants = plants.Duplicate();
            plants[state] = events;
            library.SetMeta("foot_plants", plants);
        }
        _states.Add(state);
    }

    /// <summary>Show a state, blending from the last over <paramref name="blend"/> seconds; the same state keeps playing unless restarted.</summary>
    public void Play(string state, float blend = 0.2f, float speed = 1f, bool restart = false)
    {
        if (!_states.Contains(state))
            return;
        if (_locomotion?.Call("stop").AsBool() == true) Current = null;
        _player.SpeedScale = speed;
        if (Current == state && !restart)
            return;
        Current = state;
        _player.Play(state, blend);
        if (!_idlePhaseStarted && state == "idle")
        {
            _idlePhaseStarted = true;
            _player.Seek(IdleStartPhase * Length("idle"), true);
        }
        if (restart)
            _player.Seek(0, true);
    }

    /// <summary>A state's length in seconds (0 when it has no clip).</summary>
    public double Length(string state) => _states.Contains(state) ? _player.GetAnimation(state).Length : 0;

    /// <summary>Hold a state at a point in it (0-1): an attack's pose follows the simulation's phase, not the clip's clock.</summary>
    public void Hold(string state, float fraction, float blend = 0.1f)
    {
        if (!_states.Contains(state))
            return;
        if (_locomotion?.Call("stop").AsBool() == true) Current = null;
        if (Current != state)
        {
            Current = state;
            _player.Play(state, blend);
        }
        // Held by seeking every frame, the player's clock left running: a crossfade advances by the scaled clock, so at speed 0
        // the fade from the last state froze at its start and the last state went on showing (the held blow never appeared).
        _player.SpeedScale = 1;
        _player.Seek(Mathf.Clamp(fraction, 0, 1) * _player.GetAnimation(state).Length, true);
    }

    /// <summary>Something carried on a bone (a weapon in the hand), placed relative to it.</summary>
    public BoneAttachment3D? Attach(string bone)
    {
        bone = BoneNames.Resolve(Skeleton, bone) ?? bone;
        if (Skeleton.FindBone(bone) < 0)
            return null;
        var attachment = new BoneAttachment3D { BoneName = bone, Name = "On_" + bone };
        Skeleton.AddChild(attachment);
        return attachment;
    }

    /// <summary>Drawn, or only casting its shadow (the body in first person).</summary>
    public void SetShadowOnly(bool shadowOnly)
    {
        foreach (var mesh in FindChildren("*", nameof(MeshInstance3D), true, false).Cast<MeshInstance3D>())
        {
            // What rides on a bone (a held weapon: its model's meshes sit below the model's own root) stays drawn.
            if (OnBone(mesh))
                continue;
            // The merged low-detail body is a shadow caster, never a visible outfit.
            // Promoting it to On when leaving first person covers the real garments
            // once its distance band starts (the apparent short-sleeved default).
            if (mesh.Name.ToString().EndsWith("_shadow", StringComparison.Ordinal))
                continue;
            ShadowLods.SetMode(mesh, shadowOnly ? GeometryInstance3D.ShadowCastingSetting.ShadowsOnly : GeometryInstance3D.ShadowCastingSetting.On);
        }
    }

    private bool OnBone(Node node)
    {
        for (var at = node.GetParent(); at is not null && at != this; at = at.GetParent())
        {
            if (at is BoneAttachment3D)
                return true;
        }
        return false;
    }
}

