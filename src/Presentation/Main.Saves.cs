// UNNAMED Presentation - Main's saves: new game, load, continue, quicksave, and the resync after each (D-11)
// Godot presentation only: this file renders state and submits commands; it never writes state.

using Godot;
using UNNAMED.Domain.Character;
using UNNAMED.Persistence;
using UNNAMED.Presentation.Greybox;
using UNNAMED.Presentation.Player;
using UNNAMED.Presentation.Ui;
using UNNAMED.World;

namespace UNNAMED.Presentation;

public partial class Main
{
    /// <summary>
    /// The character a new game makes until the creator screen exists (WP2's local part): the slice's acceptance character, with no Call,
    /// exactly as before the creator.
    /// </summary>
    private static readonly CharacterSpec DefaultCharacter = CharacterSpec.Quick(CharacterSpec.DefaultName, null);

    /// <summary>After a new game or a load: the only moments presentation copies the whole state.</summary>
    private void Resync()
    {
        _controller.Resync();
        _creatures.Reset();
        foreach (var door in _session.Simulation!.Doors)
            _hollow.SetDoor(door.Site.Key, door.Open);
        _hollow.SetFlags(_session.Simulation!.Switches, _session.Simulation!.Barriers);
        _items.Refresh(_session.Simulation!);
        // Panels open on the world before are closed on this one: a conversation it does not have, a container, a trader or a station
        // that was the other world's (the Phase-1 technical audit, M-03).
        _dialogue.Refresh();
        CloseScreens();
        var body = _controller.Authoritative;
        _camera.Yaw = PlayerController.FacingRadians(body.FacingMdeg) + Mathf.Pi;
        _lastFeet = HollowView.ToGodot(body.XMm, body.YMm, body.ZMm);
        // The ground scatter is this world's: placed from its seed (the same seed again keeps what is placed).
        _scatter.Build(_session.Simulation!.World.WorldSeed);
    }

    /// <summary>F5: taken now, written in the background (P-01); "Saved", or why not, when it has been.</summary>
    private void QuickSave() => _session.SaveInBackground(SaveSlots.Quick);

    private void QuickLoad() => LoadChosen(SaveSlots.Quick, SaveCopy.Current);

    /// <summary>
    /// <c>--resume-shots dir</c>, with <c>--profile</c>: the start screen as a player meets it, Continue pressed, and the saves list opened
    /// in the game - each pictured - then quit. The Phase-1 technical audit's B-01, shown.
    /// </summary>
    private void ResumeShots(string directory)
    {
        switch (++_resumeFrame)
        {
            case 20:
                SaveScreenshot(directory, "01_start_screen");
                if (!_title.PressContinue())
                {
                    GD.PushError("UNNAMED resume shots: the start screen offered no Continue");
                    GetTree().Quit(1);
                }
                break;
            case 80:
                SaveScreenshot(directory, "02_continued");
                _saves.OpenInGame();
                break;
            case 100:
                SaveScreenshot(directory, "03_saves_in_game");
                GD.Print($"UNNAMED resume shots written to {directory}: continued at world tick {_session.Simulation?.WorldTick}");
                GetTree().Quit(_session.Simulation is null ? 1 : 0);
                break;
        }
    }

    private int _resumeFrame;

    /// <summary>A world began - a new game or a load: the views copy it whole, and a player's mouse is the game's again.</summary>
    private StartCamera? _startCamera;

