// UNNAMED Presentation - the waystation's people (M4), drawn with the asset library's bodies where it has them
// Godot presentation only: no gameplay state lives here (D-11)

using Godot;
using UNNAMED.Presentation.Player;
using UNNAMED.World.Runtime;
using UNNAMED.Domain.Character;
using UNNAMED.Domain.Social;
using UNNAMED.Domain.Spatial;

namespace UNNAMED.Presentation.Greybox;

/// <summary>
/// Draws each named NPC standing where the simulation has them and turned the way it says - talking while in a conversation, a
/// companion (M6) walking as they go and lying down while downed. The asset library's skinned body and clips where the bindings name
/// one (the Phase-1 asset integration); otherwise the same full-body mannequin the character wears, in clothes of their own.
/// </summary>
public partial class NpcsView : Node3D
{
    public bool SuppressPresentationForBenchmark { get; set; }
    private int _performanceStage = System.Environment.GetEnvironmentVariable("OTHERREACH_MH_PERF_SWEEP") == "1" ? 0 : 4;
    public int PerformanceStage => _performanceStage;
    public void SetPerformanceStage(int stage)
    {
        _performanceStage = stage;
        foreach (var native in _figures.Values.OfType<MHCharacters.MetaHumanFigure>()) native.ConfigureNpcPerformance(stage);
    }
    private readonly Dictionary<string, Figure> _figures = new(StringComparer.Ordinal);
    private readonly Dictionary<string, GaitSpeed> _gaits = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _instances = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Vector3> _restOffsets = new(StringComparer.Ordinal);
    private RegionLayout? _furnitureLayout;
    private BoxBlocker[] _beds = [], _stools = [];
    private FoldEcho? _echo;
    private bool _echoTried;
    private Art.ArtLibrary _art = Art.ArtLibrary.Empty;
    private Art.ArtBindings _bindings = Art.ArtBindings.Empty;
    private Simulation? _activeSimulation;

    /// <summary>The asset library and its bindings; without them, greybox.</summary>
    public void Bind(Art.ArtLibrary art, Art.ArtBindings bindings)
    {
        _art = art;
        _bindings = bindings;
    }

    /// <summary>The ground's height at (x, z), for planting the figures' feet (Phase B); null draws them as the clips have them.</summary>
    public Func<float, float, float>? Ground { get; set; }

    /// <summary>Where the player's head is (moved each frame), for the NPCs' heads to follow.</summary>
    public Node3D PlayerHead { get; } = new() { Name = "PlayerHead" };

    private readonly Dictionary<string, LookAtModifier3D> _looks = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IReadOnlyList<LookAtModifier3D>> _eyes = new(StringComparer.Ordinal);

    /// <summary>Each drawn NPC's stride speed and clip state this frame (the state log, --state-log).</summary>
    public IEnumerable<(string Id, float Speed, string? State)> States() =>
        _figures.Select(f => (f.Key, _gaits.TryGetValue(f.Key, out var g) ? g.Speed : 0f, f.Value.ClipState));

    /// <summary>The figure drawn for a person, by their ID; null when none is (for the outline).</summary>
    public Node3D? FigureOf(string id) => _figures.GetValueOrDefault(id);

    /// <summary>The IDs of every NPC drawn this frame: what a review framed, against what the world draws (F1 Gate C's face sheets).</summary>
    public IReadOnlyList<string> DrawnIds => _figures.Keys.ToArray();

