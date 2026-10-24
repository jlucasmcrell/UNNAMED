// UNNAMED Presentation - Main's guidance: the moments that call for a hint, and the hint line that shows it (WP10, §2.10 O1)
// Godot presentation only: this file renders state and submits commands; it never writes state.

using UNNAMED.Domain.Guidance;
using UNNAMED.Presentation.Settings;
using UNNAMED.Presentation.Ui;
using UNNAMED.World.Runtime;

namespace UNNAMED.Presentation;

public partial class Main
{
    private const double LowHealthShare = 0.3;

    private HintLine? _hintLine;
    private bool _chatWas, _reputationWas, _trainerWas;

    /// <summary>Moments with nothing left to show at this guidance level: asked no more each frame until the settings change.</summary>
    private readonly HashSet<string> _hintless = new(StringComparer.Ordinal);

    /// <summary>
    /// Hints (PJ07): each named moment shows the next hint waiting on it that the player has not seen, as their guidance level allows,
    /// and marks it seen in their preferences, so it never shows again until they reset hints. A hint the line has no room for is not
    /// marked, so it waits for the moment's next turn. A harness run plays on the defaults and keeps nothing, so its hints come and go
    /// with the run. True when a hint was shown or queued.
    /// </summary>
    private bool Hint(string trigger)
    {
        if (_hintLine is null || _session.Hints.Count == 0 || _hintless.Contains(trigger))
            return false;
        var prefs = SettingsStore.Current;
        var ui = prefs.Profile.Interface;
        var depth = ui.Guidance switch { Guidance.Full => HintDepth.Every, Guidance.Light => HintDepth.FirstOfEachCategory, _ => HintDepth.None };
        var seen = ui.SeenHints.ToHashSet(StringComparer.Ordinal);
        if (HintRules.Pick(_session.Hints.Values, trigger, seen, depth) is not { } hint)
        {
            _hintless.Add(trigger);
            return false;
        }
        if (!_hintLine.Show(HintRules.Words(hint.Text, HelpPanel.Key)))
            return false;
        SettingsStore.Remember(prefs with { Profile = prefs.Profile with { Interface = ui with { SeenHints = ui.SeenHints.Append(hint.Id).ToArray() } } });
        return true;
    }

    /// <summary>The hint line on the HUD, and the moments the simulation's events name.</summary>
    private void SubscribeGuidance()
    {
        _hintLine = new HintLine { Name = "HintLine" };
        AddChild(_hintLine);
        SettingsStore.Changed += () =>
        {
            _hintless.Clear();
            if (SettingsStore.Current.Profile.Interface.Guidance == Guidance.Off)
                _hintLine?.Clear();
        };
        var player = () => _session.Simulation!.PlayerId;
        _session.Subscribe<CreatureNoticed>(e =>
        {
            if (e.Mind == World.CreatureMind.Engaged)
                Hint(HintTriggers.CreatureEngaged);
        });
        _session.Subscribe<HitResolved>(e =>
        {
            if (e.Target == player() && e.Damage > 0)
                Hint(HintTriggers.HitTaken);
        });
        _session.Subscribe<PlayerDowned>(_ => Hint(HintTriggers.PlayerDowned));
        _session.Subscribe<PlayerDied>(_ => Hint(HintTriggers.PlayerDied));
        _session.Subscribe<CreatureKilled>(e =>
        {
            if (e.Killer == player())
                Hint(HintTriggers.CreatureKilled);
        });
        _session.Subscribe<ItemMoved>(e =>
        {
            if (e.Actor == player() && e.To.Kind == PlaceKind.Inventory && e.From.Kind != PlaceKind.Inventory)
                Hint(HintTriggers.ItemPickedUp);
        });
        _session.Subscribe<QuestStarted>(_ => Hint(HintTriggers.QuestStarted));
        _session.Subscribe<ExperienceGained>(e =>
        {
            if (e.LevelsGained > 0)
                Hint(HintTriggers.LevelUp);
        });
        _session.Subscribe<SkillPracticed>(e =>
        {
            if (e.Xp > 0)
                Hint(HintTriggers.SkillPracticed);
        });
        _session.Subscribe<NodeGathered>(_ => Hint(HintTriggers.NodeGathered));
        _session.Subscribe<StoneTouched>(_ => Hint(HintTriggers.StoneTouched));
        _session.Subscribe<TechniqueLearned>(e =>
        {
            if (_session.Setup.Magic.Formulas.ContainsKey(e.DefinitionId))
                Hint(HintTriggers.FormulaLearned);
        });
        _session.Subscribe<ItemEquipped>(e =>
        {
            if (e.Actor == player())
                Hint(HintTriggers.ItemWorn);
        });
        _inventory.StashShown = () => Hint(HintTriggers.StashOpened);
        _hud.Bars.Touched = () => Hint(HintTriggers.BarOpened);
        _session.Subscribe<CompanionRecruited>(_ => Hint(HintTriggers.CompanionRecruited));
        _session.Subscribe<CompanionDowned>(_ => Hint(HintTriggers.CompanionDowned));
        _session.Subscribe<ServiceOpened>(e =>
        {
            if (e.Service == Domain.Social.NpcServices.Trade)
                Hint(HintTriggers.TradeOpened);
        });
        // hire_offered is named when the hiring sheet opens (Main.Party.cs). Still to come: price_changed (WP14's markets), when it lands.
        // bar_opened, stash_opened and item_worn above; chat_opened, reputation_opened and trainer_opened are screens opening (GuidanceFrame).
    }

    /// <summary>The moments a frame shows rather than an event: a prompt up, health low, the pack full, a station open.</summary>
    private void GuidanceFrame(Simulation simulation, CombatView combat, bool prompting)
    {
        if (_hintLine is null)
            return;
        if (prompting)
            Hint(HintTriggers.PromptShown);
        if (!combat.Defeated && combat.Health > 0 && combat.Health <= combat.MaxHealth * LowHealthShare)
            Hint(HintTriggers.HealthLow);
        if (simulation.Pack.Used >= simulation.Pack.Capacity)
            Hint(HintTriggers.PackFull);
        if (simulation.Player.CarriedGrams >= simulation.Player.CarryLimitGrams)
            Hint(HintTriggers.OverLimit);
        if (_inventory.OpenStation is not null)
            Hint(HintTriggers.StationOpened);
        // Screens that name a moment as they open: a rising edge, so it is named once a time, not every frame.
        bool chat = _chat.Visible, reputation = _reputation.Visible;
        bool trainer = _learn.Visible && simulation.Conversation is { } talk && simulation.Teaching(talk.NpcId) is { Lessons.IsEmpty: false };
        if (chat && !_chatWas)
            Hint(HintTriggers.ChatOpened);
        if (reputation && !_reputationWas)
            Hint(HintTriggers.ReputationOpened);
        if (trainer && !_trainerWas)
            Hint(HintTriggers.TrainerOpened);
        (_chatWas, _reputationWas, _trainerWas) = (chat, reputation, trainer);
    }
}
