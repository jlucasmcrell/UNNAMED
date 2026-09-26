// UNNAMED Presentation - the scripted acceptance playthrough and its verification (content bible §29-§31, §33; PROTOTYPE.md §9; M6)
// Godot presentation only

using System.Globalization;
using System.Text;
using Godot;
using UNNAMED.Application;
using UNNAMED.Domain.Companions;
using UNNAMED.Domain.Magic;
using UNNAMED.Domain.Spatial;
using UNNAMED.Persistence;
using UNNAMED.Presentation.Player;
using UNNAMED.Presentation.Ui;
using UNNAMED.World;
using UNNAMED.World.Runtime;

namespace UNNAMED.Presentation;

/// <summary>
/// <c>godot --path src/Presentation -- --playthrough &lt;dir&gt;</c>: a fresh session plays the content bible's exact acceptance path
/// (§31) through the same commands the keys send - the waystation chest, Kera's lesson and Iron Under Ash, Charwood and the hound, the
/// bow at the cart, a haft from the ash stand, Blackvein Cut and its walker, ore from the seam, the billet and the spear, Kera shown
/// the spear, Sel's primer and Tavar's story, the Foldscar - its three Quiet Stones in turn, the south-east one by the way round the
/// spider, the heart steadied - Tavar freed and recruited, and home with him following. Then a ward (Strain), a save, and quit
/// (§30: save, quit completely). One tick a frame, from a fixed seed: the run is the same every time, so a third party replays it
/// by running it (PROTOTYPE.md §9 item 6) and gets the same <c>state_replay.json</c>. Writes <c>transcript.md</c> (every beat and event
/// with its game time), <c>commands.tsv</c> (every command with its tick), a screenshot per beat, the save in <c>profile/</c>,
/// <c>state_saved.json</c>, the authoritative state field by field, and <c>state_digest.txt</c>. An earlier run's save and state files
/// are cleared first (<see cref="Clear"/>).
/// <para>
/// <c>--playthrough-verify &lt;dir&gt;</c>, the relaunch: loads that save, writes <c>state_loaded.json</c> and <c>state_diff.txt</c> - the
/// field-by-field comparison as a diff of expected against actual (§9 item 2) - and fails unless the load was complete and the state
/// digest is the save's; then tells Tavar to wait, walks to the wolves' den and stands until the pack kills the character, and checks
/// the death's XP debt landed exactly once (C17).
/// </para>
/// </summary>
public sealed class Playthrough
{
    /// <summary>The world the playthrough plays: fixed, so the run is the same every time.</summary>
    public const ulong Seed = 0x0A5E_2026_0924_0001;

    public const string Slot = "acceptance";

    private const float Near = 0.2f;
    private const string Kera = "npc.ashen_hollow.kera_voss";
    private const string Sel = "npc.ashen_hollow.sel_arien";
    private const string Tavar = "npc.ashen_hollow.tavar_orr";

    // The content bible's acceptance path (§31), point by point, in the four cells' layout.
    private static readonly (double X, double Z)[] ToTheChest = { (34, 150), (38, 138.4) };
    private static readonly (double X, double Z)[] ToTheSmithyDoor = { (40, 140), (47, 139), (51.8, 139), (51.8, 142) };
    private static readonly (double X, double Z)[] IntoTheSmithy = { (54.5, 142) };
    private static readonly (double X, double Z)[] ToTheEastExit = { (51.8, 142), (51.8, 136), (65, 136), (88, 145) };
    private static readonly (double X, double Z)[] ToTheHound = { (108, 148), (128, 150) };
    private static readonly (double X, double Z)[] ToTheCart = { (145, 156), (157, 158.8) };
    private static readonly (double X, double Z)[] ToTheStand = { (165, 150), (175, 142), (179, 138.3) };
    // South-west from the stand, keeping clear of the strays' patch round (118, 128) - the wolves are the prototype's, not the bible's.
    private static readonly (double X, double Z)[] ToTheQuarry = { (170, 118), (150, 106), (125, 102), (100, 110), (80, 106), (63, 98) };
    private static readonly (double X, double Z)[] ToTheSeam = { (60, 90), (54, 74), (48, 62), (40, 54), (34, 48), (27.2, 44.2) };
    private static readonly (double X, double Z)[] HomeToTheSmithy =
        { (34, 48), (40, 54), (48, 62), (54, 74), (60, 90), (63, 98), (64, 112), (58, 134), (51.8, 136), (51.8, 142) };
    private static readonly (double X, double Z)[] ToTheHearth = { (59.5, 143.2) };
    private static readonly (double X, double Z)[] ToTheAnvil = { (58.2, 141.4) };
    private static readonly (double X, double Z)[] OutToSel = { (54.5, 142), (51.8, 142), (51.8, 136), (60, 128) };
    private static readonly (double X, double Z)[] ToTheFoldscar = { (90, 112), (105, 108), (120, 93), (140, 80) };
    private static readonly (double X, double Z)[] ToTheNorthStone = { (148.5, 78) };
    private static readonly (double X, double Z)[] ToTheSouthWestStone = { (135, 60), (123.5, 38) };
    private static readonly (double X, double Z)[] ToTheSouthEastStone = { (140, 62), (160, 65), (187, 65), (194, 45), (182.5, 31) };
    private static readonly (double X, double Z)[] ToTheHeart = { (194, 45), (187, 65), (160, 65), (153, 50.2) };
    private static readonly (double X, double Z)[] HomeWithTavar = { (150, 45), (135, 70), (100, 100), (80, 118), (62, 130), (48, 137) };
    private static readonly (double X, double Z)[] ToTheDen = { (55, 136), (66, 140), (66, 150), (90, 162), (104, 168), (112, 172) };

    private sealed record Beat(string Name, string Says, int Budget, Func<bool> Run, string? Shot = null);

    private readonly GameSession _session;
    private readonly PlayerController _controller;
    private readonly CameraRig _camera;
    private readonly DialoguePanel _dialogue;
    private readonly bool _verify;
    private readonly (string Slot, SaveCopy Copy, LoadResult Result)? _continued;
    private readonly List<Beat> _beats = new();
    private readonly StringBuilder _transcript = new();
    private readonly List<string> _failures = new();

    private int _beat;
    private long _beatTick;
    private int _waypoint;
    private int _phase;
    private int _wait;
    private string? _pending;
    private int _frame;
    private int _deaths;
    private long _debtAdded;
    private long _debtBefore;
    private bool _respawned;
    private string? _broken;

    // M7's faction beats (design §13.7): what they watch, and a step counter for beats made of several legs.
    private readonly Action<bool>? _showFactions;
    private readonly List<ActRecorded> _acts = new();
    private readonly List<FactionLearned> _learned = new();
    private readonly List<ReputationChanged> _standing = new();
    private string? _refused;
    private int _stage;
    private int _learnedAtStart;
    private int _standingAtStart;
    private long _coinAtStart;
    private int _billetsAtStart;
    private (int Respect, int Trust) _keraAtStart;
    private int _selTrustAtStart;