    public void Draw(Simulation simulation, double delta, double tickSeconds)
    {
        _activeSimulation = simulation;
        if (SuppressPresentationForBenchmark) return;
        if (PlayerHead.GetParent() is null)
            AddChild(PlayerHead);
        PlayerHead.GlobalPosition = HollowView.ToGodot(simulation.Player.Body.XMm, simulation.Player.Body.YMm, simulation.Player.Body.ZMm) + new Vector3(0, 1.6f, 0);
        var people = simulation.Npcs;
        var present = people.Select(n => n.Id).ToHashSet(StringComparer.Ordinal);
        foreach (string id in _figures.Keys.Where(id => !present.Contains(id)).ToArray())
            Forget(id);
        if(!ReferenceEquals(_furnitureLayout,simulation.ActiveLayout))
        {
            _furnitureLayout=simulation.ActiveLayout;
            var furniture=_furnitureLayout.Space.Blockers.OfType<BoxBlocker>().ToArray();
            _beds=furniture.Where(b=>b.Id.Contains("_bed",StringComparison.Ordinal)).ToArray();
            _stools=furniture.Where(b=>b.Id.Contains("_stool",StringComparison.Ordinal)).ToArray();
            _restOffsets.Clear();
        }
        var occupiedFurniture = new HashSet<string>(StringComparer.Ordinal);
        foreach (var npc in people)
        {
            string instance = npc.InstanceId.ToString();
            if (_instances.TryGetValue(npc.Id, out var previous) && previous != instance)
                Forget(npc.Id);
            if (npc.Space != simulation.ActiveLayout.SpaceId && !_figures.ContainsKey(npc.Id))
                continue;
            if (!_figures.TryGetValue(npc.Id, out var figure))
            {
                var definition = simulation.Setup.Social.Npcs.GetValueOrDefault(npc.Id);
                string? sex = definition?.Sex ?? simulation.Setup.NpcLife.Routines.GetValueOrDefault(npc.Id)?.Sex;
                var appearance = _bindings.Person(npc.Id, npc.PeopleId, instance, sex);
                string? peopleId = definition?.PeopleId ?? npc.PeopleId
                    ?? simulation.Setup.NpcLife.Routines.GetValueOrDefault(npc.Id)?.PeopleId ?? appearance?.PeopleId;
                var worn = LookOf(simulation, npc, definition, peopleId, sex);
                // F1 Gate C: a person with a look is drawn on their people's creator body - the one base mesh a sex has, shaped by the
                // record's blend shapes, face textures, groom, garment and stature - even where the MetaHuman bundle is installed, where
                // MetaHumanFigure.Available would otherwise win and leave every NPC the same native. The player keeps the native body, and
                // so does anyone without a look to draw. The frontier garments are baked for MetaHuman bodies, so the creator's own outfit
                // part dresses this one instead (the same reason the bindings' cast entries now wear none).
                var body = worn is null || appearance is null ? null
                    : Art.SkinnedFigure.Create(_art, _bindings, npc.Id, body: CreatorBody(sex), people: peopleId,
                        appearance: appearance with { Outfit = Array.Empty<string>(), OutfitBody = null });
                var native = body is null ? MHCharacters.MetaHumanFigure.Create(_art, _bindings, npc.Id, sex: sex) : null;
                native?.ConfigureNpcPerformance(_performanceStage);
                if (body is null && native is null)
                    body = Art.SkinnedFigure.Create(_art, _bindings, npc.Id, appearance: appearance);
                if (body is not null) Art.PeopleAppearance.EnsureSex(body, sex);
                string? model = native is not null ? "mh:" + native.CharacterId : body?.ModelId ?? appearance?.Model;
                if (native is not null || body is not null)
                    _art.Coverage.Resolved("person", npc.Id, model!);
                else
                    _art.Coverage.Fallback("person", npc.Id, model is null ? "no binding: the greybox mannequin" : $"{_art.Why(model) ?? "not drawn"}: the greybox mannequin", model);
                figure = (Figure?)native ?? body ?? (Figure)new Avatar { Clothing = Clothes(npc.Id) };
                figure.Name = npc.Id;
                AddChild(figure);
                if (body is not null && worn is not null && body.ModelId.StartsWith("humgen_creator_", StringComparison.Ordinal))
                {
                    Art.PeopleAppearance.ApplyLook(body, peopleId, worn, _art, body.ModelId);
                    // A face sheet is about the face, and the outfit's own headwear covers the crown: the creator hides it for the steps
                    // that are about the head too (the owner's fix, 2026-10-28).
                    Art.HumGenCreatorAppearance.WearHeadwear(body, false);
                }
                else if (body is not null && body.ModelId.StartsWith("humgen_", StringComparison.Ordinal) && !_bindings.NamedCharacters.ContainsKey(npc.Id))
                    Art.PeopleAppearance.ApplyResidentLook(body, npc.PeopleId, instance);
                _figures[npc.Id] = figure;
                string speakerId = npc.Id;
                foreach (var face in figure.FindChildren("*", "", true, false).OfType<Art.FaceDriver>())
                    face.RecordedSpeech = () => _activeSimulation?.Conversation is { } active && active.NpcId == speakerId
                        ? Voice?.Speech(active.LineId) : _barks.TryGetValue(speakerId, out var speaking)
                            ? Voice?.Speech(speaking.Voice, speaking.Line) : null;
                _instances[npc.Id] = instance;
                // WHERE THE BODY ACTUALLY IS. Coverage.Resolved above fires when the FIGURE IS CREATED, not when it
                // is on screen, so "person 5 requested 5 resolved" was never evidence that anybody was visible - and
                // two village frames with all four NPCs in front of the camera showed none of them. If their Y is
                // zero they are standing at sea level under a village whose ground is at six metres, which looks
                // exactly like not being drawn at all. One line, once per NPC, and the question stops being a guess.
                GD.Print($"UNNAMED npc: {npc.Id} drawn from ({npc.Body.XMm / 1000f:0.0}, {npc.Body.YMm / 1000f:0.00}, " +
                         $"{npc.Body.ZMm / 1000f:0.0}) with model {model ?? "(none: greybox)"}");
                // Phase B: feet planted on the ground, and the head turning to the player when near (its influence set each frame).
                if (VisualOptions.BodyModifiers && body is not null && Ground is { } ground)
                {
                    Art.BodyModifiers.PlantFeet(body.Skeleton, body, ground);
                    if (Art.BodyModifiers.LookAt(body.Skeleton, body, PlayerHead) is { } look)
                        _looks[npc.Id] = look;
                    // A production rig's eyes follow the player too, with the head (character fidelity).
                    if (VisualOptions.ComparisonCast != "humgen" && Art.BodyModifiers.EyesLook(body.Skeleton, body, PlayerHead) is { Count: > 0 } eyes)
                        _eyes[npc.Id] = eyes;
                }
            }
            figure.Visible = npc.Space == simulation.ActiveLayout.SpaceId;
            if (!figure.Visible)
                continue;
            // A companion carries their own weapon (M6: the March Spear, from their NPC definition's companion block).
            if (figure is Art.SkinnedFigure skinned)
                skinned.Equip(simulation.Companions.Any(c => c.NpcId == npc.Id) ? _bindings.CompanionWeapon : null);
            else if (figure is MHCharacters.MetaHumanFigure nativeFigure)
                nativeFigure.Equip(simulation.Companions.Any(c => c.NpcId == npc.Id) ? _bindings.CompanionWeapon : null);
            // How fast the body is going, for the stride, and where to draw it between the simulation's steps (GaitSpeed, H04).
            if (!_gaits.TryGetValue(npc.Id, out var gait))
                _gaits[npc.Id] = gait = new GaitSpeed();
            var feet = gait.Sample(HollowView.ToGodot(npc.Body.XMm, npc.Body.YMm, npc.Body.ZMm), delta, tickSeconds);
            float speed = gait.Speed;
            figure.SetStance(CombatStance.AtRest);
            figure.SetIdleRole(npc.Role);
            figure.SetActivity(npc.Activity, npc.Role);
            bool held = npc.Id == FoldEcho.Held && simulation.Barriers.Any(b => b.Site.Key == FoldEcho.Barrier && b.Standing);
            figure.SetPosture(held, false);
            figure.SetTalking(npc.Talking && !held);
            string? spoken = simulation.Conversation is { } talk && talk.NpcId == npc.Id ? talk.Text : Bark(simulation, npc, figure, feet, delta);
            figure.Say(spoken);
            figure.SetTalking(!held && (npc.Talking || spoken is not null));
            figure.SetExpression(Ui.Models.DialoguePerformance.Mood(spoken,
                simulation.Conversation is { } performance && performance.NpcId == npc.Id ? performance.Mood : null));
            var furniture = FurnitureAt(simulation,npc,feet,speed,occupiedFurniture);
            bool resting = figure.SetRestPose(held && VisualOptions.ComparisonCast == "humgen" ? "captive_kneel" : furniture?.Clip);
            figure.Pose(feet, PlayerController.FacingRadians(npc.Body.FacingMdeg), speed, delta);
            var wantedOffset = resting && furniture is { } support && figure.RestContact is { } contact ? support.Top-contact : Vector3.Zero;
            var offset = _restOffsets.GetValueOrDefault(npc.Id).Lerp(wantedOffset,1-Mathf.Exp(-10*(float)delta));
            _restOffsets[npc.Id]=offset;
            figure.Position+=offset;
            figure.SetGaze(furniture?.Clip!="lie_to_idle" && feet.DistanceSquaredTo(PlayerHead.GlobalPosition) < 64 ? PlayerHead.GlobalPosition : null);
            // Hide an intervening companion as a whole (including their outline/weapon). Keep
            // posing while hidden so they resume at the current gait when the camera moves away.
            if (simulation.Companions.Any(c => c.NpcId == npc.Id) && GetViewport().GetCamera3D() is { } camera)
            {
                var chest = feet + Vector3.Up * 1.2f;
                // Include the figure's upper-body reach: a centre-only 1.2 m
                // cutoff still leaves the head/outline filling the foreground.
                figure.Visible = camera.GlobalPosition.DistanceTo(chest) >= 2.5f;
            }
            if (npc.Id == FoldEcho.Held && figure is Art.SkinnedFigure && VisualOptions.BodyModifiers)
            {
                // Held by the fold: the restrained doubling and delayed shadow while its barrier stands (M03).
                if (!_echoTried)
                {
                    _echoTried = true;
                    var echo = new FoldEcho { Name = "FoldEcho" };
                    if (echo.Build(_art, _bindings))
                    {
                        AddChild(echo);
                        _echo = echo;
                    }
                    else
                    {
                        echo.QueueFree();
                    }
                }
                bool standing = simulation.Barriers.Any(b => b.Site.Key == FoldEcho.Barrier && b.Standing);
                _echo?.Draw(standing, figure, feet, PlayerController.FacingRadians(npc.Body.FacingMdeg), speed, delta);
            }
            if (_looks.TryGetValue(npc.Id, out var head))
            {
                // Within 6 m (and not downed) the head follows the player; further off it eases back to the clip's.
                float near = feet.DistanceTo(PlayerHead.GlobalPosition) < 6f && !npc.Downed ? 1f : 0f;
                head.Influence = Mathf.MoveToward(head.Influence, near, (float)delta * 2.5f);
                if (_eyes.TryGetValue(npc.Id, out var eyes))
                {
                    foreach (var eye in eyes)
                        eye.Influence = head.Influence;
                }
            }
            if (!figure.SetDowned(npc.Downed) && npc.Downed)
                figure.Rotation = new Vector3(-Mathf.Pi / 2, figure.Rotation.Y, 0);
        }
    }

