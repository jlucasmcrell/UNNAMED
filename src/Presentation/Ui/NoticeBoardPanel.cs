// UNNAMED Presentation - the inn's notice board (spec 15, owner finding 15): one board, the contracts and who is out
// Godot presentation only: it reads the world's views and holds no state of its own (D-11)

using Godot;
using UNNAMED.Application;
using UNNAMED.Presentation.Player;
using UNNAMED.World.Runtime;

namespace UNNAMED.Presentation.Ui;

/// <summary>
/// The board by the inn's east door: the work the hamlet is paying for - bounties and contracts, open, taken or done - and who is out on
/// their day, which is what the empty bed and the empty seat are saying. Read only: the board posts, the character answers it elsewhere.
/// It is read standing at it, and closes when the character walks away.
/// </summary>
public partial class NoticeBoardPanel : CanvasLayer
{
    /// <summary>The station kind a board is placed as, in the region's layout (<c>stations: kind: notice_board</c>).</summary>
    public const string Kind = "notice_board";

    /// <summary>Further than this from the board, the panel closes: a board is read where it hangs.</summary>
    private const long KeepOpenMm = 3_000;

    private Sheet _sheet = null!;
    private GameSession _session = null!;
    private string? _stationKey;
    private string? _drawn;

    /// <summary>The station the panel is open at, or null.</summary>
    public string? StationKey => _stationKey;

    public void Bind(GameSession session) => _session = session;

    public override void _Ready()
    {
        Visible = false;
        _sheet = UiKit.Sheet(this, "The notice board", "what the hamlet is paying for, and who is out");
        _sheet.OnShown(() =>
        {
            _drawn = null;
            Callable.From(() => UiKit.FirstFocusable(_sheet.Root)?.GrabFocus()).CallDeferred();
        });
    }

    /// <summary>Open at a board the character stands at.</summary>
    public void Open(string stationKey)
    {
        _stationKey = stationKey;
        _drawn = null;
        Visible = true;
    }

    public void Close()
    {
        Visible = false;
        _stationKey = null;
    }

    public override void _Process(double delta)
    {
        if (!Visible || _session.Simulation is not { } simulation || _stationKey is not { } key)
            return;
        var board = simulation.ActiveLayout.Stations.FirstOrDefault(s => s.Key == key);
        var body = simulation.Player.Body;
        if (board is null || board.Kind != Kind || Distance(board, body) > KeepOpenMm)
        {
            Close();
            return;
        }
        var contracts = NoticeBoardModel.Contracts(simulation.Bounties, simulation.PlayerId);
        var away = NoticeBoardModel.Away(simulation.Adventurers, id => Names.Of(_session, id));
        string signature = $"{simulation.WorldTick}|{string.Join(",", contracts.Select(c => $"{c.Title}:{c.Standing}"))}|" +
                           string.Join(",", away.Select(a => $"{a.Name}:{a.Doing}"));
        if (signature == _drawn)
            return;
        _drawn = signature;
        Build(contracts, away);
    }

    private static long Distance(Domain.Spatial.StationSite board, Domain.Spatial.Body body)
    {
        long dx = board.XMm - body.XMm, dz = board.ZMm - body.ZMm;
        return (long)Math.Sqrt(dx * dx + dz * dz);
    }

    private void Build(System.Collections.Immutable.ImmutableArray<NoticeContract> contracts,
        System.Collections.Immutable.ImmutableArray<NoticeAway> away)
    {
        UiKit.Clear(_sheet.Body);
        var work = UiKit.Section("Work posted", "a competitive bounty goes to whoever finishes it first");
        if (contracts.IsEmpty)
            work.AddChild(UiKit.EmptyState("Nothing posted", "Renn pins what comes in, and nothing has come in."));
        foreach (var contract in contracts)
            work.AddChild(UiKit.Row(null, contract.Title, contract.Standing, contract.Open ? HudStyle.Bone : HudStyle.Ash).Root);
        _sheet.Body.AddChild(work);

        var outOnTheirDay = UiKit.Section("Out on their day", "the beds that are unmade and the seats that are empty");
        if (away.IsEmpty)
            outOnTheirDay.AddChild(UiKit.EmptyState("Everyone is in", "Not a boot missing from the rack."));
        foreach (var one in away)
            outOnTheirDay.AddChild(UiKit.Row(null, one.Name, one.Doing, HudStyle.Bone).Root);
        _sheet.Body.AddChild(outOnTheirDay);

        _sheet.Body.AddChild(UiKit.Quiet("The board is Renn's. It says what the hamlet pays, not what it promises.", HudStyle.Muted));
    }
}