    public Playthrough(GameSession session, PlayerController controller, CameraRig camera, DialoguePanel dialogue, string directory, bool verify,
        (string Slot, SaveCopy Copy, LoadResult Result)? continued = null, Action<bool>? showFactions = null)
    {
        _showFactions = showFactions;
        _session = session;
        _controller = controller;
        _camera = camera;
        _dialogue = dialogue;
        _verify = verify;
        _continued = continued;
        Directory = directory;
        System.IO.Directory.CreateDirectory(directory);
        Record();
        if (verify)
            VerifyBeats();
        else
            PlayBeats();
        var simulation = session.Simulation!;
        _beatTick = simulation.WorldTick;
        _transcript.AppendLine(verify ? "# The relaunch: load, compare, and a death (C17)" : "# The acceptance playthrough (content bible §31)")
            .AppendLine()
            .AppendLine($"Content {session.Content.Version} ({session.Content.Hash}), world seed {WorldSeed.Format(simulation.World.WorldSeed)}, " +
                        $"{session.TickSeconds * 1000:0} ms ticks, one a frame. Game time is world ticks as minutes and seconds.")
            .AppendLine()
            .AppendLine("| Game time | Tick | What happened |")
            .AppendLine("|---|---|---|");
        Note($"Start: {Where(simulation.Player.Body)}, level {simulation.Player.Progression.Level}, " +
             $"carrying {string.Join(", ", simulation.Player.Inventory.OrderBy(e => e.DefId, StringComparer.Ordinal).Select(e => $"{_session.DisplayName(e.DefId)} x{e.Count}"))}");
    }

    public string Directory { get; }

    /// <summary>Advance one frame. The name of a screenshot to take now, "done" at the end, "failed", or null.</summary>
    public string? Update()
    {
        _frame++;
        if (_wait > 0)
        {
            if (--_wait == 0 && _pending is { } shot)
            {
                _pending = null;
                return shot;
            }
            return null;
        }
        if (_beat >= _beats.Count)
            return Finish();
        var beat = _beats[_beat];
        var simulation = _session.Simulation!;
        // The acceptance run must be survived; the relaunch's death is the one C17 asks for.
        if (!_verify && _deaths > 0)
            _broken ??= "the character died";
        if (_broken is null && simulation.WorldTick - _beatTick > beat.Budget)
            _broken = $"it did not finish in {beat.Budget} ticks";
        bool done = false;
        if (_broken is null)
        {
            try
            {
                done = beat.Run();
            }
            catch (Exception e)
            {
                _broken = $"{e.GetType().Name}: {e.Message}";
            }
        }
        if (_broken is { } why)
        {
            _failures.Add($"beat '{beat.Name}': {why}");
            Note($"FAILED at '{beat.Name}': {why}, at {Where(simulation.Player.Body)}");
            WriteFiles();
            GD.PushError($"UNNAMED playthrough: beat '{beat.Name}' failed: {why}");
            return "failed";
        }
        if (!done)
            return null;
        Note($"**{beat.Says}**" + (beat.Shot is { } picture ? $" ![{picture}]({picture}.png)" : ""));
        _beat++;
        _beatTick = simulation.WorldTick;
        _waypoint = 0;
        _phase = 0;
        _stage = 0;
        _controller.SteerWorld(Vector3.Zero, Gait.Run, _camera);
        WriteFiles();   // the transcript so far, beat by beat
        if (beat.Shot is { } name)
        {
            _pending = name;
            _wait = 6;
        }
        return null;
    }

    // ── the beats ───────────────────────────────────────────────────────────

    private void PlayBeats()
    {
        var magic = _session.Setup.Magic;
        _beats.AddRange(new[]
        {
            new Beat("spawn", "Beside the Ashen Waystone, sword in hand (§29 0:00)", 60, () => _frame > 30, "01_spawn"),
            new Beat("chest", "The waystation's storage chest opened and emptied (§29 0:30)", 1_500, OpenTheChest, "02_chest"),
            new Beat("smithy", "Into Kera's smithy", 1_500, () => Travel(ToTheSmithyDoor) && Door("door.forge_shed") && Walk(IntoTheSmithy)),
            new Beat("kera", "Kera teaches the forge: Iron Under Ash begins (§29 2:00)", 900, () => Converse(Kera, "teach", "thanks"), "03_kera"),
            new Beat("east", "Out of the waystation by the east road (88, 145)", 2_000, () => Travel(ToTheEastExit)),
            new Beat("hound", "Charwood: the hound's ground (128, 150) - fought if it hunts (§29 4:00)", 3_000, () => Travel(ToTheHound), "04_charwood"),
            new Beat("cart", "The ruined merchant cart: the hunting bow and arrows (§29 5:00)", 2_500, TakeFromTheCart, "05_cart"),
            new Beat("stand", "An ash haft cut at the stand", 2_500, CutAHaft, "06_haft"),
            new Beat("quarry", "Back south-west and down to Blackvein Cut's mouth (63, 98) (§29 6:00)", 5_000, () => Travel(ToTheQuarry), "07_quarry"),
            new Beat("seam", "Past the Bone Walker's ground to the seam: raw iron ore (§29 7:00-8:00)", 5_000, StrikeTheSeam, "08_ore"),
            new Beat("home", "Home north to the smithy (§29 9:00)", 7_000, () => Travel(HomeToTheSmithy) && Door("door.forge_shed") && Walk(IntoTheSmithy)),
            new Beat("billet", "A billet smelted at the hearth", 1_500, () => Make("forge", ToTheHearth), "09_billet"),
            new Beat("spear", "The March Spear forged at the anvil, and taken in hand", 1_500, () => Make("anvil", ToTheAnvil) && WieldTheSpear(), "10_spear"),
            new Beat("quest1", "Kera shown the spear: Iron Under Ash complete", 1_200, ShowKera, "11_iron_under_ash"),
            new Beat("sel", "Sel: the Resonance Primer, and Tavar's story - The Three Quiet Stones begins", 2_000, TalkToSel, "12_sel"),
            new Beat("primer", "The primer read: three formulas known", 300, ReadThePrimer),
            new Beat("foldscar", "The Foldscar path (105, 108) and in", 4_000, () => Travel(ToTheFoldscar)),
            new Beat("north", "The north Quiet Stone turned into line", 1_500, () => Turn("switch.stone_north", ToTheNorthStone), "13_north_stone"),
            new Beat("southwest", "The south-west Quiet Stone turned into line", 2_500, () => Turn("switch.stone_southwest", ToTheSouthWestStone), "14_southwest_stone"),
            new Beat("southeast", "The south-east Quiet Stone turned, by the way round the spider (§8)", 4_000, () => Turn("switch.stone_southeast", ToTheSouthEastStone), "15_southeast_stone"),
            new Beat("fold", "At the heart: Tavar in the fold, out of reach", 3_000, () => Travel(ToTheHeart) && Look(145_000, 42_000), "16_the_fold"),
            new Beat("heart", "The heart steadied: the fold lifts", 300, () => Turn("switch.foldscar_heart", Array.Empty<(double, double)>()) && Look(145_000, 42_000), "17_heart_steadied"),
            new Beat("tavar", "Tavar freed - Quest 2 complete - and recruited", 1_200, () => Converse(Tavar, "sent", "join", "ok"), "18_tavar_joins"),
            new Beat("follow", "Home with Tavar following", 8_000, () => Travel(HomeWithTavar) && LookAtTavar(), "19_home_with_tavar"),
            // M7 (design §13.7): the factions - the armour killed, Kera and Sel told, both gates met in the world.
            new Beat("m7_wait", "Tavar told to wait before the fight", 60, () => Steps(() => Order(CompanionOrder.Wait), TavarWaits)),
            new Beat("m7_billet_refused", "Kera will not sell the waystation's billets to a stranger", 1_500, () => Steps(
                () => Travel(new[] { (51.8, 139.0), (51.8, 142.0) }), () => Door("door.forge_shed"), () => Walk(new[] { (54.5, 142.0), (60.3, 140.3) }),
                RawBilletBuyRefused)),
            new Beat("m7_armour", "The Animated Armour at Blackvein Cut, cut down from behind", 6_000, () => Steps(
                () => Walk(new[] { (54.5, 142.0), (51.8, 142.0) }), () => Travel(FromTheSmithyToTheCut), () => Travel(ToTheArmour),
                () => Travel(new[] { BehindTheArmour(12) }), MendBeforeTheFight, () => WalkQuietly(BehindTheArmour(3.1)),
                () => CreepInto(Armour), () => Engage(Armour), ArmourDown), "22_armour_down"),
            new Beat("m7_tell_kera", "Kera told the armour is down: the Waystation accepts the character, and a billet is bought", 6_000, () => Steps(
                () => Travel(FromTheCutToTheSmithy), () => Door("door.forge_shed"), () => Walk(IntoTheSmithy), StartOfKera,
                () => Converse(Kera, "armour", "back"), BuyABillet, KeraTold), "23_billets"),
            new Beat("m7_tell_sel_tavar", "Sel told Tavar is back: the Survey learns of the heart, and her notes open", 1_500, () => Steps(
                () => Walk(OutToSel), StartOfSel, () => ConverseThen(Sel, NotesOffered, "tavar_back", "back"), SelToldOfTheHeart), "24_notes_offered"),
            new Beat("m7_tell_sel_armour", "Sel told of the armour: the Survey drops back to neutral, and the notes close", 1_500, () => Steps(
                StartOfSel, () => ConverseThen(Sel, NotesClosed, "armour", "back"), SelToldOfTheArmour, () => Still("25_factions_f6"),
                () => Walk(new[] { (62.0, 130.0), (48.0, 137.0) }))),
            new Beat("ward", "Brace Wards worked at home until Strain passed tolerance and cost health (§33: Health and Strain not at rest)", 1_200,
                () => Strain(magic.Formulas.Keys.First(f => magic.Formulas[f].Targeting == Targeting.Self && f.Contains("ward", StringComparison.Ordinal))), "20_ward"),
            new Beat("save", "Saved - then the application quits (§30)", 60, SaveAndDump, "21_saved"),
        });
    }