    /// <summary>
    /// F1 Gate C: the look a person is drawn with - the named cast's own authored record, or a role slot's generated one, each held to
    /// its people's ranges by <see cref="Art.NpcLooks"/>, which is Godot-free so the tests read the same generator the game draws with.
    /// Null for a person with no record and no people (or sex) to generate one from: the MetaHuman body keeps them.
    /// </summary>
    private CharacterAppearance? LookOf(Simulation simulation, NpcView npc, NpcDefinition? definition, string? people, string? sex)
    {
        if (people is null || sex is null)
            return null;
        var lives = simulation.Setup.NpcLife;
        var slot = lives.Routines.GetValueOrDefault(npc.Id);
        // A resident's face is assigned from their place in their town's roster, in ordinal order (NpcLooks.Residents).
        int position = slot is { IsSlot: true }
            ? lives.Routines.Values.Where(r => r.IsSlot && r.Settlement == slot.Settlement).OrderBy(r => r.Id, StringComparer.Ordinal)
                .ToList().FindIndex(r => r.Id == slot.Id)
            : 0;
        // A resident's arrival count (NpcLooks' generation) is the one fact presentation cannot recompute, and the world does not hand it
        // over yet: NpcView carries no Generation, so every resident is drawn as their slot's first holder - which is what a day-one
        // roster is, and what the face sheets are taken on. One argument changes here when NpcView.Generation exists.
        var (catalogue, data) = Creator();
        // A role resident has no content file of its own: their look is generated for the slot they hold.
        var look = definition is null
            ? slot is { IsSlot: true }
                ? Art.NpcLooks.Resident(slot.Id, slot.Settlement, 0, people, sex, catalogue, data, Math.Max(0, position))
                : null
            : Art.NpcLooks.Of(definition, slot?.Settlement ?? string.Empty, 0, people, sex, catalogue, data, Math.Max(0, position));
        if (OS.GetEnvironment("OTHERREACH_NPC_LOOKS_PLAN") is { Length: > 0 })
            GD.Print($"NPC_LOOK {npc.Id}: {(look is null ? "no look" : $"face {look.Face} hair {look.Hair} outfit {look.Outfit}")}");
        return look;
    }

