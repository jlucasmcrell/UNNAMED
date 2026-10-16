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
/// pause in a fight does not drop the music. A change of cue cross-fades over <see cref="FadeSeconds"/> (faster into a fight). A place
/// discovered plays the discovery stinger over a ducked bed. Every player is on the Music bus, whose volume the Sound screen sets.
/// </summary>
public partial class MusicPlayer : Node
{
    private const float FightRange = 30f;
    private const double FightHold = 8;
    private const double FadeSeconds = 4;
    private const double FightFadeSeconds = 1.2;
    private const float Duck = 0.35f;

    private readonly AudioStreamPlayer[] _decks = new AudioStreamPlayer[2];
    private readonly List<(Vector2 At, float Radius, string Theme)> _places = new();
    private AudioStreamPlayer _stinger = null!;
    private SoundBank _bank = null!;
    private GameSession? _session;
    private int _live;
    private string? _cue;
    private double _fade = FadeSeconds;
    private double _sinceFight = double.MaxValue;
    private bool _title;

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
            if (bindings.MusicPlaces.GetValueOrDefault(site.Id) is { } place)
                _places.Add((new Vector2(site.XMm / 1000f, site.ZMm / 1000f), place.RadiusM, place.Theme));
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

    /// <summary>The start screen's theme on (before a world is played) or off.</summary>
    public void Title(bool on) => _title = on;

    /// <summary>A frame: where the player stands.</summary>
    public void Update(Vector3 feet, double delta)
    {
        bool fighting = false;
        if (_session?.Simulation is { } simulation)
        {
            bool engaged = simulation.Creatures.Any(c => c.Alive && c.Mind == CreatureMind.Engaged
                && new Vector2(c.Body.XMm / 1000f - feet.X, c.Body.ZMm / 1000f - feet.Z).Length() < FightRange);
            _sinceFight = engaged ? 0 : _sinceFight + delta;
            fighting = _sinceFight < FightHold;
        }
        var here = new Vector2(feet.X, feet.Z);
        string? theme = _places.Where(p => p.At.DistanceTo(here) <= p.Radius).Select(p => p.Theme).FirstOrDefault();
        Play(MusicChoice.Bed(_title, fighting, theme, SkyView.DaylightNow < 0.5f));
    }

    public override void _Process(double delta)
    {
        if (_decks[0] is null)
            return;
        if (_title)
            Play(MusicChoice.Title);
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
        _fade = cue == MusicChoice.Combat ? FightFadeSeconds : FadeSeconds;
        _cue = cue;
        _live = 1 - _live;
        var deck = _decks[_live];
        // A cue the set lacks (reported by the bank as unknown) is silence, asked for once until the moment changes.
        if (cue is null || _bank.Take(cue) is not { } stream)
        {
            deck.Stop();
            return;
        }
        deck.Stream = stream;
        deck.VolumeDb = -80f;
        deck.Play();
    }
}