    private void VerifyBeats()
    {
        _beats.AddRange(new[]
        {
            new Beat("loaded", "Relaunched and loaded the acceptance save", 60, LoadAndCompare, "30_loaded"),
            new Beat("wait", "Tavar told to wait", 60, () => Order(CompanionOrder.Wait)),
            new Beat("den", "To the wolves' den", 4_000, () => Walk(ToTheDen), "31_den"),
            new Beat("death", "Stood against the pack until it killed the character", 3_000, DieAtTheDen, "32_death"),
            new Beat("respawn", "Back at the Ashen Waystone, the debt owed once (C17)", 1_200, CheckTheDebt, "33_respawn"),
        });
    }

    private bool OpenTheChest()
    {
        const string chest = "container.waystation_chest";
        if (!Walk(ToTheChest, Near))
            return false;
        Look(38_000, 137_000);
        return Empty(chest);
    }

    /// <summary>Take everything a container holds, as the inventory's Take buttons do; true once it is empty.</summary>
    private bool Empty(string key)
    {
        var box = _session.Simulation!.Containers.Single(c => c.Site.Key == key);
        if (box.Items.IsEmpty)
            return _phase > 0 || (_broken = $"{key} was empty before anything was taken") is null;
        if (_phase++ > 3)
            _broken = $"{key} still holds {box.Items.Length} stacks: the takes were refused";
        foreach (var item in box.Items)
            _session.Submit(new MoveItemCommand(_session.Simulation!.PlayerId, item.Ref, ItemPlace.In(key), ItemPlace.Carried, item.Count));
        return false;
    }

    private bool TakeFromTheCart()
    {
        const string cart = "container.merchant_cart";
        if (!Travel(ToTheCart, Near))
            return false;
        Look(157_000, 160_200);
        return Empty(cart);
    }

    private bool CutAHaft()
    {
        if (!Travel(ToTheStand, Near))
            return false;
        var stand = _session.Simulation!.Nodes.Single(n => n.Name == "ash_stand");
        Look(stand.XMm, stand.ZMm);
        if (Carried("item.material.ash_haft") > 0)
            return true;
        if (_phase++ % 10 == 0)
            _controller.Gather(stand.Key);
        return false;
    }

    private bool StrikeTheSeam()
    {
        if (!Travel(ToTheSeam, Near))
            return false;
        var seam = _session.Simulation!.Nodes.Single(n => n.Name == "iron_seam");
        Look(seam.XMm, seam.ZMm);
        if (Carried("item.material.iron_ore") > 0)
            return true;
        if (_phase++ % 10 == 0)
            _controller.Gather(seam.Key);
        return false;
    }

    /// <summary>At a station of this kind, make the first recipe the character knows for it, once.</summary>
    private bool Make(string kind, (double X, double Z)[] spot)
    {
        if (!Walk(spot, Near))
            return false;
        var station = _session.Setup.Layout.Stations.First(s => s.Kind == kind);
        Look(station.XMm, station.ZMm);
        var recipe = _controller.Recipes(kind).First();
        if (Carried(recipe.OutputItemId) > 0 && _phase > 0)
            return true;
        if (_phase++ == 0)
            _controller.Craft(recipe.Id);
        return false;
    }

    private bool WieldTheSpear()
    {
        var simulation = _session.Simulation!;
        var spear = simulation.Player.Inventory.FirstOrDefault(e => e.DefId == "item.weapon.march_spear");
        if (spear is null)
            return false;
        if (simulation.Combat.Weapon.Source == spear.DefId)
            return true;
        _session.Submit(new EquipCommand(simulation.PlayerId, spear.ItemId));
        return false;
    }

    private bool ShowKera()
    {
        var simulation = _session.Simulation!;
        switch (_phase)
        {
            case 0:
                if (Approach(Kera))
                {
                    _controller.Talk(Kera);
                    _phase++;
                }
                return false;
            case 1:
                if (simulation.Conversation?.Replies.FirstOrDefault(r => r.Id.StartsWith("show", StringComparison.Ordinal)) is { } show)
                    _dialogue.Answer(show.Id);
                _phase++;
                return false;
            default:
                _dialogue.Leave();
                return simulation.Quests.Any(q => q.Id == "quest.ashen_hollow.iron_under_ash" && q.Status == Domain.Quests.QuestStatus.Completed);
        }
    }

    private bool TalkToSel()
    {
        if (_phase == 0)
        {
            if (!Walk(OutToSel))
                return false;
            _phase = 1;
        }
        return Converse(Sel, "books", "take", "back", "ruin", "tavar", "stones", "back");
    }

