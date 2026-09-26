// UNNAMED Presentation - build mode (M7 design §8.4-§8.8)
// Godot presentation only: it aims, shows the authority's advisory preview, and submits; it never decides legality (D-11)

using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using Godot;
using UNNAMED.Application;
using UNNAMED.Domain;
using UNNAMED.Domain.Building;
using UNNAMED.Domain.Spatial;
using UNNAMED.Presentation.Greybox;
using UNNAMED.Presentation.Ui;
using UNNAMED.World.Runtime;

namespace UNNAMED.Presentation.Player;

/// <summary>How a build line is toned: plain words, or the ghost's verdict. Colour is never the only signal: the words say it too.</summary>
public enum BuildTone { Plain, Allowed, Unchecked, Refused }

/// <summary>
/// Build mode (M7 design §8.4): a gameplay mode, never modal, and never saved. It aims at the ground with the camera's centre ray (the
/// domain terrain, not physics: G4), snaps with the domain's <see cref="Snapper"/>, and shows a ghost toned only by
/// <see cref="Simulation.PreviewPlacement"/> - checks 1-14 every frame, navigability when the pose or the structures change and at most
/// every half second. Left mouse always submits the ghost's pose; the authority answers. Taking down wants the key twice on the same
/// target within two seconds.
/// </summary>
public partial class BuildMode : Node3D
{
    private static readonly EntityId GhostId = EntityId.Parse("pce_00000000000000000000000000");
    private static readonly PieceFamily[] FamilyOrder =
        { PieceFamily.Pad, PieceFamily.Wall, PieceFamily.Doorway, PieceFamily.Door, PieceFamily.Roof, PieceFamily.Storage, PieceFamily.Station };

    private GameSession _session = null!;
    private StructuresView _structures = null!;
    private Art.ArtCoverage? _coverage;
    private Node3D? _ghost;
    private (string DefId, long X, long Z, int R)? _ghostPose;
    private StandardMaterial3D? _ghostTone;

    public bool Active { get; private set; }

    /// <summary>Which piece is chosen, in the build order; remembered within a session.</summary>
    public int Slot { get; private set; }

    public int Rotation { get; private set; }

    /// <summary>The piece armed to come down, until when (seconds of frame clock).</summary>
    public string? ArmedKey { get; private set; }
    public double ArmedUntil { get; private set; }

    /// <summary>The last full ask (navigability included): what, where, at which revision, when, and what it said.</summary>
    public (string DefId, (long X, long Z, int R) Pose, long Revision, double AtSeconds, PlacementPreview Preview)? Asked { get; private set; }

    /// <summary>A scripted run's aim point in world millimetres (M7 design §8.17), used instead of the camera's.</summary>
    public (long XMm, long ZMm)? ScriptedAim { get; set; }

    /// <summary>The pose the ghost stands at now, when there is one; what left mouse submits.</summary>
    public (long X, long Z, int R)? Pose { get; private set; }

    /// <summary>The piece in reach that build keys act on, by the target rule of §8.8.</summary>
    public PieceView? Target { get; private set; }

    /// <summary>The tone the ghost wears now.</summary>
    public BuildTone Tone { get; private set; }

    /// <summary>The panel's text, the status line and the target line, as last drawn.</summary>
    public string Panel { get; private set; } = string.Empty;
    public string? Status { get; private set; }
    public string? TargetLine { get; private set; }

    public void Bind(GameSession session, StructuresView structures, Art.ArtCoverage coverage)
    {
        _session = session;
        _structures = structures;
        _coverage = coverage;
    }

    /// <summary>How long the last preview took in presentation, in milliseconds: F2 shows it.</summary>
    public double PreviewMs { get; private set; }

