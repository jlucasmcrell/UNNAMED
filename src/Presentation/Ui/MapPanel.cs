// UNNAMED Presentation - the map: what the character knows of the region and nothing else (MAPS_CARTOGRAPHY_AND_WORLD_KNOWLEDGE.md; the
// owner's second-play brief §24; NEXT_AREA_TRAVEL_CITY_PLAN.md D.6 map v1)
// Godot presentation only: it draws Simulation.Map, which the simulation owns; nothing here holds state (D-11)

using Godot;
using UNNAMED.Application;
using UNNAMED.Domain.Spatial;
using UNNAMED.Domain.Travel;

namespace UNNAMED.Presentation.Ui;

/// <summary>
/// The map (M), a WIDE sheet: the region drawn from what the character knows - the ground round each place they have discovered, the
/// stretches of road they have walked, the Waystones they know (the return point ringed) - and where they stand, facing. Everything they
/// have not found is blank soot: no undiscovered marker, no quest pin. North is up; a scale bar measures it. Over it, the calendar line:
/// the date in numbered tallies and the weather now (WP7).
/// </summary>
public partial class MapPanel : CanvasLayer
{
    private Sheet _sheet = null!;
    private GameSession _session = null!;
    private HudIcons _icons = HudIcons.None;
    private MapCanvas _canvas = null!;
    private Label _known = null!;
    private Label _date = null!;

    public void Bind(GameSession session) => _session = session;

    public void UseIcons(HudIcons icons)
    {
        _icons = icons;
        _sheet.SetIcon(icons.Binds("panel.map") ? icons.For("panel.map") : null);
        _canvas.Stone = icons.Binds("panel.waystones") ? icons.For("panel.waystones") : null;
    }

    public override void _Ready()
    {
        Visible = false;
        _sheet = UiKit.Sheet(this, "Map", "map", UiKit.Wide);
        _sheet.SetHint("What you have found: the places you have been, the roads you have walked, the stones you know.");
        _date = UiKit.Words(string.Empty, 17, HudStyle.Bone);
        _sheet.Body.AddChild(_date);
        _canvas = new MapCanvas { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, ClipContents = true };
        _sheet.Body.AddChild(_canvas);
        var legend = new HBoxContainer();
        legend.AddThemeConstantOverride("separation", 12);
        legend.AddChild(UiKit.Chip("You", HudStyle.Ember));
        legend.AddChild(UiKit.Chip("Waystone you know", HudStyle.Brass));
        legend.AddChild(UiKit.Chip("Return point", HudStyle.Lichen));
        legend.AddChild(UiKit.Chip("Road walked", HudStyle.Iron, HudStyle.Ash));
        _sheet.Body.AddChild(legend);
        _known = UiKit.Words(string.Empty, 17, HudStyle.Ash);
        _sheet.Body.AddChild(_known);
    }

