// UNNAMED Presentation - the score: two decks cross-fading between cues, and stingers over them
// Godot presentation only (D-11): it listens to the drawn world and the simulation's events and plays; it holds no game state

using Godot;
using UNNAMED.Application;
using UNNAMED.Presentation.Art;
using UNNAMED.Presentation.Greybox;
using UNNAMED.World;
using UNNAMED.World.Runtime;

namespace UNNAMED.Presentation.Audio;

/// <summary>
/// Plays what <see cref="MusicChoice"/> picks. The settlements and their themes come from the bindings (<c>audio.music.places</c>:
/// a location ID to its theme and radius), placed by the region layout's location sites, so no content ID is in code. A fight is any
/// creature engaged within <see cref="FightRange"/> of the player, held for <see cref="FightHold"/> after the last one lets go, so a
/// pause in a fight does not drop the music. Before it, a creature that would start a fight and has noticed something within
/// <see cref="TenseRange"/> (suspicious, or searching after the fight) brings in the tension layer, held for <see cref="TenseHold"/>;
/// the layer is cut from a combat take, so the change between the two keeps its playback position and its take. The opening at launch has
/// its own cue, from its start, and the title theme takes over under the cue's tail. The road in keeps the
/// title theme for <see cref="RoadSeconds"/>. Any other place of the layout already known when the character walks into it plays the
/// exploration cue now and then (<see cref="ExploreWait"/>, <see cref="ExplorePlay"/>, <see cref="ExploreRest"/>); a first visit stays
/// silent. A change of cue cross-fades over <see cref="FadeSeconds"/> (faster into a fight, slower out of the title and the exploration
/// cue). A place discovered plays the discovery stinger over a ducked bed. Every player is on the Music bus, whose volume the Sound
/// screen sets.
/// </summary>
public partial class MusicPlayer : Node
{
    private const float FightRange = 30f;
    private const double FightHold = 8;
    private const float TenseRange = 40f;
    private const double TenseHold = 4;
    private const double RoadSeconds = 60;
    private const double ExploreWait = 20;
    private const double ExplorePlay = 90;
    private const double ExploreRest = 240;
    private const double FadeSeconds = 4;
    private const double FightFadeSeconds = 1.2;
    private const double TenseFadeSeconds = 2;
    private const double SlowFadeSeconds = 10;
    private const float Duck = 0.35f;

    private readonly AudioStreamPlayer[] _decks = new AudioStreamPlayer[2];
    private readonly List<(Vector2 At, float Radius, string Theme)> _places = new();
    private readonly List<(Vector2 At, float Radius, string Id)> _areas = new();
    private AudioStreamPlayer _stinger = null!;
    private SoundBank _bank = null!;
    private GameSession? _session;
    private int _live;
    private string? _cue;
    private double _fade = FadeSeconds;
    private double _sinceFight = double.MaxValue;
    private double _sinceTense = double.MaxValue;
    private double _road;
    private string? _area;
    private bool _areaKnown;
    private double _inArea;
    private bool _title;
    private bool _opening;

    /// <summary>The cue playing now (a family), or null for silence.</summary>
    public string? Cue => _cue;

    public void Bind(SoundBank bank, GameSession? session, ArtBindings bindings)
    {
        _bank = bank;
        _session = session;
        for (int i = 0; i < 2; i++)
        {
            _decks[i] = new AudioStreamPlayer { Bus = AudioMix.Music, VolumeDb = -80f };
            AddChild(_decks[i]);
        }
        _stinger = new AudioStreamPlayer { Bus = AudioMix.Music };
        AddChild(_stinger);
        if (session is null)
            return;
        foreach (var site in session.Setup.Layout.Locations)
        {
            var at = new Vector2(site.XMm / 1000f, site.ZMm / 1000f);
            if (bindings.MusicPlaces.GetValueOrDefault(site.Id) is { } place)
                _places.Add((at, place.RadiusM, place.Theme));
            else
                _areas.Add((at, Math.Max(site.RevealRadiusMm, site.DiscoveryRadiusMm) / 1000f, site.Id));
        }
        session.Subscribe<LocationDiscovered>(_ =>
        {
            if (_cue != MusicChoice.Combat && _bank.Take(MusicChoice.Discovery) is { } stream)
            {
                _stinger.Stream = stream;
                _stinger.Play();
            }
        });
    }

    /// <summary>
    /// The opening's cue: on as the opening's first frame is drawn, from the cue's own start, which is timed to the pictures; off, and what
    /// the moment calls for (the title theme) comes in under its tail.
    /// </summary>
    public void Opening(bool on) => _opening = on;

    /// <summary>The start screen's theme on (before a world is played) or off.</summary>
    public void Title(bool on) => _title = on;