    /// <summary>The one body a sex is drawn on (the creator's two bases): every NPC is that mesh, shaped by their own record.</summary>
    private static string CreatorBody(string? sex) => sex == "f" ? "humgen_creator_female" : "humgen_creator_male";

    private (Ui.Models.CreatorCatalogue Catalogue, Ui.Models.HumGenCreatorData Data)? _creator;

    /// <summary>The creator's own catalogue and slider data: what a record may name, and which people may name what (read once).</summary>
    private (Ui.Models.CreatorCatalogue Catalogue, Ui.Models.HumGenCreatorData Data) Creator()
    {
        if (_creator is { } read)
            return read;
        string json = Godot.FileAccess.GetFileAsString("res://Art/humgen_creator.json");
        return (_creator = (Ui.Models.CreatorCatalogue.Read(json), new Ui.Models.HumGenCreatorData(json))).Value;
    }

    private void Forget(string id)
    {
        if (_figures.Remove(id, out var departed))
        {
            RemoveChild(departed);
            departed.QueueFree();
        }
        _instances.Remove(id);
        _gaits.Remove(id);
        _restOffsets.Remove(id);
        _looks.Remove(id);
        _eyes.Remove(id);
        if (_barks.Remove(id, out var bark))
        {
            bark.Label.QueueFree();
            if (bark.Voice is { Playing: true } voice && voice.Stream == bark.Line)
                voice.Stop();
        }
    }

