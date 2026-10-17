// UNNAMED Presentation - the scripted acceptance playthrough and its verification (content bible §29-§31, §33; PROTOTYPE.md §9; M6)
// Godot presentation only

using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using Godot;
using UNNAMED.Application;
using UNNAMED.Domain.Companions;
using UNNAMED.Domain.Magic;
using UNNAMED.Domain.Social;
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
/// the spear, Sel's field notes and Tavar's story, the Foldscar - its three Quiet Stones in turn, the south-east one by the way round the
/// spider, the heart steadied - Tavar freed and recruited, and home with him following; then east to Cairnwold for Durn's spear from
/// the moor's own iron and ash and Mara's three ashbloom from the Sinking Moss; Berrin's rite for the Rim Stone, the stone woken and made
/// the return point, and home again by the stones (S-38). Then a ward (Strain), a save, and quit
/// (§30: save, quit completely). One tick a frame, from a fixed seed: the run is the same every time, so a third party replays it
/// by running it (PROTOTYPE.md §9 item 6) and gets the same <c>state_replay.json</c>. Writes <c>transcript.md</c> (every beat and event
/// with its game time), <c>commands.tsv</c> (every command with its tick), a screenshot per beat, the save in <c>profile/</c>,
/// <c>state_saved.json</c>, the authoritative state field by field, and <c>state_digest.txt</c>. An earlier run's save and state files
/// are cleared first (<see cref="Clear"/>).
/// <para>
/// <c>--playthrough-verify &lt;dir&gt;</c>, the relaunch: loads that save, writes <c>state_loaded.json</c> and <c>state_diff.txt</c> - the
/// field-by-field comparison as a diff of expected against actual (§9 item 2) - and fails unless the load was complete and the state
/// digest is the save's; then tells Tavar to wait, walks to the wolves' den and stands until the pack kills the character, and checks
/// the death's XP debt landed exactly once (C17) and that the character came back at the stone they chose, the Rim Stone (S-38).
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
    private const string Durn = "npc.cairnwold.durn_skell";
    private const string Mara = "npc.cairnwold.mara_ling";
    private const string Ore = "item.material.iron_ore";
    private const string Haft = "item.material.ash_haft";
    private const string Ashbloom = "item.material.herb_ashbloom";
    private const string Berrin = "npc.cairnwold.berrin_garth";
    private const string AshenStone = "waystone.ashen";
    private const string RimStone = "waystone.rim";

    // The content bible's acceptance path (§31), point by point, in the four cells' layout - authored in the hollow's own 200 m square,
    // which region.starting_frontier places at its south-west corner (Hollow).
    private static readonly (double X, double Z)[] ToTheChest = Hollow((34, 150), (38, 138.4));
    private static readonly (double X, double Z)[] ToTheSmithyDoor = Hollow((40, 140), (47, 139), (51.8, 139), (51.8, 142));
    private static readonly (double X, double Z)[] IntoTheSmithy = Hollow((54.5, 142));
    private static readonly (double X, double Z)[] ToTheEastExit = Hollow((51.8, 142), (51.8, 136), (65, 136), (88, 145));
    private static readonly (double X, double Z)[] ToTheHound = Hollow((108, 148), (128, 150));
    private static readonly (double X, double Z)[] ToTheCart = Hollow((145, 156), (157, 158.8));
    private static readonly (double X, double Z)[] ToTheStand = Hollow((165, 150), (175, 142), (179, 138.3));
    // South-west from the stand, keeping clear of the strays' patch round (118, 128) - the wolves are the prototype's, not the bible's.
    private static readonly (double X, double Z)[] ToTheQuarry = Hollow((170, 118), (150, 106), (125, 102), (100, 110), (80, 106), (63, 98));
    private static readonly (double X, double Z)[] ToTheSeam = Hollow((60, 90), (54, 74), (48, 62), (40, 54), (34, 48), (27.2, 44.2));
    private static readonly (double X, double Z)[] HomeToTheSmithy =
        Hollow((34, 48), (40, 54), (48, 62), (54, 74), (60, 90), (63, 98), (64, 112), (58, 134), (51.8, 136), (51.8, 142));
    private static readonly (double X, double Z)[] ToTheHearth = Hollow((59.5, 143.2));
    private static readonly (double X, double Z)[] ToTheAnvil = Hollow((58.2, 141.4));
    private static readonly (double X, double Z)[] OutToSel = Hollow((54.5, 142), (51.8, 142), (51.8, 136), (60, 128));
    private static readonly (double X, double Z)[] ToTheFoldscar = Hollow((90, 112), (105, 108), (120, 93), (140, 80));
    private static readonly (double X, double Z)[] ToTheNorthStone = Hollow((148.5, 78));
    private static readonly (double X, double Z)[] ToTheSouthWestStone = Hollow((135, 60), (123.5, 38));
    private static readonly (double X, double Z)[] ToTheSouthEastStone = Hollow((140, 62), (160, 65), (187, 65), (194, 45), (182.5, 31));
    private static readonly (double X, double Z)[] ToTheHeart = Hollow((194, 45), (187, 65), (160, 65), (153, 50.2));
    private static readonly (double X, double Z)[] HomeWithTavar = Hollow((150, 45), (135, 70), (100, 100), (80, 118), (62, 130), (48, 137));
    private static readonly (double X, double Z)[] ToTheDen = Hollow((55, 136), (66, 140), (66, 150), (90, 162), (104, 168), (112, 172));
    // Cairnwold, east along the road and back (region coordinates, planned round the region's blockers with the smithy door open).
    private static readonly (double X, double Z)[] ToCairnwold =
        { (744.8, 732.5), (745.5, 731.8), (908.8, 705.5), (911.8, 702.8), (937, 683), (964.5, 679.8), (970.2, 678), (976.5, 674.2) };
    private static readonly (double X, double Z)[] ToTheCairnwoldSeam =
        { (980.8, 657), (979, 655.2), (977.2, 648.2), (972.2, 640.2), (971, 639), (970.5, 633.2), (956.8, 609.2), (957.8, 605.5) };
    private static readonly (double X, double Z)[] ToDurnsDoor = { (956.8, 609.5), (971, 637), (971.8, 644.2), (978.2, 651.5), (980.2, 661), (980.8, 661.5), (982, 677.5) };
    private static readonly (double X, double Z)[] ToDurnsHearth = { (982.2, 679.8), (983.5, 683.5) };
    private static readonly (double X, double Z)[] ToTheCairnwoldStand = { (982, 680.5), (982, 677.5), (987.2, 678.2), (988, 679), (984.5, 692.5), (975, 713.2) };
    private static readonly (double X, double Z)[] BackToDurnsDoor = { (986.5, 685.2), (988, 679), (987.2, 678.2), (982, 677.5) };
    private static readonly (double X, double Z)[] ToDurnsAnvil = { (982, 679), (981.2, 681.2) };
    private static readonly (double X, double Z)[] OutOfDurnsSmithy = { (982, 679), (982, 677.5) };
    private static readonly (double X, double Z)[] ToTheMoss =
        { (985, 677.2), (987.5, 678.5), (1004.2, 703.5), (1004.5, 706.2), (1036.8, 748.5), (1037, 750.5), (1042.8, 751.8), (1044, 755.5) };
    private static readonly (double X, double Z)[] ToTheMossNorth = { (1048, 780.2) };
    private static readonly (double X, double Z)[] ToTheMossEast = { (1059.5, 775.5) };
    private static readonly (double X, double Z)[] BackToMara =
        { (1042.5, 751.5), (1037, 750.5), (1032.8, 743.2), (988, 684.2), (987.8, 678.8), (985.2, 677), (984.5, 676.8), (976.2, 666.2) };
    // The Ashen Waystone, from the spawn beside it; Berrin at the long lodge's east door from Mara's well, and up to the Rim Stone from
    // him (planned round the village's blockers, with every door shut).
    private static readonly (double X, double Z)[] ToTheAshenStone = { (428.9, 758) };
    private static readonly (double X, double Z)[] ToBerrin = { (978, 653), (978.5, 651), (971, 643), (961, 644), (959, 645) };
    private static readonly (double X, double Z)[] ToTheRimStone =
        { (955.5, 648.5), (955.5, 659.5), (954, 661), (949.5, 661), (944, 662), (938, 667), (936, 667), (908.5, 677.5), (906, 677.5), (882, 702), (880.6, 702.5) };

    /// <summary>The Ashen Hollow's south-west corner in region.starting_frontier: recovery 04f079a moved every place in it by this.</summary>
    private const double HollowX = 400, HollowZ = 600;

    /// <summary>Points authored in the hollow's own square, in the region's coordinates.</summary>
    internal static (double X, double Z)[] Hollow(params (double X, double Z)[] points) =>
        points.Select(p => (p.X + HollowX, p.Z + HollowZ)).ToArray();

    /// <summary>
    /// The way to someone, for the harnesses: over WP12's navigation lattice, through doors that stand open, and to a hand's reach (1.3 m)
    /// on the side the way arrives from. Someone sheltering from rain stands in a row inside the longhouse: walked at in a straight line
    /// they are a wall away, and from the wrong side the row pens the character short of talk reach. With no way found, the straight line.
    /// </summary>
    internal static (double X, double Z)[] WayTo(Simulation simulation, Body from, Body to)
    {
        var navigation = simulation.Navigation;
        var config = simulation.Setup.Navigation;
        bool Open(NavInput gate) => navigation.Gates.FirstOrDefault(g => g.Key == gate.GateKey)?.Open ?? false;
        var plan = NavSearch.Plan(new NavQuery(navigation.Grid, Open, config, new NavScratch(), null), new NavAgent(config.PersonClass, false),
            new NavPoint(from.XMm, from.ZMm), new NavPoint(to.XMm, to.ZMm));
        var corners = plan.Outcome == NavOutcome.Found && !plan.Partial && plan.Corners.Length > 0
            ? plan.Corners.RemoveAt(plan.Corners.Length - 1)
            : ImmutableArray<NavPoint>.Empty;
        var last = corners.Length > 0 ? corners[^1] : new NavPoint(from.XMm, from.ZMm);
        double dx = last.XMm - to.XMm, dz = last.ZMm - to.ZMm, length = Math.Max(1, Math.Sqrt(dx * dx + dz * dz));
        return corners.Select(c => (c.XMm / 1000.0, c.ZMm / 1000.0))
            .Append((to.XMm / 1000.0 + dx / length * 1.3, to.ZMm / 1000.0 + dz / length * 1.3)).ToArray();
    }

    private (double X, double Z)[]? _way;
    private string? _wayTo;
    private Body _wayFrom = new(0, 0, 0, 0);

    private sealed record Beat(string Name, string Says, int Budget, Func<bool> Run, string? Shot = null);

    private readonly GameSession _session;
    private readonly PlayerController _controller;
    private readonly CameraRig _camera;
    private readonly DialoguePanel _dialogue;
    private readonly bool _verify;
    private readonly (string Slot, SaveCopy Copy, LoadResult Result)? _continued;
    private readonly List<Beat> _beats = new();

    /// <summary>Opens a management screen by name through Main ("" closes them): the map pictured after the journey.</summary>
    private readonly Action<string>? _screen;
    private readonly StringBuilder _transcript = new();
    private readonly List<string> _failures = new();

    private int _beat;
    private long _beatTick;
    private int _waypoint;
    private int _turnSteps;
    private int _phase;
    private int _before;
    private int _wait;
    private string? _pending;
    private int _frame;
    private int _deaths;
    private long _debtAdded;
    private long _debtBefore;
    private bool _respawned;
    private Body? _respawnedAt;
    private string? _broken;

    public Playthrough(GameSession session, PlayerController controller, CameraRig camera, DialoguePanel dialogue, string directory, bool verify,
        (string Slot, SaveCopy Copy, LoadResult Result)? continued = null, Action<string>? screen = null)
    {
        _screen = screen;
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
            // The one condition the standing-turn state exists for, and the one the route otherwise never produces:
            // the body is still, and the camera swings, so with faceCamera it steps around rather than sliding.
            new Beat("turn", "Standing at the waystone: the body steps around to a new facing", 900, TurnOnTheSpot, "01b_turn"),
            new Beat("waystone", "The Ashen Waystone touched: the first of the Waystones you know (S-38)", 600, () => TouchStone(AshenStone, ToTheAshenStone), "01c_waystone"),
            new Beat("chest", "The waystation's storage chest opened and emptied (§29 0:30)", 1_500, OpenTheChest, "02_chest"),
            new Beat("smithy", "Into Kera's smithy", 1_500, () => Travel(ToTheSmithyDoor) && Door("door.forge_shed") && Walk(IntoTheSmithy)),
            new Beat("kera", "Kera teaches the forge: Iron Under Ash begins (§29 2:00)", 900, () => Converse(Kera, "teach", "thanks"), "03_kera"),
            new Beat("east", "Out of the waystation by the east road (88, 145)", 2_000, () => Travel(ToTheEastExit)),
            new Beat("hound", "Charwood: the hound's ground (128, 150) - fought if it hunts (§29 4:00)", 3_000, () => Travel(ToTheHound), "04_charwood"),
            new Beat("cart", "The ruined merchant cart: the hunting bow and arrows (§29 5:00)", 2_500, TakeFromTheCart, "05_cart"),
            new Beat("stand", "An ash haft cut at the stand", 2_500, () => Gather("ash_stand", ToTheStand, Haft), "06_haft"),
            new Beat("quarry", "Back south-west and down to Blackvein Cut's mouth (63, 98) (§29 6:00)", 5_000, () => Travel(ToTheQuarry), "07_quarry"),
            new Beat("seam", "Past the Bone Walker's ground to the seam: raw iron ore (§29 7:00-8:00)", 5_000, () => Gather("iron_seam", ToTheSeam, Ore), "08_ore"),
            new Beat("home", "Home north to the smithy (§29 9:00)", 7_000, () => Travel(HomeToTheSmithy) && Door("door.forge_shed") && Walk(IntoTheSmithy)),
            new Beat("billet", "A billet smelted at the hearth", 1_500, () => Make("forge", ToTheHearth), "09_billet"),
            new Beat("spear", "The March Spear forged at the anvil, and taken in hand", 1_500, () => Make("anvil", ToTheAnvil) && WieldTheSpear(), "10_spear"),
            new Beat("quest1", "Kera shown the spear: Iron Under Ash complete", 1_200, () => Show(Kera, "quest.ashen_hollow.iron_under_ash"), "11_iron_under_ash"),
            new Beat("sel", "Sel: her field notes, and Tavar's story - The Three Quiet Stones begins", 2_000, TalkToSel, "12_sel"),
            new Beat("primer", "The field notes read: what they teach is known", 300, ReadSelsBook),
            new Beat("foldscar", "The Foldscar path (105, 108) and in", 4_000, () => Travel(ToTheFoldscar)),
            new Beat("north", "The north Quiet Stone turned into line", 1_500, () => Turn("switch.stone_north", ToTheNorthStone), "13_north_stone"),
            new Beat("southwest", "The south-west Quiet Stone turned into line", 2_500, () => Turn("switch.stone_southwest", ToTheSouthWestStone), "14_southwest_stone"),
            new Beat("southeast", "The south-east Quiet Stone turned, by the way round the spider (§8)", 4_000, () => Turn("switch.stone_southeast", ToTheSouthEastStone), "15_southeast_stone"),
            new Beat("fold", "At the heart: Tavar in the fold, out of reach", 3_000, () => Travel(ToTheHeart) && LookAt(Tavar), "16_the_fold"),
            new Beat("heart", "The heart steadied: the fold lifts", 300, () => Turn("switch.foldscar_heart", Array.Empty<(double, double)>()) && LookAt(Tavar), "17_heart_steadied"),
            new Beat("tavar", "Tavar freed - Quest 2 complete - and recruited", 1_200, () => Converse(Tavar, "sent", "join", "ok"), "18_tavar_joins"),
            new Beat("follow", "Home with Tavar following", 8_000, () => Travel(HomeWithTavar) && LookAtTavar(), "19_home_with_tavar"),
            // Cairnwold's two quests: Durn's spear from the moor's own iron and ash, and Mara's three ashbloom from the Sinking Moss.
            new Beat("cairnwold", "East along the road to Cairnwold", 9_000, () => Travel(ToCairnwold), "c01_cairnwold"),
            new Beat("durn", "Durn teaches the forge: Iron From The Moor begins", 900, () => Converse(Durn, "teach", "thanks"), "c02_durn"),
            new Beat("moor_seam", "Down to the crags: ore from Cairnwold's own seam", 3_000, () => Gather("cairnwold_iron_seam", ToTheCairnwoldSeam, Ore), "c03_moor_ore"),
            new Beat("moor_home", "Back up into the village, to Durn's smithy door", 3_000, () => Travel(ToDurnsDoor) && Door("door.smithy")),
            new Beat("moor_billet", "A billet smelted at Durn's hearth", 1_500, () => Make("forge", ToDurnsHearth), "c04_moor_billet"),
            new Beat("moor_stand", "An ash haft cut at the stand north of the basin", 2_500, () => Gather("cairnwold_ash_stand", ToTheCairnwoldStand, Haft), "c05_moor_haft"),
            new Beat("moor_back", "Back to the smithy", 2_500, () => Travel(BackToDurnsDoor)),
            new Beat("moor_spear", "A March Spear forged at Durn's anvil", 1_500, () => Make("anvil", ToDurnsAnvil), "c06_moor_spear"),
            new Beat("moor_out", "Out of the smithy", 600, () => Walk(OutOfDurnsSmithy)),
            new Beat("quest3", "Durn shown the spear: Iron From The Moor complete", 1_200, () => Show(Durn, "quest.cairnwold.iron_from_the_moor"), "c07_iron_from_the_moor"),
            new Beat("mara", "Mara at the well: What The Bog Kept begins", 6_000, () => Converse(Mara, "ask", "offer", "back"), "c08_mara"),   // her meal waited out (WP12)
            new Beat("moss", "North-east into the Sinking Moss: an ashbloom", 5_000, () => Gather("bog_ashbloom_1", ToTheMoss, Ashbloom), "c09_moss"),
            new Beat("moss_north", "A second ashbloom, further in", 1_200, () => Gather("bog_ashbloom_3", ToTheMossNorth, Ashbloom)),
            new Beat("moss_east", "A third, where the ground is wettest", 1_200, () => Gather("bog_ashbloom_2", ToTheMossEast, Ashbloom), "c10_ashbloom"),
            new Beat("moss_back", "Back to the well", 5_000, () => Travel(BackToMara)),
            new Beat("quest4", "Mara given three ashbloom: What The Bog Kept complete", 6_000, () => Converse(Mara, "bog_return", "back") && Completed("quest.cairnwold.what_the_bog_kept"), "c11_what_the_bog_kept"),
            // The Rim Stone (NEXT_AREA_TRAVEL_CITY_PLAN.md K1, D.3): Berrin's rite, the stone woken, made the return point, and home by it.
            new Beat("berrin", "Berrin reads the Rim Stone's rite from the lodge book: three hands and a name", 3_000, AskBerrin, "c12_berrin"),
            new Beat("rim", "Up to the Rim: the cold stone woken with Berrin's rite", 4_000, () => TouchStone(RimStone, ToTheRimStone), "c13_rim_woken"),
            new Beat("bind", "The Rim Stone made the return point, by its own act at the stone", 300, () => BindStone(RimStone), "c14_return_point"),
            new Beat("homeward", "Home by the stones, Tavar with the character: the Rim Stone to the Ashen Waystone, the world a quarter of the road's walk on", 5_000,
                () => FastTravel(RimStone, AshenStone), "c15_by_the_stones"),
            new Beat("map", "The map: the places found, the Old East Road walked, the two stones known and the return point", 120, () => Screen("map"), "c16_map"),
            new Beat("map_closed", "The map put away", 120, () => Screen("")),
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
            new Beat("respawn", "Back at the stone chosen as the return point, the Rim Stone, the debt owed once (C17)", 1_200, CheckTheDebt, "33_respawn"),
        });
    }

    private bool OpenTheChest() => Walk(ToTheChest, Near) && Empty("container.waystation_chest");

    /// <summary>Take everything a container holds, as the inventory's Take buttons do, facing it; true once it is empty.</summary>
    private bool Empty(string key)
    {
        var box = _session.Simulation!.Containers.Single(c => c.Site.Key == key);
        Look(box.Site.XMm, box.Site.ZMm);
        if (box.Items.IsEmpty)
            return _phase > 0 || (_broken = $"{key} was empty before anything was taken") is null;
        if (_phase++ > 3)
            _broken = $"{key} still holds {box.Items.Length} stacks: the takes were refused";
        // Last stack first: a stack's reference is its place in the container, so taking the first renumbered the rest and their takes
        // were refused on screen ("there is no ...#01 in the container").
        foreach (var item in box.Items.Reverse())
            _session.Submit(new MoveItemCommand(_session.Simulation!.PlayerId, item.Ref, ItemPlace.In(key), ItemPlace.Carried, item.Count));
        return false;
    }

    private bool TakeFromTheCart() => Travel(ToTheCart, Near) && Empty("container.merchant_cart");

    /// <summary>Walk to a node and work it, facing it: true once it has given at least one more of its item than was carried.</summary>
    private bool Gather(string name, (double X, double Z)[] route, string itemId)
    {
        if (!Travel(route, Near))
            return false;
        var node = _session.Simulation!.Nodes.Single(n => n.Name == name);
        Look(node.XMm, node.ZMm);
        if (_phase == 0)
            _before = Carried(itemId);
        else if (Carried(itemId) > _before)
            return true;
        if (_phase++ % 10 == 0)
            _controller.Gather(node.Key);
        return false;
    }

    /// <summary>At the nearest station of this kind, make the first recipe the character knows for it, once.</summary>
    private bool Make(string kind, (double X, double Z)[] spot)
    {
        if (!Walk(spot, Near))
            return false;
        var body = _controller.Authoritative;
        var station = _session.Setup.Layout.Stations.Where(s => s.Kind == kind)
            .OrderBy(s => Math.Pow(s.XMm - body.XMm, 2) + Math.Pow(s.ZMm - body.ZMm, 2)).First();
        Look(station.XMm, station.ZMm);
        var recipe = _controller.Recipes(kind).First();
        if (_phase++ > 0)
            return Carried(recipe.OutputItemId) > _before;
        _before = Carried(recipe.OutputItemId);
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

    /// <summary>Show a smith the spear: the first "show" reply they offer, then leave; true once their quest is complete.</summary>
    private bool Show(string npcId, string questId)
    {
        var simulation = _session.Simulation!;
        switch (_phase)
        {
            case 0:
                if (Approach(npcId))
                {
                    _controller.Talk(npcId);
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
                return Completed(questId);
        }
    }

    private bool Completed(string questId) =>
        _session.Simulation!.Quests.Any(q => q.Id == questId && q.Status == Domain.Quests.QuestStatus.Completed);

    /// <summary>To Berrin at the long lodge's door, and his lines on the Rim Stone; true once the conversation is over.</summary>
    private bool AskBerrin()
    {
        if (_phase == 0)
        {
            if (!Travel(ToBerrin, Near))
                return false;
            _phase = 1;
            _waypoint = 0;
        }
        return Converse(Berrin, "rim_stone", "back");
    }

    /// <summary>Walk to a Waystone and lay a hand on it, facing it: true once the character knows it.</summary>
    private bool TouchStone(string key, (double X, double Z)[] spot)
    {
        if (!Travel(spot, Near))
            return false;
        var view = _session.Simulation!.Waystones.Single(w => w.Site.Key == key);
        var (x, z) = Footprints.Center(view.Site.Body);
        Look(x, z);
        if (view.Known)
            return _phase > 0 || (_broken = $"{key} was known before it was touched") is null;
        if (_phase++ % 10 == 0)
            _controller.TouchStone(key);
        if (_phase > 40)
            _broken = $"{key} would not answer: the touches were refused";
        return false;
    }

    /// <summary>Make the stone the character stands at their return point, as its panel's button does; true once it is.</summary>
    private bool BindStone(string key)
    {
        if (_session.Simulation!.Waystones.Single(w => w.Site.Key == key).ReturnPoint)
            return true;
        if (_phase++ % 10 == 0)
            _controller.BindStone(key);
        return false;
    }

    private long _leftAt;

    /// <summary>Open a screen by name ("" closes them), and give it a few frames to lay out; true then.</summary>
    private bool Screen(string name)
    {
        if (_screen is null)
            return true;
        if (_phase == 0)
            _screen(name);
        return ++_phase > 10;
    }

    /// <summary>
    /// Go by the stones, as the panel's Travel button does, and check what it did: the character set down beside the far stone, every
    /// following companion with them, the world on by exactly the journey's cost, and the return point untouched.
    /// </summary>
    private bool FastTravel(string from, string to)
    {
        var simulation = _session.Simulation!;
        if (_phase == 0)
        {
            var option = simulation.TravelOptions(from).Single(o => o.ToKey == to);
            if (option.Blocked is { } why)
                return (_broken = $"the stones would not go: {why}") is null;
            _leftAt = simulation.WorldTick;
            Note($"At the {_session.Setup.Layout.FindWaystone(from)!.Name}: {TravelWords.Journey(option.Cost.WalkTicks, Main.TicksPerDay)} ({option.Cost.RoadMm / 1000.0:0} m by road)");
            _controller.TravelTo(from, to);
            _phase = 1;
            return false;
        }
        if (_phase++ < 4)
            return false;
        var site = _session.Setup.Layout.FindWaystone(to)!;
        var body = simulation.Player.Body;
        string? returnPoint = simulation.Waystones.FirstOrDefault(w => w.ReturnPoint)?.Site.Key;
        string party = string.Join(", ", simulation.Companions.Select(c => $"{c.Name} {Metres(Distance(c.Body, body))} away"));
        Note($"Arrived at {Where(body)}, {Metres(Math.Sqrt(Math.Pow(body.XMm - site.ArrivalXMm, 2) + Math.Pow(body.ZMm - site.ArrivalZMm, 2)))} from the stone's arrival; " +
             $"world tick {_leftAt} -> {simulation.WorldTick}; companions: {party}; return point {returnPoint}");
        if (Math.Sqrt(Math.Pow(body.XMm - site.ArrivalXMm, 2) + Math.Pow(body.ZMm - site.ArrivalZMm, 2)) > 2_000)
            _failures.Add($"the journey set the character down at {Where(body)}, not beside the {site.Name}");
        if (simulation.WorldTick - _leftAt > 60)   // the few frames the journey is watched for, and no more: the stones skip no time (DN8)
            _failures.Add($"the world went on {simulation.WorldTick - _leftAt} ticks over the journey: the stones skip no world time");
        if (simulation.Companions.Any(c => c.Order == CompanionOrder.Follow && Distance(c.Body, body) > 8_000))
            _failures.Add("a following companion was left behind by the stones");
        if (returnPoint != RimStone)
            _failures.Add($"the return point is {returnPoint ?? "none"} after the journey, not the Rim Stone it was bound to");
        return LookAtTavar();
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

    /// <summary>Read the book Sel gave (her field notes since 6c012d2, the primer before): true once every formula it teaches is known.</summary>
    private bool ReadSelsBook()
    {
        var simulation = _session.Simulation!;
        var magic = _session.Setup.Magic;
        if (simulation.Player.Inventory.FirstOrDefault(e => magic.Teaches.ContainsKey(e.DefId)) is { } book)
        {
            _taught = magic.Teaches[book.DefId];
            _session.Submit(new UseItemCommand(simulation.PlayerId, book.ItemId));
            return false;
        }
        return !_taught.IsDefaultOrEmpty && _taught.All(f => _controller.Formulas().Contains(f));
    }

    private ImmutableArray<string> _taught;

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
        // Back at the stone chosen as the return point (S-38): the Rim Stone the acceptance run bound, not the region's spawn.
        var stone = _session.Setup.Layout.FindWaystone(RimStone)!;
        var body = simulation.Player.Body;
        double off = Math.Sqrt(Math.Pow(body.XMm - stone.ArrivalXMm, 2) + Math.Pow(body.ZMm - stone.ArrivalZMm, 2));
        Note($"Came back {Metres(off)} from the {stone.Name}'s arrival point");
        if (_respawnedAt is { } at && Math.Sqrt(Math.Pow(at.XMm - stone.ArrivalXMm, 2) + Math.Pow(at.ZMm - stone.ArrivalZMm, 2)) > 1)
            _failures.Add($"the character came back at {Where(at)}, not at the Rim Stone they chose");
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
            _respawnedAt = e.Body;
            string at = _session.Simulation!.Waystones.FirstOrDefault(w => w.ReturnPoint)?.Site.Name ?? "Ashen Waystone (the region's spawn)";
            Note($"Returned at the {at}: {Where(e.Body)}");
        });
        _session.Subscribe<StoneTouched>(e => Note($"{(e.Method == Domain.Travel.WaystoneMethod.Woken ? "Woken" : "Touched")}: {e.StoneKey} - \"{e.Text}\""));
        _session.Subscribe<ReturnPointBound>(e => Note($"Return point: {e.StoneKey}"));
        _session.Subscribe<PlayerTravelled>(e => Note($"By the stones: {e.FromKey} to {e.ToKey}, {e.Cost.RoadMm / 1000.0:0} m of road, " +
            $"{e.Cost.WalkTicks} ticks of walking spared; with {(e.Party.IsEmpty ? "no one" : string.Join(", ", e.Party.Select(Name)))}"));
    }

    private void Note(string line)
    {
        long tick = _session.Simulation?.WorldTick ?? 0;
        double seconds = tick * _session.TickSeconds;
        _transcript.AppendLine(string.Create(CultureInfo.InvariantCulture, $"| {(int)(seconds / 60)}:{(int)(seconds % 60):00} | {tick} | {line.Replace("|", "/")} |"));
    }

    // ── moving and looking ──────────────────────────────────────────────────

    /// <summary>Walk a route - fighting whatever hunts the character first, and mending between fights. True at its end.</summary>
    private bool Travel((double X, double Z)[] route, float arrive = 0.5f) => !Defend() && !Mend() && Walk(route, arrive);

    /// <summary>
    /// Stand still and swing the camera. With faceCamera the body follows the camera while stationary, which is
    /// the only way the simulation expresses a turn in place - PlayerController sends a facing with a direction
    /// of travel and otherwise keeps the last one - and so the only way the standing-turn state is ever reached.
    /// </summary>
    private bool TurnOnTheSpot()
    {
        _controller.SteerWorld(Vector3.Zero, Gait.Run, _camera, faceCamera: true);
        // The wish JUMPS a quarter turn; it does not creep. Creeping at 0.05 rad a frame let the body keep up,
        // so it never fell more than 20 degrees behind and the standing-turn state - which needs the body to be
        // behind by about that much - never fired. Snapping the wish and then holding is what a player does when
        // they flick the stick round, and it is the condition the state was written for.
        if (_turnSteps == 0)
            _camera.Yaw += 1.4f;
        return ++_turnSteps > 90;   // hold still while the body steps round to the new facing
    }

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

    /// <summary>
    /// Walk up to within a hand's reach of an NPC and face them. Someone away from their post to eat, visit or shop (WP12) is waited for: a
    /// beat meets them where it says, and following them into a crowd at the cookfire can pen the character in until the meal is over.
    /// </summary>
    private bool Approach(string npcId)
    {
        if (_session.Simulation!.Lives.FirstOrDefault(l => l.Key == npcId) is { Activity: NpcActivities.Eat or NpcActivities.Socialise or NpcActivities.Shop })
        {
            _controller.SteerWorld(Vector3.Zero, Gait.Run, _camera);
            return false;
        }
        var npc = _session.Simulation!.Npcs.Single(n => n.Id == npcId);
        var body = _controller.Authoritative;
        if (_way is null || _wayTo != npcId || Distance(_wayFrom, npc.Body) > 1_000)
        {
            // Planned once, again only if they moved: the way goes round walls and in through doors.
            (_way, _wayTo, _wayFrom, _waypoint) = (WayTo(_session.Simulation!, body, npc.Body), npcId, npc.Body, 0);
        }
        if (!_controller.InTalkReach(npc) && !Walk(_way, Near))
            return false;
        _way = null;
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

    /// <summary>Turn the camera to an NPC: true, so it can end a beat.</summary>
    private bool LookAt(string npcId)
    {
        var npc = _session.Simulation!.Npcs.Single(n => n.Id == npcId);
        return Look(npc.Body.XMm, npc.Body.ZMm);
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