    private bool ReadThePrimer()
    {
        var simulation = _session.Simulation!;
        var magic = _session.Setup.Magic;
        if (simulation.Player.Inventory.FirstOrDefault(e => magic.Teaches.ContainsKey(e.DefId)) is { } book)
        {
            _session.Submit(new UseItemCommand(simulation.PlayerId, book.ItemId));
            return false;
        }
        return _controller.Formulas().Length == 3;
    }

    /// <summary>Walk to a switch and work it, facing it: true once its flag is set.</summary>
    private bool Turn(string key, (double X, double Z)[] spot)
    {
        if (!Travel(spot, Near))
            return false;
        var view = _session.Simulation!.Switches.Single(s => s.Site.Key == key);
        var (x, z) = Footprints.Center(view.Site.Body);
        Look(x, z);
        if (view.Set)
            return true;
        if (_phase++ % 10 == 0)
            _controller.Interact(key);
        return false;
    }

    /// <summary>Walk up to an NPC and say these replies in turn, one a moment; true once the conversation has ended after the last.</summary>
    private bool Converse(string npcId, params string[] replies)
    {
        var simulation = _session.Simulation!;
        if (_phase == 0 || (_phase == 1 && simulation.Conversation is null))
        {
            if (!Approach(npcId))
                return false;
            _controller.Talk(npcId);
            _phase = 2;
            return false;
        }
        int said = (_phase - 2) / 8;
        if ((_phase - 2) % 8 == 0 && said < replies.Length)
        {
            if (simulation.Conversation?.Replies.Any(r => r.Id == replies[said]) == true)
                _dialogue.Answer(replies[said]);
            else
                _broken = $"{npcId} did not offer '{replies[said]}' at the line '{simulation.Conversation?.NodeId ?? "(no conversation)"}'";
        }
        _phase++;
        if (said < replies.Length)
            return false;
        if (simulation.Conversation is not null)
            _dialogue.Leave();
        return simulation.Conversation is null;
    }

    /// <summary>Work a formula again and again, each once the last is done, until a working past tolerance has cost health (or six).</summary>
    private bool Strain(string formula)
    {
        var combat = _session.Simulation!.Combat;
        if (combat.Health < combat.MaxHealth || _phase >= 6)
            return true;
        if (combat.Phase == CombatPhase.Idle && combat.Casting is null && _session.Simulation!.WorldTick >= _castAgainAt)
        {
            _controller.Cast(formula);
            _castAgainAt = _session.Simulation!.WorldTick + 40;
            _phase++;
        }
        return false;
    }

    private long _castAgainAt;

    private bool SaveAndDump()
    {
        var simulation = _session.Simulation!;
        _session.Save(SaveSlots.Manual(Slot));
        File.WriteAllText(Path.Combine(Directory, "state_saved.json"), StateDump.Render(simulation));
        File.WriteAllText(Path.Combine(Directory, "state_replay.json"), StateDump.Render(simulation, replayable: true));
        File.WriteAllText(Path.Combine(Directory, "state_digest.txt"), simulation.StateDigest());
        var player = simulation.Player;
        var pools = player.Progression.Pools;
        Note($"The persistence acceptance state (§33): level {player.Progression.Level}, XP {player.Progression.LifetimeXp.Values.Sum()}; " +
             $"health {simulation.Combat.Health}/{simulation.Combat.MaxHealth}, Strain {pools.Strain}, Focus {simulation.Combat.Focus}/{simulation.Combat.MaxFocus}; " +
             $"in hand {_session.DisplayName(simulation.Combat.Weapon.Source)}; at {Where(player.Body)}, {Metres(Distance(player.Body, _session.Setup.Layout.Spawn))} from the spawn; " +
             $"quests {string.Join(", ", simulation.Quests.Select(q => $"{q.Title} {q.Status.ToString().ToLowerInvariant()}"))}; " +
             $"companions {string.Join(", ", simulation.Companions.Select(c => $"{c.Name} {c.Doing} at {Where(c.Body)}"))}; " +
             $"switches set {simulation.Switches.Count(s => s.Set)} of {simulation.Switches.Length}; " +
             $"creature records {simulation.World.TakeSnapshot().Creatures.Length}, changed containers {simulation.World.TakeSnapshot().Containers.Length}");
        Note($"State digest at the save: `{simulation.StateDigest()}`");
        return true;
    }

    // ── the relaunch ────────────────────────────────────────────────────────

    private bool LoadAndCompare()
    {
        var simulation = _session.Simulation!;
        // The relaunch went through the start screen's Continue (B-01), which must have picked the save the run made last.
        Note($"Continue loaded {_continued?.Slot ?? "(nothing)"} ({_continued?.Copy})");
        if (_continued is not { Slot: var slot, Copy: SaveCopy.Current } || slot != SaveSlots.Manual(Slot))
            _failures.Add($"Continue loaded {_continued?.Slot ?? "nothing"} ({_continued?.Copy}), not the acceptance save {SaveSlots.Manual(Slot)}");
        // A load that lost anything did not bring the save back, whatever the fields say (the Phase-1 technical audit, T-01, L-20).
        if (_continued?.Result is { } result)
        {
            string losses = $"sections quarantined [{string.Join(", ", result.QuarantinedSections)}], " +
                            $"records rejected [{string.Join("; ", result.RejectedRecords.Select(r => $"{r.Section} {r.Key}: {r.Reason}"))}], " +
                            $"integrity root re-derived {result.IntegrityRootRederived}, loss reported [{string.Join("; ", result.Report.Loss)}]";
            Note($"The load {(result.IsComplete ? "was complete" : "was NOT complete")}: {losses}");
            if (!result.IsComplete)
                _failures.Add($"the load was not complete: {losses}");
        }
        string savedPath = Path.Combine(Directory, "state_saved.json");
        if (!File.Exists(savedPath))
        {
            _failures.Add("there is no state_saved.json: the run never reached its save");
            return true;
        }
        string loaded = StateDump.Render(simulation);
        File.WriteAllText(Path.Combine(Directory, "state_loaded.json"), loaded);
        string saved = File.ReadAllText(savedPath);
        var differences = StateDump.Compare(saved, loaded, out int leaves);
        var diff = new StringBuilder()
            .AppendLine("Field-by-field comparison of the authoritative state: expected (state_saved.json, written just before the save and quit)")
            .AppendLine("against actual (state_loaded.json, written just after the relaunch loaded the save).")
            .AppendLine()
            .AppendLine($"Fields compared: {leaves}")
            .AppendLine($"Differences: {differences.Count}");
        foreach (string difference in differences)
            diff.AppendLine(difference);
        File.WriteAllText(Path.Combine(Directory, "state_diff.txt"), diff.ToString());
        Note($"Loaded: {leaves} fields compared, {differences.Count} differences (state_diff.txt); state digest `{simulation.StateDigest()}`");
        if (differences.Count > 0)
            _failures.Add($"{differences.Count} fields differ after the load");
        string digestPath = Path.Combine(Directory, "state_digest.txt");
        string? savedDigest = File.Exists(digestPath) ? File.ReadAllText(digestPath).Trim() : null;
        if (savedDigest != simulation.StateDigest())
            _failures.Add($"the state digest after the load, {simulation.StateDigest()}, is not the one at the save, {savedDigest ?? "(none written)"}");
        return true;
    }