    /// <summary>Every piece the build knows, by family in the closed order, then by ID: what 1-7 and PageUp/PageDown choose among.</summary>
    public IReadOnlyList<PieceDefinition> Pieces =>
        _session.Setup.Building.Catalog.Pieces.Values.OrderBy(p => Array.IndexOf(FamilyOrder, p.Family)).ThenBy(p => p.Id, StringComparer.Ordinal).ToList();

    public PieceDefinition? Selected => Pieces.Count == 0 ? null : Pieces[Math.Clamp(Slot, 0, Pieces.Count - 1)];

    /// <summary>Into build mode; false, with nothing changed, when there is nothing to build with.</summary>
    public bool Enter(CameraRig camera)
    {
        if (Pieces.Count == 0)
            return false;
        Active = true;
        camera.Cap = CameraRig.BuildMaxDistance;
        _structures.ShowOutlines(true);
        return true;
    }

    /// <summary>Out of build mode: the ghost, the outlines, the highlight and any armed take-down gone; the camera back within 6 m.</summary>
    public void Exit(CameraRig camera)
    {
        if (!Active)
            return;
        Active = false;
        ArmedKey = null;
        Asked = null;
        Pose = null;
        Target = null;
        Status = TargetLine = null;
        camera.Cap = CameraRig.MaxDistance;
        _structures.ShowOutlines(false);
        _structures.Highlight(null, null);
        RemoveGhost();
    }

    public void Select(int index)
    {
        if (index >= 0 && index < Pieces.Count)
            Slot = index;
    }

    public void Cycle(int by)
    {
        if (Pieces.Count > 0)
            Slot = ((Slot + by) % Pieces.Count + Pieces.Count) % Pieces.Count;
    }

    /// <summary>A quarter turn clockwise.</summary>
    public void Turn() => Rotation = (Rotation + 1) & 3;

    /// <summary>Left mouse: the ghost's pose, submitted whatever its tone. False when there is no pose (a door with no doorway near).</summary>
    public bool Place(PlayerController controller)
    {
        if (Selected is not { } piece || Pose is not { } pose)
            return false;
        controller.Place(piece.Id, pose.X, pose.Z, pose.R);
        return true;
    }

    /// <summary>The take-down key: the first press arms the target for two seconds, the second on the same target sends the command.</summary>
    public void Dismantle(PlayerController controller, double now)
    {
        if (Target is not { } target)
            return;
        if (ArmedKey == target.Id.Value && now <= ArmedUntil)
        {
            controller.Dismantle(target.Id);
            ArmedKey = null;
            return;
        }
        ArmedKey = target.Id.Value;
        ArmedUntil = now + 2.0;
    }

    /// <summary>The mend key (E8): the target, when it has blocking parts, is mended - no confirmation; the authority refuses one whole.</summary>
    public void Repair(PlayerController controller)
    {
        if (Target is { Parts.IsEmpty: false } target)
            controller.Repair(target.Id);
    }