    private sealed class Barking
    {
        public required Label3D Label { get; init; }
        public double Left;
        public double Shown;
        public double Quiet;
        public string? Last;
        public bool Voiced;
        public AudioStreamPlayer3D? Voice;
        public AudioStream? Line;
        // Owner finding 52: when each line was last said (seconds of play), so none comes round again within RepeatSeconds.
        public readonly Dictionary<string, double> Said = new(StringComparer.Ordinal);
        public double LastHint = double.NegativeInfinity;
    }

    private const double RepeatSeconds = 600;
    private double _clock;

    private const float BarkReach = Audio.VoiceRules.BarkFullM;
    private const float BarkPixelSize = 0.0035f, BarkSizeFullM = 4f, BarkNearestM = 1.2f;

    /// <summary>Who says barks aloud when their recording exists (WP5); none in a run without sound.</summary>
    public Audio.VoicePlayer? Voice { get; set; }

    /// <summary>(NPC ID, the line, seconds it is said for): a bark began; the subtitles caption it with the speaker.</summary>
    public Action<string, string, double>? Said { get; set; }

    private readonly Dictionary<string, Barking> _barks = new(StringComparer.Ordinal);
    private readonly RandomNumberGenerator _random = new();

    /// <summary>
    /// A line said in passing (the NPC's barks, from their conversation's content): when the character comes within reach of someone
    /// not talking to anyone, and that someone has been quiet long enough, they say one of the barks that hold now - over their head,
    /// with the face speaking it - then keep quiet for half a minute or so. Decoration: which line and when are presentation's. A bark
    /// with a recording is said aloud where they stand, for as long as it lasts, and its words show while subtitles are on; the words
    /// fade in, fade as the character walks off, and fade out at the end (E-5).
    /// </summary>
    private string? Bark(Simulation simulation, NpcView npc, Figure figure, Vector3 feet, double delta)
    {
        if (!_barks.TryGetValue(npc.Id, out var bark))
        {
            var label = new Label3D
            {
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, FontSize = 40, OutlineSize = 10, PixelSize = BarkPixelSize, Width = 520,
                AutowrapMode = TextServer.AutowrapMode.WordSmart, Visible = false,
            };
            AddChild(label);
            _barks[npc.Id] = bark = new Barking { Label = label, Quiet = _random.RandfRange(1f, 4f) };
        }
        bark.Left -= delta;
        bark.Shown += delta;
        bark.Quiet -= delta;
        _clock += delta / Math.Max(1, _barks.Count);
        float distance = feet.DistanceTo(PlayerHead.GlobalPosition);
        var head = figure.Head ?? feet + Vector3.Up * 1.6f;
        bool near = distance < BarkReach && simulation.Conversation is null && !npc.Downed;
        var lines = near && bark.Quiet <= 0 ? simulation.Barks(npc.Id) : default;
        if (!lines.IsDefault && lines.IsEmpty)
            bark.Quiet = 5;
        else if (!lines.IsDefault)
        {
            var fresh = lines.Where(l => l.Text != bark.Last && (!bark.Said.TryGetValue(l.Text, out double at) || _clock - at >= RepeatSeconds)).ToArray();
            if (fresh.Length == 0)
            {
                // Everything they have to say was said lately: they keep quiet rather than say it again.
                bark.Quiet = 60;
                return bark.Left > 0 ? bark.Label.Text : null;
            }
            var line = fresh[_random.RandiRange(0, fresh.Length - 1)];
            bark.Last = line.Text;
            bark.Said[line.Text] = _clock;
            bark.Voice = Voice?.Bark(line.LineId, head);
            bark.Line = bark.Voice?.Stream;
            bark.Voiced = bark.Line is not null;
            bark.Left = bark.Line is { } voiced ? voiced.GetLength() + 0.3 : 1.5 + line.Text.Length / 15.0;
            bark.Shown = 0;
            bark.Quiet = bark.Left + _random.RandfRange(25f, 45f);
            bark.Label.Text = line.Text;
            Said?.Invoke(npc.Id, line.Text, bark.Left);
        }
        if (bark.Left <= 0 || simulation.Conversation is not null)
        {
            bark.Left = 0;
            bark.Label.Visible = false;
            return null;
        }
        float alpha = Audio.VoiceRules.BarkAlpha(distance, bark.Shown, bark.Left);
        bark.Label.Visible = alpha > 0.01f && Audio.VoiceRules.ShowOverhead(bark.Voiced, Settings.SettingsStore.Current.Profile.Interface.Subtitles);
        bark.Label.Modulate = new Color(1, 1, 1, alpha);
        bark.Label.OutlineModulate = new Color(0, 0, 0, alpha);
        bark.Label.GlobalPosition = head + Vector3.Up * 0.45f;
        // A world-space line must never loom over the HUD: its apparent size stops growing at BarkSizeFullM, and it goes when the camera is on top of it.
        float seen = GetViewport().GetCamera3D() is { } eye ? eye.GlobalPosition.DistanceTo(bark.Label.GlobalPosition) : BarkSizeFullM;
        bark.Label.PixelSize = BarkPixelSize * Math.Min(1f, seen / BarkSizeFullM);
        // Nor does it show through a management screen: the sheet holds the picture.
        if (seen < BarkNearestM || GetTree().GetNodesInGroup(Ui.UiKit.SheetGroup).OfType<CanvasLayer>().Any(l => l.Visible))
            bark.Label.Visible = false;
        if (bark.Voice is { Playing: true } voice && voice.Stream == bark.Line)
            voice.GlobalPosition = head;   // a speaker who walks on - a companion following - takes their voice along
        return bark.Label.Text;
    }

