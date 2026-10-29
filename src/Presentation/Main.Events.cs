// UNNAMED Presentation - Main's event subscriptions: what the simulation's events show (D-11)
// Godot presentation only: this file renders state and submits commands; it never writes state.

using System.Text.RegularExpressions;
using UNNAMED.Domain;
using Godot;
using UNNAMED.Domain.Combat;
using UNNAMED.Domain.Companions;
using UNNAMED.Domain.Crafting;
using UNNAMED.Domain.Knowledge;
using UNNAMED.Domain.Spatial;
using UNNAMED.Presentation.Greybox;
using UNNAMED.Presentation.Player;
using UNNAMED.Presentation.Settings;
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

    /// <summary>A notice of a kind the player may switch off (Settings, Notices): shown unless that kind is hidden.</summary>
    private void Notice(NoticeKind kind, string text, double seconds = 4) => _hud.Toast(text, seconds, kind: kind);

    /// <summary>What a creature out in the world is called, for a notice about it; "the animal" when it has already gone.</summary>
    private string WildName(EntityId creature)
    {
        string? defId = _session.Simulation?.Creatures.FirstOrDefault(c => c.Id == creature)?.DefId;
        return defId is null ? "The animal" : Names.Of(_session, defId).ToLowerInvariant();
    }

    private void Subscribe()
    {
        SubscribeDenGorge();
        // A wild animal's offer says its odds while it stands, and why it failed when it does: the number here is the one the roll used, so
        // what the meat, the approach and the Handling skill bought the character is visible (spec 43's taming).
        _session.Subscribe<TameAttempted>(e =>
            _hud.Toast($"{WildName(e.Creature)}: {e.ChancePercent}% it comes round to the meat - hold still", 5));
        // A companion's word on the way on (owner finding 52), said over their head like a bark, as the player's setting allows.
        _session.Subscribe<BanterSpoken>(e =>
        {
            var hints = SettingsStore.Current.Profile.Interface.CompanionHints;
            if (hints != Guidance.Off && e.LineId.StartsWith("hint.", StringComparison.Ordinal))
                _npcs.Say(e.NpcId, e.LineId, e.Text, hints == Guidance.Light ? 300 : 0);
        });
        _session.Subscribe<TameFailed>(e => _hud.Toast($"{WildName(e.Creature)} turned away ({e.ChancePercent}%)", 4));
        _session.Subscribe<TameBroken>(e => _hud.Toast($"The offer broke: {e.Reason}", 4));
        _session.Subscribe<EncounterRestarted>(e => _hud.Toast(e.Loaded
            ? "Active boss encounters restart when loaded. You return outside the court with your saved resources."
            : "The Custodian disengages. The court falls quiet.", 7));
        // Gear sets: a switch takes time, so it says it has begun, and then what it did - and never what it could not, which it names.
        // D6: a climb or a vault plays the climb pose on the avatar, whoever the body is.
        _session.Subscribe<Climbed>(e =>
        {
            switch (_avatar)
            {
                case MHCharacters.MetaHumanFigure mh:
                    mh.BeginClimb();
                    break;
                case Art.SkinnedFigure skinned:
                    skinned.BeginClimb();
                    break;
            }
        });
        _session.Subscribe<GearSetSaved>(e => Notice(NoticeKind.Gear, $"Kept {e.Name} as gear set {e.Index}", 2));
        _session.Subscribe<GearSetDeleted>(e => Notice(NoticeKind.Gear, $"Gear set {e.Index} deleted", 2));
        _session.Subscribe<GearSwapStarted>(e => Notice(NoticeKind.Gear, $"Changing gear... {(e.DueTick - e.Tick) * _session.TickSeconds:0.#} s{(e.InCombat ? " (slower in a fight)" : "")}", 2));
        _session.Subscribe<GearSwapCancelled>(e => Notice(NoticeKind.Gear, $"Gear change stopped: {e.Why}", 3));
        _session.Subscribe<GearSwapped>(e =>
        {
            string name = _session.Simulation?.GearSets.FirstOrDefault(g => g.Index == e.Index)?.Name ?? $"set {e.Index}";
            Notice(NoticeKind.Gear, $"Wearing {name}", 2);
            if (e.ArmourKept)
                Notice(NoticeKind.Gear, UNNAMED.Domain.Items.GearSetRules.ArmourStays, 3);
            if (!e.Missing.IsEmpty)
                _hud.Toast($"Not carried: {string.Join(", ", e.Missing.Select(i => Names.Of(_session, i.DefId)))}", 4);
            foreach (var (item, why) in e.Refused)
                _hud.Toast($"{Names.Of(_session, item.DefId)}: {why}", 4);
        });
        _session.Subscribe<BodyMoved>(_controller.OnBodyMoved);
        _session.Subscribe<CampChanged>(e=> { _inventory.Refresh(); _hud.Toast(e.Placed?"Camp set. Use the fire to cook, rest or pack up.":"Camp packed.",4); });
        _session.Subscribe<CampRestChanged>(e=> { _inventory.Refresh(); _hud.Toast(e.Resting?"Resting by the fire. Walking ends rest; the world keeps moving.":"You leave your rest.",4); });
        _session.Subscribe<StationVisited>(_=>_inventory.Refresh());
        _session.Subscribe<PlayerRespawned>(_controller.OnRespawned);
        // Waystones (S-38): what a hand on a stone found, a return point chosen, and a journey's arrival with the time it took.
        _session.Subscribe<StoneTouched>(e =>
        {
            _hud.Toast(e.Text, 7);
            // A stone woken for the first time is one the character will remember (note 18): say so, once, under the stone's own line.
            if (e.Method == Domain.Travel.WaystoneMethod.Woken)
                _hud.Toast($"The {StoneName(e.StoneKey)} remembers you now.", 6);
            WaystoneResponse(e.StoneKey, false);
            Show(_waystones, e.StoneKey);
        });
        _session.Subscribe<ReturnPointBound>(e =>
        {
            _hud.Toast($"The {StoneName(e.StoneKey)} is your return point. When you fall, you come back here.", 5);
            WaystoneResponse(e.StoneKey, true);
        });
        _session.Subscribe<PassageTaken>(e =>
        {
            _controller.Resync();
            _autorun = false;
            CloseScreens();
            _hud.Toast($"The ferry puts you ashore at {Names.Of(_session,e.LocationId)}. Passage: {e.Coin} coin.", 6);
        });
        _session.Subscribe<PlayerTravelled>(e =>
        {
            _waystoneArrived=true;
            if(_waystoneTravelBusy)TravelStonePulse(e.ToKey,true);
            _controller.OnTravelled(e);
            _autorun = false;
            CloseScreens();
            _hud.ClearWaystoneAnnouncements();
            WaystoneResponse(e.ToKey, true);
            _hud.Toast($"The {StoneName(e.ToKey)}. {TravelWords.Journey(e.Cost.WalkTicks, TicksPerDay).Replace("You arrive the moment you leave", "You arrived the moment you left").Replace("would take", "would have taken")}", 7);
        });        _session.Subscribe<DoorToggled>(e =>
        {
            _controller.OnDoorToggled(e);
            _hollow.SetDoor(e.DoorKey, e.Open, swing: true);
            _interior?.SetDoor(e.DoorKey, e.Open, swing: true);
        });
        // Switches and barriers (M6): what a switch did, and every flag change shown as it now stands.
        _session.Subscribe<SwitchSet>(e =>
        {
            if (_session.Simulation!.ActiveLayout.FindSwitch(e.SwitchKey) is { } site)
            {
                if (_session.Simulation.PlayerInExterior) _hud.Toast(site.DoneText, 6);
                else _hud.Reading(site.DoneText);
                // A circle worked with the body's own hands: the drawing motion is the catalog's cast clip (D18
                // SYSTEMS, needed-2; ANIM.DRAW_CIRCLE).
                if (e.SwitchKey.StartsWith("switch.ritual.", StringComparison.Ordinal))
                    _avatar.Work();
            }
        });
        _session.Subscribe<WorldFlagChanged>(_ =>
        {
            _hollow.SetFlags(_session.Simulation!.Switches, _session.Simulation!.Barriers, inPlay: true);
            _interior?.SetFlags(_session.Simulation!.Switches, _session.Simulation!.Barriers, inPlay: true);
        });
        _session.Subscribe<LocationDiscovered>(e =>
        {
            if (SettingsStore.Current.Profile.Interface.Shows(NoticeKind.Discovery))
                _hud.Announce("Landmark discovered", Names.Of(_session, e.LocationId), HudStyle.Brass);
        });
        // Lore (spec 41): a landmark the character has walked into says so on the place's own banner, with the journal key that reads what the
        // place has to say - one arrival, one banner, however many pages it opens (owner ruling 2026-10-24); a page found any other way - told,
        // read, opened by a quest - is a line. Nothing pauses for either.
        _session.Subscribe<LorePageUnlocked>(e =>
        {
            string title = _session.Setup.Lore.GetValueOrDefault(e.EntryId)?.Title ?? Names.Of(_session, e.EntryId);
            if (e.Source == LoreSourceKind.Visited)
            {
                _loreOffered = e.EntryId;
                _hud.Announce("Landmark discovered", Names.Of(_session, e.SourceRef), HudStyle.Brass, ("Read what is written there", HelpPanel.Key("journal")));
            }
            else
            {
                Notice(NoticeKind.Discovery, $"Lore: {title}", 3);
            }
        });
        // Something written, read where it stands: it opens on its own panel at its first page (opening at the page a site grants
        // read as the last record's page carried over, owner 2026-10-27).
        _session.Subscribe<LoreReadableRead>(e => Show(_reading, e.EntryId, 1));
        // Companions (M6): joined, told, downed, helped up, fallen back to the Waystone.
        _session.Subscribe<CompanionRecruited>(e => Notice(NoticeKind.Party, $"{Names.Of(_session, e.NpcId)} joins you"));
        _session.Subscribe<CompanionOrdered>(e => Notice(NoticeKind.Party, $"{Names.Of(_session, e.NpcId)}: {(e.Order == CompanionOrder.Follow ? "following" : "waiting")}", 2));
        _session.Subscribe<CompanionDowned>(e => _hud.Toast($"{Names.Of(_session, e.NpcId)} is down - reach them and press {HelpPanel.Key("interact")}", 6));
        _session.Subscribe<CompanionRevived>(e => Notice(NoticeKind.Party, $"{Names.Of(_session, e.NpcId)} is back on their feet"));
        _session.Subscribe<CompanionFell>(e => Notice(NoticeKind.Party, $"{Names.Of(_session, e.NpcId)} fell, and will be waiting at the Ashen Waystone", 6));
        // What the award did: what is left once the XP debt took its share, and that share (the Phase-1 technical audit, L-12).
        _session.Subscribe<ExperienceGained>(e =>
        {
            long kept = e.Awarded - e.Repaid;
            string xp = e.Repaid == 0 ? $"+{e.Awarded} XP" : kept > 0 ? $"+{kept} XP, {e.Repaid} to the XP debt" : $"{e.Repaid} XP to the XP debt";
            Notice(NoticeKind.Progress, xp, 3);
            if (e.LevelsGained > 0)
                _hud.Announce("Level up", $"Level {e.Level}", HudStyle.Ember, ("A point to spend on the character sheet", HelpPanel.Key("character")));
        });
        _session.Subscribe<CommandRejected>(e =>
        {
            if (e.Command is InteractCommand or MoveItemCommand or EquipCommand or UnequipCommand or GatherCommand or SwingAtNodeCommand
                or CraftCommand or TakeAllCommand
                or SpendAttributeCommand or TouchStoneCommand or BindStoneCommand or TravelCommand or PickLockCommand
                or SaveGearSetCommand or DeleteGearSetCommand or SwapGearSetCommand or CancelGearSwapCommand
                or EquipBagCommand or UnequipBagCommand or PlaceStackCommand or SortPackCommand or MarkJunkCommand or LockStackCommand or SellJunkCommand)
            {
                // A stack already taken (a second click before the first landed) needs no word; any other refusal names things, never ids.
                if (e.Command is not MoveItemCommand || !e.Reason.StartsWith("there is no '", StringComparison.Ordinal))
                    _hud.Toast(Regex.Replace(e.Reason, @"\b[a-z_]+(\.[a-z0-9_]+)+\b", m => Names.Of(_session, m.Value)), 3);
            }
            // A jump or a stand refused under the beam says why; one refused mid-action or in the air stays quiet.
            if (e.Command is JumpCommand or CrouchCommand && e.Reason == "no room to stand")
                _hud.Toast("No room to stand", 2);
        });
        // The owner's M6 playtest: a take-all's tally, a point spent, and every shot drawn along the path it took.
        _session.Subscribe<TookAll>(e =>
        {
            if (!_session.Simulation!.ActiveLayout.Containers.Any(c => PlayerController.IsPickup(c) && c.Key == e.ContainerKey))
                Notice(NoticeKind.Items, e.Left == 0 ? $"Took everything ({e.Taken})" : $"Took {e.Taken}; {e.Left} left - {e.Why}", 3);
            // Emptied, the loot is done: the panel closes rather than leaving the pack open behind it (the owner's first playtest).
            if (e.Left == 0 && (_inventory.OpenContainer == e.ContainerKey || _loot.OpenContainer == e.ContainerKey))
                CloseInventory();
        });
        _session.Subscribe<AttributeSpent>(e => Notice(NoticeKind.Progress, $"{char.ToUpperInvariant(UNNAMED.Domain.Progression.ProgressionKeys.Key(e.Attribute)[0])}" +
            $"{UNNAMED.Domain.Progression.ProgressionKeys.Key(e.Attribute)[1..]} {e.Value}", 3));
        _session.Subscribe<ShotLoosed>(Shot);
        _session.Subscribe<ItemMoved>(e =>
        {
            _inventory.Refresh();
            _loot.Refresh();
            if (e.To.Kind == PlaceKind.Inventory && (e.From.Kind == PlaceKind.Ground
                || _session.Simulation!.ActiveLayout.Containers.Any(c => PlayerController.IsPickup(c) && c.Key == e.From.ContainerKey)))
                Notice(NoticeKind.Items, $"Picked up {Names.Of(_session, e.DefId)}{(e.Count > 1 ? $" x{e.Count}" : "")}", 3);
            if (e.From.Kind == PlaceKind.Ground || e.To.Kind == PlaceKind.Ground
                || _session.Simulation!.ActiveLayout.Containers.Any(c => PlayerController.IsPickup(c) && c.Key == e.From.ContainerKey))
                _items.Refresh(_session.Simulation!);
        });
        _session.Subscribe<QuickSlotAssigned>(_ => _inventory.Refresh());
        // The pack was laid out again (a sort, a bag, a lock, a mark): the inventory draws it again; junk sold says what it made.
        _session.Subscribe<PackOrganised>(_ => _inventory.Refresh());
        _session.Subscribe<JunkSold>(e => Notice(NoticeKind.Items, $"Sold {e.Stacks} junk stack{(e.Stacks == 1 ? "" : "s")} for {e.Price}", 3));
        _session.Subscribe<ItemEquipped>(_ => _inventory.Refresh());
        _session.Subscribe<ItemUnequipped>(_ => _inventory.Refresh());
        // Gathering: the pick goes in, a seam that holds more says so, and what it gives is named on the swing that fills it.
        _session.Subscribe<SwingStarted>(e => _hud.Log($"You swing at the {Names.Of(_session, e.NodeDefId)}"));
        _session.Subscribe<NodeStruck>(_ => _hud.Log("The seam shows more"));
        _session.Subscribe<NodeRefused>(e => _hud.Toast(e.Reason, 3));
        _session.Subscribe<NodeGathered>(e =>
        {
            _inventory.Refresh();
            string spent = !e.Spent ? ""
                : _session.Setup.Crafting.Nodes[e.NodeDefId].Respawn == Respawn.None ? $" - the {Names.Of(_session, e.NodeDefId)} is worked out"
                : $" - nothing more until the {Names.Of(_session, e.NodeDefId)} grows back";
            Notice(NoticeKind.Items, $"You have it: {Names.Of(_session, e.ItemId)}{(e.Count > 1 ? $" x{e.Count}" : "")}{spent}", 3);
        });
        _session.Subscribe<ItemCrafted>(e =>
        {
            _inventory.Refresh();
            _inventory.CraftGlint(e.ItemId, e.Quality);
            if (_inventory.OpenStation is { } station)
                _successEffects.Craft((_crafting.Station(station.Key)?.GlobalPosition ?? _avatar.GlobalPosition) + Vector3.Up * .8f,
                    _bindings.Stations.GetValueOrDefault(station.Kind)?.Sound is "anvil" or "forge", e.Quality);
            Notice(NoticeKind.Items, $"Made {Names.Item(_session, e.ItemId, e.Quality)}{(e.Count > 1 ? $" x{e.Count}" : "")}", 3);
        });
        _session.Subscribe<ItemSalvaged>(e =>
        {
            _inventory.Refresh();
            string recovered = e.Materials.IsEmpty ? "no usable materials" : string.Join(", ", e.Materials.Select(m => $"{m.Count} {Names.Of(_session, m.ItemId)}"));
            Notice(NoticeKind.Items, $"Salvaged {Names.Of(_session, e.ItemId)}: {recovered}", 4);
        });
        _session.Subscribe<FindingRecorded>(e =>
        {
            if (FindingWords.Toast(e.Finding, key => Names.Of(_session, key)) is { } said)
                _hud.Toast(said, 4);
        });
        // Fishing: one line at each turn of the loop - the cast as it goes out, the line when it lands, and the catch by name. A line that
        // comes back empty says why in the simulation's own words.
        _session.Subscribe<FishCastStarted>(_ =>
        {
            Notice(NoticeKind.World, "You cast", 2);
            Tackle().LineOut = false;
        });
        _session.Subscribe<FishingLineOut>(_ =>
        {
            _hud.Log("The line is out");
            Tackle().LineOut = true;
            Tackle().Biting = false;
        });
        _session.Subscribe<FishCaught>(e =>
        {
            Tackle().LineOut = false;
            _inventory.Refresh();
            Notice(NoticeKind.Items, $"You have it: {Names.Of(_session, e.ItemId)}{(e.Count > 1 ? $" x{e.Count}" : "")}", 3);
        });
        _session.Subscribe<FishLost>(e =>
        {
            _hud.Log(e.Reason);
            Tackle().LineOut = false;
        });
        // Tracking: what the ground said, in the read's own words - the way, how old it is, how many left it, and when the sign refuses to
        // say (cold, stone, water) the reason it refuses.
        _session.Subscribe<SpoorRead>(e =>
        {
            _tracked = e.Readable;
            _hud.Log(e.Readable
                ? $"The ground says: {e.Count} went {Compass(e.BearingMdeg)}, {e.AgeTicks / 20.0:0.#} s ago{(e.Uncertain ? " - and it is not certain" : "")}"
                : e.Reason);
        });
        // The water's second use (D20): a wash is a rest at the bank, and going under is the swim's own lesson, in the HUD's own words.
        _session.Subscribe<Washed>(_ => Notice(NoticeKind.World, "You wash at the water's edge", 3));
        _session.Subscribe<WentUnder>(_ => Notice(NoticeKind.World, "You go under - the water puts you ashore", 4));
        // Lockpicking: the picks go in, the lock gives or holds, and a turn that binds takes them out of the pack with it.
        _session.Subscribe<LockAttemptStarted>(e => Notice(NoticeKind.World, "You work the picks", 2));
        _session.Subscribe<LockPicked>(e => Notice(NoticeKind.World, "The lock gives", 3));
        _session.Subscribe<LockPickFailed>(e =>
        {
            if (!e.Broke)
            {
                _hud.Log("It holds");
                return;
            }
            _inventory.Refresh();
            _hud.Log("Your picks bind and snap");
        });
        SubscribeSocial();
        SubscribeParty();
        SubscribeQuests();
        SubscribePeople();
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
        void Card(string id, string? giver, long tick, bool started)
        {
            if (_session.Simulation?.Quests.FirstOrDefault(q => q.Id == id) is not { } view || !quests.TryGetValue(id, out var definition))
                return;
            if (view.Status != Domain.Quests.QuestStatus.Active)
                return; // An objective activated and completed in one evaluation has no next step to announce.
            string? from = giver ?? definition.GiverId;
            _questCards.Cards.Add(new Ui.Models.QuestCard(id, tick, view.Title, from is null ? "" : Names.Of(_session, from),
                view.Summary, Ui.Models.QuestPresentation.Step(view), started));
        }
        _session.Subscribe<QuestStarted>(e => Card(e.QuestId, e.GiverId, e.Tick, true));
        _session.Subscribe<ObjectiveActivated>(e => Card(e.QuestId, null, e.Tick, false));
        _session.Subscribe<ObjectiveSatisfied>(e =>
        {
            if (_session.Simulation!.Quests.FirstOrDefault(q => q.Id == e.QuestId)?.Status == Domain.Quests.QuestStatus.Active)
                _hud.Toast($"Done: {Step(e.QuestId, e.ObjectiveId)}", 3);
        });
        _session.Subscribe<QuestCompleted>(e =>
        {
            _hud.Announce("Quest complete", Title(e.QuestId), HudStyle.Brass);
            _hud.SuccessGlow(HudStyle.Brass);
        });
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
            if (e.Service is Domain.Social.NpcServices.Trade or Domain.Social.NpcServices.Repair)
                OpenTrade(e.NpcId);
        });
        _session.Subscribe<ItemBought>(e =>
        {
            _inventory.Refresh();
            Notice(NoticeKind.Items, $"Bought {Names.Of(_session, e.ItemId)}{(e.Count > 1 ? $" x{e.Count}" : "")} for {e.Price}", 3);
        });
        _session.Subscribe<ItemSold>(e =>
        {
            _inventory.Refresh();
            Notice(NoticeKind.Items, $"Sold {Names.Of(_session, e.ItemId)}{(e.Count > 1 ? $" x{e.Count}" : "")} for {e.Price}", 3);
        });
        _session.Subscribe<ItemRepaired>(e =>
        {
            _inventory.Refresh();
            Notice(NoticeKind.Items,$"Repaired {Names.Of(_session,e.DefId)} for {e.Coin} coin",3);
        });
        _session.Subscribe<RelationshipChanged>(QueueRelationshipNotice);
        _session.Subscribe<CommandRejected>(e =>
        {
            if (e.Command is TalkCommand or ChooseCommand or BuyCommand or SellCommand or MerchantRepairCommand)
                _hud.Toast(e.Reason, 3);
        });
    }

    private readonly Dictionary<(string Npc,string Dimension),(int From,int To)> _relationshipNotices=new();
    private bool _relationshipNoticeQueued;

    // Catch-up can publish several days of loyalty in one frame. Show its net change once.
    private void QueueRelationshipNotice(RelationshipChanged change)
    {
        var key=(change.NpcId,change.Dimension);
        _relationshipNotices[key]=(_relationshipNotices.TryGetValue(key,out var previous)?previous.From:change.From,change.To);
        if(_relationshipNoticeQueued)return;
        _relationshipNoticeQueued=true;
        Callable.From(()=>{
            _relationshipNoticeQueued=false;
            foreach(var (who,values) in _relationshipNotices)
            {
                int delta=values.To-values.From;
                if(delta!=0)_hud.Log($"{Names.Of(_session,who.Npc)}: {who.Dimension} {(delta>0?"+":"")}{delta}");
            }
            _relationshipNotices.Clear();
        }).CallDeferred();
    }

    /// <summary>Combat reads in words as well as poses: every blow, effect, kill and death goes to the log (ROADMAP.md M3c).</summary>
    private void SubscribeCombat()
    {
        string Name(string id) => Names.Of(_session, id);
        // A companion's blow lands as it strikes: their figure plays its attack (the Phase-1 asset integration).
        _session.Subscribe<HitResolved>(e =>
        {
            // Takedowns resolve immediately, outside the ordinary windup/active phases.
            // The actual hit event drives the same existing blade feedback in both casts.
            var simulation=_session.Simulation!;
            if(e.Attacker==simulation.PlayerId && simulation.Combat.Phase==CombatPhase.Idle
                && StealthRules.IsDagger(simulation.Combat.Weapon)) _avatar?.Strike();
            if (_session.Simulation!.Companions.FirstOrDefault(c => c.InstanceId == e.Attacker) is { } companion)
                _npcs.Strike(companion.NpcId);
            // A blow on the character: what a working left on the body answers where it came from (the ward's shell rings there).
            // A solid blow jolts the view (Settings, Camera: camera shake scales or removes it).
            if (e.Target == _session.Simulation!.PlayerId && !e.Dodged && !e.Blocked && e.Damage > 0)
                _camera.Kick(Math.Min(1f, e.Damage / 25f));
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
            // NPCs and creatures also exchange blows. Only a player target is "your torso".
            bool namedAttacker = _session.Simulation.Npcs.Any(n => n.InstanceId == e.Attacker);
            var targetNpc = _session.Simulation.Npcs.FirstOrDefault(n => n.InstanceId == e.Target);
            _hud.Log(Ui.Models.CombatWords.Hit(e, player, Name(e.AttackerDefId), namedAttacker,
                Name(targetNpc?.Id ?? CreatureDef(e.Target)), Name(e.Source), targetNpc is not null));
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
        _session.Subscribe<TechniqueLearned>(e => Notice(NoticeKind.Progress, $"Learned {Name(e.DefinitionId)}", 4));
        _session.Subscribe<EffectApplied>(e =>
        {
            if (e.Target != _session.Simulation!.PlayerId) return;
            if (_session.Setup.Combat.Effects.GetValueOrDefault(e.EffectId)?.AppliesOnHit is not null)
            {
                string? item = _session.Setup.Combat.UseEffects.FirstOrDefault(pair => pair.Value == e.EffectId).Key;
                _hud.Log($"You coat your blade with {Name(item ?? e.EffectId)}");
            }
            else
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
                Notice(NoticeKind.Progress, $"Skill: {Name(e.SkillId)} {e.Level}", 3);
        });
        _session.Subscribe<ItemUsed>(e => Notice(NoticeKind.Items, e.EffectId is null ? $"Read {Name(e.DefId)}" : $"Used {Name(e.DefId)}", 2));
        _session.Subscribe<ItemConsumed>(_ => _inventory.Refresh());
        // Downed with a hand near (OD-13 = B): who will help the character up, and when they are up.
        _session.Subscribe<PlayerDowned>(e =>
        {
            _autorun = false;
            _hud.Toast($"You are down - {Names.Of(_session, e.HandNpcId)} will help you up", 6);
        });
        _session.Subscribe<PlayerRevived>(e => Notice(NoticeKind.Party, $"{Names.Of(_session, e.NpcId)} helps you up", 4));
        _session.Subscribe<PlayerDied>(e =>
        {
            string recap = string.Join("\n", e.Recap.Select(r =>
                r.AttackerDefId == r.Source ? $"{Name(r.Source)}: {r.Damage}" : $"{Name(r.AttackerDefId)}, {Name(r.Source).ToLowerInvariant()}: {r.Damage}"));
            string killer = e.KillerDefId == e.Cause ? Name(e.Cause) : $"the {Name(e.KillerDefId)} ({Name(e.Cause).ToLowerInvariant()})";
            _autorun = false;
            string returnAt = _session.Simulation!.Waystones.FirstOrDefault(w => w.ReturnPoint)?.Site.Name ?? "Ashen Waystone";
            _hud.ShowDeath($"You died - killed by {killer}.\n\nThe last blows:\n{recap}\n\nXP debt +{e.DebtAdded} (nothing earned is lost). " +
                           (_session.Simulation.Camp is not null ? "Choose your camp or bound Waystone below. You return weakened for a minute." : $"You return at the {returnAt}, weakened for a minute."));
        });
        _session.Subscribe<CommandRejected>(e =>
        {
            if (e.Command is AttackCommand or DodgeCommand or UseItemCommand or CastCommand or TechniqueCommand or PlaceCampCommand or PackCampCommand or RestAtCampCommand or RefillFlaskCommand or CampReturnCommand or VisitStationCommand or RepairCommand or StartFishingCommand && !e.Reason.StartsWith("already", StringComparison.Ordinal)
                && e.Reason is not ("staggered" or "dodging" or "dead"))
                _hud.Toast(e.Command is TechniqueCommand ? Regex.Replace(e.Reason, @"\b[a-z_]+(\.[a-z0-9_]+)+\b", m => Name(m.Value)) : e.Reason, 2);
        });
    }

    private string StoneName(string key) => _session.Setup.Layout.FindWaystone(key)?.Name ?? key;

    private void WaystoneResponse(string key, bool repeat)
    {
        if (_session.Setup.Layout.FindWaystone(key) is not { } site) return;
        var (x,z) = Footprints.Center(site.Body);
        var at = new Vector3(x/1000f, _session.Setup.Layout.Space.Terrain.HeightAtMm(x,z)/1000f, z/1000f);
        _successEffects.Waystone(at, _avatar.GlobalPosition, repeat);
        _camera.Kick(.025f);
        _hud.Announce(repeat ? "Waystone" : "Waystone attuned", StoneName(key), HudStyle.Lichen);
    }

    private string CreatureDef(Domain.EntityId id) =>
        _session.Simulation!.Creatures.FirstOrDefault(c => c.Id == id)?.DefId ?? "creature";



    /// <summary>A bearing in degrees (0 = north, 90 = east) in the eight words a walker uses.</summary>
    private static string Compass(int bearingMdeg)
    {
        string[] points = { "north", "north-east", "east", "south-east", "south", "south-west", "west", "north-west" };
        int octant = (int)Math.Round(((bearingMdeg % 360 + 360) % 360) / 45.0) % 8;
        return points[octant];
    }

    /// <summary>Whether the last read of the ground found sign - what a capture waits on.</summary>
    private bool _tracked;

    /// <summary>The rod, line and float the character fishes with, made once and kept on the player's body.</summary>
    private Art.FishingTackle? _tackle;

    private Art.FishingTackle Tackle()
    {
        if (_tackle is not null && GodotObject.IsInstanceValid(_tackle))
            return _tackle;
        _tackle = new Art.FishingTackle { Name = "Tackle" };
        (_avatar as Node3D ?? this).AddChild(_tackle);
        return _tackle;
    }
}