    /// <summary>
    /// One frame of build mode: aim, snap, ask the authority, tone the ghost, find the target, and write the panel and the two lines.
    /// Returns nothing to the world; <see cref="Place"/>, <see cref="Dismantle"/> and <see cref="Repair"/> submit.
    /// </summary>
    public void BuildFrame(Simulation simulation, CameraRig camera, Body body, double now)
    {
        if (!Active || Selected is not { } piece)
            return;
        var terrain = _session.Setup.Layout.Space.Terrain;
        var aim = ScriptedAim ?? AimPoint(camera, terrain, body);
        var poses = simulation.Pieces.Select(p => new PiecePose(p.Id, p.DefId, p.XMm, p.ZMm, p.Rotation)).ToList();
        Pose = Snapper.Snap(_session.Setup.Building.Catalog, poses, piece.Id, aim.XMm, aim.ZMm, Rotation);

        if (Pose is { } pose)
        {
            bool ask = Asked is not { } asked || asked.DefId != piece.Id || asked.Pose != pose || asked.Revision != simulation.StructureRevision
                       || now - asked.AtSeconds >= 0.5;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var preview = simulation.PreviewPlacement(piece.Id, pose.X, pose.Z, pose.R, checkNavigability: ask);
            PreviewMs = clock.Elapsed.TotalMilliseconds;
            if (ask)
                Asked = (piece.Id, pose, simulation.StructureRevision, now, preview);
            bool matches = Asked is { } a && a.DefId == piece.Id && a.Pose == pose && a.Revision == simulation.StructureRevision;
            (Tone, string? reason) = !preview.Allowed ? (BuildTone.Refused, preview.Reason)
                : preview.Navigability is NavVerdict.NotApplicable or NavVerdict.Proven ? (BuildTone.Allowed, null)
                : matches && Asked!.Value.Preview.Failed == PlacementRule.Navigability ? (BuildTone.Refused, Asked.Value.Preview.Reason)
                : matches && Asked!.Value.Preview.Navigability == NavVerdict.Proven ? (BuildTone.Allowed, null)
                : (BuildTone.Unchecked, null);
            string name = _session.DisplayName(piece.Id);
            var cost = preview.Cost.FirstOrDefault();
            Status = Tone switch
            {
                BuildTone.Refused => $"{name}: {Words(_session, reason ?? string.Empty)}",
                BuildTone.Allowed => cost is null ? $"{name}: can be built here"
                    : $"{name}: can be built here - {cost.Count} {_session.DisplayName(cost.ItemId)} ({cost.Carried} carried)",
                _ => cost is null ? $"{name}: will be checked when placed" : $"{name}: will be checked when placed - {cost.Count} {_session.DisplayName(cost.ItemId)}",
            };
            ShowGhost(piece, preview, pose, terrain);
        }
        else
        {
            Tone = BuildTone.Plain;
            Status = $"{_session.DisplayName(piece.Id)}: aim at a doorway to hang it";
            RemoveGhost();
        }

        // The target: the first part the facing meets within reach, else the roof, then the pad, on the aimed square.
        Target = FindTarget(simulation, camera, body, aim);
        if (ArmedKey is not null && (now > ArmedUntil || Target?.Id.Value != ArmedKey))
            ArmedKey = null;
        if (Target is { } target)
        {
            string name = _session.DisplayName(target.DefId);
            TargetLine = ArmedKey == target.Id.Value
                ? $"Press {HelpPanel.Key("build_dismantle")} again to take down the {name}"
                : target.Parts.IsEmpty
                    ? $"{name} - [{HelpPanel.Key("build_dismantle")}] take down"
                    : $"{name} {target.HealthCurrent}/{target.HealthMax} - [{HelpPanel.Key("build_repair")}] mend   [{HelpPanel.Key("build_dismantle")}] take down";
            _structures.Highlight(target.Id.Value, ArmedKey == target.Id.Value ? Palette.GhostRefused : Palette.Highlight);
        }
        else
        {
            TargetLine = null;
            _structures.Highlight(null, null);
        }
        Panel = PanelText(simulation);
    }

    /// <summary>The build panel (M7 design §8.4): the pieces with their keys, names and costs, the choice marked, the turn, the legend.</summary>
    private string PanelText(Simulation simulation)
    {
        var text = new StringBuilder("BUILDING\n");
        var pieces = Pieces;
        for (int i = 0; i < pieces.Count; i++)
        {
            string key = i < 7 ? (i + 1).ToString(CultureInfo.InvariantCulture) : " ";
            string cost = string.Join(", ", pieces[i].Cost.Select(c => $"{c.Count} {_session.DisplayName(c.ItemId)}"));
            text.Append(i == Slot ? "> " : "  ").Append($"[{key}] {_session.DisplayName(pieces[i].Id)} - {cost}\n");
        }
        var carried = simulation.Player.Inventory.GroupBy(e => e.DefId).ToDictionary(g => g.Key, g => g.Sum(e => e.Count), StringComparer.Ordinal);
        foreach (string item in pieces.SelectMany(p => p.Cost.Select(c => c.ItemId)).Distinct().Order(StringComparer.Ordinal))
            text.Append($"Carried: {carried.GetValueOrDefault(item)} {_session.DisplayName(item)}\n");
        text.Append($"Turned {Rotation * 90} degrees\n");
        text.Append($"[{HelpPanel.Key("build_piece_next")}]/[{HelpPanel.Key("build_piece_prev")}] next/previous   [{HelpPanel.Key("build_rotate")}] turn\n");
        text.Append($"[{HelpPanel.Key("build_place")}] place   [{HelpPanel.Key("build_repair")}] mend   [{HelpPanel.Key("build_dismantle")}] x2 take down   [{HelpPanel.Key("build_mode")}]/[{HelpPanel.Key("release_mouse")}] leave");
        return text.ToString();
    }

