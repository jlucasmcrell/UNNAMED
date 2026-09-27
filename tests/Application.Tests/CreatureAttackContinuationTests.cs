using System.Text.Json;
using System.Text.Json.Nodes;
using MessagePack;
using UNNAMED.Domain;
using UNNAMED.Domain.Combat;
using UNNAMED.Persistence;
using UNNAMED.Persistence.Sections;
using UNNAMED.World;
using UNNAMED.World.Runtime;
using Xunit.Abstractions;
using Registry = UNNAMED.EntityRegistry.EntityRegistry;

namespace UNNAMED.Application.Tests;

/// <summary>
/// Schema 17 (the owner's ruling on the second M7 E8.5 STOP): a creature's ordinary attack in progress survives a save. A grey wolf bites
/// a character who stands still. The world is saved at a moment of its first bite - as the windup begins, one tick before it lands, on
/// the tick it lands, in its recovery - or with no attack in progress, loaded, and stepped beside the unsaved world. Every creature, the
/// character's pools and effects, and the world's creature records must match tick by tick; every bite and every effect applied must
/// land on the ticks the never-saved world lands them; and at the end every field of the two worlds must match. Before the fix, a save
/// one tick before a bite lost the windup: the loaded wolf began again and bit late (the Crossing Workshop's step 10).
/// </summary>
public class CreatureAttackContinuationTests
{
    private const int Horizon = 600;

    private readonly ITestOutputHelper _output;

    public CreatureAttackContinuationTests(ITestOutputHelper output) => _output = output;

    public enum Moment
    {
        NoAttackInProgress,
        TheWindupBegins,
        OneTickBeforeTheBite,
        TheBiteLands,
        TheRecovery,
    }

    /// <summary>
    /// The character at (120, 60) facing north, a grey wolf 8 m ahead; a swing at the air, which the wolf hears. The swing is long over
    /// when the wolf's first windup begins, so no save here holds an action of the character's own (transient by design).
    /// </summary>
    private static Arena Engaged(GameSession session)
    {
        var arena = Arena.OpenCreatures(session, session.Setup, (120, 60), 0, new[] { (Arena.Wolf, 120.0, 68.0, "hunter") });
        Assert.Null(arena.Submit(new AttackCommand(arena.Player)));
        return arena;
    }

    /// <summary>What a world did from a tick on: the blows on the character and the effects applied, without identities.</summary>
    private sealed class Seen
    {
        private readonly List<HitResolved> _hits;
        private readonly List<EffectApplied> _effects;
        private readonly List<AttackStarted> _started;
        private readonly EntityId _player;

        public Seen(Arena arena)
        {
            _hits = arena.Record<HitResolved>();
            _effects = arena.Record<EffectApplied>();
            _started = arena.Record<AttackStarted>();
            _player = arena.Player;
        }

        public List<(long Tick, int Damage, BodyRegion Region, bool Critical, int HealthAfter)> Bites =>
            _hits.Where(h => h.Target == _player).Select(h => (h.Tick, h.Damage, h.Region, h.Critical, h.HealthAfter)).ToList();

        public List<(long Tick, string Effect, int Stacks, long Expires)> Effects =>
            _effects.Where(e => e.Target == _player).Select(e => (e.Tick, e.EffectId, e.Stacks, e.ExpiresTick)).ToList();

        public List<long> Started => _started.Select(s => s.Tick).ToList();
    }

    /// <summary>The never-saved world, run through to the horizon.</summary>
    private static (Seen Seen, long Began, long Landed) Uninterrupted(GameSession session)
    {
        var arena = Engaged(session);
        var seen = new Seen(arena);
        arena.Tick(Horizon);
        long landed = seen.Bites[0].Tick;
        return (seen, seen.Started.Last(t => t < landed), landed);
    }

    private static long SaveTick(Moment moment, long began, long landed) => moment switch
    {
        Moment.NoAttackInProgress => began - 1,
        Moment.TheWindupBegins => began,
        Moment.OneTickBeforeTheBite => landed - 1,
        Moment.TheBiteLands => landed,
        _ => landed + 5,
    };

    /// <summary>The world saved now - <paramref name="alter"/> may rewrite the slot's files first - and loaded again.</summary>
    private static Arena SavedAndLoaded(TempProfile profile, GameSession session, Arena arena, string slot, Action<string>? alter = null,
        Action<LoadResult>? check = null)
    {
        var store = new SaveStore(profile.Root);
        store.Save(SaveSlots.Manual(slot), SaveDocuments.Capture(arena.Simulation.World, arena.Simulation.CaptureRecord(), session.Content,
            arena.Simulation.WorldTick, 0));
        alter?.Invoke(store.SlotPath(SaveSlots.Manual(slot)));
        var loaded = store.Load(SaveSlots.Manual(slot), new LoadContext(session.Generator, session.Content, new Registry()));
        Assert.True(loaded.IsComplete, $"rejected [{string.Join("; ", loaded.RejectedRecords.Select(r => $"{r.Key}: {r.Reason}"))}]");
        check?.Invoke(loaded);
        return Arena.Resume(arena.Simulation.Setup, loaded);
    }