    /// <summary>
    /// A new run's directory keeps nothing of an earlier run's (the Phase-1 technical audit, L-20): its save profile and every file the
    /// relaunch reads or writes go before the session starts, so a run that stops short of its save leaves nothing a relaunch could pass
    /// on. Screenshots and the run's own transcript are written afresh.
    /// </summary>
    public static void Clear(string directory)
    {
        if (!System.IO.Directory.Exists(directory))
            return;
        string profile = Path.Combine(directory, "profile");
        if (System.IO.Directory.Exists(profile))
            System.IO.Directory.Delete(profile, recursive: true);
        foreach (string name in new[] { "state_saved.json", "state_replay.json", "state_digest.txt", "state_loaded.json", "state_diff.txt",
                     "transcript_relaunch.md", "commands_relaunch.tsv", "art_coverage_relaunch.json", "art_coverage_relaunch.md",
                     "audio_coverage_relaunch.json", "audio_coverage_relaunch.md" })
            File.Delete(Path.Combine(directory, name));
    }

    private bool Order(CompanionOrder order)
    {
        foreach (var companion in _session.Simulation!.Companions)
            _controller.Order(companion.NpcId, order);
        return true;
    }

    private bool DieAtTheDen()
    {
        var simulation = _session.Simulation!;
        if (_deaths > 0)
            return true;
        // Wound one of the pack, then stand: PROTOTYPE.md §5 step 7's deliberate death.
        var wolf = simulation.Creatures.Where(c => c.Alive && c.Key.StartsWith("spawn.hollow.den_pack#", StringComparison.Ordinal))
            .OrderBy(c => Distance(simulation.Player.Body, c.Body)).FirstOrDefault();
        if (wolf is null)
            return false;
        if (_phase == 0)
        {
            _debtBefore = simulation.Player.Progression.XpDebt;
            var to = new Vector3((float)((wolf.Body.XMm - simulation.Player.Body.XMm) / 1000.0), 0, (float)((wolf.Body.ZMm - simulation.Player.Body.ZMm) / 1000.0));
            _camera.Yaw = Mathf.Atan2(-to.X, -to.Z);
            bool inReach = to.Length() <= (simulation.Combat.Weapon.ReachMm + 400) / 1000f;
            _controller.SteerWorld(inReach ? Vector3.Zero : to.Normalized(), Gait.Run, _camera, faceCamera: true);
            if (inReach && simulation.Combat.Phase == CombatPhase.Idle)
            {
                _controller.Attack();
                _phase = 1;
            }
            return false;
        }
        _controller.SteerWorld(Vector3.Zero, Gait.Run, _camera);
        return false;
    }

    private bool CheckTheDebt()
    {
        var simulation = _session.Simulation!;
        if (!_respawned)
            return false;
        long debt = simulation.Player.Progression.XpDebt;
        Note($"Deaths {_deaths}; XP debt {_debtBefore} before, {_debtAdded} added by the death, {debt} now: " +
             (_deaths == 1 && debt == _debtBefore + _debtAdded ? "the penalty applied exactly once" : "NOT applied exactly once"));
        if (_deaths != 1 || debt != _debtBefore + _debtAdded)
            _failures.Add("the death's penalty was not applied exactly once");
        return _frame > 0;
    }

    // ── finishing ───────────────────────────────────────────────────────────

    private string Finish()
    {
        Note(_failures.Count == 0 ? "Finished: every beat happened." : $"Finished with {_failures.Count} problem(s): {string.Join("; ", _failures)}");
        WriteFiles();
        GD.Print($"UNNAMED playthrough {(_verify ? "verification" : "run")} written to {Directory}");
        return _failures.Count == 0 ? "done" : "failed";
    }

    private void WriteFiles()
    {
        File.WriteAllText(Path.Combine(Directory, _verify ? "transcript_relaunch.md" : "transcript.md"), _transcript.ToString());
        var log = new StringBuilder();
        foreach (var entry in _session.Simulation!.CommandLog)
            log.AppendLine($"{entry.Tick}\t{entry.Sequence}\t{entry.Command}{(entry.RejectedReason is { } why ? $"\trejected: {why}" : "")}");
        File.WriteAllText(Path.Combine(Directory, _verify ? "commands_relaunch.tsv" : "commands.tsv"), log.ToString());
    }

    // ── recording ───────────────────────────────────────────────────────────

    private void Record()
    {
        string Name(string id) => _session.DisplayName(id);
        _session.Subscribe<QuestStarted>(e => Note($"Quest started: {Name(e.QuestId)}"));
        _session.Subscribe<ObjectiveSatisfied>(e => Note($"Objective satisfied: {e.ObjectiveId} of {Name(e.QuestId)}"));
        _session.Subscribe<QuestCompleted>(e => Note($"Quest completed: {Name(e.QuestId)}"));
        _session.Subscribe<RewardGranted>(e => Note($"Reward: {e.What}"));
        _session.Subscribe<LocationDiscovered>(e => Note($"Discovered: {Name(e.LocationId)}"));
        _session.Subscribe<NodeGathered>(e => Note($"Gathered: {Name(e.ItemId)} x{e.Count}"));
        _session.Subscribe<ItemCrafted>(e => Note($"Made: {Name(e.ItemId)} (quality {e.Quality})"));
        _session.Subscribe<ItemMoved>(e =>
        {
            if (e.From.Kind == PlaceKind.Container && e.To.Kind == PlaceKind.Inventory)
                Note($"Taken from {e.From.ContainerKey}: {Name(e.DefId)} x{e.Count}");
        });
        _session.Subscribe<CreatureKilled>(e => Note($"Killed: {Name(e.DefId)}, by {(e.Killer == _session.Simulation!.PlayerId ? "the character" : "a companion")}"));
        _session.Subscribe<TechniqueLearned>(e => Note($"Learned: {Name(e.DefinitionId)}"));
        _session.Subscribe<SwitchSet>(e => Note($"Switch set: {e.SwitchKey}"));
        _session.Subscribe<ExperienceGained>(e =>
        {
            if (e.LevelsGained > 0)
                Note($"Level {e.Level}");
        });
        _session.Subscribe<CompanionRecruited>(e => Note($"{Name(e.NpcId)} joins the character"));
        _session.Subscribe<CompanionOrdered>(e => Note($"{Name(e.NpcId)}: {(e.Order == CompanionOrder.Follow ? "follow" : "wait")}"));
        _session.Subscribe<CompanionCaughtUp>(e => Note($"{Name(e.NpcId)} caught up ({e.Reason})"));
        _session.Subscribe<CompanionDowned>(e => Note($"{Name(e.NpcId)} downed by {Name(e.ByDefId)}"));
        _session.Subscribe<CompanionFell>(e => Note($"{Name(e.NpcId)} fell, back at the Waystone"));
        _session.Subscribe<PlayerDied>(e =>
        {
            _deaths++;
            _debtAdded = e.DebtAdded;
            Note($"The character died: {e.Cause} ({Name(e.KillerDefId)}); XP debt added {e.DebtAdded}");
        });
        _session.Subscribe<PlayerRespawned>(e =>
        {
            _respawned = true;
            Note($"Returned at the Ashen Waystone: {Where(e.Body)}");
        });
        // M7: the factions.
        _session.Subscribe<ActRecorded>(e =>
        {
            _acts.Add(e);
            Note($"Act {e.Seq}: {e.Kind} {e.Subject}");
        });
        _session.Subscribe<FactionLearned>(e =>
        {
            _learned.Add(e);
            Note($"{Name(e.FactionId)} learned of act {e.ActSeq} ({e.Source}, via {(e.Via is { } via ? Name(via) : "nobody")})");
        });
        _session.Subscribe<ReputationChanged>(e =>
        {
            _standing.Add(e);
            Note($"{Name(e.FactionId)}: {e.From} -> {e.To} ({e.TierFrom} -> {e.TierTo})");
        });
        _session.Subscribe<CommandRejected>(e => _refused = e.Reason);
    }

