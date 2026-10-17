using UNNAMED.Domain.Character;
using UNNAMED.Domain.Items;
using UNNAMED.Domain.Progression;
using UNNAMED.Persistence;

namespace UNNAMED.Application.Tests;

/// <summary>
/// WP2's cloud part (SYSTEMS_COMPLETENESS_PLAN_20261017.md §2.2): a new game is made from the creator's spec. Each of the four CC-0 Calls
/// adds its attribute bias, its two skills at 15, its kit over the common starting kit and its contact; the creator offers the Veth with
/// canon names to Suggest; and every character's saves go to a folder of their own (OD-14).
/// </summary>
public class CharacterCreationTests
{
    public static readonly TheoryData<string> Calls = new() { "call.wayfarer", "call.scholar", "call.smiths_hand", "call.hedge_healer" };

    [Fact]
    public void TheCreator_OffersTheVeth_AndTheFourCalls()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);

        Assert.Equal(new[] { "people.veth" }, session.Characters.PlayablePeoples.Select(p => p.Id));
        Assert.Equal(new[] { "call.hedge_healer", "call.scholar", "call.smiths_hand", "call.wayfarer" }, session.Characters.Calls.Keys);
        Assert.Equal(new[] { "Hedge healer", "Scholar", "Smith's hand", "Wayfarer" }, session.Characters.Calls.Keys.Select(session.DisplayName));
    }

    [Theory]
    [MemberData(nameof(Calls))]
    public void ANewGame_WithEachCall_StartsWithItsBias_ItsSkills_ItsKit_AndItsContact(string callId)
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var call = session.Characters.Calls[callId];
        var rules = session.Setup.Progression;
        var spec = new CharacterSpec("  Wenna   Lind ", Pronouns.She, "people.veth", "body.veth_f.lean", callId);

        var simulation = session.NewGame(spec, seed: 42);
        var record = simulation.CaptureRecord();
        using var other = new TempProfile();
        var plain = Harness.Boot(other).NewGame(Harness.Named("Wanderer"), seed: 42).CaptureRecord();

        Assert.Equal("Wenna Lind", record.Name);
        Assert.Equal(new CharacterIdentity(Pronouns.She, "people.veth", "body.veth_f.lean", callId), record.Identity);
        Assert.Equal(record.Identity, simulation.Player.Identity);   // what presentation draws and words the character by
        // Attributes: the bias over the base, as creation grants - outside the cap on later grants.
        Assert.Equal(CallDefinition.BiasPoints, call.Package.AttributeBias.Values.Sum());
        foreach (var attribute in ProgressionKeys.Attributes)
            Assert.Equal(rules.AttributeBase + call.Package.AttributeBias.GetValueOrDefault(attribute),
                ProgressionEngine.AttributeValue(record.Progression, attribute, rules));
        Assert.All(record.Progression.Grants, g => Assert.Equal(GrantSource.Creation, g.Source));
        // Skills: two at 15, and nothing else started.
        Assert.Equal(call.Package.Skills.Keys, record.Progression.Skills.Where(s => s.Value.Level > 0).Select(s => s.Key));
        Assert.All(call.Package.Skills.Keys, skill => Assert.Equal(CallDefinition.SkillLevel, record.Progression.Skills[skill].Level));
        Assert.Empty(record.Progression.Known);   // a Call teaches nothing: formulas and recipes come from people and books
        // Kit: the common kit, plus the Call's, worn where it says.
        var expected = plain.Inventory.Select(e => (e.DefId, e.Count)).Concat(call.Kit.Select(k => (k.ItemId, k.Count))).OrderBy(e => e.Item1).ThenBy(e => e.Item2);
        Assert.Equal(expected, record.Inventory.Select(e => (e.DefId, e.Count)).OrderBy(e => e.DefId).ThenBy(e => e.Count));
        var worn = record.Equipment.Values.Select(id => record.Inventory.Single(e => e.ItemId == id).DefId).Order(StringComparer.Ordinal);
        var wear = plain.Equipment.Values.Select(id => plain.Inventory.Single(e => e.ItemId == id).DefId)
            .Concat(call.Kit.Where(k => k.Equip).Select(k => k.ItemId)).Order(StringComparer.Ordinal);
        Assert.Equal(wear, worn);
        // Contact: the one NPC who already trusts the character a little.
        var contact = Assert.Single(record.Relationships);
        Assert.Equal((call.Contact.NpcId, "trust", call.Contact.Trust), (contact.NpcId, contact.Dimension, contact.Value));
        // And the world runs: the character is a body at the spawn like any other.
        Harness.Ticks(session, 20);
        Assert.Equal(20, simulation.WorldTick);
    }

    [Theory]
    [MemberData(nameof(Calls))]
    public void ACharacterFromTheCreator_RoundTripsThroughASave_IdentityAndAll(string callId)
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        var spec = new CharacterSpec("Corin Vessmoor", Pronouns.He, "people.veth", "body.veth_m.heavy", callId);
        var simulation = session.NewGame(spec, seed: 42);
        Harness.Ticks(session, 40);
        var before = simulation.CaptureRecord();
        string digest = simulation.StateDigest();
        session.Save(SaveSlots.Quick);

        var again = Harness.Boot(profile);
        var loaded = again.Load(SaveSlots.Quick);

        Assert.True(loaded.IsComplete);
        Assert.Equal(new CharacterIdentity(Pronouns.He, "people.veth", "body.veth_m.heavy", callId), loaded.Player.Identity);
        Assert.Equal(before.Digest, loaded.Player.Digest);
        Assert.Equal(digest, again.Simulation!.StateDigest());
        Assert.Equal(new CharacterCard(again.Character!, "Corin Vessmoor", "people.veth", callId), CharacterFolders.ReadCard(again.SaveFolder));
    }

    [Fact]
    public void ANewGame_WithNoCall_IsTheCharacterEveryGameMadeBeforeTheCreator()
    {
        using var profile = new TempProfile();
        var record = Harness.Boot(profile).NewGame(Harness.Named("Wanderer"), seed: 42).CaptureRecord();

        Assert.Equal("Wanderer", record.Name);
        Assert.Equal(CharacterIdentity.BeforeTheCreator, record.Identity);
        Assert.Empty(record.Progression.Grants);
        Assert.Empty(record.Relationships);
        Assert.Equal(new[] { "item.consumable.salve_minor", "item.story.folded_note", "item.tool.water_flask", "item.weapon.rusted_sword" },
            record.Inventory.Select(e => e.DefId).Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Twenty-five characters ab")]
    [InlineData("Tab\there")]
    public void ANameThatCannotBeACharactersName_IsRefused_AndNoGameStarts(string name)
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);

        Assert.Throws<ArgumentException>(() => session.NewGame(CharacterSpec.Quick(name, "call.wayfarer")));
        Assert.Null(session.Simulation);
        Assert.Empty(CharacterFolders.List(profile.Root));
    }

    [Fact]
    public void ANameOfTwentyFourCharacters_IsAccepted()
    {
        using var profile = new TempProfile();
        var name = new string('a', CharacterSpec.MaxNameLength);
        Assert.Equal(name, Harness.Boot(profile).NewGame(CharacterSpec.Quick(name, null), seed: 1).Player.Name);
    }

    [Theory]
    [InlineData("people.veth", "call.unknown")]
    [InlineData("people.kal", "call.wayfarer")]
    [InlineData("people.veth", "item.weapon.rusted_sword")]
    public void ASpecNamingWhatTheCreatorDoesNotOffer_IsRefused(string people, string call)
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);

        Assert.Throws<ArgumentException>(() => session.NewGame(new CharacterSpec("Wenna Lind", Pronouns.They, people, CharacterSpec.DefaultBodyPreset, call)));
        Assert.Null(session.Simulation);
    }

    [Theory]
    [InlineData("body.build_slender")]
    [InlineData("body.veth_x.lean")]
    [InlineData("body.veth_m.portly")]
    public void ASpecNamingABodyContentDoesNotOffer_IsRefused(string body)
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);

        Assert.Throws<ArgumentException>(() => session.NewGame(new CharacterSpec("Wenna Lind", Pronouns.They, "people.veth", body, "call.wayfarer")));
        Assert.Null(session.Simulation);
    }

    [Fact]
    public void Suggest_DrawsOnlyFromTheVethList_AndAResidentsDrawNeverRepeatsANameWhileTheListLasts()
    {
        using var profile = new TempProfile();
        var names = Harness.Boot(profile).Characters.Peoples["people.veth"].Names;

        Assert.Equal(30, names.Names.Length);
        Assert.All(names.Names, n => Assert.Null(CharacterSpec.NameProblem(n)));
        Assert.Equal(names.Pick(7), names.Pick(7));
        Assert.Equal(names.Names[7], names.Pick(7 + 30));

        var taken = new HashSet<string>(StringComparer.Ordinal);
        for (ulong sample = 0; sample < (ulong)names.Names.Length; sample++)
            Assert.True(taken.Add(names.PickUnused(sample * 11, taken)!));
        Assert.Null(names.PickUnused(3, taken));
    }

    [Fact]
    public void EachCharacter_SavesToItsOwnFolder_NamedOnItsCard()
    {
        using var profile = new TempProfile();
        var session = Harness.Boot(profile);
        session.NewGame(new CharacterSpec("Wenna Lind", Pronouns.She, "people.veth", "body.default", "call.hedge_healer"), seed: 42);
        session.Save(SaveSlots.Quick);
        string wenna = session.Character!;
        session.NewGame(new CharacterSpec("Bram Hallow", Pronouns.He, "people.veth", "body.default", "call.smiths_hand"), seed: 7);
        Harness.Ticks(session, 10);   // played longer, so his is the newest save even inside one timestamp
        session.SaveInBackground(SaveSlots.Quick);
        session.WaitForSaves(TimeSpan.FromSeconds(30));
        string bram = session.Character!;

        Assert.Equal(new[] { bram, wenna }.Order(StringComparer.Ordinal), CharacterFolders.List(profile.Root).Select(id => id.Value));
        Assert.Equal(new CharacterCard(wenna, "Wenna Lind", "people.veth", "call.hedge_healer"),
            CharacterFolders.ReadCard(Path.Combine(profile.Root, CharacterFolders.Directory, wenna)));
        Assert.Equal(new CharacterCard(bram, "Bram Hallow", "people.veth", "call.smiths_hand"),
            CharacterFolders.ReadCard(session.SaveFolder));
        Assert.Empty(Directory.EnumerateDirectories(profile.Root).Select(Path.GetFileName).Where(SaveSlots.IsValid));   // nothing at the root

        var again = Harness.Boot(profile);
        Assert.Equal(bram, again.Character);   // the one Continue would load
        Assert.Equal(new[] { "Bram Hallow", "Wenna Lind" }, again.CharacterSaves().Select(c => c.Name));
        Assert.Equal("Wenna Lind", again.Load(again.CharacterSaves()[1].Saves[0]).Player.Name);
        Assert.Equal(wenna, again.Character);
    }

    [Fact]
    public void ASaveFromBeforeFolders_StaysAtTheProfileRoot_LoadsFromThere_AndItsCharacterKeepsSavingThere()
    {
        using var profile = new TempProfile();
        using var elsewhere = new TempProfile();
        var maker = Harness.Boot(elsewhere);
        var simulation = maker.NewGame(Harness.Named("Wanderer"), seed: 42);
        Harness.Ticks(maker, 30);
        new SaveStore(profile.Root).Save(SaveSlots.Quick,
            SaveDocuments.Capture(simulation.World, simulation.CaptureRecord(), maker.Content, simulation.WorldTick, maker.PlaytimeSeconds));

        var session = Harness.Boot(profile);
        var choice = session.StartChoice();
        Assert.Null(choice.Continue!.Character);
        Assert.True(session.Load(choice.Continue).IsComplete);
        Assert.Null(session.Character);
        Assert.Equal(Path.GetFullPath(profile.Root), session.SaveFolder);
        Harness.Ticks(session, 10);
        session.Save(SaveSlots.Quick);

        Assert.Empty(CharacterFolders.List(profile.Root));
        Assert.Equal(40, Assert.Single(session.CharacterSaves()).Saves.Single().WorldTick);
        Assert.Equal(new[] { 1 }, session.Backups(SaveSlots.Quick));
    }
}