    /// <summary>Everything the next ticks of a fight depend on that the game shows or holds, as one string to compare.</summary>
    private static string Now(Arena arena, bool records)
    {
        var simulation = arena.Simulation;
        var combat = simulation.Combat;
        return JsonSerializer.Serialize(new
        {
            simulation.WorldTick,
            Player = simulation.Player.Body,
            combat.Health,
            combat.Stamina,
            combat.Effects,
            Creatures = simulation.Creatures.OrderBy(c => c.Key, StringComparer.Ordinal)
                .Select(c => new { c.Key, c.Body, c.Health, c.Phase, c.PhaseTicksLeft, c.Condition, c.Mind, c.Awareness }),
            Records = records ? (object)simulation.World.TakeSnapshot().Creatures : null,
        });
    }

    /// <summary>Both worlds on, compared every tick; at the end, every field of the two.</summary>
    private static void GoOnTogether(Arena running, Arena loaded, int ticks, bool records = true)
    {
        Assert.Equal(Now(running, records), Now(loaded, records));
        for (int i = 0; i < ticks; i++)
        {
            running.Tick();
            loaded.Tick();
            Assert.Equal(Now(running, records), Now(loaded, records));
        }
        var differences = StateDump.Compare(StateDump.Render(running.Simulation), StateDump.Render(loaded.Simulation), out int leaves);
        Assert.True(differences.Count == 0, $"{differences.Count} of {leaves} fields differ: {string.Join("; ", differences.Take(6))}");
        Assert.Equal(running.Simulation.StateDigest(), loaded.Simulation.StateDigest());
    }

    [Theory]
    [InlineData(Moment.OneTickBeforeTheBite)]
    [InlineData(Moment.TheWindupBegins)]
    [InlineData(Moment.TheBiteLands)]
    [InlineData(Moment.TheRecovery)]
    [InlineData(Moment.NoAttackInProgress)]
    public void AnAttackInProgress_GoesOnAfterALoad_AsIfNeverSaved(Moment moment)
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var (never, began, landed) = Uninterrupted(session);
        long saveTick = SaveTick(moment, began, landed);
        _output.WriteLine($"first bite: windup from {began}, lands at {landed}; saved at {saveTick}; bites at " +
                          $"{string.Join(", ", never.Bites.Select(b => $"{b.Tick} ({b.Damage})"))}; effects " +
                          $"{string.Join(", ", never.Effects.Select(e => $"{e.Effect} at {e.Tick}"))}");

        var running = Engaged(session);
        running.Tick((int)saveTick);
        Assert.Equal(saveTick, running.Simulation.WorldTick);
        var wolf = running.Simulation.Creatures.Single();
        var expected = moment switch
        {
            Moment.NoAttackInProgress => CombatPhase.Idle,
            Moment.TheWindupBegins or Moment.OneTickBeforeTheBite => CombatPhase.Windup,
            Moment.TheBiteLands => CombatPhase.Active,
            _ => CombatPhase.Recovery,
        };
        Assert.Equal(expected, wolf.Phase);
        Assert.Equal(CombatPhase.Idle, running.Simulation.Combat.Phase);   // the character's own action is transient: nothing in progress

        var loaded = SavedAndLoaded(profile, session, running, "moment");
        var record = loaded.Simulation.World.TakeSnapshot().Creatures.Single();
        Assert.Equal(moment == Moment.NoAttackInProgress ? null : began, record.AttackTick);
        Assert.Equal(moment is Moment.TheBiteLands or Moment.TheRecovery, record.AttackStruck == loaded.Player);

        var (seenRunning, seenLoaded) = (new Seen(running), new Seen(loaded));
        GoOnTogether(running, loaded, Horizon - (int)saveTick);

