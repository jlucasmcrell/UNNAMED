// UNNAMED Presentation - Main's event subscriptions: what the simulation's events show (D-11)
// Godot presentation only: this file renders state and submits commands; it never writes state.

using Godot;
using UNNAMED.Domain.Combat;
using UNNAMED.Domain.Companions;
using UNNAMED.Domain.Crafting;
using UNNAMED.Presentation.Greybox;
using UNNAMED.Presentation.Ui;
using UNNAMED.World.Runtime;

namespace UNNAMED.Presentation;

public partial class Main
{
    private readonly Dictionary<Type, int> _handlerFailures = new();

    /// <summary>
    /// A view's handler threw while the tick ran (H-02). The session's bus isolated it, so the tick went on; this says so - the first time
    /// for each kind of event, then every hundredth.
    /// </summary>
    private void SubscriberFailed(Exception exception, object @event)
    {
        var kind = @event.GetType();
        int seen = _handlerFailures[kind] = _handlerFailures.GetValueOrDefault(kind) + 1;
        if (seen == 1 || seen % 100 == 0)
            GD.PushError($"UNNAMED: a handler for {kind.Name} threw ({seen} so far); the tick went on without it: {exception}");
    }

    /// <summary>The last reply given, and when: what a hand-over in the same tick was for.</summary>
    private (string NpcId, long Tick)? _reply;

