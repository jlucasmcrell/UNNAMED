using System.Collections.Immutable;
using UNNAMED.Domain;
using UNNAMED.Domain.Items;
using UNNAMED.Domain.Companions;
using UNNAMED.Domain.Progression;
using UNNAMED.Domain.Social;
using UNNAMED.World;
using UNNAMED.World.Runtime;

namespace UNNAMED.Application.Tests;

/// <summary>
/// WP9 (C10, D11): teachers over the game's own content - the Learn column's view of what a teacher offers and on what terms, a lesson
/// asked for there and paid for in coin, its gates (a skill band, a prerequisite) refused whole, lessons a conversation gives left to the
/// conversation, and no lesson ever raising a skill; then the technique a lesson teaches, used with a weapon of its family. The
/// conversations' own lessons keep their outcomes (NpcTests, CairnwoldQuestTests).
/// </summary>
public class TeachingTests
{
    private const string Kera = "npc.ashen_hollow.kera_voss";
    private const string Berrin = "npc.cairnwold.berrin_garth";
    private const string KerasPurse = "merchant.ashen_hollow.kera_voss";
    private const string PowerStrike = "ability.blade.power_strike";
    private const string AimedShot = "ability.bow.aimed_shot";
    private const string Blade = "skill.one_hand_blade";
    private const string Pad = "piece.pad.timber";
    private const string Wall = "piece.wall.timber";
    private const string Billet = "recipe.smithing.iron_billet";

    private static readonly (double X, double Z) AtKera = (60.3, 140.3);
    private static readonly (double X, double Z) AtBerrin = (959.0, 644.0);   // region metres
    private static readonly (double X, double Z) Open = (120, 60);

    private static PlayerRecord With(PlayerRecord r, long coin, int blade = 0, params string[] known) =>
        new PlayerRecord(r.Id, r.Name, r.XMm, r.YMm, r.ZMm, r.AppearanceSeed, r.Inventory, r.Progression, r.FacingMdeg, r.Discoveries,
                r.Equipment, coin, r.Effects, r.Relationships, r.Conversations)
            .WithProgression(r.Progression with
            {
                Skills = blade == 0 ? r.Progression.Skills : r.Progression.Skills.SetItem(Blade, new SkillState(blade, 0)),
                Known = known.Aggregate(r.Progression.Known, (k, id) => k.SetItem(id, new KnownTechnique(LearningSource.Teacher, Kera, 0))),
            });

    private static Arena AtKeras(GameSession session, long coin, int blade = 0) =>
        Arena.OpenCreatures(session, session.Setup, AtKera, 0, Array.Empty<(string, double, double, string)>(), r => With(r, coin, blade));

    private static Arena AtBerrins(GameSession session, long coin) =>
        Arena.OpenCreatures(session, session.Setup, AtBerrin, 0, Array.Empty<(string, double, double, string)>(), r => With(r, coin), origin: Harness.Region);

    private static string? Learn(Arena arena, string npc, string id) => arena.Submit(new LearnFromTeacherCommand(arena.Player, npc, id));