        var after = never.Bites.Where(b => b.Tick > saveTick).ToList();
        Assert.True(after.Count >= 5, $"only {after.Count} bites after the save");
        Assert.Equal(after, seenRunning.Bites);
        Assert.Equal(after, seenLoaded.Bites);
        Assert.Equal(never.Effects.Where(e => e.Tick > saveTick), seenLoaded.Effects);
        Assert.Contains(seenLoaded.Effects, e => e.Effect == "effect.bleeding");   // the bleed's timing is compared, not only the bites'
        if (moment == Moment.OneTickBeforeTheBite)
            Assert.Equal(landed, seenLoaded.Bites[0].Tick);
    }

    /// <summary>
    /// A save from before schema 17 kept no attack in progress. One taken a tick before a bite loads through the 16 -> 17 step with none:
    /// the wolf has lost its windup, begins again, and bites late - the compatibility limitation the step documents, since the attack
    /// that was not kept cannot be reconstructed.
    /// </summary>
    [Fact]
    public void AnOlderSave_TakenMidAttack_LoadsWithNoAttackInProgress_AndTheWolfBeginsAgain()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var (_, _, landed) = Uninterrupted(session);
        var running = Engaged(session);
        running.Tick((int)landed - 1);

        var loaded = SavedAndLoaded(profile, session, running, "older", AsSchema16,
            result => Assert.Equal(new[] { "schema 16 -> 17:" }, result.Report.Steps.Select(s => s[..16])));
        Assert.Null(loaded.Simulation.World.TakeSnapshot().Creatures.Single().AttackTick);
        Assert.Equal(CombatPhase.Idle, loaded.Simulation.Creatures.Single().Phase);

        var (seenRunning, seenLoaded) = (new Seen(running), new Seen(loaded));
        running.Tick(40);
        loaded.Tick(40);
        Assert.Equal(landed, seenRunning.Bites[0].Tick);
        Assert.True(seenLoaded.Started[0] > landed - 1, "the loaded wolf did not begin again");
        Assert.True(seenLoaded.Bites[0].Tick > landed, $"the loaded wolf bit at {seenLoaded.Bites[0].Tick}, not after {landed}");
    }

    /// <summary>
    /// Whom a swing landed on only marks it spent: no identity is looked up at the load, and no target is stored - the owner of the
    /// attack, the creature, picks its foe at the impact tick, as it always did. So a landing that names someone with no body in the world
    /// loads, and the swing stays spent: the loaded world goes on exactly as the one never saved.
    /// </summary>
    [Fact]
    public void ABiteThatLandedOnSomeoneNoLongerThere_StaysSpent_AndGoesOnTheSame()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var (never, _, landed) = Uninterrupted(session);
        var running = Engaged(session);
        running.Tick((int)landed);
        string absent = EntityId.Create(EntityKind.Npc, 1_700_000_000_500, new byte[] { 4, 4, 4, 4, 4, 4, 4, 4, 4, 4 }).Value;

        var loaded = SavedAndLoaded(profile, session, running, "absent", slot => Entities(slot, dto =>
            dto.Creatures!.Single().Continuation!.Attack!.Struck = absent));
        Assert.Equal(absent, loaded.Simulation.World.TakeSnapshot().Creatures.Single().AttackStruck!.Value);

        var seenLoaded = new Seen(loaded);
        GoOnTogether(running, loaded, 120, records: false);
        Assert.Equal(never.Bites.Where(b => b.Tick > landed && b.Tick <= landed + 120), seenLoaded.Bites);
    }

    // ── rewriting a saved slot, as an older build or damage would have left it ──

    private static readonly MessagePackSerializerOptions Options = MessagePackSerializerOptions.Standard;

    private static void Entities(string slot, Action<EntitiesSectionDto> change)
    {
        string path = Path.Combine(slot, SaveFormat.Entities);
        var dto = MessagePackSerializer.Deserialize<EntitiesSectionDto>(File.ReadAllBytes(path), Options);
        change(dto);
        Rewrite(slot, SaveFormat.Entities, MessagePackSerializer.Serialize(dto, Options));
    }

    /// <summary>The slot as a schema-16 build wrote it: the same entities without the attack, and the manifest's schema 16.</summary>
    private static void AsSchema16(string slot)
    {
        byte[] current = File.ReadAllBytes(Path.Combine(slot, SaveFormat.Entities));
        Rewrite(slot, SaveFormat.Entities, MessagePackSerializer.Serialize(MessagePackSerializer.Deserialize<UNNAMED.Persistence.Sections.V16.EntitiesSection>(current, Options), Options));
        var manifest = JsonNode.Parse(File.ReadAllText(Path.Combine(slot, SaveFormat.Manifest)))!.AsObject();
        manifest["schema_version"] = 16;
        Rewrite(slot, SaveFormat.Manifest, System.Text.Encoding.UTF8.GetBytes(manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true })));
    }

    /// <summary>Replace a file and its line in <c>sections.sha256</c>, so the change passes the integrity check.</summary>
    private static void Rewrite(string slot, string file, byte[] bytes)
    {
        File.WriteAllBytes(Path.Combine(slot, file), bytes);
        string hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();
        string root = Path.Combine(slot, SaveFormat.IntegrityRoot);
        var lines = File.ReadAllLines(root).Select(l => l.EndsWith("  " + file, StringComparison.Ordinal) ? $"{hash}  {file}" : l);
        File.WriteAllText(root, string.Join("\n", lines) + "\n");
    }
}