    private void Note(string line)
    {
        long tick = _session.Simulation?.WorldTick ?? 0;
        double seconds = tick * _session.TickSeconds;
        _transcript.AppendLine(string.Create(CultureInfo.InvariantCulture, $"| {(int)(seconds / 60)}:{(int)(seconds % 60):00} | {tick} | {line.Replace("|", "/")} |"));
    }

    // ── M7: the faction beats ───────────────────────────────────────────────

    private const string Armour = "creature.construct.animated_armour";
    private const string Billet = "item.material.iron_ingot";
    private const string KeraWares = "merchant.ashen_hollow.kera_voss";
    private const string Waystation = "faction.ashen_hollow.waystation";
    private const string Survey = "faction.ashen_hollow.survey";

    // Out of the smithy by HomeToTheSmithy reversed to (54, 74), and down the cut to the armour's post (65, 34); checked against the layout.
    private static readonly (double X, double Z)[] FromTheSmithyToTheCut = { (51.8, 136), (58, 134), (64, 112), (63, 98), (60, 90), (54, 74) };
    private static readonly (double X, double Z)[] ToTheArmour = { (58, 55) };
    private static readonly (double X, double Z)[] FromTheCutToTheSmithy =
        { (58, 55), (54, 74), (60, 90), (63, 98), (64, 112), (58, 134), (51.8, 136), (51.8, 142) };

    /// <summary>A beat's legs and checks in order; each is run until it is done, and the next starts afresh.</summary>
    private bool Steps(params Func<bool>[] steps)
    {
        while (_stage < steps.Length)
        {
            if (!steps[_stage]())
                return false;
            _stage++;
            _waypoint = 0;
            _phase = 0;
        }
        return true;
    }

    private bool Fail(string why)
    {
        _broken ??= why;
        return false;
    }

    /// <summary>A still in the middle of a beat, with F6 open for it: open, wait for the picture, close.</summary>
    private bool Still(string name)
    {
        if (_phase++ == 0)
        {
            _showFactions?.Invoke(true);
            _pending = name;
            _wait = 6;
            return false;
        }
        _showFactions?.Invoke(false);
        return true;
    }

    private bool TavarWaits() => _session.Simulation!.Companions.All(c => c.Order == CompanionOrder.Wait);

    /// <summary>The billets' ware: the stack in Kera's wares once they have a record, else the untouched ref from her stock rows (§5.7.2).</summary>
    private string BilletRef()
    {
        if (_session.Simulation!.World.Container(KeraWares) is { } record)
            return record.Items.First(i => i.DefId == Billet).ItemId.Value;
        var merchant = _session.Setup.Items.Merchants[KeraWares];
        int index = 0;
        foreach (var row in merchant.Stock)
        {
            if (row.ItemId == Billet)
                return $"{KeraWares}#{index:00}";
            int stackMax = Math.Max(1, _session.Setup.Items.Catalog.Get(row.ItemId).StackMax);
            index += (row.Count + stackMax - 1) / stackMax;
        }
        throw new InvalidOperationException("Kera stocks no billet");
    }

    private bool RawBilletBuyRefused()
    {
        var simulation = _session.Simulation!;
        if (_phase++ == 0)
        {
            _refused = null;
            _session.Submit(new BuyCommand(simulation.PlayerId, Kera, BilletRef(), 1));
            return false;
        }
        if (_refused is null)
            return _phase > 20 && Fail("the raw buy of a billet was not refused");
        if (_refused != "Kera Voss will not sell you that")
            return Fail($"the raw buy was refused with '{_refused}'");
        if (simulation.Wares(Kera)!.Wares.Any(w => w.ItemId == Billet))
            return Fail("the billets are listed before Kera is told");
        return true;
    }

    /// <summary>
    /// A point <paramref name="metres"/> behind the sentinel along its facing, read from the creature (§13.7). The owner's ruling on the
    /// E3 STOP tunes the approach, not the encounter: the character runs round to 12 m behind, outside the 100-degree sight cone and more
    /// than a run's 8 m of noise from it, mends there, and walks to 3.1 m behind - a walk carries 3 m - before the last step in.
    /// </summary>
    private (double X, double Z) BehindTheArmour(double metres)
    {
        var armour = _session.Simulation!.Creatures.First(c => c.DefId == Armour);
        double facing = armour.Body.FacingMdeg / 1000.0 * Math.PI / 180;
        return (armour.Body.XMm / 1000.0 - Math.Sin(facing) * metres, armour.Body.ZMm / 1000.0 - Math.Cos(facing) * metres);
    }

    /// <summary>A mending stop before a fight: work the mending formula (or a salve) until nine tenths of health, or until nothing more can be worked without strain costing health.</summary>
    private bool MendBeforeTheFight()
    {
        var simulation = _session.Simulation!;
        var combat = simulation.Combat;
        var magic = _session.Setup.Magic;
        _controller.SteerWorld(Vector3.Zero, Gait.Walk, _camera);
        if (combat.Health * 10 >= combat.MaxHealth * 9)
            return true;
        if (simulation.WorldTick - _mendTick < 40 || combat.Phase != CombatPhase.Idle)
            return false;
        string? mend = _controller.Formulas().LastOrDefault(f => magic.Formulas[f].Targeting == Targeting.Self);
        bool castable = mend is not null && combat.Focus >= magic.Formulas[mend].FocusCost
            && combat.Strain + magic.Formulas[mend].StrainCost <= combat.StrainTolerance;
        if (castable)
            _controller.Cast(mend!);
        else if (!_controller.UseConsumable())
            return true;   // nothing left to mend with: it goes as it stands
        _mendTick = simulation.WorldTick;
        return false;
    }

    /// <summary><see cref="Walk"/> at a walk: footfalls that carry 3 m rather than a run's 8.</summary>
    private bool WalkQuietly(params (double X, double Z)[] route)
    {
        if (_waypoint >= route.Length)
        {
            _controller.SteerWorld(Vector3.Zero, Gait.Walk, _camera);
            return true;
        }
        var body = _controller.Authoritative;
        var (x, z) = route[_waypoint];
        var to = new Vector3((float)(x - body.XMm / 1000.0), 0, (float)(z - body.ZMm / 1000.0));
        if (to.Length() < Near)
            _waypoint++;
        else
            _controller.SteerWorld(to.Normalized(), Gait.Walk, _camera);
        _camera.Yaw = Mathf.Atan2(-to.X, -to.Z);
        return false;
    }

