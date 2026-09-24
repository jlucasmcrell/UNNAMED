// UNNAMED Presentation - the waystation's people in the greybox (M4)
// Godot presentation only: no gameplay state lives here (D-11)

using Godot;
using UNNAMED.Presentation.Player;
using UNNAMED.World.Runtime;

namespace UNNAMED.Presentation.Greybox;

/// <summary>
/// Draws each named NPC as the same full-body mannequin the character wears, in clothes of their own, standing where the
/// simulation has them and turned the way it says - a companion (M6) walking as they go, and lying down while downed. A
/// placeholder until the asset pipeline delivers people.
/// </summary>
public partial class NpcsView : Node3D
{
    private readonly Dictionary<string, Avatar> _figures = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Vector3> _last = new(StringComparer.Ordinal);

    public void Draw(Simulation simulation, double delta)
    {
        foreach (var npc in simulation.Npcs)
        {
            if (!_figures.TryGetValue(npc.Id, out var figure))
            {
                figure = new Avatar { Name = npc.Id, Clothing = Clothes(npc.Id) };
                AddChild(figure);
                _figures[npc.Id] = figure;
            }
            var feet = HollowView.ToGodot(npc.Body.XMm, npc.Body.YMm, npc.Body.ZMm);
            // How fast the body is going, for the stride: its drawn position between ticks is not predicted, only followed.
            float speed = delta > 0 && _last.TryGetValue(npc.Id, out var was) ? new Vector2(feet.X - was.X, feet.Z - was.Z).Length() / (float)delta : 0;
            _last[npc.Id] = feet;
            figure.SetStance(CombatStance.AtRest);
            figure.Pose(feet, PlayerController.FacingRadians(npc.Body.FacingMdeg), Math.Min(speed, 8f), delta);
            if (npc.Downed)
                figure.Rotation = new Vector3(-Mathf.Pi / 2, figure.Rotation.Y, 0);
        }
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
