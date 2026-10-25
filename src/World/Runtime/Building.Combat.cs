// UNNAMED World - a swing on a placed piece (M7 design §4.12; re-ported onto humgen-main by WP-1G)
// No Godot references - pure C#

using System.Collections.Immutable;
using UNNAMED.Domain;
using UNNAMED.Domain.Spatial;

namespace UNNAMED.World.Runtime;

internal sealed partial class CombatSystem
{
    /// <summary>The one damage source of building (<c>config.building</c>'s <c>damage</c> key).</summary>
    private const string MeleeDamage = "melee";

    /// <summary>A swing that struck no creature lands on the placed piece it meets, if any (the one damage rule); true when it did.</summary>
    private bool StrikePiece(Body body, long reachMm)
    {
        if (!State.Space.IsExterior || State.World.Pieces.Count == 0 || StruckPiece(body, reachMm) is not { } piece)
            return false;
        _context.Dispatch(new DamagePiece(piece, _context.Setup.Building.Constants.Damage.GetValueOrDefault(MeleeDamage), MeleeDamage));
        return true;
    }

    /// <summary>
    /// The piece a swing lands on: the first blocker along the facing within reach, when every blocker the line meets there is part of a
    /// placed piece - an authored wall, door or barrier among them shields it.
    /// </summary>
    private EntityId? StruckPiece(Body from, long reachMm)
    {
        EntityId? first = null;
        foreach (var blocker in FirstStop(from, reachMm))
        {
            int hash = blocker.Id.IndexOf('#');
            if (hash <= 0 || !blocker.Id.StartsWith("pce_", StringComparison.Ordinal) || !EntityId.TryParse(blocker.Id[..hash], out var id))
                return null;
            first ??= id;
        }
        return first;
    }

    /// <summary>
    /// What stops the line along the body's facing within a length: nothing when it runs clear; else the gap halved down to a centimetre,
    /// and every wall, closed door and barrier the line to the far side of that centimetre crosses.
    /// </summary>
    private ImmutableArray<Blocker> FirstStop(Body from, long lengthMm)
    {
        double facing = from.FacingMdeg / 1000.0 * Math.PI / 180;
        double dx = Math.Sin(facing), dz = Math.Cos(facing);
        double reach = lengthMm;
        if (!Walled(from.XMm, from.ZMm, from.XMm + dx * reach, from.ZMm + dz * reach))
            return ImmutableArray<Blocker>.Empty;
        double clear = 0;
        while (reach - clear > 10)
        {
            double mid = (clear + reach) / 2;
            if (Walled(from.XMm, from.ZMm, from.XMm + dx * mid, from.ZMm + dz * mid))
                reach = mid;
            else
                clear = mid;
        }
        double x1 = from.XMm + dx * reach, z1 = from.ZMm + dz * reach;
        return _context.SpaceFor(State.Space).Blockers.Concat(_context.ClosedDoors())
            .Where(b => b.Crosses(from.XMm, from.ZMm, x1, z1)).ToImmutableArray();
    }
}
