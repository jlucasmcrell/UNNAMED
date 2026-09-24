using System.Collections.Immutable;
using UNNAMED.Domain.Progression;
using UNNAMED.Persistence;
using UNNAMED.World;
using UNNAMED.World.Runtime;

namespace UNNAMED.Application.Tests;

/// <summary>
/// The Phase-1 technical audit, H-02: events reach their subscribers - presentation, harnesses - while the tick that published them is
/// still running. A subscriber that throws used to escape the tick half done, and the next frame ran the same tick again: Strain charged
/// twice, a death's XP debt owed twice. The session's bus isolates a subscriber that throws and reports it; the tick completes, once.
/// </summary>
public class ObserverTests
{
    private const string Bolt = "spell.force.impulse_bolt";

    private static GameSession Playing(TempProfile profile, (double X, double Z) at, int facingDeg, Func<PlayerRecord, PlayerRecord> change) =>
        Harness.Playing(profile, at, facingDeg, change);

    [Fact]
    public void AShotLoosedSubscriberThatThrows_NeitherRepeatsTheTick_NorChargesStrainTwice()
    {
        using var profile = new TempProfile();
        var session = Playing(profile, (120, 60), 0, r => r.WithProgression(r.Progression with
        {
            Known = r.Progression.Known.SetItem(Bolt, new KnownTechnique(LearningSource.Book, "item.tome.resonance_primer", 0)),
            Skills = r.Progression.Skills.SetItem("skill.force", new SkillState(5, 0)),
        }));
        var simulation = session.Simulation!;
        var failed = new List<object>();
        session.SubscriberFailed += (_, e) => failed.Add(e);
        var loosed = new List<ShotLoosed>();
        session.Subscribe<ShotLoosed>(loosed.Add);
        session.Subscribe<ShotLoosed>(_ => throw new FormatException("a flipbook grid of 0 columns"));
        var cast = new List<CastCompleted>();
        session.Subscribe<CastCompleted>(cast.Add);

        long start = simulation.WorldTick;
        var formula = session.Setup.Magic.Formulas[Bolt];
        int frames = formula.CastTicks + formula.RecoveryTicks + 10;
        Assert.Equal(0, simulation.Combat.Strain);
        session.Submit(new CastCommand(simulation.PlayerId, Bolt));
        for (int i = 1; i <= frames; i++)
        {
            Assert.Equal(1, session.Frame(session.TickSeconds).TicksRun);
            Assert.Equal(start + i, simulation.WorldTick);
            // Charged once, in the tick the bolt flew: the same tick is never run again.
            if (loosed.Count == 1 && loosed[0].Tick == simulation.WorldTick)
                Assert.Equal(Assert.Single(cast).Strain, simulation.Combat.Strain);
            Assert.True(simulation.Combat.Strain <= formula.StrainCost, $"Strain {simulation.Combat.Strain} after frame {i}");
        }

        Assert.Single(cast);
        Assert.Single(loosed);
        Assert.Equal(new object[] { loosed[0] }, failed);
        Assert.Equal(1, session.SubscriberFailures);
    }

    [Fact]
    public void APlayerDiedSubscriberThatThrows_DoesNotOweTheDebtTwice()
    {
        using var profile = new TempProfile();
        // Three health, at the mouth of the wolves' den.
        var session = Playing(profile, (112, 176), 0, r => r.WithProgression(r.Progression with { Pools = r.Progression.Pools with { Health = 3 } }));
        var simulation = session.Simulation!;
        var died = new List<PlayerDied>();
        session.Subscribe<PlayerDied>(died.Add);
        session.Subscribe<PlayerDied>(_ => throw new InvalidOperationException("the death screen's art is malformed"));

        long start = simulation.WorldTick;
        int frames = 0;
        for (; frames < 2_000 && died.Count == 0; frames++)
            session.Frame(session.TickSeconds);
        var death = Assert.Single(died);
        for (int i = 0; i < 400; i++, frames++)
            session.Frame(session.TickSeconds);

        Assert.Equal(start + frames, simulation.WorldTick);
        Assert.Single(died);
        var rules = session.Setup.Progression;
        long owed = (long)Math.Round(rules.Curve.ToReach(2) * rules.Guards.DebtFraction, MidpointRounding.AwayFromZero);
        Assert.Equal((owed, owed), (death.DebtAdded, simulation.Player.Progression.XpDebt));
        Assert.Equal(1, session.SubscriberFailures);
    }

    [Fact]
    public void ABareBus_StillLetsASubscribersExceptionThrough()
    {
        var bus = new EventBus();
        bus.Subscribe<string>(_ => throw new InvalidOperationException("an assertion in a test's handler"));
        Assert.Throws<InvalidOperationException>(() => bus.Publish("event"));
    }

    [Fact]
    public void AnIsolatingBus_ReachesEverySubscriber_PastOneThatThrows()
    {
        var failures = new List<(Exception, object)>();
        var bus = new EventBus((e, evt) => failures.Add((e, evt)));
        var reached = new List<string>();
        bus.Subscribe<string>(e => reached.Add("first " + e));
        bus.Subscribe<string>(_ => throw new InvalidOperationException("broken"));
        bus.Subscribe<string>(e => reached.Add("third " + e));

        bus.Publish("event");

        Assert.Equal(new[] { "first event", "third event" }, reached);
        var (exception, @event) = Assert.Single(failures);
        Assert.Equal(("broken", "event"), (exception.Message, (string)@event));
    }
}