    private void Started()
    {
        if (_startCamera is not null)
        {
            _startCamera.QueueFree();
            _startCamera = null;
            _camera.Camera.MakeCurrent();
            StartCamera.Track(this, _camera.Camera);
        }
        CloseFrontEnd();
        _music.Title(false);
        _lastSave = null;
        _loadedFrom = null;
        _hud.Visible = true;
        Resync();
        GD.Print($"UNNAMED world: seed {WorldSeed.Format(_session.Simulation!.World.WorldSeed)}, tick {_session.Simulation.WorldTick}");
        // THE BASELINE TUPLE, PRINTED - so a change can be PROVED baseline-neutral by two boots instead of
        // argued to be from the code. `worldgen_fingerprint` plus every cell's `baseline_hash` is what
        // PERSISTENCE.md 6.1/6.4 decides save compatibility on; if a change moves neither, no transition is
        // required and no existing save can refuse. One line per cell, so the diff is the evidence.
        // (The smoke DIGEST is not this: it compares a run's save against its own load.)
        if (System.Environment.GetEnvironmentVariable("UNNAMED_WORLDGEN_REPORT") is { Length: > 0 })
        {
            // The EXACT tuple PERSISTENCE.md 6.1/6.4 decides save compatibility on: the fingerprint
            // (generator identity + version + rng contract + profile digest + canonical probes) and every
            // cell's own baseline_hash. Two boots are the measurement; this is the instrument.
            var generator = _session.Generator;
            ulong seed = _session.Simulation!.World.WorldSeed;
            GD.Print($"UNNAMED worldgen: version {generator.WorldgenVersion}, rng contract {generator.RngContractVersion}, fingerprint {generator.Fingerprint}");
            foreach (var cell in _session.Setup.Layout.CellKeys)
                GD.Print($"UNNAMED worldgen:   baseline_hash {cell} = {generator.Generate(seed, UNNAMED.World.CellKey.Parse(cell)).Digest}");
            // WHICH ANCHORS THIS BUILD ACCEPTS A SAVE THROUGH. A refusal names a FromFingerprint that is in
            // neither list, so printing the lists is what turns "refused" into "refused because nothing covers it".
            GD.Print($"UNNAMED worldgen:   terrain-only fingerprint (no fixed nodes, the M3f FromFingerprint) = {_session.FingerprintWithoutNodes}");
            foreach (var t in _session.Transitions)
                GD.Print($"UNNAMED worldgen:   transition '{t.Name}': {t.FromFingerprint} -> {t.ToFingerprint}");
        }
        // SURVEY EVERY SAVE, WRITING NOTHING. PlanLoad runs the whole load pipeline (version gate, migration
        // chain, per-cell baseline proof, transition lookup) into a throwaway registry and reports instead of
        // throwing - and it does NOT record the load in the rotation metadata the way a real Load does (see
        // the store's PlanMigration). So this answers "does the owner have usable saves" without modifying the
        // saves it is asking about. A Load per save would have written to every one of them.
        if (System.Environment.GetEnvironmentVariable("UNNAMED_LOAD_CHECK") is { Length: > 0 } loadCheckDir)
        {
            // SURVEY A SAVE DIRECTORY, WRITING NOTHING. PlanLoadAt runs the whole load pipeline (version gate,
            // migration chain, per-cell baseline proof, transition lookup) into a throwaway registry and reports
            // instead of throwing - and it does NOT record the load in the rotation metadata the way a real Load
            // does. It also reaches saves/default, which no scripted mode can: every one of them routes its
            // profile to scratch/run-<pid> (Main.cs:185-191), so no harness has ever read the owner's saves.
            foreach (string dir in Directory.EnumerateDirectories(loadCheckDir).OrderBy(d => d, StringComparer.Ordinal))
            {
                string name = Path.GetFileName(dir);
                try
                {
                    var plan = _session.PlanLoadAt(dir);
                    GD.Print($"UNNAMED loadcheck: {name}|{plan.Result}|schema {plan.SourceSchema}->{plan.CurrentSchema}|" +
                             $"worldgen {plan.SourceWorldgenVersion}->{plan.CurrentWorldgenVersion}|rng {plan.SourceRngContract}->{plan.CurrentRngContract}|" +
                             $"fingerprint {(plan.FingerprintChanged ? "CHANGED" : "same")}|content {(plan.ContentChanged ? "changed" : "same")}|" +
                             $"loss {plan.Loss.Count}|blockers {plan.Blockers.Count}|warnings {plan.Warnings.Count}");
                    foreach (string b in plan.Blockers)
                        GD.Print($"UNNAMED loadcheck:   BLOCKER {name}: {b}");
                    foreach (string w in plan.Warnings)
                        GD.Print($"UNNAMED loadcheck:   warning {name}: {w}");
                    // THE REPORT'S OWN TEXT BODY, which is where the cells are actually NAMED.
                    // MigrationReport.CellsMismatched holds "{cell}: {detail}" per cell and ToText() prints each
                    // one (MigrationReport.cs:118-119); the blocker line carries only a COUNT. Capturing the
                    // console summary alone loses the list - the information was there, the capture missed it.
                    GD.Print($"UNNAMED loadcheck: --- report text for {name} ---");
                    foreach (string line in plan.ToText().Split('\n'))
                        GD.Print($"UNNAMED loadcheck: | {line.TrimEnd('\r')}");
                }
                catch (Exception e)
                {
                    GD.Print($"UNNAMED loadcheck: {name}|THREW|{e.GetType().Name}: {e.Message}");
                }
            }
        }
    }

    /// <summary>Continue (B-01): the newest save that can be loaded, as the start screen offers it. Null when there is none, or it failed.</summary>
    private (string Slot, SaveCopy Copy, LoadResult Result)? Continue() =>
        _session.StartChoice().Continue is { } next && LoadChosen(next.Slot, next.Copy, next) is { } result ? (next.Slot, next.Copy, result) : null;

    /// <summary>
    /// Load the copy of a save the player chose. When it cannot be loaded, the saves list opens saying why, with that save's backups and
    /// the save it displaced beneath it - offered, never loaded in its place (PERSISTENCE.md §7.2). A save the list offered is loaded from
    /// its own character's folder (OD-14); without one, the slot is the character in play's.
    /// </summary>
    private LoadResult? LoadChosen(string slot, SaveCopy copy, SaveSummary? offered = null)
    {
        string what = SavesPanel.Describe(slot, copy);
        try
        {
            var result = offered is null ? _session.Load(slot, copy) : _session.Load(offered);
            Started();
            _loadedFrom = what;
            _hud.Toast(result.IsComplete ? $"Loaded: {what}" : $"Loaded {what}, with losses - see the log");
            foreach (string problem in result.Report.Loss.Concat(result.Report.Warnings))
                GD.PushWarning(problem);
            return result;
        }
        catch (Exception e) when (e is SaveException or IOException or UnauthorizedAccessException)
        {
            GD.PushError($"UNNAMED load of {slot} ({copy}) failed: {e.Message}");
            string failure = $"{what} could not be loaded: {e.Message}";
            if (_session.Simulation is null)
                _saves.ShowFailure(failure);
            else
                _saves.OpenInGame(failure);
            return null;
        }
    }
}
