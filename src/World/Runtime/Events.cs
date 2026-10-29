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

/// <summary>
/// A climb or a vault (BRIEF D6): the feet came up more than a step in one tick, and it cost this much stamina. The presentation plays the
/// climb pose on it and the HUD can show the cost.
/// </summary>
public sealed record Climbed(EntityId Actor, Body From, Body To, int StaminaSpent, long Tick);

/// <summary>A winged body's wings changed (Kal): opened for a leap, spread to glide, flared to brake, folded to drop, folded on landing.</summary>
public sealed record WingsChanged(EntityId Actor, UNNAMED.Domain.Spatial.WingState Wing, long Tick);

/// <summary>The body crouched or stood up (the owner's M6 playtest).</summary>
public sealed record StanceChanged(EntityId Actor, Stance Stance, long Tick);

/// <summary>
/// A swimmer's wind ran out and the water took it: the body is ashore at this water's nearest bank, and a little of its health is gone
/// with the going under (D20).
/// </summary>
public sealed record WentUnder(string WaterKey, long Tick);

public sealed record WorldFlagChanged(string CellKey, string FlagId, long From, long To, long Tick);

public sealed record DoorToggled(EntityId Actor, string DoorKey, bool Open, long Tick);

/// <summary>A switch was worked (M6): its flag is set, for good.</summary>
public sealed record SwitchSet(EntityId Actor, string SwitchKey, long Tick);

/// <summary>A way gave under the body crossing it (a rotten rope): it is gone, and the body fell the last <paramref name="DropMm"/>.</summary>
public sealed record PortalGaveWay(string PortalKey, long DropMm, long Tick);

public sealed record LocationDiscovered(string LocationId, DiscoveryMethod Method, long Tick);

/// <summary>A page of lore was filed (spec 41): which entry and page, and the discovery that opened it.</summary>
public sealed record LorePageUnlocked(string EntryId, int Page, UNNAMED.Domain.Knowledge.LoreSourceKind Source, string SourceRef, long Tick);

/// <summary>Something in the world was read: the readable's key, and the entry its content names.</summary>
public sealed record LoreReadableRead(string Key, string EntryId, long Tick);

public sealed record ExperienceGained(XpSource Source, long Awarded, long Repaid, int LevelsGained, int Level, long Tick);

public sealed record CellTierChanged(string CellKey, SimulationTier From, SimulationTier To, long Tick);

/// <summary>
/// The bus the simulation's systems publish to: what a system listens for runs first, in the order it was registered, then the event goes
/// on to the views. A listener lives and dies with its simulation - a session's bus outlives every world it runs - and responds only by
/// dispatching commands (ARCHITECTURE.md §4.3).
/// </summary>
internal sealed class SimulationEvents : IEventBus
{
    private readonly IEventBus _views;
    private readonly Dictionary<Type, List<Delegate>> _listeners = new();

    public SimulationEvents(IEventBus views) => _views = views;

    public void Listen<T>(Action<T> listener)
    {
        if (!_listeners.TryGetValue(typeof(T), out var list))
            _listeners[typeof(T)] = list = new List<Delegate>();
        list.Add(listener);
    }

    public void Subscribe<T>(Action<T> handler) => _views.Subscribe(handler);

    public void Unsubscribe<T>(Action<T> handler) => _views.Unsubscribe(handler);

    public void Publish<T>(T @event)
    {
        if (_listeners.TryGetValue(typeof(T), out var list))
            foreach (var listener in list.ToArray())
                ((Action<T>)listener)(@event);
        _views.Publish(@event);
    }
}
/// World time passed at once while the character was away (WP11): the calendar's hook. <see cref="DaysCrossed"/> counts the midnights the
/// world clock passed; a system that keeps a calendar reads them here or from the ticks.
/// </summary>
public sealed record WorldTimeAdvanced(long FromTick, long ToTick, long DaysCrossed);
