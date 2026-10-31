using UNNAMED.Domain;
using UNNAMED.Domain.Creatures;
using UNNAMED.Domain.Spatial;
using UNNAMED.World;
using UNNAMED.World.Runtime;
using UNNAMED.World.Runtime;

namespace UNNAMED.Application.Tests;

/// <summary>
/// S1's HUD can say WHAT gave the character away (STEALTH.md §2.7a), from the creature's own record rather than from guessing at the geometry: an eye
/// reports sight, an ear reports sound, a body reports a sign, a nose reports scent. The senses compete - a wolf that can see the character reports
/// sight even when it can also smell them - so this pins the precedence as well as the field.
/// </summary>
public class NoticeKindTests
{
    private const string Wolf = "creature.beast.wolf_grey";

    /// <summary>A wolf that can see the character reports sight, and it says so on the view the HUD reads.</summary>
    [Fact]
    public void AWolfThatSeesTheCharacter_ReportsSight()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        const double px = 470, pz = 745;
        var arena = Arena.OpenCreatures(session, session.Setup, (px, pz), 0, new[] { (Wolf, px, pz + 10, "pack_hunter") },
            null, startTick: 40_000, origin: (0, 0));
        arena.Tick(200);
        NoticeKind? notice = null;
        for (int i = 0; i < 120 && notice is null; i++)
        {
            arena.Tick(1);
            if (arena.Creature().Awareness > 0)
                notice = arena.Creature().LastNotice;
        }
        Assert.Equal(NoticeKind.Sight, notice);
    }

    /// <summary>A footfall the wolf cannot see reports sound: what the ear is for.</summary>
    [Fact]
    public void AWolfThatHearsAFootfall_ReportsSound()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        const double px = 470, pz = 745;
        var arena = Arena.OpenCreatures(session, session.Setup, (px, pz), 0, new[] { (Wolf, px, pz - 10, "pack_hunter") },
            null, startTick: 40_000, origin: (0, 0));
        arena.Tick(200);
        // It is behind the animal, so the ear is what finds it (STEALTH §3): a sprint is a 16 m ring and the wolf is ten metres off.
        arena.Submit(new MoveCommand(arena.Player, new MoveIntent(1_000, 0, UNNAMED.Domain.Spatial.Gait.Sprint, 0)));
        arena.Tick(2);
        Assert.Equal(NoticeKind.Sound, arena.Creature().LastNotice);
        arena.Submit(new MoveCommand(arena.Player, MoveIntent.Idle(0)));
    }
}