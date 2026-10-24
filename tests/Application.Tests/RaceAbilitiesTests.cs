using UNNAMED.Domain;
using UNNAMED.Domain.Character;
using UNNAMED.Domain.Progression;
using UNNAMED.Domain.Spatial;
using UNNAMED.Persistence;
using UNNAMED.World;
using UNNAMED.World.Runtime;

namespace UNNAMED.Application.Tests;

/// <summary>
/// The four starting peoples in play (owner, 2026-10-24; RACE_ABILITIES_PROPOSAL.md): each is made with its people's creation grant and
/// biology, the Kal leap and glide on their wings, the Orenth blink, wraith-step and recall and pay the hidden toll for it, the Siann's
/// Kindled Ring eases a working, and a save keeps the crossing record and the origin.
/// </summary>
public class RaceAbilitiesTests
{
    private static readonly (double X, double Z) Open = (167, 120);

    private static Arena As(GameSession session, string people, (double X, double Z)? at = null, Func<PlayerRecord, PlayerRecord>? also = null) =>
        Arena.OpenCreatures(session, session.Setup, at ?? Open, 0, Array.Empty<(string, double, double, string)>(),
            r => (also ?? (x => x))(r with { Identity = CharacterIdentity.BeforeTheCreator with { PeopleId = people } }));

    [Theory]
    [InlineData("people.veth", "body.veth_m.average")]
    [InlineData("people.kal", "body.kal_m.average")]
    [InlineData("people.siann", "body.siann_f.average")]
    [InlineData("people.orenth", "body.orenth_f.average")]
    public void EachStartingPeople_IsMadeWithItsGrant_ItsBiology_AndItsAbilitiesOnTheBar(string people, string body)
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var rules = session.Setup.Progression;
        var traits = session.Characters.TraitsOf(people);
        var simulation = session.NewGame(new CharacterSpec("Wenna Lind", Pronouns.She, people, body, null), seed: 42);
        Harness.Ticks(session, 2);
        var record = simulation.CaptureRecord();

