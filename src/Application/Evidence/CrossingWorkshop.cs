// UNNAMED Application - the Crossing Workshop, M7's acceptance scenario, as data and a start builder (M7 design §4.22)
// No Godot references - pure C#

using System.Collections.Immutable;
using UNNAMED.Domain;
using UNNAMED.Domain.Companions;
using UNNAMED.Domain.Progression;
using UNNAMED.Domain.Spatial;
using UNNAMED.World;
using UNNAMED.World.Runtime;
using Registry = UNNAMED.EntityRegistry.EntityRegistry;

namespace UNNAMED.Application.Evidence;

/// <summary>
/// Where a row stands the player (§4.22's notation): the run legs, each to 300 mm; then the pose, to 50 mm at a walk; then one idle frame
/// facing the stated degrees.
/// </summary>
public sealed record WorkshopPose(ImmutableArray<(double X, double Z)> Legs, double X, double Z, int FacingDeg);

/// <summary>What a row does at its tick. Each action after a row's first is one tick after the one before (§4.22 "+1 each").</summary>
public abstract record WorkshopAction(int From);

/// <summary><c>Place(def, x, z, r)</c>: a <see cref="PlacePieceCommand"/> from the player.</summary>
public sealed record PlaceAction(string DefId, long XMm, long ZMm, int Rotation, int From = CrossingWorkshop.E5) : WorkshopAction(From)
{
    public GameCommand Command(EntityId player) => new PlacePieceCommand(player, DefId, XMm, ZMm, Rotation);
}

/// <summary>
/// <c>InteractCommand(door)</c> on the placed piece of a definition at an anchor (R10, R11, R13: the workshop's door), resolved to its
/// <c>pce_</c> key when the row plays.
/// </summary>
public sealed record InteractPieceAction(string DefId, long XMm, long ZMm, int From = CrossingWorkshop.E6) : WorkshopAction(From)
{
    /// <summary>The piece this action works, in a world.</summary>
    public EntityId? Target(Simulation simulation) =>
        simulation.Pieces.FirstOrDefault(p => p.DefId == DefId && p.XMm == XMm && p.ZMm == ZMm)?.Id;
}

/// <summary><c>DismantlePieceCommand</c> on the placed piece of a definition at an anchor (R25: the vestibule taken down).</summary>
public sealed record DismantleAction(string DefId, long XMm, long ZMm, int From = CrossingWorkshop.E7) : WorkshopAction(From)
{
    /// <summary>The piece this action takes down, in a world.</summary>
    public EntityId? Target(Simulation simulation) =>
        simulation.Pieces.FirstOrDefault(p => p.DefId == DefId && p.XMm == XMm && p.ZMm == ZMm)?.Id;
}

/// <summary>A move held for a number of ticks, one <see cref="MoveCommand"/> a tick (R12: north at a walk for 40 ticks).</summary>
public sealed record HoldAction(MoveIntent Intent, int Ticks, int From = CrossingWorkshop.E5) : WorkshopAction(From);

/// <summary>A read, not a command: <see cref="Simulation.Aim"/> along a facing, to a range (R14).</summary>
public sealed record AimAction(int FacingMdeg, long RangeMm, int From = CrossingWorkshop.E5) : WorkshopAction(From);

/// <summary>An order to a companion (R45).</summary>
public sealed record OrderAction(string NpcId, CompanionOrder Order, int From = CrossingWorkshop.E5) : WorkshopAction(From)
{
    public GameCommand Command(EntityId player) => new OrderCompanionCommand(player, NpcId, Order);
}

/// <summary>The save to <c>quick</c>, then the continuation (R46). Each consumer saves, continues and reloads its own way.</summary>
public sealed record SaveAction(int ContinueTicks, int From = CrossingWorkshop.E5) : WorkshopAction(From);

/// <summary>
/// One row of the command table. A row with a pose starts at the first tick boundary after the pose is reached; one without starts
/// <paramref name="After"/> ticks after the previous row's last action. <paramref name="Expect"/> is §4.22's column, for transcripts; each
/// consumer asserts it.
/// </summary>
public sealed record WorkshopRow(string Id, int Step, int From, WorkshopPose? Pose, int After, ImmutableArray<WorkshopAction> Actions, string Expect)
{
    /// <summary>The actions whose slice has landed.</summary>
    public IEnumerable<WorkshopAction> Landed => Actions.Where(a => a.From <= CrossingWorkshop.Landed);
}

/// <summary>
/// The Crossing Workshop (M7 design §4.22): a 2 × 2 workshop over the four-cell corner, its south doorway straddling x = 100. One command
/// table, <see cref="Rows"/>, run headless by <c>BuildingAcceptanceTests</c> and as <c>--build-shots</c> beats; and <see cref="Start"/>,
/// the character the committed start save S0 holds. Each row names the slice that adds it; a run plays every row, and every action, whose
/// slice has landed. Scripts may name content; the game may not.
/// </summary>
public static class CrossingWorkshop
{
    public const int E5 = 5, E6 = 6, E7 = 7, E8 = 8, E9 = 9;