    /// <summary>
    /// Where the camera's centre ray meets the ground (M7 design §8.5): marched in quarter-metre steps up to 20 m against the domain
    /// terrain, then halved eight times; with no ground met, three metres ahead of the body.
    /// </summary>
    private (long XMm, long ZMm) AimPoint(CameraRig camera, TerrainGrid terrain, Body body)
    {
        var centre = GetViewport().GetVisibleRect().Size / 2;
        var from = camera.Camera.ProjectRayOrigin(centre);
        var dir = camera.Camera.ProjectRayNormal(centre);
        bool Below(float t)
        {
            var p = from + dir * t;
            return p.Y <= terrain.HeightAtMm((long)Math.Round(p.X * 1000), (long)Math.Round(p.Z * 1000)) / 1000f;
        }
        for (float t = 0.25f; t <= 20f; t += 0.25f)
        {
            if (!Below(t))
                continue;
            float lo = t - 0.25f, hi = t;
            for (int i = 0; i < 8; i++)
            {
                float mid = (lo + hi) / 2;
                if (Below(mid))
                    hi = mid;
                else
                    lo = mid;
            }
            var hit = from + dir * hi;
            return ((long)Math.Round(hit.X * 1000), (long)Math.Round(hit.Z * 1000));
        }
        var ahead = camera.GroundForward * 3f;
        return (body.XMm + (long)Math.Round(ahead.X * 1000), body.ZMm + (long)Math.Round(ahead.Z * 1000));
    }

    /// <summary>
    /// The target rule (M7 design §8.8), presentation only: among pieces in reach with blocking parts, the first part the facing segment
    /// enters (a door before its doorway, then the lower ID); otherwise the roof, then the pad, on the aimed square.
    /// </summary>
    private PieceView? FindTarget(Simulation simulation, CameraRig camera, Body body, (long XMm, long ZMm) aim)
    {
        long reach = _session.Setup.Building.Constants.PlaceReachMm;
        var near = simulation.Pieces.Where(p =>
            BuildingMath.DistanceSquared(new BoundsMm(p.MinXMm, p.MinZMm, p.MaxXMm, p.MaxZMm), body.XMm, body.ZMm) <= reach * reach).ToList();
        var forward = camera.GroundForward;
        double dx = forward.X * reach, dz = forward.Z * reach;
        var hit = near.Where(p => !p.Parts.IsEmpty)
            .Select(p => (Piece: p, T: p.Parts.Select(part => Entry(body.XMm, body.ZMm, dx, dz, part)).Min()))
            .Where(h => h.T <= 1)
            .OrderBy(h => h.T).ThenBy(h => h.Piece.Family == PieceFamily.Door ? 0 : 1).ThenBy(h => h.Piece.Id.Value, StringComparer.Ordinal)
            .Select(h => h.Piece).FirstOrDefault();
        if (hit is not null)
            return hit;
        long sx = Lattice.ModuleMm * NavGeometry.FloorDiv(aim.XMm, Lattice.ModuleMm) + Lattice.HalfMm;
        long sz = Lattice.ModuleMm * NavGeometry.FloorDiv(aim.ZMm, Lattice.ModuleMm) + Lattice.HalfMm;
        return near.FirstOrDefault(p => p.Family == PieceFamily.Roof && p.XMm == sx && p.ZMm == sz)
               ?? near.FirstOrDefault(p => p.Family == PieceFamily.Pad && p.XMm == sx && p.ZMm == sz);
    }