    /// <summary>The last step in, at a walk, until a creature of the definition is within the weapon's reach.</summary>
    private bool CreepInto(string defId)
    {
        var simulation = _session.Simulation!;
        var body = _controller.Authoritative;
        var target = simulation.Creatures.Where(c => c.Alive && c.DefId == defId).OrderBy(c => Distance(body, c.Body)).FirstOrDefault();
        if (target is null)
            return true;
        var to = new Vector3((float)((target.Body.XMm - body.XMm) / 1000.0), 0, (float)((target.Body.ZMm - body.ZMm) / 1000.0));
        _camera.Yaw = Mathf.Atan2(-to.X, -to.Z);
        long radius = _session.Setup.Combat.Creatures[target.DefId].RadiusMm;
        if (to.Length() <= (simulation.Combat.Weapon.ReachMm + radius - 150) / 1000f)
        {
            _controller.SteerWorld(Vector3.Zero, Gait.Walk, _camera, faceCamera: true);
            var combat = simulation.Combat;
            Note(string.Create(CultureInfo.InvariantCulture,
                $"In reach of the {_session.DisplayName(defId)}: it is {target.Mind.ToString().ToLowerInvariant()}, awareness {target.Awareness}, facing {target.Body.FacingMdeg / 1000.0:0} degrees, {to.Length():0.00} m; the character {combat.Health}/{combat.MaxHealth} health"));
            return true;
        }
        _controller.SteerWorld(to.Normalized(), Gait.Walk, _camera, faceCamera: true);
        return false;
    }

    /// <summary>
    /// Fight one creature of a definition: <see cref="Defend"/>'s body with the target fixed and no role filter, because Defend leaves a
    /// sentinel alone. True once none of that kind lives.
    /// </summary>
    private bool Engage(string defId)
    {
        var simulation = _session.Simulation!;
        var body = _controller.Authoritative;
        var target = simulation.Creatures.Where(c => c.Alive && c.DefId == defId).OrderBy(c => Distance(body, c.Body)).FirstOrDefault();
        if (target is null)
            return true;
        if (_phase++ == 0)
            _learnedAtStart = _learned.Count;
        var to = new Vector3((float)((target.Body.XMm - body.XMm) / 1000.0), 0, (float)((target.Body.ZMm - body.ZMm) / 1000.0));
        _camera.Yaw = Mathf.Atan2(-to.X, -to.Z);
        var combat = simulation.Combat;
        long radius = _session.Setup.Combat.Creatures[target.DefId].RadiusMm;
        bool inReach = to.Length() <= (combat.Weapon.ReachMm + radius - 150) / 1000f;
        _controller.SteerWorld(inReach ? Vector3.Zero : to.Normalized(), Gait.Run, _camera, faceCamera: true);
        if (inReach && combat.Phase == CombatPhase.Idle)
            _controller.Attack();
        return false;
    }

    private bool ArmourDown()
    {
        var act = _acts.LastOrDefault();
        if (act is null || act.Kind != "creature_killed" || act.Subject != Armour)
            return Fail("the armour's kill recorded no act");
        if (act.Seq != 2)
            return Fail($"the armour's kill is act {act.Seq}, not act 2");
        if (_learned.Count != _learnedAtStart)
            return Fail("a faction learned of the kill before anyone was told");
        var combat = _session.Simulation!.Combat;
        Note($"The character after the fight: {combat.Health}/{combat.MaxHealth} health");
        return true;
    }

    private (int Respect, int Trust) KeraRegard()
    {
        var record = _session.Simulation!.CaptureRecord();
        int Of(string dimension) => record.Relationships.FirstOrDefault(r => r.NpcId == Kera && r.Dimension == dimension)?.Value ?? 0;
        return (Of("respect"), Of("trust"));
    }

    private int SelTrust() =>
        _session.Simulation!.CaptureRecord().Relationships.FirstOrDefault(r => r.NpcId == Sel && r.Dimension == "trust")?.Value ?? 0;

    private bool StartOfKera()
    {
        _standingAtStart = _standing.Count;
        _keraAtStart = KeraRegard();
        return true;
    }

    private bool BuyABillet()
    {
        var simulation = _session.Simulation!;
        int Billets() => simulation.Player.Inventory.Where(e => e.DefId == Billet).Sum(e => e.Count);
        if (_phase++ == 0)
        {
            var ware = simulation.Wares(Kera)!.Wares.FirstOrDefault(w => w.ItemId == Billet);
            if (ware is null)
                return Fail("the billets are not listed after Kera was told");
            _coinAtStart = simulation.Player.Currency;
            _billetsAtStart = Billets();
            _refused = null;
            _session.Submit(new BuyCommand(simulation.PlayerId, Kera, ware.Ref, 1));
            return false;
        }
        if (_refused is { } refused)
            return Fail($"the billet was refused: {refused}");
        if (Billets() == _billetsAtStart)
            return _phase > 20 && Fail("the billet was not bought");
        return _coinAtStart - simulation.Player.Currency == 20 || Fail($"the billet cost {_coinAtStart - simulation.Player.Currency} coin, not 20");
    }

    private bool KeraTold()
    {
        var told = _standing.Skip(_standingAtStart).ToList();
        var expected = new ReputationChanged(Waystation, 0, 100, "neutral", "accepted", 2, "reported", Kera, told.FirstOrDefault()?.Tick ?? 0);
        if (told.Count != 1 || told[0] != expected)
            return Fail($"Kera's telling moved {string.Join("; ", told)}, not the Waystation 0 -> 100");
        if (KeraRegard() != _keraAtStart)
            return Fail($"Kera's respect and trust moved: {_keraAtStart} -> {KeraRegard()}");
        return true;
    }

    private bool StartOfSel()
    {
        _standingAtStart = _standing.Count;
        _selTrustAtStart = SelTrust();
        return true;
    }

    /// <summary><see cref="Converse"/>, with a look at the replies offered before leaving.</summary>
    private bool ConverseThen(string npcId, Func<ConversationView, string?> check, params string[] replies)
    {
        var simulation = _session.Simulation!;
        int said = _phase >= 2 ? (_phase - 2) / 8 : 0;
        if (_phase >= 2 && said >= replies.Length && (_phase - 2) % 8 == 0 && simulation.Conversation is { } open && check(open) is { } wrong)
            return Fail(wrong);
        return Converse(npcId, replies);
    }

    private static string? NotesOffered(ConversationView open) =>
        open.Replies.Any(r => r.Id == "notes") ? null : $"'notes' is not offered at '{open.NodeId}' once the Survey is told of the heart";

    private static string? NotesClosed(ConversationView open) =>
        open.Replies.Any(r => r.Id == "notes") ? $"'notes' is still offered at '{open.NodeId}' after the Survey learned of the armour" : null;