        Assert.Equal(people, record.Identity.PeopleId);
        Assert.Equal(CharacterSpec.DefaultOrigin, record.Identity.OriginId);
        var grants = record.Progression.Grants.Where(g => g.Source == GrantSource.Race).ToList();
        Assert.Equal(2, grants.Sum(g => g.Amount));   // every people nets the same
        foreach (var (attribute, points) in traits.AttributeGrant)
            Assert.Equal(rules.AttributeBase + points + (attribute == traits.FreeAttributeDefault ? traits.FreeAttributePoints : 0),
                ProgressionEngine.AttributeValue(record.Progression, attribute, rules));
        var plain = ProgressionEngine.Derive(record.Progression, rules);
        Assert.Equal(plain.HealthMax * traits.HealthPercent / 100, simulation.Player.Stats.HealthMax);
        Assert.Equal(plain.Resonance * traits.ResonancePercent / 100, simulation.Player.Stats.Resonance);
        Assert.Equal(session.Setup.Movement.BaseSpeedMmPerSecond * traits.SpeedPercent / 100, simulation.PlayerMovement.BaseSpeedMmPerSecond);
        // Abilities used from a key are on the bar from the start; the wings are in the jump, and passives are always on.
        var keyed = simulation.PlayerAbilities.Where(a => !a.Passive && a.Action is not (PeopleAction.Glide or PeopleAction.Flare or PeopleAction.Leap)).Select(a => a.Id);
        Assert.All(keyed, id => Assert.Contains(id, simulation.ActionBars[0]));
    }

    [Fact]
    public void AKal_SprintJumpLeapsFurther_ForItsStamina_AndHeldInTheAirGlides()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);

        double Leap(string people, out Posture airborne, out int stamina)
        {
            var arena = As(session, people);
            var start = arena.At;
            arena.Simulation.Enqueue(new MoveCommand(arena.Player, new MoveIntent(0, 1000, Gait.Sprint, 0)));
            arena.Tick(4);
            arena.Simulation.Enqueue(new JumpCommand(arena.Player));
            arena.Tick();
            airborne = arena.Simulation.Posture;
            stamina = arena.Simulation.Player.Progression.Pools.Stamina ?? (int)arena.Simulation.Player.Stats.StaminaMax;
            for (int i = 0; i < 40 && arena.Simulation.Posture.Airborne; i++)
                arena.Tick();
            return arena.At.Z - start.Z;
        }

        double veth = Leap("people.veth", out var vethAir, out _);
        double kal = Leap("people.kal", out var kalAir, out int kalStamina);
        Assert.Equal(WingState.Folded, vethAir.Wing);
        Assert.Equal(WingState.Leap, kalAir.Wing);
        Assert.True(kal > veth, $"the Kal's gap leap carried {kal:0.00} m, the Veth's jump {veth:0.00} m");
        Assert.True(kalStamina < 100, $"the leap costs stamina ({kalStamina} left)");

        var glider = As(session, "people.kal");
        Assert.Equal("on the ground", glider.Submit(new GlideCommand(glider.Player, true)));
        glider.Submit(new JumpCommand(glider.Player));
        glider.Tick(6);
        Assert.Null(glider.Submit(new GlideCommand(glider.Player, true)));
        Assert.True(glider.Simulation.Posture.Aloft);
        Assert.Equal(WingState.Glide, glider.Simulation.Posture.Wing);
        Assert.Null(glider.Submit(new CrouchCommand(glider.Player, true)));
        Assert.Equal(WingState.Flare, glider.Simulation.Posture.Wing);
        for (int i = 0; i < 200 && glider.Simulation.Posture.Airborne; i++)
            glider.Tick();
        Assert.False(glider.Simulation.Posture.Airborne);
        Assert.Equal(WingState.Folded, glider.Simulation.Posture.Wing);

        var veth2 = As(session, "people.veth");
        veth2.Submit(new JumpCommand(veth2.Player));
        veth2.Tick(6);
        Assert.Equal("no wings to spread", veth2.Submit(new GlideCommand(veth2.Player, true)));
    }

    [Fact]
    public void AnOrenth_Blinks_WraithSteps_AndRecalls_AndEachCrossingPaysTheHiddenToll()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = As(session, "people.orenth");
        var blinked = arena.Record<Blinked>();
        var start = arena.Simulation.Player.Body;

        Assert.Null(arena.Submit(new PeopleAbilityCommand(arena.Player, "ability.people.orenth.blink")));
        arena.Tick();
        var to = arena.Simulation.Player.Body;
        double moved = Math.Sqrt(Math.Pow(to.XMm - start.XMm, 2) + Math.Pow(to.ZMm - start.ZMm, 2));
        Assert.InRange(moved, 1000, 8000);
        Assert.Single(blinked);
        Assert.Equal(1, arena.Simulation.CaptureRecord().Crossing.Toll);
        Assert.StartsWith("Blink is not ready", arena.Submit(new PeopleAbilityCommand(arena.Player, "ability.people.orenth.blink")));

        Assert.Null(arena.Submit(new PeopleAbilityCommand(arena.Player, "ability.people.orenth.wraith_step")));
        arena.Tick();
        Assert.Equal(4, arena.Simulation.CaptureRecord().Crossing.Toll);

        Assert.Equal("no anchor to cross back to", arena.Submit(new PeopleAbilityCommand(arena.Player, "ability.people.orenth.anchor_recall")));
        Assert.Null(arena.Submit(new PeopleAbilityCommand(arena.Player, "ability.people.orenth.planar_anchor")));
        var anchor = arena.Simulation.Player.Body;
        for (int i = 0; i < 40; i++)
        {
            arena.Simulation.Enqueue(new MoveCommand(arena.Player, new MoveIntent(0, -1000, Gait.Run, 180_000)));
            arena.Tick();
        }
        arena.Simulation.Enqueue(new MoveCommand(arena.Player, MoveIntent.Idle(180_000)));
        arena.Tick();
        Assert.Null(arena.Submit(new PeopleAbilityCommand(arena.Player, "ability.people.orenth.anchor_recall")));
        arena.Tick(70);
        var back = arena.Simulation.Player.Body;
        Assert.Equal((anchor.XMm, anchor.ZMm), (back.XMm, back.ZMm));
        Assert.Equal(12, arena.Simulation.CaptureRecord().Crossing.Toll);
        // Nothing of the toll is a skill, a level or an attribute.
        Assert.Equal(1, arena.Simulation.Player.Progression.Level);
    }

    [Fact]
    public void AVeth_CannotUseAnotherPeoplesAbility()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var arena = As(session, "people.veth");
        Assert.Equal("ability.people.orenth.blink is not an ability of people.veth",
            arena.Submit(new PeopleAbilityCommand(arena.Player, "ability.people.orenth.blink")));
    }

    [Fact]
    public void ASiannsKindledRing_EasesAWorkingsStrain()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        const string Ward = "spell.warding.brace_ward";
        PlayerRecord Knowing(PlayerRecord r) => r.WithProgression(ProgressionEngine.Learn(r.Progression, new TechniqueLearning(Ward, LearningSource.Book, 0)).Progression);

        int Strain(bool ring)
        {
            var arena = As(session, "people.siann", also: Knowing);
            var done = arena.Record<CastCompleted>();
            if (ring)
            {
                Assert.Null(arena.Submit(new PeopleAbilityCommand(arena.Player, "ability.people.siann.kindled_ring")));
                arena.Tick();
            }
            Assert.Null(arena.Submit(new CastCommand(arena.Player, Ward)));
            arena.Tick(30);
            return Assert.Single(done).Strain;
        }

        Assert.True(Strain(ring: true) < Strain(ring: false));
    }

    [Fact]
    public void ASave_KeepsTheCrossingRecord_AndTheOrigin()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var simulation = session.NewGame(new CharacterSpec("Rhun Tallis", Pronouns.He, "people.orenth", "body.orenth_m.average", null), seed: 42);
        Harness.Ticks(session, 1);
        session.Submit(new PeopleAbilityCommand(simulation.PlayerId, "ability.people.orenth.planar_anchor"));
        session.Submit(new PeopleAbilityCommand(simulation.PlayerId, "ability.people.orenth.wraith_step"));
        Harness.Ticks(session, 2);
        var before = simulation.CaptureRecord();
        Assert.Equal(3, before.Crossing.Toll);
        Assert.NotNull(before.Crossing.Anchor);

        session.Save(SaveSlots.Quick);
        Assert.True(session.Load(SaveSlots.Quick).IsComplete);
        var after = session.Simulation!.CaptureRecord();
        Assert.Equal(before.Crossing.Toll, after.Crossing.Toll);
        Assert.Equal(before.Crossing.Anchor, after.Crossing.Anchor);
        Assert.Equal(CharacterSpec.DefaultOrigin, after.Identity.OriginId);
        Assert.Equal(before.Digest, after.Digest);
    }
}
