// UNNAMED Presentation - building in the running game: the pieces drawn, build mode and its panel (M7 design §8; WP-1G)
// Godot presentation only: it draws, aims and submits; the simulation decides (D-11)

using Godot;
using UNNAMED.Presentation.Greybox;
using UNNAMED.Presentation.Player;
using UNNAMED.Presentation.Ui;
using UNNAMED.World.Runtime;

namespace UNNAMED.Presentation;

public partial class Main
{
    private StructuresView _structures = null!;
    private BuildMode _build = null!;
    private BuildPanel _buildPanel = null!;
    private Simulation? _buildWorld;

    /// <summary>The placed pieces drawn, build mode bound, and its panel on the HUD: once, as the world's views are made.</summary>
    private void SetUpBuilding()
    {
        _structures = new StructuresView { Name = "Structures" };
        AddChild(_structures);
        _structures.Bind(_session.Setup.Layout, _session.Setup.Building.Catalog, _art.Coverage);
        _build = new BuildMode { Name = "BuildMode" };
        AddChild(_build);
        _build.Bind(_session, _structures, _art.Coverage);
        _buildPanel = new BuildPanel { Name = "BuildPanel", Visible = false };
        _hud.AddChild(_buildPanel);
    }

    /// <summary>What building publishes, shown: the pieces redrawn on a change, a placed door swung, a piece destroyed told.</summary>
    private void SubscribeBuilding()
    {
        _session.Subscribe<StructuresChanged>(_ => _structures.MarkDirty());
        _session.Subscribe<DoorToggled>(e => _structures.SetDoor(e.DoorKey, e.Open));
        _session.Subscribe<PieceDestroyed>(e => _hud.Toast($"The {Names.Of(_session, e.DefId)} is destroyed", 3));
        _session.Subscribe<PieceRemoved>(e => _hud.Toast(e.Refund.IsEmpty ? $"Took down the {Names.Of(_session, e.DefId)}"
            : $"Took down the {Names.Of(_session, e.DefId)}: {string.Join(", ", e.Refund.Select(r => $"{r.Count} {Names.Of(_session, r.ItemId)}"))} back", 3));
    }

    /// <summary>
    /// Build mode's keys (M7 design §8.2-§8.3): B in and out; in it, 1-7 and PageUp/PageDown choose, R turns, the left button places,
    /// Delete twice takes down, N mends. True while build mode is on: then the left button and the digits are building's, not combat's or
    /// the quick bar's.
    /// </summary>
    private bool ReadBuildKeys(bool captured)
    {
        if (_build is null)
            return false;
        if (Input.IsActionJustPressed("build_mode"))
        {
            if (_build.Active)
                _build.Exit(_camera);
            else if (_session.Simulation!.ActiveLayout.SpaceId != _session.Setup.Layout.SpaceId)
                _hud.Toast("You cannot build indoors", 2);
            else if (!_build.Enter(_camera))
                _hud.Toast("Nothing to build with here", 2);
        }
        if (!_build.Active)
            return false;
        for (int n = 1; n <= 7; n++)
        {
            if (Input.IsActionJustPressed($"build_piece_{n}"))
                _build.Select(n - 1);
        }
        if (Input.IsActionJustPressed("build_piece_next"))
            _build.Cycle(1);
        if (Input.IsActionJustPressed("build_piece_prev"))
            _build.Cycle(-1);
        if (Input.IsActionJustPressed("build_rotate"))
            _build.Turn();
        if (captured && Input.IsActionJustPressed("build_place"))
            _build.Place();
        if (Input.IsActionJustPressed("build_dismantle"))
            _build.Dismantle(Time.GetTicksMsec() / 1000.0);
        if (Input.IsActionJustPressed("build_repair"))
            _build.Repair();
        return true;
    }

    /// <summary>Escape leaves build mode first; only the next one frees the mouse. True when it did.</summary>
    private bool LeaveBuild()
    {
        if (_build is null || !_build.Active)
            return false;
        _build.Exit(_camera);
        return true;
    }

    /// <summary>
    /// One frame of building: the pieces redrawn when they changed (outdoors only, where building is), then build mode's aim, ghost and
    /// target, and its panel. A new world (a load, a new game) redraws everything and leaves build mode.
    /// </summary>
    private void BuildingFrame()
    {
        if (_build is null || _session.Simulation is not { } simulation)
            return;
        if (!ReferenceEquals(simulation, _buildWorld))
        {
            _buildWorld = simulation;
            _build.Exit(_camera);
            _structures.MarkDirty();
        }
        bool outside = simulation.ActiveLayout.SpaceId == _session.Setup.Layout.SpaceId;
        _structures.Visible = outside;
        if (!outside)
            _build.Exit(_camera);
        _structures.SyncIfDirty(simulation);
        _build.BuildFrame(simulation, _camera, simulation.Player.Body, Time.GetTicksMsec() / 1000.0);
        _buildPanel.Show(_build.Active ? _build.Panel : null, _build.Status, _build.TargetLine, _build.Tone);
    }

    /// <summary>Building's keys, bound with the rest (B, 1-7, PageUp/PageDown, R, the left button, Delete or Backspace twice, N).</summary>
    private static void DefineBuildInput(Action<string, Key[]> bind)
    {
        bind("build_mode", new[] { Key.B });
        for (int n = 1; n <= 7; n++)
            bind($"build_piece_{n}", new[] { Key.Key1 + n - 1 });   // in build mode the number keys choose a piece, not a quick slot
        bind("build_piece_next", new[] { Key.Pagedown });
        bind("build_piece_prev", new[] { Key.Pageup });
        bind("build_rotate", new[] { Key.R });
        bind("build_dismantle", new[] { Key.Delete, Key.Backspace });
        bind("build_repair", new[] { Key.N });
        if (!InputMap.HasAction("build_place"))
            InputMap.AddAction("build_place");
        InputMap.ActionAddEvent("build_place", new InputEventMouseButton { ButtonIndex = MouseButton.Left });
    }
}