    private bool SelToldOfTheHeart()
    {
        var told = _standing.Skip(_standingAtStart).ToList();
        var expected = new ReputationChanged(Survey, 0, 100, "neutral", "accepted", 1, "reported", Sel, told.FirstOrDefault()?.Tick ?? 0);
        if (told.Count != 1 || told[0] != expected)
            return Fail($"Sel's telling of Tavar moved {string.Join("; ", told)}, not the Survey 0 -> 100");
        if (SelTrust() - _selTrustAtStart != 5)
            return Fail($"Sel's trust moved {SelTrust() - _selTrustAtStart}, not the authored +5 of brought_tavar_back");
        return true;
    }

    private bool SelToldOfTheArmour()
    {
        var told = _standing.Skip(_standingAtStart).ToList();
        var expected = new ReputationChanged(Survey, 100, 0, "accepted", "neutral", 2, "reported", Sel, told.FirstOrDefault()?.Tick ?? 0);
        if (told.Count != 1 || told[0] != expected)
            return Fail($"Sel's telling of the armour moved {string.Join("; ", told)}, not the Survey 100 -> 0");
        string line = FactionLines.Reported(told[0], _session.Simulation!.Factions.First(f => f.Id == Survey).Name, _session.DisplayName(Sel));
        if (line != "The Survey: neutral (-100), told to Sel Arien")
            return Fail($"the HUD line reads '{line}'");
        if (SelTrust() != _selTrustAtStart)
            return Fail($"Sel's trust moved {SelTrust() - _selTrustAtStart} when she was told of the armour");
        return true;
    }

    // ── moving and looking ──────────────────────────────────────────────────

    /// <summary>Walk a route - fighting whatever hunts the character first, and mending between fights. True at its end.</summary>
    private bool Travel((double X, double Z)[] route, float arrive = 0.5f) => !Defend() && !Mend() && Walk(route, arrive);

    private bool Walk((double X, double Z)[] route, float arrive = 0.5f)
    {
        if (_waypoint >= route.Length)
        {
            _controller.SteerWorld(Vector3.Zero, Gait.Run, _camera);
            return true;
        }
        var body = _controller.Authoritative;
        var (x, z) = route[_waypoint];
        var to = new Vector3((float)(x - body.XMm / 1000.0), 0, (float)(z - body.ZMm / 1000.0));
        if (to.Length() < arrive)
            _waypoint++;
        else
            _controller.SteerWorld(to.Normalized(), Gait.Run, _camera);
        _camera.Yaw = Mathf.Atan2(-to.X, -to.Z);
        return false;
    }

    /// <summary>
    /// Fight the nearest creature hunting the character. A sentinel keeps its post and a stray keeps its distance, so both are left
    /// alone: walking on is the way past them. True while a fight is on.
    /// </summary>
    private bool Defend()
    {
        var simulation = _session.Simulation!;
        var body = _controller.Authoritative;
        var threat = simulation.Creatures
            .Where(c => c.Alive && c.Hostile && c.RoleId is not ("sentinel" or "stray") && Distance(body, c.Body) < 25_000)
            .OrderBy(c => Distance(body, c.Body))
            .FirstOrDefault();
        if (threat is null)
            return false;
        var to = new Vector3((float)((threat.Body.XMm - body.XMm) / 1000.0), 0, (float)((threat.Body.ZMm - body.ZMm) / 1000.0));
        _camera.Yaw = Mathf.Atan2(-to.X, -to.Z);
        var combat = simulation.Combat;
        long radius = _session.Setup.Combat.Creatures[threat.DefId].RadiusMm;
        bool inReach = to.Length() <= (combat.Weapon.ReachMm + radius - 150) / 1000f;
        _controller.SteerWorld(inReach ? Vector3.Zero : to.Normalized(), Gait.Run, _camera, faceCamera: true);
        if (inReach && combat.Phase == CombatPhase.Idle)
            _controller.Attack();
        return true;
    }

    private long _mendTick = -10_000;

    /// <summary>
    /// Below half health and out of a fight, mend once in a while: the last self formula the book taught, or before the book, a salve.
    /// True while it is worked.
    /// </summary>
    private bool Mend()
    {
        var simulation = _session.Simulation!;
        var magic = _session.Setup.Magic;
        var combat = simulation.Combat;
        if (simulation.WorldTick - _mendTick < 40)
            return true;
        if (combat.Health * 2 >= combat.MaxHealth || combat.Phase != CombatPhase.Idle || simulation.WorldTick - _mendTick < 300)
            return false;
        string? mend = _controller.Formulas().LastOrDefault(f => magic.Formulas[f].Targeting == Targeting.Self);
        if (mend is not null && combat.Focus >= magic.Formulas[mend].FocusCost)
            _controller.Cast(mend);
        else if (!_controller.UseConsumable())
            return false;
        _controller.SteerWorld(Vector3.Zero, Gait.Run, _camera);
        _mendTick = simulation.WorldTick;
        return true;
    }

    /// <summary>Open a door the character stands at, facing it; true once it is open.</summary>
    private bool Door(string key)
    {
        if (_controller.IsOpen(key))
            return true;
        var door = _session.Setup.Layout.FindDoor(key)!;
        Look(door.ClosedFootprint.CenterXMm, door.ClosedFootprint.CenterZMm);
        if (_phase++ % 10 == 0)
            _controller.Interact(key);
        return false;
    }

    /// <summary>Walk up to within a hand's reach of an NPC and face them.</summary>
    private bool Approach(string npcId)
    {
        var npc = _session.Simulation!.Npcs.Single(n => n.Id == npcId);
        var body = _controller.Authoritative;
        var away = new Vector3(body.XMm - npc.Body.XMm, 0, body.ZMm - npc.Body.ZMm).Normalized() * 1.3f;
        var spot = new[] { (npc.Body.XMm / 1000.0 + away.X, npc.Body.ZMm / 1000.0 + away.Z) };
        if (_waypoint > 0 && Distance(body, npc.Body) > 1_900)
            _waypoint = 0;   // they moved: go again
        if (!Walk(spot, Near))
            return false;
        Look(npc.Body.XMm, npc.Body.ZMm);
        return true;
    }

    /// <summary>Turn the camera to a point: true, so it can end a beat.</summary>
    private bool Look(long xMm, long zMm)
    {
        var body = _controller.Authoritative;
        _camera.Yaw = Mathf.Atan2(-(xMm - body.XMm) / 1000f, -(zMm - body.ZMm) / 1000f);
        return true;
    }

    /// <summary>Look back at the companion: true, so it can end a beat - or the beat breaks, if there is none.</summary>
    private bool LookAtTavar()
    {
        if (_session.Simulation!.Companions.FirstOrDefault() is not { } tavar)
        {
            _broken = "Tavar is not with the character";
            return false;
        }
        return Look(tavar.Body.XMm, tavar.Body.ZMm);
    }

    private int Carried(string defId) => _session.Simulation!.Player.Inventory.Where(e => e.DefId == defId).Sum(e => e.Count);

    private static double Distance(Body a, Body b) => Math.Sqrt(Math.Pow(a.XMm - b.XMm, 2) + Math.Pow(a.ZMm - b.ZMm, 2));

    private static string Where(Body body) => string.Create(CultureInfo.InvariantCulture, $"({body.XMm / 1000.0:0.0}, {body.ZMm / 1000.0:0.0})");

    private static string Metres(double mm) => string.Create(CultureInfo.InvariantCulture, $"{mm / 1000:0.0} m");
}
