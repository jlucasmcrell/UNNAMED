using UNNAMED.Domain;
using UNNAMED.Domain.Crafting;
using UNNAMED.Domain.Progression;
using UNNAMED.World;
using UNNAMED.World.Runtime;

namespace UNNAMED.Application.Tests;

/// <summary>
/// D13's gathering family at the nodes the regions author (SKILLS_AND_TECHNIQUES.md §2.7), and the two crafting skills the catalogue gives
/// the stations: a node practises the skill its yield belongs to, and a station practises the skill of the work done at it. Every node used
/// to practise skill.survival whatever it held, which is the defect this reads for. The practice is read off the event a gather and a craft
/// pay, the way SwingingTests reads a swing's.
/// </summary>
public class SkillPracticeTests
{
    private const string Mining = "skill.mining";
    private const string Herbalism = "skill.herbalism";
    private const string Foraging = "skill.foraging";
    private const string Logging = "skill.logging";
    private const string Skinning = "skill.skinning";
    private const string Cooking = "skill.cooking";
    private const string Woodworking = "skill.woodworking";

    private const string Arrows = "recipe.fletching.arrows";
    private const string Kera = "npc.ashen_hollow.kera_voss";

    // The places the region authors, in its own metres: a step off the station, inside the arm's reach.
    private static readonly (double X, double Z) AtCookfire = (447.0, 738.0);     // station.hollow.cookfire at (447.0, 736.5)
    private static readonly (double X, double Z) AtWorkbench = (438.5, 745.5);    // station.hollow.workbench at (438.5, 744.0)

    private static Arena At(GameSession session, (double X, double Z) place, Func<PlayerRecord, PlayerRecord>? change = null) =>
        Arena.OpenCreatures(session, session.Setup, place, 0, Array.Empty<(string, double, double, string)>(), change ?? Arena.Usual,
            origin: Harness.Region);

    /// <summary>Where a node of that name stands, a step off it, in the region's own metres.</summary>
    private static (double X, double Z) StepOff(GameSession session, string name)
    {
        var site = session.Setup.Layout.Nodes.Single(n => n.Name == name);
        return (site.XMm / 1000.0 - 1, site.ZMm / 1000.0);
    }

    /// <summary>Gather at a node, letting the swings its content asks for run out, and hand back what the harvest practised.</summary>
    private static IReadOnlyList<SkillPracticed> GatherAt(GameSession session, string name)
    {
        var arena = At(session, StepOff(session, name));
        var practised = arena.Record<SkillPracticed>();
        var gathered = arena.Record<NodeGathered>();
        Assert.Null(arena.Submit(new SwingAtNodeCommand(arena.Player, arena.Simulation.Nodes.Single(n => n.Name == name).Key)));
        arena.Tick(SwingingSystem.SwingTicks * 4);   // three is the most swings any node asks for
        Assert.Null(arena.Simulation.Activity);
        Assert.Single(gathered);
        return practised;
    }

    /// <summary>A node of every family the regions place, gathered, and the skill the harvest paid read off the event.</summary>
    [Fact]
    public void EveryNode_PractisesTheSkillItsYieldBelongsTo()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);

        (string Node, string Skill)[] nodes =
        {
            ("iron_seam", Mining),                  // the finite seam: three workings of three swings
            ("blackvein_spoil_1", Mining),          // loose ore in the old workings' spoil
            ("hollow_woundmoss_1", Herbalism),      // a herb
            ("foldscar_boneleaf_1", Herbalism),     // a herb
            ("bog_ashbloom_1", Herbalism),          // the bog's herb
            ("ah_yard_bog_cotton", Foraging),       // a fibre plant: food, fibre, water, tinder
            ("charwood_deadfall_1", Logging),       // timber off a fallen trunk
            ("hollow_ash_stand_2", Logging),        // a standing tree
            ("halda_pelt_snare_west", Skinning),    // a hide, off Halda's line
            ("halda_larder_snare_west", Foraging),  // meat, which is food off ground you do not own
        };

        foreach (var (node, skill) in nodes)
        {
            var practised = GatherAt(session, node);
            Assert.True(practised.Count > 0, $"{node} practised nothing");
            Assert.True(practised.All(p => p.SkillId == skill),
                $"{node} practised {string.Join(", ", practised.Select(p => p.SkillId).Distinct())}, not {skill}");
        }
    }

    /// <summary>The cookfire's own recipes practise cooking, and the bench's shaft work practises woodworking (D13 §2.6).</summary>
    [Fact]
    public void ACookfirePractisesCooking_AndTheWorkbenchPractisesWoodworking()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);

        // A station teaches its own recipes the first time a character visits it (the hollow's fire does), so the fire needs only the meat.
        var fire = At(session, AtCookfire);
        var cooking = fire.Record<SkillPracticed>();
        Assert.Null(fire.Submit(new VisitStationCommand(fire.Player, "station.hollow.cookfire")));
        Assert.Null(fire.Simulation.Submit(new GrantItem("item.material.raw_meat", 1, Quality.Standard)));
        Assert.Null(fire.Submit(new CraftCommand(fire.Player, "recipe.cooking.roast_meat")));
        fire.Tick();
        Assert.Equal(new[] { Cooking }, cooking.Select(p => p.SkillId));

        // Splitting an ash haft into shafts is woodworking's work, and Kera's teaching is how a bench learns it.
        var bench = At(session, AtWorkbench, Taught);
        var working = bench.Record<SkillPracticed>();
        Assert.Null(bench.Simulation.Submit(new GrantItem("item.material.ash_haft", 1, Quality.Standard)));
        Assert.Null(bench.Simulation.Submit(new GrantItem("item.material.feathers", 2, Quality.Standard)));
        Assert.Null(bench.Submit(new CraftCommand(bench.Player, Arrows)));
        bench.Tick();
        Assert.Equal(new[] { Woodworking }, working.Select(p => p.SkillId));

        static PlayerRecord Taught(PlayerRecord r)
        {
            var usual = Arena.Usual(r);
            return usual.WithProgression(usual.Progression with
            {
                Known = usual.Progression.Known.SetItems(new[]
                {
                    KeyValuePair.Create(Arrows, new KnownTechnique(LearningSource.Teacher, Kera, 0)),
                }),
            });
        }
    }
}
