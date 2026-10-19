using System.Collections.Immutable;
using System.Text.RegularExpressions;
using UNNAMED.Domain;
using UNNAMED.Domain.Character;
using UNNAMED.Domain.Combat;
using UNNAMED.Domain.Companions;
using UNNAMED.Domain.Items;
using UNNAMED.Domain.Progression;
using UNNAMED.Domain.Quests;
using UNNAMED.Persistence;
using UNNAMED.World;
using UNNAMED.World.Runtime;
using static UNNAMED.Application.Tests.Acceptance.AcceptanceReport;

namespace UNNAMED.Application.Tests.Acceptance;

public partial class AcceptanceWalkthroughTests
{
    private static readonly (double X, double Z) OpenGround = (120, 60);
    private static readonly (double X, double Z) AtKeraForge = (60.3, 140.3);
    private const string Arrows = "item.ammo.arrow_rough";
    private const string PowerStrike = "ability.blade.power_strike";

    private static Arena Open(GameSession session, IEnumerable<(string, double, double, string)> creatures, Func<PlayerRecord, PlayerRecord>? change = null) =>
        Arena.OpenCreatures(session, session.Setup, OpenGround, 0, creatures, change);

    private static PlayerRecord WithHealth(PlayerRecord r, int health) =>
        r.WithProgression(r.Progression with { Pools = r.Progression.Pools with { Health = health } });

    /// <summary>The wolf 2 m ahead, fought with whatever the character holds until it is dead or the budget runs out.</summary>
    private static (bool Dead, int Landed) FightAWolf(Arena arena, int budget = 900)
    {
        var landed = arena.Record<HitResolved>();
        arena.Fight(arena.Creature(), budget);
        return (!arena.Creature().Alive, landed.Count(h => h.Attacker == arena.Player && h.Damage > 0));
    }