    /// <summary>Where along a segment (0 at its start, 1 at its end) it first enters a box; above 1 when it never does.</summary>
    private static double Entry(long x0, long z0, double dx, double dz, PiecePartView box)
    {
        double enter = 0, leave = 1;
        foreach (var (origin, delta, min, max) in new[] { ((double)x0, dx, (double)box.MinXMm, (double)box.MaxXMm), ((double)z0, dz, (double)box.MinZMm, (double)box.MaxZMm) })
        {
            if (Math.Abs(delta) < 1e-9)
            {
                if (origin < min || origin > max)
                    return 2;
                continue;
            }
            double a = (min - origin) / delta, b = (max - origin) / delta;
            enter = Math.Max(enter, Math.Min(a, b));
            leave = Math.Min(leave, Math.Max(a, b));
        }
        return enter <= leave ? enter : 2;
    }

    /// <summary>The ghost at the pose, built once per (piece, pose) and re-toned every frame.</summary>
    private void ShowGhost(PieceDefinition piece, PlacementPreview preview, (long X, long Z, int R) pose, TerrainGrid terrain)
    {
        var tone = Tone switch
        {
            BuildTone.Allowed => Palette.GhostAllowed,
            BuildTone.Refused => Palette.GhostRefused,
            _ => Palette.GhostUnchecked,
        };
        if (_ghost is null || _ghostPose != (piece.Id, pose.X, pose.Z, pose.R))
        {
            RemoveGhost();
            var parts = preview.Parts.Select((p, i) => new PiecePartView(p.MinXMm, p.MinZMm, p.MaxXMm, p.MaxZMm, p.HeightMm, piece.Parts[i].Traversal))
                .ToImmutableArray();
            var view = new PieceView(GhostId, piece.Id, piece.Family, pose.X, pose.Z, pose.R, GhostId, piece.HealthMax, piece.HealthMax, false, null,
                preview.MinXMm, preview.MinZMm, preview.MaxXMm, preview.MaxZMm, parts, null, null);
            _ghost = StructuresView.BuildPiece(view, terrain, tone);
            _ghost.Name = "Ghost";
            _coverage?.Resolved("ghost", piece.Id, "m7_effect");
            AddChild(_ghost);
            _ghostPose = (piece.Id, pose.X, pose.Z, pose.R);
            _ghostTone = tone;
        }
        else if (_ghostTone != tone)
        {
            Retone(_ghost, tone);
            _ghostTone = tone;
        }
    }

    private static void Retone(Node node, Material material)
    {
        if (node is MeshInstance3D mesh)
            mesh.MaterialOverride = material;
        foreach (var child in node.GetChildren())
            Retone(child, material);
    }

    private void RemoveGhost()
    {
        _ghost?.QueueFree();
        _ghost = null;
        _ghostPose = null;
        _ghostTone = null;
    }

    internal static readonly System.Text.RegularExpressions.Regex DottedId =
        new(@"(?<![A-Za-z0-9_.])[a-z][a-z0-9_]*(?:\.[a-z0-9_]+)+(?![A-Za-z0-9_])", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>
    /// An M7 reason in words (M7 design §8.7): every dotted definition ID becomes its display name, so "needs 2 item.material.timber"
    /// reads "needs 2 Rough Timber"; "7.20 m" never matches, and an unknown ID passes through.
    /// </summary>
    public static string Words(GameSession session, string reason) => DottedId.Replace(reason, m => session.DisplayName(m.Value));
}