    private void Subscribe()
    {
        _session.Subscribe<BodyMoved>(_controller.OnBodyMoved);
        _session.Subscribe<PlayerRespawned>(_controller.OnRespawned);
        // Waystones (S-38): what a hand on a stone found, a return point chosen, and a journey's arrival with the time it took.
        _session.Subscribe<StoneTouched>(e =>
        {
            _hud.Toast(e.Text, 7);
            Show(_waystones, e.StoneKey);
        });
        _session.Subscribe<ReturnPointBound>(e => _hud.Toast($"The {StoneName(e.StoneKey)} is your return point. When you fall, you come back here.", 5));
        _session.Subscribe<PlayerTravelled>(e =>
        {
            _controller.OnTravelled(e);
            _autorun = false;
            CloseScreens();
            _hud.Toast($"The {StoneName(e.ToKey)}. {TravelWords.Journey(e.Cost.WalkTicks, TicksPerDay).Replace("You arrive the moment you leave", "You arrived the moment you left").Replace("would take", "would have taken")}", 7);
        });        _session.Subscribe<DoorToggled>(e =>
        {
            _controller.OnDoorToggled(e);
            _hollow.SetDoor(e.DoorKey, e.Open);
            _interior?.SetDoor(e.DoorKey, e.Open);
        });
        // Switches and barriers (M6): what a switch did, and every flag change shown as it now stands.
        _session.Subscribe<SwitchSet>(e =>
        {
            if (_session.Simulation!.ActiveLayout.FindSwitch(e.SwitchKey) is { } site)
                _hud.Toast(site.DoneText, 6);
        });
        _session.Subscribe<WorldFlagChanged>(_ =>
        {
            _hollow.SetFlags(_session.Simulation!.Switches, _session.Simulation!.Barriers, inPlay: true);
            _interior?.SetFlags(_session.Simulation!.Switches, _session.Simulation!.Barriers, inPlay: true);
        });
        _session.Subscribe<LocationDiscovered>(e => _hud.Toast($"Discovered: {Names.Of(_session, e.LocationId)}"));
        // Companions (M6): joined, told, downed, helped up, fallen back to the Waystone.
        _session.Subscribe<CompanionRecruited>(e => _hud.Toast($"{Names.Of(_session, e.NpcId)} joins you"));
        _session.Subscribe<CompanionOrdered>(e => _hud.Toast($"{Names.Of(_session, e.NpcId)}: {(e.Order == CompanionOrder.Follow ? "following" : "waiting")}", 2));
        _session.Subscribe<CompanionDowned>(e => _hud.Toast($"{Names.Of(_session, e.NpcId)} is down - reach them and press {HelpPanel.Key("interact")}", 6));
        _session.Subscribe<CompanionRevived>(e => _hud.Toast($"{Names.Of(_session, e.NpcId)} is back on their feet"));
        _session.Subscribe<CompanionFell>(e => _hud.Toast($"{Names.Of(_session, e.NpcId)} fell, and will be waiting at the Ashen Waystone", 6));
        // What the award did: what is left once the XP debt took its share, and that share (the Phase-1 technical audit, L-12).
        _session.Subscribe<ExperienceGained>(e =>
        {
            long kept = e.Awarded - e.Repaid;
            string xp = e.Repaid == 0 ? $"+{e.Awarded} XP" : kept > 0 ? $"+{kept} XP, {e.Repaid} to the XP debt" : $"{e.Repaid} XP to the XP debt";
            _hud.Toast(xp, 3);
            if (e.LevelsGained > 0)
                _hud.Announce("Level up", $"Level {e.Level}", HudStyle.Ember, ("A point to spend on the character sheet", HelpPanel.Key("character")));
        });
        _session.Subscribe<CommandRejected>(e =>
        {
            if (e.Command is InteractCommand or MoveItemCommand or EquipCommand or UnequipCommand or GatherCommand or CraftCommand or TakeAllCommand
                or SpendAttributeCommand or TouchStoneCommand or BindStoneCommand or TravelCommand)
                _hud.Toast(e.Reason, 3);
            // A jump or a stand refused under the beam says why; one refused mid-action or in the air stays quiet.
            if (e.Command is JumpCommand or CrouchCommand && e.Reason == "no room to stand")
                _hud.Toast("No room to stand", 2);
        });
        // The owner's M6 playtest: a take-all's tally, a point spent, and every shot drawn along the path it took.
        _session.Subscribe<TookAll>(e =>
        {
            _hud.Toast(e.Left == 0 ? $"Took everything ({e.Taken})" : $"Took {e.Taken}; {e.Left} left - {e.Why}", 3);
            // Emptied, the loot is done: the panel closes rather than leaving the pack open behind it (the owner's first playtest).
            if (e.Left == 0 && _inventory.OpenContainer == e.ContainerKey)
                CloseInventory();
        });
        _session.Subscribe<AttributeSpent>(e => _hud.Toast($"{char.ToUpperInvariant(UNNAMED.Domain.Progression.ProgressionKeys.Key(e.Attribute)[0])}" +
            $"{UNNAMED.Domain.Progression.ProgressionKeys.Key(e.Attribute)[1..]} {e.Value}", 3));
        _session.Subscribe<ShotLoosed>(Shot);
        _session.Subscribe<ItemMoved>(e =>
        {
            _inventory.Refresh();
            if (e.From.Kind == PlaceKind.Ground || e.To.Kind == PlaceKind.Ground)
                _items.Refresh(_session.Simulation!);
        });
        _session.Subscribe<QuickSlotAssigned>(_ => _inventory.Refresh());
        _session.Subscribe<ItemEquipped>(_ => _inventory.Refresh());
        _session.Subscribe<ItemUnequipped>(_ => _inventory.Refresh());
        _session.Subscribe<NodeGathered>(e =>
        {
            _inventory.Refresh();
            string spent = !e.Spent ? ""
                : _session.Setup.Crafting.Nodes[e.NodeDefId].Respawn == Respawn.None ? $" - the {Names.Of(_session, e.NodeDefId)} is worked out"
                : $" - nothing more until the {Names.Of(_session, e.NodeDefId)} grows back";
            _hud.Toast($"+{e.Count} {Names.Of(_session, e.ItemId)}{spent}", 3);
        });
        _session.Subscribe<ItemCrafted>(e =>
        {
            _inventory.Refresh();
            _hud.Toast($"Made {Names.Item(_session, e.ItemId, e.Quality)}{(e.Count > 1 ? $" x{e.Count}" : "")}", 3);
        });
        SubscribeSocial();
        SubscribeQuests();
        SubscribeCombat();
        SubscribeGuidance();
    }

