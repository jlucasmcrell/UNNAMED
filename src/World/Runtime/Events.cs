// UNNAMED World - facts the simulation publishes (ARCHITECTURE.md §4.3)
// No Godot references - pure C#

using UNNAMED.Domain;
using UNNAMED.Domain.Progression;
using UNNAMED.Domain.Spatial;

namespace UNNAMED.World.Runtime;

// Events are past tense, immutable, and never persisted. Views subscribe to them; nothing a view does with one
// reaches the simulation, which accepts only commands. Tick is the world tick the change belongs to.

public sealed record CommandRejected(GameCommand Command, string Reason, long Tick);

public sealed record BodyMoved(EntityId Actor, Body From, Body To, long Tick);

/// <summary>The body left the ground (the owner's M6 playtest).</summary>
public sealed record Jumped(EntityId Actor, long Tick);

/// <summary>The body crouched or stood up (the owner's M6 playtest).</summary>
public sealed record StanceChanged(EntityId Actor, Stance Stance, long Tick);

public sealed record WorldFlagChanged(string CellKey, string FlagId, long From, long To, long Tick);

public sealed record DoorToggled(EntityId Actor, string DoorKey, bool Open, long Tick);

/// <summary>A switch was worked (M6): its flag is set, for good.</summary>
public sealed record SwitchSet(EntityId Actor, string SwitchKey, long Tick);

public sealed record LocationDiscovered(string LocationId, DiscoveryMethod Method, long Tick);

public sealed record ExperienceGained(XpSource Source, long Awarded, long Repaid, int LevelsGained, int Level, long Tick);

public sealed record CellTierChanged(string CellKey, SimulationTier From, SimulationTier To, long Tick);

/// <summary>
/// World time passed at once while the character was away (WP11): the calendar's hook. <see cref="DaysCrossed"/> counts the midnights the
/// world clock passed; a system that keeps a calendar reads them here or from the ticks.
/// </summary>
public sealed record WorldTimeAdvanced(long FromTick, long ToTick, long DaysCrossed);
