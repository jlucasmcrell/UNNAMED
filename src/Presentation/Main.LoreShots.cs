// UNNAMED Presentation - the lore system's capture pass (spec 41, owner finding 41)
// Godot presentation only: it drives the world through the game's own commands and photographs what it shows (D-11)

using Godot;
using System.Text.Json;
using UNNAMED.Domain.Knowledge;
using UNNAMED.Domain.Spatial;
using UNNAMED.Presentation.Player;
using UNNAMED.World.Runtime;

namespace UNNAMED.Presentation;

public partial class Main
{
    // Opt-in evidence for spec 41: the four places lore can be met - the banner a landmark raises, an NPC asked, a book read, and the journal's
    // Lore tab - photographed in one run. Nothing here writes simulation state: the character walks with the gameplay's own steering, talks
    // through the dialogue's own replies and reads through the interact the E key sends, so every page in the frames was filed by the systems
    // that file them. The banner is the waystation's welcome: a new character starts inside its discovery radius, so it rises on the first tick.
    private const string LoreShotsOption = "--lore-shots";

    /// <summary>Where the character is put for the book: Sel Arien's survey record on her table, east of the lodge.</summary>
    private static readonly (double X, double Z) LoreShotsTable = (474.6, 721.4);

    private string? _loreShotsDirectory;
    private ulong _loreNext;
    private int _loreStep;
    private bool _loreBusy;

    private void InstallLoreShots()
    {
        if (_options.GetValueOrDefault(LoreShotsOption) is not { Length: > 0 } directory)
            return;
        _loreShotsDirectory = Path.GetFullPath(directory);
        Directory.CreateDirectory(_loreShotsDirectory);
        _loreNext = Time.GetTicksMsec() + 2600;
        // The harness has the controller: ReadInput leaves the body alone while this stands, so the steering below is what moves it.
        _rcd3Driving = true;
        GD.Print("LORE_SHOTS installed " + _loreShotsDirectory);
    }

    /// <summary>Driven from the main loop: one step is taken at a time, and the loop waits for it.</summary>
    private void TickLoreShots()
    {
        if (_loreShotsDirectory is null || _loreBusy || Time.GetTicksMsec() < _loreNext)
            return;
        _loreNext = Time.GetTicksMsec() + 400;
        _loreBusy = true;
        _ = StepLoreShots();
    }

    private async Task StepLoreShots()
    {
        try
        {
            await OneLoreStep();
        }
        catch (Exception error)
        {
            GD.PushError("LORE_SHOTS failed: " + error);
            File.WriteAllText(Path.Combine(_loreShotsDirectory!, "failed.txt"), error.ToString());
            GetTree().Quit(3);
        }
        finally
        {
            _loreBusy = false;
        }
    }