    /// <summary>
    /// A shot drawn along the path the simulation resolved (the owner's M6 playtest): from the shoulder to where it stopped - into a
    /// creature at its middle, a wall at chest height, or at the end of its range into the ground (an arrow) or bursting (a working).
    /// A working's art is named for it by the pipeline's convention: <c>spell.force.impulse_bolt</c> is <c>vfx.force.impulse_bolt_*</c>.
    /// </summary>
    private void Shot(ShotLoosed shot)
    {
        var terrain = _session.Setup.Layout.Space.Terrain;
        bool working = _session.Setup.Magic.Formulas.TryGetValue(shot.Source, out var formula);
        long range = working ? formula!.Blow?.ReachMm ?? 0 : _session.Simulation!.Combat.Weapon.ReachMm;
        double length = Math.Sqrt(Math.Pow(shot.ToXMm - shot.FromXMm, 2) + Math.Pow(shot.ToZMm - shot.FromZMm, 2));
        bool atRange = Math.Abs(length - range) < 20;
        var from = new Vector3(shot.FromXMm / 1000f, terrain.HeightAtMm(shot.FromXMm, shot.FromZMm) / 1000f + 1.4f, shot.FromZMm / 1000f);
        float height = shot.Target is not null ? 0.7f : atRange ? (working ? 1.2f : 0.05f) : 1.3f;
        var to = new Vector3(shot.ToXMm / 1000f, terrain.HeightAtMm(shot.ToXMm, shot.ToZMm) / 1000f + height, shot.ToZMm / 1000f);
        // Out of the hands, not the chest.
        from += (to - from).Normalized() * 0.4f;
        string? stem = working && shot.Source.IndexOf('.') is > 0 and var dot ? "vfx." + shot.Source[(dot + 1)..] : null;
        _projectiles.Loose(from, to, working, shot.Target is not null, stem);
    }

    /// <summary>Quests (M5): a toast when one starts, moves or ends; what it pays in the log. The tracker and journal redraw each frame.</summary>
    private void SubscribeQuests()
    {
        var quests = _session.Setup.Quests.Quests;
        string Title(string id) => quests.TryGetValue(id, out var q) ? q.Title : id;
        string Step(string quest, string objective) =>
            quests.TryGetValue(quest, out var q) && q.Objectives.TryGetValue(objective, out var o) ? o.Description : objective;
        _session.Subscribe<QuestStarted>(e => _hud.Toast($"New quest: {Title(e.QuestId)}", 5));
        _session.Subscribe<ObjectiveSatisfied>(e =>
        {
            if (_session.Simulation!.Quests.FirstOrDefault(q => q.Id == e.QuestId)?.Status == Domain.Quests.QuestStatus.Active)
                _hud.Toast($"Done: {Step(e.QuestId, e.ObjectiveId)}", 3);
        });
        _session.Subscribe<QuestCompleted>(e => _hud.Announce("Quest complete", Title(e.QuestId), HudStyle.Brass));
        _session.Subscribe<QuestFailed>(e => _hud.Toast($"Quest failed: {Title(e.QuestId)}", 6));
        // XP, a technique and a relationship already speak for themselves (their own toast or log line); coin and goods do not.
        _session.Subscribe<RewardGranted>(e =>
        {
            string? paid = e.Kind switch
            {
                "xp" => $"+{e.Amount} XP",
                "currency" => $"+{e.Amount} coin",
                "item" when e.Ref is { } item => $"+ {Names.Of(_session, item)}{(e.Amount > 1 ? $" ×{e.Amount}" : "")}",
                "recipe" or "spell" when e.Ref is { } known => $"Learned {Names.Of(_session, known)}",
                _ => null,
            };
            if (paid is null)
                return;
            _hud.AnnounceMore("Quest complete", Title(e.QuestId), paid);
            if (e.Kind is "currency" or "item")
                _hud.Log($"{Title(e.QuestId)}: {paid}");
        });
    }

    /// <summary>The waystation's people (M4): conversations on their panel, trade on the inventory's, and what they think in the log.</summary>
    private void SubscribeSocial()
    {
        _session.Subscribe<ConversationLine>(_ => _dialogue.Refresh(spoken: true));
        // What a reply hands over is said, by name and count (the owner's first playtest: "it doesn't say what you're giving them").
        _session.Subscribe<ReplyChosen>(e => _reply = (e.NpcId, e.Tick));
        _session.Subscribe<ItemConsumed>(e =>
        {
            if (_reply is { } reply && reply.Tick == e.Tick)
                _hud.Toast($"Handed to {Names.Of(_session, reply.NpcId)}: {e.Count} × {Names.Of(_session, e.DefId)}", 4);
        });
        _session.Subscribe<ConversationEnded>(_ => _dialogue.Refresh());
        _session.Subscribe<ServiceOpened>(e =>
        {
            if (e.Service == Domain.Social.NpcServices.Trade)
                OpenTrade(e.NpcId);
        });
        _session.Subscribe<ItemBought>(e =>
        {
            _inventory.Refresh();
            _hud.Toast($"Bought {Names.Of(_session, e.ItemId)}{(e.Count > 1 ? $" x{e.Count}" : "")} for {e.Price}", 3);
        });
        _session.Subscribe<ItemSold>(e =>
        {
            _inventory.Refresh();
            _hud.Toast($"Sold {Names.Of(_session, e.ItemId)}{(e.Count > 1 ? $" x{e.Count}" : "")} for {e.Price}", 3);
        });
        _session.Subscribe<RelationshipChanged>(e =>
            _hud.Log($"{Names.Of(_session, e.NpcId)}: {e.Dimension} {(e.To >= e.From ? "+" : "")}{e.To - e.From}"));
        _session.Subscribe<CommandRejected>(e =>
        {
            if (e.Command is TalkCommand or ChooseCommand or BuyCommand or SellCommand)
                _hud.Toast(e.Reason, 3);
        });
    }