    [Fact]
    public void TheLearnColumn_ListsWhatKeraTeaches_WithTheGatesAsTheyStand()
    {
        using var profile = new TempProfile();
        var arena = AtKeras(Harness.Boot(profile), 100, blade: 4);

        var view = arena.Simulation.Teaching(Kera)!;
        Assert.True(view.Available);   // at the forge, in her working hours (WP12)
        Assert.Equal(new[] { "recipe.smithing.torch", "recipe.cooking.torch", "recipe.cooking.boil_water", "recipe.cooking.herb_tea",
            "recipe.herbal.salve_minor_boiled", Billet, "recipe.smithing.march_spear", "recipe.fletching.arrows", "recipe.fletching.hunting_bow",
            "recipe.fletching.hunting_bow_silk", "recipe.smithing.lamework_jack", "recipe.smithing.lame_coif", "recipe.smithing.lame_sabatons",
            "recipe.smithing.fitting_wedge", PowerStrike }, view.Lessons.Select(l => l.DefinitionId));
        Assert.All(view.Lessons.Take(8), l => Assert.True(l.ByDialogue && l.Fee == 0 && l.Met));
        // The Barrowworks wedge waits on the ring being reachable, which it never is with the Barrowworks off.
        var wedge = view.Lessons.Single(l => l.DefinitionId == "recipe.smithing.fitting_wedge");
        Assert.False(wedge.Met);
        Assert.IsType<FlagRequirement>(Assert.Single(wedge.Requirements).Requirement);
        var strike = view.Lessons.Last();
        Assert.Equal((TeachingKind.Technique, 25L, false, false), (strike.Kind, strike.Fee, strike.Known, strike.ByDialogue));
        var gate = Assert.Single(strike.Requirements);
        Assert.Equal((new SkillBandRequirement(Blade, "familiar", 15) as TeachingRequirement, false, 4L), (gate.Requirement, gate.Met, gate.Have));
        Assert.False(strike.Met);
        Assert.Null(arena.Simulation.Teaching("npc.ashen_hollow.renn_vale"));   // Renn teaches nothing
    }

