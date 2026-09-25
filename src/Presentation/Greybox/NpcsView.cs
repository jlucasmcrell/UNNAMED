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
    private readonly Dictionary<string, Vector3> _last = new(StringComparer.Ordinal);
    private Art.ArtLibrary _art = Art.ArtLibrary.Empty;
    private Art.ArtBindings _bindings = Art.ArtBindings.Empty;

    /// <summary>The asset library and its bindings; without them, greybox.</summary>
    public void Bind(Art.ArtLibrary art, Art.ArtBindings bindings)
    {
        _art = art;
        _bindings = bindings;
    }

    public void Draw(Simulation simulation, double delta)
    {
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
            }
            // A companion carries their own weapon (M6: the March Spear, from their NPC definition's companion block).
            if (figure is Art.SkinnedFigure skinned)
                skinned.Equip(simulation.Companions.Any(c => c.NpcId == npc.Id) ? _bindings.CompanionWeapon : null);
            var feet = HollowView.ToGodot(npc.Body.XMm, npc.Body.YMm, npc.Body.ZMm);
            // How fast the body is going, for the stride: its drawn position between ticks is not predicted, only followed.
            float speed = delta > 0 && _last.TryGetValue(npc.Id, out var was) ? new Vector2(feet.X - was.X, feet.Z - was.Z).Length() / (float)delta : 0;
            _last[npc.Id] = feet;
            figure.SetStance(CombatStance.AtRest);
            figure.SetTalking(npc.Talking);
            figure.Pose(feet, PlayerController.FacingRadians(npc.Body.FacingMdeg), Math.Min(speed, 8f), delta);
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