    public override void _Process(double delta)
    {
        if (!Visible || _session?.Simulation is not { } simulation)
            return;
        // The sheet draws THIS region, and the body's position is read in the same metres. Inside an interior
        // neither is true: the layout is the exterior's and the body stands in the interior's own coordinates,
        // so drawing it would put the character at a point in the overworld they are not standing on. A map
        // that lies is worse than no map, so under the hill the sheet is blank and says why (M8 D10).
        if (!simulation.PlayerInExterior)
        {
            _canvas.Clear();
            _known.Text = "You are underground, and this map is of the land above it.";
            return;
        }
        var map = simulation.Map;
        _date.Text = CalendarWords.Line(simulation.Calendar, _session.Setup.Calendar, simulation.Weather.State);
        var stones = simulation.Waystones.Where(w => w.Known).ToList();
        // What the character has read about a place, written under its name: the journal's own entries, matched by the page that is found by
        // walking into it (spec 41, map notes). A place nobody has written up carries no note.
        var notes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in _session.Setup.Lore.Values)
        {
            var visited = entry.Pages.SelectMany(p => p.Sources).Where(s => s.Kind == UNNAMED.Domain.Knowledge.LoreSourceKind.Visited)
                .Select(s => s.Ref).Distinct(StringComparer.Ordinal).ToList();
            int known = simulation.Lore.Count(r => r.EntryId == entry.Id);
            if (known == 0)
                continue;
            foreach (string place in visited)
                notes[place] = $"{entry.Title} {known}/{entry.PageCount}";
        }
        _canvas.Show(_session.Setup.Layout, map, stones.Select(w => (w.Site, w.ReturnPoint)).ToList(),
            map.Places.Select(p => (p, Names.Of(_session, p.LocationId))).ToList(), simulation.Player.Body, notes);
        _known.Text = stones.Count == 0
            ? "You know no Waystone yet. Touch one, and it will be on your map."
            : $"Waystones you know: {string.Join(", ", stones.Select(w => w.Site.Name + (w.ReturnPoint ? " (your return point)" : "")))}.";
    }

    /// <summary>The drawing itself: the region's walkable bounds fitted to the sheet's width, north up.</summary>
    public sealed partial class MapCanvas : Control
    {
        private const float Margin = 18;
        private RegionLayout? _layout;
        private KnownMap? _map;
        private IReadOnlyList<(WaystoneSite Site, bool ReturnPoint)> _stones = Array.Empty<(WaystoneSite, bool)>();
        private IReadOnlyList<(KnownPlace Place, string Name)> _places = Array.Empty<(KnownPlace, string)>();

        /// <summary>What the journal holds about a place, by its location id: drawn under the place's name.</summary>
        private IReadOnlyDictionary<string, string> _notes = new Dictionary<string, string>(StringComparer.Ordinal);
        private Body? _player;
        private string _drawn = string.Empty;

        public Texture2D? Stone { get; set; }

        /// <summary>Blank the sheet: nothing drawn, and nothing the last frame left behind.</summary>
        public void Clear()
        {
            _layout = null;
            _map = null;
            _stones = Array.Empty<(WaystoneSite, bool)>();
            _places = Array.Empty<(KnownPlace, string)>();
            _player = null;
            _drawn = string.Empty;
            CustomMinimumSize = Vector2.Zero;
            QueueRedraw();
        }

        public void Show(RegionLayout layout, KnownMap map, IReadOnlyList<(WaystoneSite Site, bool ReturnPoint)> stones,
            IReadOnlyList<(KnownPlace Place, string Name)> places, Body player, IReadOnlyDictionary<string, string>? notes = null)
        {
            _layout = layout;
            _notes = notes ?? new Dictionary<string, string>(StringComparer.Ordinal);
            float width = Math.Max(320, Size.X);
            var space = layout.Space;
            float height = width * (space.MaxZMm - space.MinZMm) / Math.Max(1f, space.MaxXMm - space.MinXMm) + 2 * Margin;
            if (Math.Abs(CustomMinimumSize.Y - height) > 0.5f)
                CustomMinimumSize = new Vector2(0, height);
            string signature = $"{map.Places.Length}:{map.Roads.Length}:{string.Join(",", stones.Select(s => s.Site.Key + s.ReturnPoint))}:" +
                               $"{player.XMm / 500}:{player.ZMm / 500}:{player.FacingMdeg / 5000}:{Size.X}";
            _map = map;
            _stones = stones;
            _places = places;
            _player = player;
            if (signature == _drawn)
                return;
            _drawn = signature;
            QueueRedraw();
        }

        public override void _Draw()
        {
            if (_layout is null || _map is null || _player is null)
                return;
            var space = _layout.Space;
            float scale = (Size.X - 2 * Margin) / Math.Max(1f, space.MaxXMm - space.MinXMm);
            Vector2 At(long x, long z) => new(Margin + (x - space.MinXMm) * scale, Margin + (space.MaxZMm - z) * scale);

            var frame = new Rect2(Vector2.Zero, Size);
            DrawRect(frame, HudStyle.Coal);
            DrawRect(frame, HudStyle.Iron, filled: false);
            // The ground round every place found: what the map shows of the land.
            var ground = new Color(0.17f, 0.165f, 0.15f);
            foreach (var (place, _) in _places)
                DrawCircle(At(place.XMm, place.ZMm), Math.Max(4, place.RevealRadiusMm * scale), ground);
            foreach (var road in _map.Roads)
                DrawLine(At(road.X0Mm, road.Z0Mm), At(road.X1Mm, road.Z1Mm), HudStyle.Ash, 2.5f, antialiased: true);

            var font = HudStyle.Caps;
            var taken = new List<Rect2>();
            void Label(Vector2 at, string text, Color color)
            {
                var size = font.GetStringSize(text, HorizontalAlignment.Left, -1, 16);
                var box = new Rect2(at + new Vector2(8, -size.Y / 2 - 2), size);
                box.Position = new Vector2(Math.Clamp(box.Position.X, 4, Size.X - size.X - 4), Math.Clamp(box.Position.Y, 4, Size.Y - size.Y - 4));
                if (taken.Any(t => t.Grow(2).Intersects(box)))
                    return;
                taken.Add(box);
                DrawString(font, box.Position + new Vector2(0, size.Y - 4), text, HorizontalAlignment.Left, -1, 16, color);
            }

            foreach (var (site, returnPoint) in _stones)
            {
                var (x, z) = Footprints.Center(site.Body);
                var at = At(x, z);
                if (returnPoint)
                    DrawArc(at, 13, 0, Mathf.Tau, 32, HudStyle.Lichen, 2, antialiased: true);
                if (Stone is not null)
                    DrawTextureRect(Stone, new Rect2(at - new Vector2(10, 10), new Vector2(20, 20)), false);
                else
                    DrawRect(new Rect2(at - new Vector2(3, 7), new Vector2(6, 14)), HudStyle.Brass);
                Label(at + new Vector2(6, 0), site.Name.ToUpperInvariant(), HudStyle.Brass);
            }
            foreach (var (place, name) in _places)
            {
                var at = At(place.XMm, place.ZMm);
                DrawRect(new Rect2(at - new Vector2(2.5f, 2.5f), new Vector2(5, 5)), HudStyle.Bone);
                Label(at, name.ToUpperInvariant(), HudStyle.Bone);
                // The note is what the journal holds about the place: its entry and how much of it is known (spec 41, map notes).
                if (_notes.TryGetValue(place.LocationId, out string? note))
                    Label(at + new Vector2(0, 15), note, HudStyle.Ash);
            }

            // Where the character stands, and which way they face (0 is north, +Z; clockwise towards east, +X).
            var me = At(_player.XMm, _player.ZMm);
            float facing = _player.FacingMdeg / 1000f * Mathf.Pi / 180f;
            var ahead = new Vector2(Mathf.Sin(facing), -Mathf.Cos(facing));
            var side = new Vector2(-ahead.Y, ahead.X);
            DrawColoredPolygon(new[] { me + ahead * 11, me - ahead * 6 + side * 6, me - ahead * 3, me - ahead * 6 - side * 6 }, HudStyle.Ember);

            // A 100 m scale bar and the north mark: the instrument's measure.
            float hundred = 100_000 * scale;
            var foot = new Vector2(Margin, Size.Y - 10);
            DrawLine(foot, foot + new Vector2(hundred, 0), HudStyle.IronBright, 1.5f);
            for (int i = 0; i <= 4; i++)
                DrawLine(foot + new Vector2(hundred * i / 4, 0), foot + new Vector2(hundred * i / 4, i % 4 == 0 ? -6 : -3), HudStyle.IronBright, 1);
            DrawString(font, foot + new Vector2(hundred + 6, 4), "100 M", HorizontalAlignment.Left, -1, 16, HudStyle.Muted);
            var north = new Vector2(Size.X - Margin - 6, Margin + 14);
            DrawColoredPolygon(new[] { north + new Vector2(0, -12), north + new Vector2(5, 2), north + new Vector2(-5, 2) }, HudStyle.IronBright);
            DrawString(font, north + new Vector2(-5, 20), "N", HorizontalAlignment.Left, -1, 16, HudStyle.Muted);
        }
    }
}