    [Fact]
    public void TheSystems_SweptByTheChecklist()
    {
        using var run = new Run();
        var session = run.Session;
        var simulation = session.NewGame(Spec(call: "call.scholar"), seed: 42);

        // ── §2.5 the journal ───────────────────────────────────────────────────────
        Step("2.5", () =>
        {
            var journal = run.Sim.Journal;
            var ids = journal.Quests.Select(q => q.Id).ToList();
            That(ids.Contains("quest.main.the_road_in"), "The Road In is not in the journal");
            That(!ids.Contains("quest.main.closed_seam") && !ids.Contains("quest.main.moss_lights"), "a beat ahead is already in the journal");
            That(ids.All(id => run.Sim.Quests.Any(q => q.Id == id)), "the journal lists a quest the character has not taken up");
            run.StandBeside(Renn);
            run.Do(new TalkCommand(run.Sim.PlayerId, Renn));
            run.Do(new LeaveCommand(run.Sim.PlayerId));
            That(run.Sim.Journal.Quests.Single(q => q.Id == "quest.main.the_road_in").Objectives.Any(o => o.Status == ObjectiveStatus.Satisfied), "the journal does not record the beat just done");
            return $"{ids.Count} quest(s) in the journal, the beats ahead are not listed, and the beat just done is recorded";
        });

        // ── §6 the Learn column ────────────────────────────────────────────────────
        Step("6.1", () =>
        {
            run.Give(100);
            run.StandBeside(Kera);
            var view = run.Sim.Teaching(Kera)!;
            That(view.Available, $"Kera is not teaching: {view.Unavailable}");
            That(view.Lessons.Length >= 5, $"only {view.Lessons.Length} lessons listed");
            That(view.Lessons.All(l => l.Fee >= 0 && !l.Requirements.IsDefault), "a lesson lists no cost or requirements");
            var strike = view.Lessons.Single(l => l.DefinitionId == PowerStrike);
            That(strike.Fee > 0 && strike.Requirements.Length > 0, "the technique lesson shows no cost or requirement");
            return $"{view.Lessons.Length} lessons, each with a cost and its requirements (the technique: {strike.Fee} coin, {strike.Requirements.Length} requirement)";
        });
        Step("6.2", () =>
        {
            var view = run.Sim.Teaching(Kera)!;
            foreach (var lesson in view.Lessons)
            {
                string name = session.DisplayName(lesson.DefinitionId);
                That(name.Length > 0 && name != lesson.DefinitionId, $"{lesson.DefinitionId} has no words in the Learn column");
            }
            return "every lesson in the column is named in words, not as an id";
        });
        Step("6.4", () =>
        {
            Equal(null, run.Do(new TalkCommand(run.Sim.PlayerId, Kera)), "talking to Kera");
            string? why = run.Do(new LearnFromTeacherCommand(run.Sim.PlayerId, Kera, PowerStrike));
            That(why is not null && why.Length > 6, "a lesson whose requirement is unmet was not refused with a reason");
            That(!run.Sim.Player.Progression.Known.ContainsKey(PowerStrike), "the lesson was taught despite the refusal");
            run.Do(new LeaveCommand(run.Sim.PlayerId));
            return $"refused with \"{why}\"; nothing taught, nothing charged";
        });

        // ── §7 skills ──────────────────────────────────────────────────────────────
        Step("7.1", () =>
        {
            var rules = run.Sim.Setup.Progression;
            var skills = run.Sim.Player.Progression.Skills.Where(s => s.Value.Level > 0).ToList();
            That(skills.Count > 0, "the character has no skill at all");
            foreach (var (id, state) in skills)
            {
                That(rules.BandOf(state.Level).Name.Length > 0, $"{id} has no band word");
                That(rules.NextBand(state.Level) is not null || state.Level >= rules.SkillBands.Last().From, $"{id} shows no step to the next band");
            }
            return $"{skills.Count} skills each have a band word ({string.Join(", ", skills.Select(s => rules.BandOf(s.Value.Level).Name).Distinct())}) and a next band";
        });

        // ── §8 hiring ──────────────────────────────────────────────────────────────
        Step("8.1", () =>
        {
            var hireable = run.Sim.Npcs.Where(n => run.Sim.IsHireable(n.Id)).ToList();
            That(hireable.Any(n => n.Id.StartsWith("npc.adventurer.", StringComparison.Ordinal)), "no adventurer can be hired");
            That(hireable.Any(n => n.Id.StartsWith("routine.", StringComparison.Ordinal)), "no resident can be hired");
            var resident = hireable.First(n => n.Id == "routine.ashen_hollow.guard");
            var terms = run.Sim.HireTerms(resident.Id, HirelingRole.Guard, 2);
            That(terms.DailyWage > 0 && terms.Total == terms.DailyWage * 2 && terms.Refusal is null, $"the resident's terms are not stated: {terms}");
            return $"{hireable.Count(n => n.Id.StartsWith("routine.", StringComparison.Ordinal))} residents and {hireable.Count(n => n.Id.StartsWith("npc.adventurer.", StringComparison.Ordinal))} adventurers hireable; a guard {terms.DailyWage} a day, {terms.Total} for two days";
        });
        string? residentId = null;
        Step("8.2", () =>
        {
            residentId = "routine.ashen_hollow.guard";
            That(run.Sim.IsHireable(residentId), "the guard resident cannot be hired");
            run.Give(100);
            run.StandBeside(residentId);
            long coin = run.Sim.CaptureRecord().Currency;
            var terms = run.Sim.HireTerms(residentId, HirelingRole.Guard, 2);
            Equal(null, run.Do(new HireCommand(run.Sim.PlayerId, residentId, HirelingRole.Guard, 2, AcceptsRisk: true)), "hiring a resident");
            That(run.Sim.Companions.Any(c => c.NpcId == residentId), "the resident is not in the party");
            Equal(coin - terms.DailyWage, run.Sim.CaptureRecord().Currency, "coin after the first day's wage");
            return $"hired a resident as a guard; the first day's wage ({terms.DailyWage}) was taken and they joined the party";
        });
        Step("8.3", () =>
        {
            var adventurer = run.Sim.Adventurers.First(a => a.InTown && a.Party is null && run.Sim.IsHireable(a.Key));
            run.Give(100);
            run.StandBeside(adventurer.Key);
            Equal(null, run.Do(new HireCommand(run.Sim.PlayerId, adventurer.Key, HirelingRole.Guard, 2, AcceptsRisk: true)), "hiring an adventurer");
            var member = run.Sim.Companions.Single(c => c.NpcId == adventurer.Key);
            Equal(PartyMembership.Hireling, member.Membership, "membership");
            That(run.Sim.Adventurers.Single(a => a.Key == adventurer.Key).Party == "hireling", "the adventurers' list does not show them hired");
            adventurerId = adventurer.Key;
            return $"hired {adventurer.Name} (level {adventurer.Level}); they are in the party and the adventurers' list says so";
        });
        Step("9.1", () =>
        {
            var party = run.Sim.Companions;
            That(party.Length >= 2, $"{party.Length} in the party, expected the two hires");
            That(party.All(c => c.Name.Length > 0 && c.Standing.Length > 0 && c.Doing.Length > 0), "a member lacks a name, a condition or an order");
            return $"{party.Length} members, each with a name, a condition in words and what they are doing";
        });
        Step("9.2", () =>
        {
            Equal(null, run.Do(new OrderCompanionCommand(run.Sim.PlayerId, residentId!, CompanionOrder.Wait)), "ordering a wait");
            Equal(CompanionOrder.Wait, run.Sim.Companions.Single(c => c.NpcId == residentId).Order, "the member's order");
            Equal(null, run.Do(new OrderCompanionCommand(run.Sim.PlayerId, residentId!, CompanionOrder.Follow)), "ordering a follow");
            Equal(CompanionOrder.Follow, run.Sim.Companions.Single(c => c.NpcId == residentId).Order, "the member's order");
            return "a hired member took 'wait' then 'follow', and the party list shows each";
        });
        Step("8.5", () =>
        {
            Equal(null, run.Do(new DismissCommand(run.Sim.PlayerId, adventurerId!)), "dismissing the adventurer");
            That(run.Sim.Companions.All(c => c.NpcId != adventurerId), "a ghost is left in the party");
            That(run.Sim.Adventurers.Single(a => a.Key == adventurerId).Party is null, "the adventurer is still marked as hired");
            Equal(null, run.Do(new DismissCommand(run.Sim.PlayerId, residentId!)), "dismissing the resident");
            That(run.Sim.Companions.Length == 0, "the party is not empty after letting both go");
            return "both were let go: the party is empty and the adventurer is their own again";
        });

        // ── §10 chat ───────────────────────────────────────────────────────────────
        Step("10.4", () =>
        {
            run.Ticks(400);
            session.Save(SaveSlots.Quick);
            session.Load(SaveSlots.Quick);
            That(run.Sim.Chat.Length == 0, $"{run.Sim.Chat.Length} chat lines survive a reload");
            return "a reload starts with no chat history (only the filter setting is kept, in the client preferences)";
        });

        // ── §12 calendar and weather ───────────────────────────────────────────────
        Step("12.1", () =>
        {
            var before = run.Sim.Calendar;
            run.Sim.Advance(2_400 * 2);
            var after = run.Sim.Calendar;
            That(after != before && (after.Hour != before.Hour || after.Day != before.Day), "the calendar did not advance");
            return $"two game hours on: {before.Hour:00}:{before.Minute:00} became {after.Hour:00}:{after.Minute:00}";
        });
        Step("12.2", () =>
        {
            var weather = run.Sim.Weather;
            That(weather.FrontEndTick > weather.FrontStartTick && run.Sim.WorldTick >= weather.FrontStartTick && run.Sim.WorldTick < weather.FrontEndTick, "the weather front does not cover the present");
            return $"the sky is {weather.State} under a front that covers now";
        });

        // ── §13 reputation ─────────────────────────────────────────────────────────
        Step("13.1", () =>
        {
            var factions = run.Sim.Factions;
            That(factions.Length >= 2 && factions.All(f => f.Name.Length > 0 && f.Tier.Length > 0), "a faction has no name or standing word");
            return $"{factions.Length} factions, each named with a standing word ({string.Join(", ", factions.Select(f => f.Tier).Distinct())})";
        });

        // ── §14.1 a clean exit ─────────────────────────────────────────────────────
        Step("14.1", () =>
        {
            session.Save(SaveSlots.Quick);
            That(session.WaitForSaves(TimeSpan.FromSeconds(30)), "a save did not finish");
            That(session.SaveSlots().Any(s => s.Slot == SaveSlots.Quick && s.Problem is null), "the quick save is not a clean one");
            return "a save completes and loads clean: quitting loses nothing";
        });

        // ── §16 saves screen ───────────────────────────────────────────────────────
        Step("16.2", () =>
        {
            string first = run.Sim.Player.Name;
            session.Save(SaveSlots.Quick);
            session.NewGame(Spec("Corin Vessmoor"), seed: 7);
            session.Save(SaveSlots.Quick);
            var characters = session.CharacterSaves().Where(c => c.Character is not null).ToList();
            That(characters.Count >= 2, $"{characters.Count} characters have saves, expected two");
            That(characters.All(c => !string.IsNullOrWhiteSpace(c.Name)), "a save row does not say whose it is");
            That(characters.Select(c => c.Name).Distinct().Count() == characters.Count, "two rows carry the same name");
            return $"two characters in one profile: each row names its owner ({string.Join(", ", characters.Select(c => c.Name))})";
        });
        Step("16.4", () =>
        {
            session.Save("manual_extra");
            var store = new SaveStore(session.SaveFolder);
            That(session.SaveSlots().Any(s => s.Slot == "manual_extra"), "the extra save was not made");
            store.Delete("manual_extra");
            That(session.SaveSlots().All(s => s.Slot != "manual_extra"), "the deleted save is still listed");
            That(session.SaveSlots().Any(s => s.Slot == SaveSlots.Quick), "deleting one save touched another");
            return "deleted one save; the others are untouched";
        });

        // ── §17, §18 credits and the disclosure ────────────────────────────────────
        string notice = System.IO.Path.Combine(Harness.RepoRoot(), "tools", "release", "payload", "THIRD_PARTY_NOTICES", "NOTICE.txt");
        if (Environment.GetEnvironmentVariable("UNNAMED_ACCEPTANCE_GAME_ROOT") is { Length: > 0 } game)
            notice = System.IO.Path.Combine(game, "THIRD_PARTY_NOTICES", "NOTICE.txt");
        Step("17.1", () =>
        {
            string text = File.ReadAllText(notice);
            That(text.Contains("Otherreach", StringComparison.Ordinal), "the notices do not name the game");
            That(File.ReadAllText(System.IO.Path.Combine(Harness.RepoRoot(), "src", "Presentation", "Ui", "TitleScreen.cs")).Contains("Rift Studios", StringComparison.Ordinal)
                && File.ReadAllText(System.IO.Path.Combine(Harness.RepoRoot(), "src", "Presentation", "Ui", "TitleScreen.cs")).Contains("J.L. McRell", StringComparison.Ordinal), "the credits' maker line does not name Rift Studios - J.L. McRell");
            That(text.Contains("CC BY", StringComparison.Ordinal) || text.Contains("Creative Commons", StringComparison.Ordinal), "no third-party credit is listed");
            return "the credits name Rift Studios - J.L. McRell and list third-party work (NOTICE.txt)";
        });
        Step("17.2", () =>
        {
            string text = File.ReadAllText(notice);
            int at = text.IndexOf("== Use of AI in this game", StringComparison.Ordinal);
            That(at >= 0, "Credits has no AI disclosure section");
            That(text[at..].Contains("generative AI", StringComparison.OrdinalIgnoreCase), "the disclosure does not say plainly that generative AI was used");
            return "Credits has a Use of AI in this game section that says generative AI was used and by what (what it leaves out is section 18)";
        });
        Step("18.1", () =>
        {
            string text = File.ReadAllText(notice);
            int at = text.IndexOf("== Use of AI in this game", StringComparison.Ordinal);
            That(at >= 0, "the notices have no AI disclosure section");
            string disclosure = text[at..];
            bool chatterIsAi = run.Sim.Setup.Chat.Corpus.Any(l => l.AiGenerated);
            That(chatterIsAi, "no chat line is flagged AI-generated, so the checklist's premise does not hold");
            That(disclosure.Contains("chat", StringComparison.OrdinalIgnoreCase),
                "the disclosure does not mention the AI-written ambient chat lines (content/chatter: every corpus line is ai_generated: true)");
            That(disclosure.Contains("icon", StringComparison.OrdinalIgnoreCase), "the disclosure does not mention the AI-made icons");
            return "the disclosure names chat lines and icons";
        });
        Step("18.2", () =>
        {
            string disclosure = File.ReadAllText(notice);
            disclosure = disclosure[disclosure.IndexOf("== Use of AI in this game", StringComparison.Ordinal)..];
            That(disclosure.Contains("texture", StringComparison.OrdinalIgnoreCase), "the disclosure does not mention AI-made textures, which the checklist says it should");
            That(!Regex.IsMatch(disclosure, @"character (and gear )?(meshes|models)", RegexOptions.IgnoreCase), "the disclosure claims AI-made character or gear meshes");
            return "textures are named and character and gear meshes are not claimed";
        });

        // ── §20.1 the last relaunch ────────────────────────────────────────────────
        Step("20.1", () =>
        {
            run.Now = run.Now.AddMinutes(5);   // saves are ordered by the session clock's time, which this run sets
            session.Save(SaveSlots.Quick);
            var login = session.Continue();
            That(run.Sim.Player.Name == "Corin Vessmoor", $"Continue loaded {run.Sim.Player.Name}, not the last character played");
            return $"Continue finds the last character's last save ({login.Slot}) and loads it";
        });

        Write();
        Assert.Empty(OwnFailures());
    }

    private string? adventurerId;

}
