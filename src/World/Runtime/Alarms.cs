// UNNAMED World - the alarm bells (STEALTH.md 8.2, 3.2): who rings one, and who comes
// No Godot references - pure C#

using System.Collections.Immutable;
using UNNAMED.Domain.Combat;
using UNNAMED.Domain.Creatures;
using UNNAMED.Domain.Spatial;

namespace UNNAMED.World.Runtime;

/// <summary>A bell has been rung, where it hangs and when: the noise the creatures hear and the fact the town remembers.</summary>
public sealed record AlarmRung(string BellKey, string Space, long XMm, long ZMm, long Tick);

/// <summary>
/// Owns: nothing - it is a reader. A bell is a switch with the Ring verb (STEALTH §8.2), so the world flag it sets is the alarm; this system
/// watches those flags and turns them into the three things an alarm is: the bell is heard (a noise at the post, which the creatures' own
/// perception turns to), the town is told (an <see cref="AlarmRung"/>), and the guards come (the NPCs within earshot are sent to the bell).
/// The bell is rung either by the character (the switch's own use) or by a guard who has seen what the character did: a creature that has engaged
/// the character beside a bellpost rings it rather than standing there.
/// </summary>
internal sealed class AlarmSystem
{
    /// <summary>How far a rung bell carries to the ears of the town: every NPC inside this walks to the post.</summary>
    public const long EarshotMm = 45_000;

    /// <summary>How close a creature must be to a bell, and engaged, before it rings it.</summary>
    public const long RingMm = 14_000;

    /// <summary>How long the guards keep heading for the bell after one rings.</summary>
    public const long RespondTicks = 600;

    private readonly SystemContext _context;
    private readonly NpcSystem _npcs;
    private readonly HashSet<string> _announced = new(StringComparer.Ordinal);

    public AlarmSystem(SystemContext context, NpcSystem npcs)
    {
        _context = context;
        _npcs = npcs;
    }

    /// <summary>The bells in the place being played, by key.</summary>
    public ImmutableArray<SwitchSite> Bells()
    {
        var layout = _context.Setup.Layout;
        return layout.Switches.IsDefaultOrEmpty
            ? ImmutableArray<SwitchSite>.Empty
            : layout.Switches.Where(s => s.Key.StartsWith("switch.bell.", StringComparison.Ordinal)).ToImmutableArray();
    }

    public void Tick(long tick)
    {
        var layout = _context.Setup.Layout;
        foreach (var bell in Bells())
        {
            var cell = Simulation.CellOf(layout.SpaceId, bell);
            bool set = _context.State.World.GetFlag(cell, bell.FlagId) != 0;
            if (!set && RingedByAGuard(layout, bell))
            {
                // The guard at the post saw the character and reached for the rope: the bell is a fact, not a one-off event.
                _context.Dispatch(new SetWorldFlag(cell, bell.FlagId, 1));
                set = true;
            }
            if (!set || !_announced.Add(bell.Key))
                continue;
            var (x, z) = Footprints.Center(bell.Body);
            _context.Dispatch(new RaiseNoise(new Noise(x, z, EarshotMm) { Source = SoundSource.Call }));
            _context.Events.Publish(new AlarmRung(bell.Key, layout.SpaceId.ToString(), x, z, tick));
            _npcs.RespondToAlarm(x, z, tick + RespondTicks);
        }
    }

    /// <summary>Whether a body that has engaged the character is standing close enough to the bell to ring it.</summary>
    private bool RingedByAGuard(RegionLayout layout, SwitchSite bell)
    {
        foreach (var creature in _context.State.Creatures.Values)
        {
            if (!creature.Alive || creature.Site.Space != layout.SpaceId || creature.Mind != CreatureMind.Engaged)
                continue;
            var (bx, bz) = Footprints.Center(bell.Body);
            long dx = creature.Body.XMm - bx, dz = creature.Body.ZMm - bz;
            if (dx * dx + dz * dz <= RingMm * RingMm)
                return true;
        }
        return false;
    }
}