    /// <summary>The slices landed so far.</summary>
    public const int Landed = E7;

    /// <summary>S0's world seed: the playthrough's (<c>Playthrough.Seed</c>, presentation), so both proofs play one world.</summary>
    public const ulong Seed = 0x0A5E_2026_0924_0001;

    public const string Pad = "piece.pad.timber", Wall = "piece.wall.timber", Doorway = "piece.doorway.timber", Roof = "piece.roof.timber",
        Door = "piece.door.timber";
    public const string Timber = "item.material.timber", IronIngot = "item.material.iron_ingot", AshHaft = "item.material.ash_haft";
    public const string SpearRecipe = "recipe.smithing.march_spear";
    public const string Kera = "npc.ashen_hollow.kera_voss", Tavar = "npc.ashen_hollow.tavar_orr";

    /// <summary>Where S0 stands the character, and Tavar waiting north of the workshop.</summary>
    public static readonly (long XMm, long ZMm, int FacingMdeg) Character = (100_500, 94_500, 0), TavarWaits = (100_500, 108_500, 180_000);

    /// <summary>
    /// S0's character: a new character (the starting package and kit) at (100.5, 94.5) facing 0, carrying 45 timber as standard stacks of
    /// 20, 20 and 5, one iron ingot and one ash haft, knowing the March Spear, with Tavar Orr recruited and told to wait at (100.5, 108.5)
    /// facing 180°. Its instance IDs are fresh, as every new character's are (D-04).
    /// </summary>
    public static PlayerRecord Start(SimulationSetup setup)
    {
        var id = EntityId.NewId(EntityKind.Character);
        var fresh = Simulation.NewCharacter(setup, id, "Wanderer", PlayerRecord.DerivedAppearanceSeed(id));
        var carried = new[] { (Timber, 20), (Timber, 20), (Timber, 5), (IronIngot, 1), (AshHaft, 1) }
            .Select(s => new InventoryEntry(EntityId.NewId(EntityKind.Item), s.Item1, s.Item2));
        var (x, z, facing) = Character;
        var record = new PlayerRecord(fresh.Id, fresh.Name, x, setup.Layout.Space.Terrain.HeightAtMm(x, z), z, fresh.AppearanceSeed,
            fresh.Inventory.Concat(carried), fresh.Progression, facing, equipment: fresh.Equipment);
        var profile = setup.Social.Npcs[Tavar].Companion ?? throw new InvalidOperationException($"{Tavar} cannot join");
        return record
            .WithProgression(record.Progression with
            {
                Known = record.Progression.Known.SetItem(SpearRecipe, new KnownTechnique(LearningSource.Teacher, Kera, 0)),
            })
            .WithCompanions(new[]
            {
                new CompanionRecord(Tavar, CompanionOrder.Wait, CompanionCondition.Up, TavarWaits.XMm, TavarWaits.ZMm, TavarWaits.FacingMdeg,
                    profile.MaxHealth),
            });
    }

    /// <summary>S0's world: nothing changed yet, at <see cref="Seed"/>, tick 0.</summary>
    public static WorldDelta StartWorld(ICellBaselineGenerator generator) => new(generator, Seed, new Registry());

    private static WorkshopPose At(double x, double z, int facingDeg, params (double X, double Z)[] legs) => new(legs.ToImmutableArray(), x, z, facingDeg);

    private static WorkshopRow Row(string id, int step, WorkshopPose? pose, int after, string expect, params WorkshopAction[] actions) =>
        RowFrom(E5, id, step, pose, after, expect, actions);

    private static WorkshopRow RowFrom(int from, string id, int step, WorkshopPose? pose, int after, string expect, params WorkshopAction[] actions) =>
        new(id, step, from, pose, after, actions.ToImmutableArray(), expect);

    /// <summary>The workshop's door, as R10, R11 and R13 work it.</summary>
    private static InteractPieceAction TheDoor => new(Door, 100_500, 99_000);

    private static PlaceAction Place(string def, long x, long z, int r) => new(def, x, z, r);