    /// <summary>
    /// A line said now, over someone's head (a companion's hint): shown as a bark is, unless the same line was said within ten minutes or,
    /// with <paramref name="spacingSeconds"/>, any hint was said that recently.
    /// </summary>
    public void Say(string npcId, string lineId, string text, double spacingSeconds)
    {
        if (!_barks.TryGetValue(npcId, out var bark) || _clock - bark.LastHint < spacingSeconds
            || bark.Said.TryGetValue(text, out double at) && _clock - at < RepeatSeconds)
            return;
        bark.Said[text] = bark.LastHint = _clock;
        bark.Last = bark.Label.Text = text;
        bark.Voice = null;
        bark.Line = null;
        bark.Voiced = false;
        bark.Left = 1.5 + text.Length / 15.0;
        bark.Shown = 0;
        bark.Quiet = bark.Left + _random.RandfRange(25f, 45f);
        Said?.Invoke(npcId, text, bark.Left);
    }

    /// <summary>A companion's blow landed (the simulation reports it as it lands): their figure strikes.</summary>
    public void Strike(string npcId)
    {
        if (_figures.TryGetValue(npcId, out var figure))
            figure.Strike();
    }

    /// <summary>A colour per person, from their ID, so each reads apart at a glance.</summary>
    private (string Clip,Vector3 Top)? FurnitureAt(Simulation simulation,NpcView npc,Vector3 feet,float speed,HashSet<string> occupied)
    {
        if(speed>.2f || npc.Moving || npc.Downed || simulation.Companions.Any(c=>c.NpcId==npc.Id)) return null;
        bool sleeping=npc.Activity==NpcActivities.Sleep;
        if(npc.Role=="adventurer" && !npc.Talking && simulation.Setup.Adventurers is { } setup)
        {
            int hour=simulation.Setup.Clock.MinuteOfDay(simulation.WorldTick)/60;
            sleeping=setup.Settlements.Values.Any(t=>t.Inn is { } inn &&
                (inn.SleepFromHour>inn.BreakfastFromHour ? hour>=inn.SleepFromHour || hour<inn.BreakfastFromHour : hour>=inn.SleepFromHour && hour<inn.BreakfastFromHour) &&
                inn.Beds.Any(b=>new Vector2(feet.X,feet.Z).DistanceTo(new Vector2(b.XMm/1000f,b.ZMm/1000f))<1.6f));
        }
        if(sleeping && npc.Talking) return null;
        float reach=sleeping?1.65f:.85f;
        var body=(sleeping?_beds:_stools)
            .Where(b=>!occupied.Contains(b.Id))
            .Select(b=>(Body:b,At:new Vector3((b.MinXMm+b.MaxXMm)/2000f,0,(b.MinZMm+b.MaxZMm)/2000f)))
            .Where(b=>new Vector2(b.At.X-feet.X,b.At.Z-feet.Z).Length()<reach)
            .OrderBy(b=>new Vector2(b.At.X-feet.X,b.At.Z-feet.Z).LengthSquared()).FirstOrDefault();
        if(body.Body is null) return null;
        occupied.Add(body.Body.Id);
        float floor=simulation.ActiveLayout.Space.Terrain.HeightAtMm((long)(body.At.X*1000),(long)(body.At.Z*1000))/1000f;
        return (sleeping?"lie_to_idle":"sit",body.At+Vector3.Up*(floor+body.Body.HeightMm/1000f));
    }

    private static StandardMaterial3D Clothes(string npcId)
    {
        uint hash = 2166136261;
        foreach (char c in npcId)
            hash = (hash ^ c) * 16777619;
        var colour = Color.FromHsv(hash % 360 / 360f, 0.45f, 0.55f);
        return new StandardMaterial3D { AlbedoColor = colour, Roughness = 0.9f };
    }

    /// <summary>Where an NPC's head is drawn (for the conversation framing), or null when they are not drawn.</summary>
    public Vector3? HeadOf(string npcId) =>
        _figures.TryGetValue(npcId, out var figure) ? figure.Head ?? figure.GlobalPosition + Vector3.Up * 1.6f : null;
}
