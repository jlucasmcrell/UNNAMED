using Godot;
using UNNAMED.Domain.Spatial;
using UNNAMED.Presentation.Greybox;
using UNNAMED.World.Runtime;

namespace UNNAMED.Presentation;

public partial class Main
{
    /// <summary>
    /// Each interior this run holds (the Barrowworks; the hollow under the den): its layout, its bindings, and its view and figures once
    /// first entered. Built on first entry and then kept, hidden while outside (freeing one crashed the renderer: see BuildInterior).
    /// </summary>
    private sealed class HeldInterior(RegionLayout layout, Art.ArtBindings bindings)
    {
        public RegionLayout Layout { get; } = layout;
        public Art.ArtBindings Bindings { get; } = bindings;
        public HollowView? View { get; set; }
        public CreaturesView? Creatures { get; set; }
    }

    private readonly Dictionary<SpaceId, HeldInterior> _interiors = new();

    private static readonly SpaceId BarrowworksSpace = SpaceId.Parse("space.barrowworks");

    /// <summary>
    /// An interior under the open sky: the gorge's ledge and the hollow behind it stand at the world's own metres, so the frontier stays
    /// drawn round them - the gorge from the ledge, the mouth's daylight from inside - and the cave's own air begins a few metres in.
    /// </summary>
    private static bool OpenAir(SpaceId space) => space == DenGorgeCave.Space;

    /// <summary>Whose air the hollow under the den is breathing: the gorge's on the ledge, the cave's past <see cref="DenGorgeCave.AirLineZ"/>.</summary>
    private bool? _caveAir;

    private void UpdateOpenAir(Vector3 feet)
    {
        if (_session.Simulation!.PlayerInExterior || !OpenAir(_session.Simulation.ActiveLayout.SpaceId) || _interior is not { } cave)
        {
            if (_caveAir is not null)
            {
                DenGorge.ShowPassage(_hollow, true);
                ShowFrontierWater(true);
            }
            _caveAir = null;
            return;
        }
        bool deep = feet.Z < DenGorgeCave.AirLineZ;
        if (deep == _caveAir)
            return;
        if (_caveAir is null)
        {
            DenGorge.ShowPassage(_hollow, false);
            ShowFrontierWater(false);
        }
        _caveAir = deep;
        _hollow.SetAirActive(!deep);
        cave.SetAirActive(deep);
        Greybox.WeatherStates.UnderSky = !deep;
    }

    /// <summary>
    /// The frontier's stream is one plane over its whole walk space at 0.12 m, left to the ground's relief to hide - and the hollow under
    /// the den is below that ground, so the plane runs through it. It stands aside while the body is in the hollow.
    /// </summary>
    private void ShowFrontierWater(bool shown)
    {
        if (_hollow.GetNodeOrNull<Node3D>("Water") is { } water)
            water.Visible = shown;
    }

    /// <summary>
    /// Halda's rope giving way (spec 28), drawn over the crossing the world has already made: the character lowers hand under hand
    /// from the lip, the rope parts above them, and they drop the last of it to the ledge. The world put the body on the ledge and took
    /// the fall's health at once; this only holds the figure on the rope for the moment that is, and the input with it.
    /// </summary>
    private sealed class RopeDescent
    {
        public const double Lower = 2.9, Fall = 0.85, Land = 0.9;
        public double At;
        public required Vector3 Top, Hang, Ledge;
        public bool Snapped;

        public bool Done => At >= Lower + Fall + Land;

        /// <summary>
        /// Where the feet are, <see cref="At"/> seconds in, and how the body holds: gathered close to the face while it is on the rope
        /// (there is no climbing clip; the crouch is the nearest the body has), falling once it parts.
        /// </summary>
        public (Vector3 Feet, bool Airborne, bool Clinging) Now()
        {
            if (At < 0.5)
                return (Top.Lerp(Hang with { Y = Top.Y - 0.6f }, (float)(At / 0.5)), false, true);
            if (At < Lower)
            {
                // Hand under hand: down in short pulls, a check between each.
                double t = (At - 0.5) / (Lower - 0.5);
                float pulled = (float)(t + Math.Sin(t * Math.PI * 7) * 0.025);
                return (new Vector3(Hang.X, Mathf.Lerp(Top.Y - 0.6f, Hang.Y, pulled), Hang.Z), false, true);
            }
            if (At < Lower + Fall)
            {
                float t = (float)((At - Lower) / Fall);
                return (new Vector3(Hang.X, Mathf.Lerp(Hang.Y, Ledge.Y, t * t), Mathf.Lerp(Hang.Z, Ledge.Z, t)), true, false);
            }
            return (Ledge, false, false);
        }
    }

    private RopeDescent? _rope;

    /// <summary>What a portal's prompt says: the gorge's rope and cleft are climbed, the Barrowworks entered.</summary>
    private static string PortalWords(string key, bool outside) => key switch
    {
        "portal.den_gorge.rope" => "Climb down the rope - it is frayed",
        "portal.den_gorge.cleft" => outside ? "Climb down the cleft" : "Climb the cleft",
        _ => outside ? "Enter the Barrowworks" : "Return to the frontier",
    };

    /// <summary>The rope as the world has it: still hanging, or gone (after a load, or another's crossing).</summary>
    private void SyncDenGorgeRope()
    {
        if (_rope is null && _session.Setup.Layout.Portals.Portals.Any(p => p.GivesWay is not null && _session.Simulation!.PortalGone(p)))
            DenGorge.ShowRopeGone(_hollow, burst: false);
    }

    private void SubscribeDenGorge()
    {
        _session.Subscribe<WorldFlagChanged>(_ => SyncDenGorgeRope());
        _session.Subscribe<PortalGaveWay>(e =>
        {
            if (_session.Setup.Layout.Portals.Find(e.PortalKey) is not { } portal)
                return;
            float x = portal.ExteriorXMm / 1000f, z = portal.ExteriorZMm / 1000f;
            float lip = _ground.Height(x, z);
            var body = _controller.Authoritative;
            var ledge = HollowView.ToGodot(body.XMm, body.YMm, body.ZMm);
            _rope = new RopeDescent
            {
                Top = new Vector3(x, lip, DenGorge.Edge + 0.45f),
                Hang = new Vector3(x, ledge.Y + e.DropMm / 1000f, DenGorge.Edge + 1.05f),
                Ledge = ledge,
            };
            _autorun = false;
            // The camera out over the gorge, looking back at the face: the rope, the hands, the ledge below.
            _camera.Yaw = 0f;
            _camera.Pitch = -0.32f;
            _camera.TargetDistance = 5.5f;
        });
    }

    /// <summary>The rope's moment, a frame of it: the figure's feet and posture while it lasts; null once the character has their legs again.</summary>
    private (Vector3 Feet, bool Airborne, bool Clinging)? DrawRopeDescent(double delta)
    {
        if (_rope is not { } rope)
            return null;
        rope.At += delta;
        if (!rope.Snapped && rope.At >= RopeDescent.Lower)
        {
            rope.Snapped = true;
            DenGorge.ShowRopeGone(_hollow, burst: true);
            _sounds.Play("sfx.amb.ashen_hollow.timber_creak.01", rope.Hang);
        }
        if (rope.Snapped && rope.At >= RopeDescent.Lower + RopeDescent.Fall && rope.At - delta < RopeDescent.Lower + RopeDescent.Fall)
            _hud.Toast("The rope gave way. Find another way up.", 5);
        if (rope.Done)
        {
            _rope = null;
            return null;
        }
        return rope.Now();
    }
}
