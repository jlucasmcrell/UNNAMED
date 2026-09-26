// UNNAMED Presentation - the waystation's people (M4), drawn with the asset library's bodies where it has them
// Godot presentation only: no gameplay state lives here (D-11)

using Godot;
using UNNAMED.Presentation.Player;
using UNNAMED.World.Runtime;

namespace UNNAMED.Presentation.Greybox;

/// <summary>
/// Draws each named NPC standing where the simulation has them and turned the way it says - talking while in a conversation, a
/// companion (M6) walking as they go and lying down while downed. The asset library's skinned body and clips where the bindings name
/// one (the Phase-1 asset integration); otherwise the same full-body mannequin the character wears, in clothes of their own.
/// </summary>
public partial class NpcsView : Node3D
{
    private readonly Dictionary<string, Figure> _figures = new(StringComparer.Ordinal);
    private readonly Dictionary<string, GaitSpeed> _gaits = new(StringComparer.Ordinal);
    private FoldEcho? _echo;
    private bool _echoTried;
    private Art.ArtLibrary _art = Art.ArtLibrary.Empty;
    private Art.ArtBindings _bindings = Art.ArtBindings.Empty;

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

    public void Draw(Simulation simulation, double delta, double tickSeconds)
    {
        if (PlayerHead.GetParent() is null)
            AddChild(PlayerHead);
        PlayerHead.GlobalPosition = HollowView.ToGodot(simulation.Player.Body.XMm, simulation.Player.Body.YMm, simulation.Player.Body.ZMm) + new Vector3(0, 1.6f, 0);
        foreach (var npc in simulation.Npcs)
        {
            if (!_figures.TryGetValue(npc.Id, out var figure))
            {
                var body = Art.SkinnedFigure.Create(_art, _bindings, npc.Id);
                string? model = _bindings.People.GetValueOrDefault(npc.Id)?.Model;
                if (body is not null)
                    _art.Coverage.Resolved("person", npc.Id, model!);
                else
                    _art.Coverage.Fallback("person", npc.Id, model is null ? "no binding: the greybox mannequin" : $"{_art.Why(model) ?? "not drawn"}: the greybox mannequin", model);
                figure = (Figure?)body ?? new Avatar { Clothing = Clothes(npc.Id) };
                figure.Name = npc.Id;
                AddChild(figure);
                _figures[npc.Id] = figure;
                // Phase B: feet planted on the ground, and the head turning to the player when near (its influence set each frame).
                if (VisualOptions.BodyModifiers && body is not null && Ground is { } ground)
                {
                    Art.BodyModifiers.PlantFeet(body.Skeleton, body, ground);
                    if (Art.BodyModifiers.LookAt(body.Skeleton, body, PlayerHead) is { } look)
                        _looks[npc.Id] = look;
                    // A production rig's eyes follow the player too, with the head (character fidelity).
                    if (Art.BodyModifiers.EyesLook(body.Skeleton, body, PlayerHead) is { Count: > 0 } eyes)
                        _eyes[npc.Id] = eyes;
                }
            }
            // A companion carries their own weapon (M6: the March Spear, from their NPC definition's companion block).
            if (figure is Art.SkinnedFigure skinned)
                skinned.Equip(simulation.Companions.Any(c => c.NpcId == npc.Id) ? _bindings.CompanionWeapon : null);
            // How fast the body is going, for the stride, and where to draw it between the simulation's steps (GaitSpeed, H04).
            if (!_gaits.TryGetValue(npc.Id, out var gait))
                _gaits[npc.Id] = gait = new GaitSpeed();
            var feet = gait.Sample(HollowView.ToGodot(npc.Body.XMm, npc.Body.YMm, npc.Body.ZMm), delta, tickSeconds);
            float speed = gait.Speed;
            figure.SetStance(CombatStance.AtRest);
            figure.SetTalking(npc.Talking);
            figure.Pose(feet, PlayerController.FacingRadians(npc.Body.FacingMdeg), speed, delta);
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

    /// <summary>A companion's blow landed (the simulation reports it as it lands): their figure strikes.</summary>
    public void Strike(string npcId)
    {
        if (_figures.TryGetValue(npcId, out var figure))
            figure.Strike();
    }

    /// <summary>A colour per person, from their ID, so each reads apart at a glance.</summary>
    private static StandardMaterial3D Clothes(string npcId)
    {
        uint hash = 2166136261;
        foreach (char c in npcId)
            hash = (hash ^ c) * 16777619;
        var colour = Color.FromHsv(hash % 360 / 360f, 0.45f, 0.55f);
        return new StandardMaterial3D { AlbedoColor = colour, Roughness = 0.9f };
    }
}
