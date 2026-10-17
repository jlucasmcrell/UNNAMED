using UNNAMED.Domain.Companions;
using UNNAMED.Domain.Social;

namespace UNNAMED.Content.Tests;

/// <summary>
/// WP12b from content: the town roles and what they hire out as, the trips, and the lints - NPL005 (roles: defined, a trader is a vendor,
/// a quest's giver a quest giver, neither hires on, speaker types the chatter schema knows), NPL006 (a trip's places stood at and reached)
/// and NPL007 (whoever can join or be hired has a routine to walk home to).
/// </summary>
public class NpcLifeLinksContentTests
{
    private static ContentLoader Load(string root)
    {
        var loader = new ContentLoader();
        loader.LoadAll(root);
        return loader;
    }

    /// <summary>A copy of the game's content with one file edited, in a directory removed afterwards.</summary>
    private sealed class EditedContent : IDisposable
    {
        public EditedContent(string relativePath, string find, string replace)
        {
            string source = Path.Combine(RepoPaths.Root(), "content");
            foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                string target = Path.Combine(Root, Path.GetRelativePath(source, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target);
            }
            string path = Path.Combine(Root, relativePath);
            string text = File.ReadAllText(path).Replace("\r\n", "\n");
            Assert.Contains(find, text);
            File.WriteAllText(path, text.Replace(find, replace));
        }

        public string Root { get; } = Path.Combine(Path.GetTempPath(), "unnamed-npl-" + Guid.NewGuid().ToString("N"));

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }

    private static void AssertRefused(string code, string relativePath, string find, string replace, string mentions)
    {
        using var content = new EditedContent(relativePath, find, replace);
        var loader = Load(content.Root);
        Assert.Contains(loader.Errors, e => e.Code == code && e.Message.Contains(mentions, StringComparison.Ordinal));
    }

    [Fact]
    public void TheRoles_DeriveWhoHiresOn_AndTheTripsNameOnlyWhatContentNames()
    {
        var loader = Load(Path.Combine(RepoPaths.Root(), "content"));
        Assert.Empty(loader.Errors);
        var life = NpcLifeContent.Build(loader, "region.starting_frontier");

        Assert.Empty(life.Roles[NpcRoleKeys.Vendor].HiresAs);
        Assert.Empty(life.Roles[NpcRoleKeys.QuestGiver].HiresAs);
        Assert.NotNull(life.ResidentBody);
        Assert.InRange(life.TripLeadMinutes, 20, 60);
        // Every resident hires out as something; no named NPC does (vendors, quest givers, a steward, a companion).
        Assert.All(life.Routines.Values.Where(r => r.IsSlot), r => Assert.NotEmpty(NpcHireability.HiresAs(r.Roles, life.Roles)));
        Assert.All(life.Routines.Values.Where(r => !r.IsSlot), r => Assert.Empty(NpcHireability.HiresAs(r.Roles, life.Roles)));
        Assert.Contains(HirelingRole.Porter, NpcHireability.HiresAs(life.Routines["routine.ashen_hollow.porter"].Roles, life.Roles));

        var trips = life.Routines.Values.Where(r => r.Trip is not null).ToList();
        Assert.True(trips.Count >= 4);
        Assert.All(trips, r => Assert.Null(r.Trip!.Problem()));
        var hunt = life.Routines["routine.ashen_hollow.hunter"].Trip!;
        Assert.Equal(("hunt", "Grey Wolf", "Ashen Hollow Waystation", "The Wolves' Den"), (hunt.Purpose, hunt.PurposeName, hunt.MeetName, hunt.DestinationName));
    }

    [Fact]
    public void ATraderWhoIsNotAVendor_IsRefused() =>
        AssertRefused("NPL005", "routines/ashen_hollow/kera_voss.yaml", "roles: [smith, vendor, quest_giver]", "roles: [smith, quest_giver]", "trades, so is a vendor");

    [Fact]
    public void AQuestGiverWhoIsNotOne_IsRefused() =>
        AssertRefused("NPL005", "routines/ashen_hollow/renn_vale.yaml", "roles: [steward, quest_giver]", "roles: [steward]", "gives a quest, so is a quest_giver");

    [Fact]
    public void ARoleNobodyDefined_IsRefused() =>
        AssertRefused("NPL005", "routines/ashen_hollow/porter.yaml", "roles: [porter]", "roles: [juggler]", "role 'juggler'");

    [Fact]
    public void ARoleThatTalksAsNoSpeakerType_IsRefused() =>
        AssertRefused("NPL005", "config/npc_life.yaml", "porter:        { hires_as: [porter], speaker: labourer }",
            "porter:        { hires_as: [porter], speaker: juggler }", "not a chatter speaker type");

    [Fact]
    public void AVendorThatHiresOut_IsRefused() =>
        AssertRefused("NPL001", "config/npc_life.yaml", "vendor:        { speaker: villager }", "vendor:        { hires_as: [guard], speaker: villager }", "never hires out");

    [Fact]
    public void ATripToSomewhereNoBodyCanStand_IsRefused() =>
        AssertRefused("NPL006", "routines/ashen_hollow/hunter.yaml", "destination: { position_m: [508, 770], facing_deg: 45 }",
            "destination: { position_m: [5, 5], facing_deg: 45 }", "trip's destination");

    [Fact]
    public void ATripThatLeavesTooSoonToBePosted_IsRefused()
    {
        AssertRefused("NPL001", "config/npc_life.yaml", "trip_lead_minutes: 40", "trip_lead_minutes: 10", "trip_lead_minutes");
        AssertRefused("NPL001", "routines/ashen_hollow/hunter.yaml", "away_hours: 4", "away_hours: 40", "away");
    }

    [Fact]
    public void SomeoneWhoCanJoin_WithNoRoutineToGoHomeTo_IsRefused()
    {
        using var content = new EditedContent("routines/ashen_hollow/tavar_orr.yaml", "npc_ref: npc.ashen_hollow.tavar_orr", "npc_ref: npc.ashen_hollow.tavar_orr_gone");
        File.Delete(Path.Combine(content.Root, "routines", "ashen_hollow", "tavar_orr.yaml"));
        var loader = Load(content.Root);
        Assert.Contains(loader.Errors, e => e.Code == "NPL007" && e.Message.Contains("npc.ashen_hollow.tavar_orr", StringComparison.Ordinal));
    }
}