    /// <summary>
    /// The table as far as E7 has built it: rows R00-R06, R09-R14 (the door hung, worked from both sides, and the walk north stopped by
    /// it), R22-R25 (the vestibule refused as unnavigable, and taken down), R39-R42, R45 and R46. E8-E9 add the rest, and E9 changes R45's
    /// pose (§4.22).
    /// </summary>
    public static readonly ImmutableArray<WorkshopRow> Rows = ImmutableArray.Create(
        Row("R00", 0, null, 0, "S0 loaded"),
        Row("R01", 1, At(102.0, 102.0, 0), 0, "-"),
        Row("R02", 1, null, 1, "seq 1-4; pad (33, 33) hosted in c_01_01; no NavigationRebuilt",
            Place(Pad, 100_500, 100_500, 0), Place(Pad, 103_500, 100_500, 0), Place(Pad, 100_500, 103_500, 0), Place(Pad, 103_500, 103_500, 0)),
        Row("R03", 1, null, 1, "seq 5; opening x 99 700-101 300 straddles x = 100", Place(Doorway, 100_500, 99_000, 0)),
        Row("R04", 1, null, 1, "seq 6-12; R03-R04 publish 8 NavigationRebuilt",
            Place(Wall, 103_500, 99_000, 0), Place(Wall, 100_500, 105_000, 0), Place(Wall, 103_500, 105_000, 0), Place(Wall, 99_000, 100_500, 1),
            Place(Wall, 99_000, 103_500, 1), Place(Wall, 105_000, 100_500, 1), Place(Wall, 105_000, 103_500, 1)),
        Row("R05", 1, null, 1, "seq 13-16; no rebuild",
            Place(Roof, 100_500, 100_500, 0), Place(Roof, 103_500, 100_500, 0), Place(Roof, 100_500, 103_500, 0), Place(Roof, 103_500, 103_500, 0)),
        RowFrom(E6, "R06", 1, null, 1, "seq 17; one rebuild; placed closed", new PlaceAction(Door, 100_500, 99_000, 0, E6)),
        Row("R09", 2, null, 1, "refused Slot, \"a Timber Doorway already stands there\"; StateDigest unchanged", Place(Wall, 100_500, 99_000, 0)),
        Row("R10", 3, At(100.5, 100.3, 180), 0, "E6+: the door opened", TheDoor),
        Row("R11", 3, At(100.5, 97.6, 0), 0, "E6+: the door closed", TheDoor),
        Row("R12", 3, At(100.5, 97.0, 0), 0, "E6+: body z <= 98 450 on every tick and >= 98 400 at the end; E5: z > 99 200 at the end",
            new HoldAction(new MoveIntent(0, MoveIntent.FullDeflection, Gait.Walk, 0), 40)),
        RowFrom(E6, "R13", 3, null, 1, "the door opened", TheDoor),
        Row("R14", 3, At(100.5, 101.0, 270), 0, "stop x in [99 200, 99 210], OnCreature false", new AimAction(270_000, 20_000)),
        RowFrom(E7, "R22", 7, At(100.5, 94.5, 0), 0, "accepted", new PlaceAction(Pad, 100_500, 97_500, 0, E7)),
        RowFrom(E7, "R23", 7, null, 1, "accepted", new PlaceAction(Wall, 99_000, 97_500, 1, E7), new PlaceAction(Wall, 102_000, 97_500, 1, E7)),
        RowFrom(E7, "R24", 7, null, 1, "refused Navigability, rule V-N1; E9 text \"that would cut Kera Voss's work place off\"; digest unchanged",
            new PlaceAction(Wall, 100_500, 96_000, 0, E7)),
        RowFrom(E7, "R25", 7, null, 1, "refunds 1, 1, 0",
            new DismantleAction(Wall, 99_000, 97_500), new DismantleAction(Wall, 102_000, 97_500), new DismantleAction(Pad, 100_500, 97_500)),
        Row("R39", 9, At(97.5, 102.0, 270, (100.5, 100.3), (100.5, 97.6), (97.5, 97.0)), 0, "accepted",
            Place(Pad, 97_500, 100_500, 0), Place(Pad, 97_500, 103_500, 0)),
        Row("R40", 9, null, 1, "accepted", Place(Wall, 96_000, 100_500, 1)),
        Row("R41", 9, At(96.0, 103.5, 0), 0, "refused Bodies, \"someone is standing there\"", Place(Wall, 96_000, 103_500, 1)),
        Row("R42", 9, At(97.5, 103.5, 270), 0, "accepted; the line x = 96 runs z 98.8-105.2", Place(Wall, 96_000, 103_500, 1)),
        Row("R45", 9, At(102.0, 102.0, 0, (97.5, 97.0), (100.5, 97.6), (100.5, 100.3)), 0, "RoutePlanned for Tavar",
            new OrderAction(Tavar, CompanionOrder.Follow)),
        Row("R46", 10, null, 60, "the step-10 assertions (saved 60 ticks on, not 100: at 100 Tavar has just come into clear view and left his route)", new SaveAction(600)));

    /// <summary>The rows whose slice has landed, in order.</summary>
    public static IEnumerable<WorkshopRow> LandedRows => Rows.Where(r => r.From <= Landed);

    /// <summary>§4.22's per-slice counts: after step 1 (pieces, timber spent, sequence), and at the end of the table (pieces, timber carried, sequence).</summary>
    public static ((int Pieces, int Spent, long Sequence) StepOne, (int Pieces, int Carried, long Sequence) End) Counts => Landed switch
    {
        E5 => ((16, 24, 16), (20, 15, 20)),
        E6 => ((17, 25, 17), (21, 14, 21)),
        E7 => ((17, 25, 17), (21, 11, 27)),
        _ => ((19, 31, 19), (23, 0, 31)),
    };
}