    /// <summary>Combat reads in words as well as poses: every blow, effect, kill and death goes to the log (ROADMAP.md M3c).</summary>
    private void SubscribeCombat()
    {
        string Name(string id) => Names.Of(_session, id);
        // A companion's blow lands as it strikes: their figure plays its attack (the Phase-1 asset integration).
        _session.Subscribe<HitResolved>(e =>
        {
            if (_session.Simulation!.Companions.FirstOrDefault(c => c.InstanceId == e.Attacker) is { } companion)
                _npcs.Strike(companion.NpcId);
            // A blow on the character: what a working left on the body answers where it came from (the ward's shell rings there).
            if (e.Target == _session.Simulation!.PlayerId && !e.Dodged
                && _session.Simulation!.Creatures.FirstOrDefault(c => c.Id == e.Attacker) is { } attacker)
                _magicEffects.Struck(HollowView.ToGodot(attacker.Body.XMm, attacker.Body.YMm, attacker.Body.ZMm) + new Vector3(0, 0.9f, 0));
        });
        // What the blow throws off the body it landed on (blood, sparks, bone dust): only for a blow the simulation resolved.
        _session.Subscribe<HitResolved>(e =>
        {
            var simulation = _session.Simulation!;
            var target = e.Target == simulation.PlayerId ? simulation.Player.Body : simulation.Creatures.FirstOrDefault(c => c.Id == e.Target)?.Body;
            if (e.Dodged || target is null)
                return;
            var attacker = e.Attacker == simulation.PlayerId ? simulation.Player.Body : simulation.Creatures.FirstOrDefault(c => c.Id == e.Attacker)?.Body;
            var at = HollowView.ToGodot(target.XMm, target.YMm, target.ZMm) + new Vector3(0, 1.0f, 0);
            var from = attacker is null ? at + Vector3.Forward : HollowView.ToGodot(attacker.XMm, attacker.YMm, attacker.ZMm) + new Vector3(0, 1.0f, 0);
            _hits.Hit(e.Target == simulation.PlayerId ? null : CreatureDef(e.Target), at, from, e.Blocked);
        });
        _session.Subscribe<HitResolved>(e =>
        {
            var player = _session.Simulation!.PlayerId;
            string how = (e.Critical ? ", critical" : "") + (e.Blocked ? ", blocked" : "") + (e.Staggered ? ", staggered" : "");
            if (e.Attacker == player)
                _hud.Log($"You hit the {Name(CreatureDef(e.Target))} ({BodyRegions.Key(e.Region)}) for {e.Damage}{how}");
            else if (e.Dodged)
                _hud.Log($"You dodge the {Name(e.AttackerDefId)}'s {Name(e.Source).ToLowerInvariant()}");
            else
                _hud.Log($"The {Name(e.AttackerDefId)}'s {Name(e.Source).ToLowerInvariant()} hits your {BodyRegions.Key(e.Region)} for {e.Damage}{how}");
        });
        _session.Subscribe<AttackMissed>(e =>
        {
            if (e.Attacker != _session.Simulation!.PlayerId)
                return;
            if (_session.Setup.Magic.Formulas.ContainsKey(e.Source))
                _hud.Log($"Your {Names.Of(_session, e.Source)} finds nothing");
            else
                _hud.Log(_session.Setup.Items.Catalog.Find(e.Source)?.Weapon?.Ranged == true ? "Your arrow finds nothing" : "Your swing finds nothing");
        });
        _session.Subscribe<HealthChanged>(e =>
        {
            if (e.Target == _session.Simulation!.PlayerId && e.Source != "strain")
                _hud.Log($"{Name(e.Source)}: {(e.Delta > 0 ? "+" : "")}{e.Delta}");
        });
        _session.Subscribe<CastCompleted>(e => _hud.Log($"You work {Name(e.FormulaId)} (+{e.Strain} Strain)"));
        _session.Subscribe<CastFizzled>(e => _hud.Log($"Your {Name(e.FormulaId)} fizzles (+{e.Strain} Strain)"));
        _session.Subscribe<CastInterrupted>(e => _hud.Log($"Your {Name(e.FormulaId)} is broken off ({e.Reason})"));
        _session.Subscribe<StrainBacklash>(e => _hud.Log($"Strain backlash: -{e.Damage} health"));
        _session.Subscribe<TechniqueLearned>(e => _hud.Toast($"Learned {Name(e.DefinitionId)}", 4));
        _session.Subscribe<EffectApplied>(e =>
        {
            if (e.Target == _session.Simulation!.PlayerId)
                _hud.Log($"You are {Name(e.EffectId).ToLowerInvariant()}{(e.Stacks > 1 ? $" (x{e.Stacks})" : "")}");
        });
        _session.Subscribe<GuardBroken>(_ => _hud.Toast("Guard broken - no stamina behind it", 2));
        _session.Subscribe<CreatureKilled>(e => _hud.Log($"The {Name(e.DefId)} dies"));
        _session.Subscribe<SkillPracticed>(e =>
        {
            if (e.Xp > 0)
                _hud.Log($"{Name(e.SkillId)} +{e.Xp} XP (level {e.Level})");
            // A skill risen by use is told quietly (WP9, O4): its name and the level it reached, and the skill-up sound.
            if (e.LevelsGained > 0)
                _hud.Toast($"Skill: {Name(e.SkillId)} {e.Level}", 3);
        });
        _session.Subscribe<ItemUsed>(e => _hud.Toast(e.EffectId is null ? $"Read {Name(e.DefId)}" : $"Used {Name(e.DefId)}", 2));
        _session.Subscribe<ItemConsumed>(_ => _inventory.Refresh());
        // Downed with a hand near (OD-13 = B): who will help the character up, and when they are up.
        _session.Subscribe<PlayerDowned>(e =>
        {
            _autorun = false;
            _hud.Toast($"You are down - {Names.Of(_session, e.HandNpcId)} will help you up", 6);
        });
        _session.Subscribe<PlayerRevived>(e => _hud.Toast($"{Names.Of(_session, e.NpcId)} helps you up", 4));
        _session.Subscribe<PlayerDied>(e =>
        {
            string recap = string.Join("\n", e.Recap.Select(r =>
                r.AttackerDefId == r.Source ? $"{Name(r.Source)}: {r.Damage}" : $"{Name(r.AttackerDefId)}, {Name(r.Source).ToLowerInvariant()}: {r.Damage}"));
            string killer = e.KillerDefId == e.Cause ? Name(e.Cause) : $"the {Name(e.KillerDefId)} ({Name(e.Cause).ToLowerInvariant()})";
            _autorun = false;
            string returnAt = _session.Simulation!.Waystones.FirstOrDefault(w => w.ReturnPoint)?.Site.Name ?? "Ashen Waystone";
            _hud.ShowDeath($"You died - killed by {killer}.\n\nThe last blows:\n{recap}\n\nXP debt +{e.DebtAdded} (nothing earned is lost). " +
                           $"You return at the {returnAt}, weakened for a minute.");   // content bible §18; the return point chosen (S-38)
        });
        _session.Subscribe<CommandRejected>(e =>
        {
            if (e.Command is AttackCommand or DodgeCommand or UseItemCommand or CastCommand && !e.Reason.StartsWith("already", StringComparison.Ordinal)
                && e.Reason is not ("staggered" or "dodging" or "dead"))
                _hud.Toast(e.Reason, 2);
        });
    }

    private string StoneName(string key) => _session.Setup.Layout.FindWaystone(key)?.Name ?? key;

    private string CreatureDef(Domain.EntityId id) =>
        _session.Simulation!.Creatures.FirstOrDefault(c => c.Id == id)?.DefId ?? "creature";
}
