// UNNAMED Content - D23 S6: the stealth routes are placed, and the hook rows that audit them are real
// No Godot references - pure C#

using UNNAMED.Content;
using UNNAMED.Domain.Combat;
using Xunit;

namespace UNNAMED.Content.Tests;

/// <summary>
/// S6 of D23 (canon/STEALTH.md sections 8.1-8.4): every build-2 area carries the authored ways through it that are not a fight - a shadow line, a
/// grass line, a ledge, roof or vent way, a disguise - with the cover, the lights and the prices canon asks for. The hook rows in
/// content/_world_hooks.yaml read those same sections, so a route that is deleted in content is a MISSING hook on the next audit, not a silent hole.
/// </summary>
public class StealthRouteTests
{
    private static ContentLoader Content()
    {
        var loader = new ContentLoader();
        loader.LoadAll(Path.Combine(RepoPaths.Root(), "content"));
        return loader;
    }

    /// <summary>Every area a build-2 dossier audits. A new area must bring its routes, or this list is where it says so.</summary>
    private static readonly string[] Areas =
    {
        "region.ashen_hollow", "region.starting_frontier", "region.sedge_levels",
        "region.den_gorge_cave", "region.barrowworks",
    };

    [Fact]
    public void EveryBuildTwoArea_HasThreeRoutes_AShadowLine_Cover_AndALightItCanPutOut()
    {
        var loader = Content();
        foreach (string region in Areas)
        {
            var layout = WorldContent.BuildLayout(loader, region);

            Assert.True(layout.Routes.Length >= 3, $"{region} has {layout.Routes.Length} routes");
            Assert.Contains(layout.Routes, r => r.Kind == "shadow");
            Assert.True(layout.Covers.Length >= 2, $"{region} has {layout.Covers.Length} covers");
            Assert.Contains(layout.Lights, l => l.Dousable);

            foreach (var route in layout.Routes)
            {
                Assert.Contains(route.Kind, StealthRouteSite.Kinds);
                Assert.InRange(route.CoverPercent, 0, 100);
                Assert.True(route.XMm != route.ToXMm || route.ZMm != route.ToZMm, $"{route.Key} goes nowhere");
            }
        }
    }

    [Fact]
    public void ARouteThatAsksForASkill_SaysWhichAndHowMuch()
    {
        var loader = Content();
        var priced = Areas.Select(r => WorldContent.BuildLayout(loader, r))
            .SelectMany(l => l.Routes)
            .Where(r => r.SkillId is not null)
            .ToArray();

        // Canon's routes have a price (STEALTH.md 8.1): the ledge, the vent, the wall. If none is priced, the routes are free and the choice is not one.
        Assert.NotEmpty(priced);
        foreach (var route in priced)
        {
            Assert.StartsWith("skill.", route.SkillId);
            Assert.True(route.SkillMin >= 1);
        }
    }

    [Fact]
    public void APerch_SeesAtLeastFortyMetres()
    {
        var loader = Content();
        var perches = Areas.Select(r => WorldContent.BuildLayout(loader, r))
            .SelectMany(l => l.Routes)
            .Where(r => r.SightlineMm > 0)
            .ToArray();

        // HOOK.STEALTH.PERCH's own rule: an elevated, shaded, braced perch with a sightline to a target zone of at least 40 m (STEALTH.md 8.4).
        Assert.NotEmpty(perches);
        Assert.All(perches, p => Assert.True(p.SightlineMm >= 40_000, $"{p.Key} sees only {p.SightlineMm} mm"));
    }
}