    private async Task OneLoreStep()
    {
        int step = _loreStep++;
        var simulation = _session.Simulation!;
        switch (step)
        {
            case 0:
            {
                // The banner the waystation's own discovery raises on the first tick, and the page it filed behind it. The clock is put at
                // mid-morning first, through the world's own advance step, so the frame is the hollow in daylight rather than the moment the
                // world opens (the same step an absence takes; nothing here writes state by hand).
                for (int frame = 0; frame < 1200 && simulation.Lore.All(r => r.EntryId != "lore.ashen_hollow"); frame++)
                    await Rcd3Wait(0.02);
                LoreClockTo(10 * 60);
                await Rcd3Wait(1.6);
                await Rcd3Shot(_loreShotsDirectory!, "lore_landmark_banner");
                LoreEvidence("banner", $"the banner a landmark raises; the character stands {Where(simulation)}");
                break;
            }
            case 1:
            {
                // Sel Arien, asked about the hounds. She keeps her survey table in the open east of the lodge, so the walk is across open
                // ground: the steward is behind a door and a harness that walks at a wall learns nothing.
                if (!await Approach("npc.ashen_hollow.sel_arien", 3.0, 150))
                    throw new InvalidOperationException($"could not reach Sel Arien; the character stands {Where(simulation)}");
                _controller.Talk("npc.ashen_hollow.sel_arien");
                await Rcd3Wait(0.6);
                // A first meeting greets once; leave it and come back, so the conversation is the one that offers the questions.
                if (_dialogue.Visible)
                {
                    _dialogue.Answer("leave");
                    await Rcd3Wait(0.5);
                }
                _controller.Talk("npc.ashen_hollow.sel_arien");
                await Rcd3Wait(0.6);
                string? asked = _dialogue.Replies.FirstOrDefault(id => id.Contains("hounds", StringComparison.Ordinal))
                    ?? _dialogue.Replies.FirstOrDefault(id => id.StartsWith("ask", StringComparison.Ordinal) && id != "ask");
                GD.Print($"LORE_SHOTS sel replies: {string.Join(", ", _dialogue.Replies)} asked={asked ?? "none"}");
                if (asked is not null)
                {
                    _dialogue.Answer(asked);
                    await Rcd3Wait(0.8);
                    await Rcd3Shot(_loreShotsDirectory!, "lore_npc_told");
                    LoreEvidence("told", $"an NPC asked about a creature ({asked}): the reply, and the page it files");
                    if (_dialogue.Replies.Contains("back"))
                        _dialogue.Answer("back");
                    await Rcd3Wait(0.3);
                }
                else
                {
                    // The conversation is pictured as it stands, and the line says the question was not offered: an honest frame, not a claim.
                    await Rcd3Shot(_loreShotsDirectory!, "lore_npc_dialogue");
                    LoreEvidence("dialogue", $"the conversation as Sel Arien offers it ({string.Join(", ", _dialogue.Replies)}); no tell-me-about reply was offered");
                }
                _dialogue.Leave();
                await Rcd3Wait(0.4);
                break;
            }
            case 2:
            {
                // The survey record: something written, read where it stands. Pressing E sends the interact the panel is opened by.
                if (!await WalkTo(LoreShotsTable.X, LoreShotsTable.Z, 2.0, 120))
                    throw new InvalidOperationException($"could not reach the survey table; the character stands {Where(simulation)}");
                _controller.Interact("readable.sel_field_record");
                for (int frame = 0; frame < 400 && !_reading.Visible; frame++)
                    await Rcd3Wait(0.02);
                await Rcd3Wait(0.7);
                if (!_reading.Visible)
                    throw new InvalidOperationException("the survey record did not open a reading panel");
                await Rcd3Shot(_loreShotsDirectory!, "lore_book");
                LoreEvidence("book", $"a book read at its table ({_reading.EntryId})");
                CloseScreens();
                await Rcd3Wait(0.4);
                break;
            }
            case 3:
            {
                // The journal's Lore tab, at the entry the banner offered.
                _reading.Close();
                _journal.OpenLore("lore.old_late");
                Show(_journal);
                await Rcd3Wait(0.9);
                await Rcd3Shot(_loreShotsDirectory!, "lore_journal");
                LoreEvidence("journal", "the journal's Lore tab: the headings, the page counts and the dots");
                CloseScreens();
                await Rcd3Wait(0.3);
                break;
            }
            default:
            {
                File.WriteAllText(Path.Combine(_loreShotsDirectory!, "complete.json"),
                    JsonSerializer.Serialize(new
                    {
                        pages = simulation.Lore.Select(r => new { entry = r.EntryId, page = r.Page, how = LoreSources.Key(r.Source), source = r.SourceRef }),
                        entries = _session.Setup.Lore.Count,
                    }, new JsonSerializerOptions { WriteIndented = true }));
                GD.Print($"LORE_SHOTS complete: {simulation.Lore.Length} pages of {_session.Setup.Lore.Count} entries");
                GetTree().Quit();
                break;
            }
        }
    }

