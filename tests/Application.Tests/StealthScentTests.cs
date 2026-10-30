using UNNAMED.Domain;
using UNNAMED.Domain.Creatures;
using UNNAMED.Domain.Items;
using UNNAMED.Domain.Spatial;
using UNNAMED.World;
using UNNAMED.World.Runtime;

namespace UNNAMED.Application.Tests;

/// <summary>
/// D23 S3's scent on the wind through the world (STEALTH.md section 4.6): a wolf downwind of the character smells them through the dark, and the same
/// wolf the same distance upwind does not. The wind is the world's, read from the simulation, so the test stands the animal where the air carries
/// rather than where the author hopes it does.
/// </summary>
public class StealthScentTests
{
    private const string Wolf = "creature.beast.wolf_grey";

    /// <summary>Night, so sight is 6.3 metres and the character twelve metres off is out of it; and quiet, so no footfall gives them away.</summary>
    private const long Night = 40_000;

    private static Func<PlayerRecord, PlayerRecord> Quiet => r => r;

    /// <summary>
    /// Twelve metres away, with the air running from the character to the wolf, the wolf notices. Twelve metres the other way it does not - the
    /// difference is the wind and nothing else, which is the whole of the rule.
    /// </summary>
    [Fact]
    public void AWolfDownwindOfTheCharacter_SmellsThem_TheSameWolfUpwindDoesNot()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        const double px = 470, pz = 745;
        const double metres = 12;

        // The first arena exists only to learn which way the air moves today.
        long wind = Arena.OpenCreatures(session, session.Setup, (px, pz), 0, Array.Empty<(string, double, double, string)>(),
            Quiet, startTick: Night, origin: (0, 0)).Simulation.WindBearing();
        double windRad = wind / 1000.0 * Math.PI / 180;
        double sin = Math.Sin(windRad), cos = Math.Cos(windRad);

        bool Noticed(double wolfX, double wolfZ)
        {
            var arena = Arena.OpenCreatures(session, session.Setup, (px, pz), 0, new[] { (Wolf, wolfX, wolfZ, "pack_hunter") },
                Quiet, startTick: Night, origin: (0, 0));
            arena.Tick(200);   // past the login grace
            for (int i = 0; i < 120; i++)
            {
                arena.Tick(1);
                if (arena.Creature().Awareness > 0)
                    return true;
            }
            return false;
        }

        // Downwind of the character: the wolf stands where the wind blows, so the air carries the character's scent to it.
        bool smells = Noticed(px + sin * metres, pz + cos * metres);
        // Upwind of the character: the air runs the other way, and a nose is all it has at this range in the dark.
        bool blind = Noticed(px - sin * metres, pz - cos * metres);

        // Ask the world the same questions the scent check asks, so a failure says which one answered no.
        var probe = Arena.OpenCreatures(session, session.Setup, (px, pz), 0,
            new[] { (Wolf, px + sin * metres, pz + cos * metres, "pack_hunter") }, Quiet, startTick: Night, origin: (0, 0));
        probe.Tick(200);
        var animal = probe.Creature();
        var me = probe.Simulation.Player.Body;
        string why = $"wind {wind / 1000.0:0} deg, gap {Math.Sqrt(Math.Pow(animal.Body.XMm - me.XMm, 2) + Math.Pow(animal.Body.ZMm - me.ZMm, 2)) / 1000.0:0.0} m, "
                   + $"asleep {animal.Asleep}, carries {ScentRules.Carries(wind, animal.Body.XMm, animal.Body.ZMm, me.XMm, me.ZMm)}, "
                   + $"walled {probe.Simulation.Walled(animal.Body.XMm, animal.Body.ZMm, me.XMm, me.ZMm)}, scent_mm {session.Setup.Combat.Creatures[Wolf].Senses.ScentMm}";
        Assert.True(smells, $"a wolf downwind has to smell the character at twelve metres ({why})");
        Assert.False(blind, $"the same wolf upwind has nothing to smell (wind {wind / 1000.0:0} deg)");
    }
}