    /// <summary>A new character takes the road in: the title theme stays with them down it (owner ruling 2026-10-24).</summary>
    public void TheRoadIn() => _road = RoadSeconds;

    /// <summary>A frame: where the player stands.</summary>
    public void Update(Vector3 feet, double delta)
    {
        var here = new Vector2(feet.X, feet.Z);
        bool fighting = false, tense = false;
        if (_session?.Simulation is { } simulation)
        {
            bool engaged = false, noticed = false;
            foreach (var c in simulation.Creatures)
            {
                if (!c.Alive)
                    continue;
                float away = new Vector2(c.Body.XMm / 1000f, c.Body.ZMm / 1000f).DistanceTo(here);
                engaged |= c.Mind == CreatureMind.Engaged && away < FightRange;
                noticed |= !c.Peaceful && c.Mind is CreatureMind.Suspicious or CreatureMind.Searching && away < TenseRange;
            }
            _sinceFight = engaged ? 0 : _sinceFight + delta;
            _sinceTense = noticed ? 0 : _sinceTense + delta;
            fighting = _sinceFight < FightHold;
            tense = _sinceTense < TenseHold;
            Walk(here, simulation, delta);
        }
        _road = Math.Max(0, _road - delta);
        string? theme = _places.Where(p => p.At.DistanceTo(here) <= p.Radius).Select(p => p.Theme).FirstOrDefault();
        Play(MusicChoice.Bed(_opening, _title, fighting, tense, theme, SkyView.DaylightNow < 0.5f, _road > 0, Exploring()));
    }

    // The place the character is in, and whether it was already known when they walked in: discovering it on the way in is a first visit.
    private void Walk(Vector2 here, Simulation simulation, double delta)
    {
        string? area = _areas.Where(a => a.At.DistanceTo(here) <= a.Radius).OrderBy(a => a.At.DistanceTo(here)).Select(a => a.Id).FirstOrDefault();
        if (area != _area)
        {
            _area = area;
            _areaKnown = area is not null && simulation.Map.Places.Any(p => p.LocationId == area);
            _inArea = 0;
        }
        _inArea += delta;
    }

    private bool Exploring()
    {
        if (_area is null || !_areaKnown || _inArea < ExploreWait)
            return false;
        return (_inArea - ExploreWait) % (ExplorePlay + ExploreRest) < ExplorePlay;
    }

    public override void _Process(double delta)
    {
        if (_decks[0] is null)
            return;
        if (_opening || _title)
            Play(_opening ? MusicChoice.Opening : MusicChoice.Title);
        float step = (float)(delta / _fade);
        float duck = _stinger.Playing ? Duck : 1f;
        for (int i = 0; i < 2; i++)
        {
            var deck = _decks[i];
            float target = i == _live ? duck : 0f;
            float level = Mathf.MoveToward(Mathf.DbToLinear(deck.VolumeDb), target, step);
            deck.VolumeDb = Mathf.LinearToDb(Math.Max(level, 0.0001f));
            if (i != _live && level <= 0.0001f && deck.Playing)
                deck.Stop();
        }
    }

    private void Play(string? cue)
    {
        if (cue == _cue)
            return;
        // The opening's cue begins from its own silence: in at once, at its start, so the swell lands where the pictures expect it.
        _fade = cue == MusicChoice.Opening ? 0.05
            : cue == MusicChoice.Combat ? FightFadeSeconds
            : cue == MusicChoice.Tension ? TenseFadeSeconds
            : _cue is MusicChoice.Title or MusicChoice.Explore or MusicChoice.Opening || cue == MusicChoice.Explore ? SlowFadeSeconds
            : FadeSeconds;
        // The tension layer and the fight are one take: into the fight goes the take the layer was cut from, at the layer's place in it.
        bool layered = _cue is MusicChoice.Tension or MusicChoice.Combat && cue is MusicChoice.Tension or MusicChoice.Combat;
        var from = _decks[_live];
        string? paired = layered && _bank.Last(_cue!) is { } playing ? cue + playing[_cue!.Length..] : null;
        _cue = cue;
        _live = 1 - _live;
        var deck = _decks[_live];
        // A cue the set lacks (reported by the bank as unknown) is silence, asked for once until the moment changes.
        if (cue is null || _bank.Take(cue, paired) is not { } stream)
        {
            deck.Stop();
            return;
        }
        deck.Stream = stream;
        deck.VolumeDb = -80f;
        bool together = layered && from.Playing && from.Stream is { } other && Math.Abs(other.GetLength() - stream.GetLength()) < 0.01;
        deck.Play(together ? from.GetPlaybackPosition() : 0f);
    }
}