    /// <summary>Walk to a place in metres on the region's own ground, steering every frame with the gameplay's own controller.</summary>
    private async Task<bool> WalkTo(double xMetres, double zMetres, double withinMetres, double maxSeconds)
    {
        var simulation = _session.Simulation!;
        double deadline = Time.GetTicksMsec() + maxSeconds * 1000;
        double said = Time.GetTicksMsec();
        while (Time.GetTicksMsec() < deadline)
        {
            var body = simulation.Player.Body;
            var to = new Vector3((float)(xMetres - body.XMm / 1000.0), 0, (float)(zMetres - body.ZMm / 1000.0));
            if (Time.GetTicksMsec() - said > 5000)
            {
                said = Time.GetTicksMsec();
                GD.Print($"LORE_SHOTS walking to ({xMetres}, {zMetres}) from {Where(simulation)} scripted={_scripted} modal={Modal}");
            }
            if (to.Length() <= withinMetres)
            {
                _controller.SteerWorld(Vector3.Zero, Gait.Run, _camera);
                return true;
            }
            _camera.Yaw = Mathf.Atan2(-to.X, -to.Z);
            _controller.SteerWorld(to.Normalized(), Gait.Run, _camera, faceCamera: true);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        _controller.SteerWorld(Vector3.Zero, Gait.Run, _camera);
        return false;
    }

    /// <summary>Walk to where an NPC stands now - they have a day of their own - and stop inside talk reach.</summary>
    private async Task<bool> Approach(string npcId, double withinMetres, double maxSeconds)
    {
        var simulation = _session.Simulation!;
        double deadline = Time.GetTicksMsec() + maxSeconds * 1000;
        while (Time.GetTicksMsec() < deadline)
        {
            var view = simulation.Npcs.FirstOrDefault(n => n.Id == npcId);
            if (view is null)
                return false;
            var body = simulation.Player.Body;
            var to = new Vector3((float)((view.Body.XMm - body.XMm) / 1000.0), 0, (float)((view.Body.ZMm - body.ZMm) / 1000.0));
            if (to.Length() <= withinMetres && _controller.InTalkReach(view))
            {
                _controller.SteerWorld(Vector3.Zero, Gait.Run, _camera);
                await Rcd3Wait(0.2);
                return true;
            }
            _camera.Yaw = Mathf.Atan2(-to.X, -to.Z);
            _controller.SteerWorld(to.Normalized(), Gait.Run, _camera, faceCamera: true);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        _controller.SteerWorld(Vector3.Zero, Gait.Run, _camera);
        return false;
    }

    /// <summary>The first tick of a minute of the first capture day, by the world's own advance - the same catch-up an absence takes.</summary>
    private void LoreClockTo(int minuteOfDay)
    {
        var simulation = _session.Simulation!;
        var clock = _session.Setup.Clock;
        long ticksPerMinute = clock.TicksPerDay / (clock.HoursPerDay * 60L);
        // Day one, as the inn's frames are taken: a fresh world opens at 07:30 and plans no day before its first midnight.
        long target = clock.TicksPerDay - clock.StartTicks + minuteOfDay * ticksPerMinute;
        if (target > simulation.WorldTick)
            simulation.Advance(target - simulation.WorldTick);
    }

    private static string Where(Simulation simulation)
    {
        var body = simulation.Player.Body;
        return $"({body.XMm / 1000.0:0.0}, {body.ZMm / 1000.0:0.0})";
    }

    /// <summary>One line per frame taken, beside the pictures: what was filed, and why that is the thing the picture shows.</summary>
    private void LoreEvidence(string shot, string what)
    {
        var simulation = _session.Simulation!;
        File.AppendAllText(Path.Combine(_loreShotsDirectory!, "shots.jsonl"),
            JsonSerializer.Serialize(new
            {
                shot,
                what,
                tick = simulation.WorldTick,
                pages = simulation.Lore.Select(r => $"{r.EntryId}#{r.Page}").ToArray(),
            }) + System.Environment.NewLine);
    }
}