    /// <summary>DN13: a teacher who hires on does not teach while in the character's hire, at their work in work hours or anywhere else.</summary>
    [Fact]
    public void ATeacherInTheCharactersHire_DoesNotTeach()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = Arena.OpenCreatures(session, Harness.HireableTownsfolk(session.Setup, Kera), AtKera, 0, Array.Empty<(string, double, double, string)>(),
            r => With(r, 100, 15));
        Assert.Null(arena.Simulation.TeachRefusal(Kera));
        Assert.Null(arena.Submit(new HireCommand(arena.Player, Kera, HirelingRole.Guard, 3, AcceptsRisk: true)));
        Assert.Equal("Kera Voss is in your hire: they teach only at their work", arena.Simulation.TeachRefusal(Kera));
        Assert.False(arena.Simulation.Teaching(Kera)!.Available);
    }

    [Fact]
    public void ATeacher_TeachesOnlyAtTheirWork_InTheirWorkHours()
    {
        using var profile = new TempProfile();
        var arena = AtKeras(Harness.Boot(profile), 100, blade: 15);
        Assert.True(arena.Simulation.Teaching(Kera)!.Available);
        // The night comes on: she leaves the forge, and the Learn column says why she is not teaching (WP12's answer).
        for (int hour = 0; hour < 24 && arena.Simulation.TeachRefusal(Kera) is null; hour++)
            arena.Simulation.Advance(2_400);
        string? refusal = arena.Simulation.TeachRefusal(Kera);
        Assert.NotNull(refusal);
        Assert.Equal(refusal, arena.Simulation.Teaching(Kera)!.Unavailable);
    }

    [Fact]
    public void ALessonFromTheLearnColumn_IsPaidFor_AndTeachesWithoutRaisingASkill()
    {
        using var profile = new TempProfile();
        var arena = AtKeras(Harness.Boot(profile), 40, blade: 15);
        var taught = arena.Record<Taught>();
        var learned = arena.Record<TechniqueLearned>();
        var practice = arena.Record<SkillPracticed>();
        long purse = arena.Simulation.World.Merchant(KerasPurse)!.Purse;
        var skills = arena.Simulation.Player.Progression.Skills;

        Assert.Equal("not talking to Kera Voss", Learn(arena, Kera, PowerStrike));
        Assert.Null(arena.Submit(new TalkCommand(arena.Player, Kera)));
        Assert.Null(Learn(arena, Kera, PowerStrike));

        Assert.Equal(new Taught(Kera, PowerStrike, TeachingKind.Technique, 25, arena.Simulation.WorldTick), Assert.Single(taught));
        Assert.Equal((PowerStrike, "teacher"), (Assert.Single(learned).DefinitionId, learned[0].Source));
        var known = arena.Simulation.Player.Progression.Known[PowerStrike];
        Assert.Equal((LearningSource.Teacher, Kera), (known.Source, known.SourceRef));
        Assert.Equal(15, arena.Simulation.Player.Currency);
        Assert.True(arena.Simulation.World.Merchant(KerasPurse)!.Purse >= purse);   // into her purse, up to its top (WP14)
        // Canon: no fee or teacher ever raises a skill (PROGRESSION.md:223).
        Assert.Equal(skills, arena.Simulation.Player.Progression.Skills);
        Assert.Empty(practice);

        Assert.Equal($"{PowerStrike} is already known", Learn(arena, Kera, PowerStrike));
        Assert.True(arena.Simulation.Teaching(Kera)!.Lessons.Single(l => l.DefinitionId == PowerStrike).Known);
    }

    [Fact]
    public void ALessonWhoseGatesOrFeeAreNotMet_IsRefusedWhole()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);

        var green = AtKeras(session, 40, blade: 14);
        Assert.Null(green.Submit(new TalkCommand(green.Player, Kera)));
        Assert.Equal($"Kera Voss will not teach {PowerStrike} yet: needs {Blade} at familiar (15), has 14", Learn(green, Kera, PowerStrike));
        Assert.False(ProgressionEngine.Knows(green.Simulation.Player.Progression, PowerStrike));
        Assert.Equal(40, green.Simulation.Player.Currency);

        var poor = AtKeras(session, 24, blade: 20);
        Assert.Null(poor.Submit(new TalkCommand(poor.Player, Kera)));
        Assert.Equal("not enough coin: 25 asked, 24 carried", Learn(poor, Kera, PowerStrike));
        Assert.False(ProgressionEngine.Knows(poor.Simulation.Player.Progression, PowerStrike));
        Assert.Equal(24, poor.Simulation.Player.Currency);

        // What her conversation gives is asked for there, and what she does not teach is not hers to give.
        Assert.Equal($"Kera Voss teaches {Billet} when asked in conversation", Learn(poor, Kera, Billet));
        Assert.Equal($"Kera Voss does not teach {AimedShot}", Learn(poor, Kera, AimedShot));
    }

    /// <summary>Berrin teaches the building pieces (owner decision), a pad before walls: a prerequisite is known first.</summary>
    [Fact]
    public void Berrin_TeachesTheBuildingPieces_EachAfterItsPrerequisite()
    {
        using var profile = new TempProfile();
        var arena = AtBerrins(Harness.Boot(profile), 20);
        Assert.Null(arena.Submit(new TalkCommand(arena.Player, Berrin)));

        Assert.Equal($"Berrin Garth will not teach {Wall} yet: needs {Pad} known first", Learn(arena, Berrin, Wall));
        Assert.Null(Learn(arena, Berrin, Pad));
        Assert.Null(Learn(arena, Berrin, Wall));

        Assert.Equal(5, arena.Simulation.Player.Currency);   // 5 for the pad, 10 for the wall; Berrin keeps no purse, so it simply goes
        Assert.All(new[] { Pad, Wall }, id => Assert.Equal(Berrin, arena.Simulation.Player.Progression.Known[id].SourceRef));
        var lessons = arena.Simulation.Teaching(Berrin)!.Lessons;
        Assert.All(lessons, l => Assert.Equal(TeachingKind.BuildingPiece, l.Kind));
        Assert.True(lessons.Single(l => l.DefinitionId == "piece.doorway.timber").Met);   // the wall is known now
        Assert.False(lessons.Single(l => l.DefinitionId == "piece.door.timber").Met);
    }

    /// <summary>T-22 for teaching: a lesson has no clock, so any split of an absence leaves what the teachers offer and what is known alike.</summary>
    [Fact]
    public void TheTeachingAdvance_IsANoOp_AndChunkInvariant()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        string Offered(Arena arena) => string.Join("|", arena.Simulation.Teaching(Kera)!.Lessons
            .Select(l => $"{l.DefinitionId}:{l.Known}:{l.Met}:{string.Join(",", l.Requirements.Select(r => $"{r.Met}{r.Have}"))}"));
        var once = AtKeras(session, 40, blade: 15);
        var pieces = AtKeras(session, 40, blade: 15);
        string before = Offered(once);
        long day = session.Setup.Clock.TicksPerDay;

        once.Simulation.Advance(6 * day + 11);
        pieces.Simulation.Advance(5);
        pieces.Simulation.Advance(day);
        pieces.Simulation.Advance(5 * day + 6);

        Assert.Equal(before, Offered(once));
        Assert.Equal(Offered(once), Offered(pieces));
        Assert.Equal(once.Simulation.Player.Progression.Known.Keys, pieces.Simulation.Player.Progression.Known.Keys);
        Assert.Equal(once.Simulation.Player.Currency, pieces.Simulation.Player.Currency);
    }

    // ── techniques (D11) ────────────────────────────────────────────────────

    [Fact]
    public void PowerStrike_IsTheSwordsOwnBlow_Changed_AndComesReadyAfterItsCooldown()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = Arena.OpenCreatures(session, session.Setup, Open, 0, Array.Empty<(string, double, double, string)>(),
            r => With(r, 0, 0, PowerStrike));
        var started = arena.Record<AttackStarted>();
        var used = arena.Record<TechniqueUsed>();
        int stamina = arena.Simulation.Combat.Stamina;

        Assert.Null(arena.Submit(new TechniqueCommand(arena.Player, PowerStrike)));
        var swing = Assert.Single(started);
        Assert.Equal((PowerStrike, 9, 3, 5), (swing.Source, swing.WindupTicks, swing.ActiveTicks, swing.RecoveryTicks));   // the sword's 6-tick windup, x1.5
        Assert.Equal(stamina - 22, arena.Simulation.Combat.Stamina);
        Assert.Equal("item.weapon.rusted_sword", Assert.Single(used).WeaponDefId);

        arena.Tick(20);
        int left = arena.Simulation.TechniqueCooldown(PowerStrike);
        Assert.Equal(120 - 20, left);   // 6 s at 20 Hz, counted from its use
        Assert.Equal($"{PowerStrike} is not ready ({left} ticks)", arena.Submit(new TechniqueCommand(arena.Player, PowerStrike)));
        arena.Tick(left);
        Assert.Equal(0, arena.Simulation.TechniqueCooldown(PowerStrike));
        Assert.Null(arena.Submit(new TechniqueCommand(arena.Player, PowerStrike)));
    }

    [Fact]
    public void ATechnique_IsRefusedUnknown_OrWithoutAWeaponOfItsFamily()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = Arena.OpenCreatures(session, session.Setup, Open, 0, Array.Empty<(string, double, double, string)>(),
            r => With(r, 0, 0, AimedShot));

        Assert.Equal($"{PowerStrike} is not known", arena.Submit(new TechniqueCommand(arena.Player, PowerStrike)));
        Assert.Equal($"{AimedShot} needs a skill.bow weapon in hand", arena.Submit(new TechniqueCommand(arena.Player, AimedShot)));
        Assert.Equal("ability.creature.wolf_bite is not a technique this build knows",
            arena.Submit(new TechniqueCommand(arena.Player, "ability.creature.wolf_bite")));
        Assert.Equal(CombatPhase.Idle, arena.Simulation.Combat.Phase);
    }

    [Fact]
    public void PowerStrike_LandsUnderItsOwnName_AndTrainsTheBlade()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = Arena.Open(session, Open, 0, new[] { (Open.X, Open.Z + 2.0) }, r => With(r, 0, 0, PowerStrike));
        var hits = arena.Record<HitResolved>();
        var practice = arena.Record<SkillPracticed>();
        arena.Face(arena.Creature());
        arena.Tick();

        Assert.Null(arena.Submit(new TechniqueCommand(arena.Player, PowerStrike)));
        arena.Tick(14);

        var hit = Assert.Single(hits, h => h.Attacker == arena.Player);
        Assert.Equal(PowerStrike, hit.Source);
        Assert.True(hit.Damage > 0);
        Assert.Equal(Blade, Assert.Single(practice).SkillId);   // an effective technique trains its family, as any wounding blow does
    }
}
