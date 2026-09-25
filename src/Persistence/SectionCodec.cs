// UNNAMED Persistence - section encoding (PERSISTENCE.md §5.1-§5.3)
// No Godot references - pure C#

using System.Collections.Immutable;
using System.Text.Json;
using MessagePack;
using UNNAMED.Domain;
using UNNAMED.Domain.Combat;
using UNNAMED.Domain.Companions;
using UNNAMED.Domain.Creatures;
using UNNAMED.Domain.Factions;
using UNNAMED.Domain.Items;
using UNNAMED.Domain.Quests;
using UNNAMED.Domain.Spatial;
using UNNAMED.World;

namespace UNNAMED.Persistence.Sections;

// Section files are string-keyed MessagePack maps, so a dump is self-describing: an AI session
// diagnosing a persistence bug can read one (D-04's rationale for legible IDs applies here too).

[MessagePackObject]
public sealed class PlayerDto
{
    [Key("instance_id")] public string InstanceId { get; set; } = "";
    [Key("name")] public string Name { get; set; } = "";
    [Key("x_mm")] public long XMm { get; set; }
    [Key("y_mm")] public long YMm { get; set; }
    [Key("z_mm")] public long ZMm { get; set; }
    [Key("appearance_seed")] public ulong AppearanceSeed { get; set; }
    [Key("inventory")] public InventoryDto[] Inventory { get; set; } = Array.Empty<InventoryDto>();

    /// <summary>Required from schema 4. The 3 -> 4 step gives older saves the empty record.</summary>
    [Key("progression")] public ProgressionDto? Progression { get; set; }

    /// <summary>Required from schema 5. The 4 -> 5 step gives older saves facing 0 (+Z).</summary>
    [Key("facing_mdeg")] public int? FacingMdeg { get; set; }

    /// <summary>Required from schema 5. The 4 -> 5 step gives older saves no discoveries.</summary>
    [Key("discoveries")] public DiscoveryDto[]? Discoveries { get; set; }

    /// <summary>Required from schema 6. The 5 -> 6 step gives older saves nothing equipped.</summary>
    [Key("equipment")] public EquipmentDto[]? Equipment { get; set; }

    /// <summary>Required from schema 6. The 5 -> 6 step gives older saves an empty purse.</summary>
    [Key("currency")] public long? Currency { get; set; }

    /// <summary>Required from schema 7. The 6 -> 7 step gives older saves no effects.</summary>
    [Key("effects")] public EffectDto[]? Effects { get; set; }

    /// <summary>Required from schema 10. The 9 -> 10 step gives older saves none: no NPC could think anything of anyone before M4.</summary>
    [Key("relationships")] public RelationshipDto[]? Relationships { get; set; }

    /// <summary>Required from schema 10. The 9 -> 10 step gives older saves none: there was no one to talk to before M4.</summary>
    [Key("conversations")] public ConversationDto[]? Conversations { get; set; }

    /// <summary>Required from schema 11. The 10 -> 11 step gives older saves none: there were no quests before M5.</summary>
    [Key("quests")] public QuestDto[]? Quests { get; set; }

    /// <summary>Required from schema 12. The 11 -> 12 step gives older saves none: no one could join before M6.</summary>
    [Key("companions")] public CompanionDto[]? Companions { get; set; }

    /// <summary>Required from schema 13. The 12 -> 13 step gives older saves a body standing on the ground (the owner's M6 playtest).</summary>
    [Key("posture")] public PostureDto? Posture { get; set; }

    /// <summary>Required from schema 15. The 14 -> 15 step gives older saves an empty ledger: no act was recorded before M7.</summary>
    [Key("factions")] public FactionsDto? Factions { get; set; }
}

/// <summary>The player's faction ledger (schema 15): the acts recorded, what each faction knows of them, and standing.</summary>
[MessagePackObject]
public sealed class FactionsDto
{
    [Key("next_act_seq")] public long NextActSeq { get; set; }
    [Key("acts")] public ActDto[] Acts { get; set; } = Array.Empty<ActDto>();
    [Key("knowledge")] public KnowledgeDto[] Knowledge { get; set; } = Array.Empty<KnowledgeDto>();
    [Key("standing")] public StandingDto[] Standing { get; set; } = Array.Empty<StandingDto>();
}

[MessagePackObject]
public sealed class ActDto
{
    [Key("seq")] public long Seq { get; set; }
    [Key("kind")] public string Kind { get; set; } = "";
    [Key("subject")] public string Subject { get; set; } = "";
    [Key("cell_key")] public string CellKey { get; set; } = "";
    [Key("x_mm")] public long XMm { get; set; }
    [Key("z_mm")] public long ZMm { get; set; }
    [Key("tick")] public long Tick { get; set; }
}

[MessagePackObject]
public sealed class KnowledgeDto
{
    [Key("knower")] public string Knower { get; set; } = "";
    [Key("act")] public long Act { get; set; }
    [Key("identity")] public string Identity { get; set; } = "";
    [Key("source")] public string Source { get; set; } = "";
    [Key("via")] public string? Via { get; set; }
    [Key("tick")] public long Tick { get; set; }
    [Key("delta")] public int Delta { get; set; }
}

[MessagePackObject]
public sealed class StandingDto
{
    [Key("faction_id")] public string FactionId { get; set; } = "";
    [Key("points")] public int Points { get; set; }
}

/// <summary>A mover's committed route (schema 15), one shape for both hosts: a companion in the player, an errand in the entities.</summary>
[MessagePackObject]
public sealed class NavRouteDto
{
    [Key("status")] public string Status { get; set; } = "";
    [Key("goal_mm")] public long[] GoalMm { get; set; } = Array.Empty<long>();
    [Key("corners_mm")] public long[] CornersMm { get; set; } = Array.Empty<long>();
    [Key("planned_tick")] public long PlannedTick { get; set; }
    [Key("stamp")] public ulong Stamp { get; set; }
    [Key("watch_mm")] public long[] WatchMm { get; set; } = Array.Empty<long>();
    [Key("partial")] public bool Partial { get; set; }
}

/// <summary>Standing or crouched, and how far into a jump (schema 13).</summary>
[MessagePackObject]
public sealed class PostureDto
{
    [Key("stance")] public string Stance { get; set; } = "";
    [Key("airborne")] public bool Airborne { get; set; }
    [Key("air_ms")] public int AirMs { get; set; }
}

[MessagePackObject]
public sealed class CompanionDto
{
    [Key("npc_id")] public string NpcId { get; set; } = "";
    [Key("order")] public string Order { get; set; } = "";
    [Key("condition")] public string Condition { get; set; } = "";
    [Key("x_mm")] public long XMm { get; set; }
    [Key("z_mm")] public long ZMm { get; set; }
    [Key("facing_mdeg")] public int FacingMdeg { get; set; }
    [Key("health")] public int Health { get; set; }
    [Key("downed_tick")] public long DownedTick { get; set; }
    [Key("stuck_ticks")] public int StuckTicks { get; set; }
    [Key("last_combat_tick")] public long LastCombatTick { get; set; }

    /// <summary>The trail being walked, as x, z pairs in millimetres.</summary>
    [Key("trail_mm")] public long[] TrailMm { get; set; } = Array.Empty<long>();

    /// <summary>Required from schema 15. The 14 -> 15 step gives older saves route none: no one walked a planned route before M7.</summary>
    [Key("route")] public NavRouteDto? Route { get; set; }
}

[MessagePackObject]
public sealed class QuestDto
{
    [Key("quest_id")] public string QuestId { get; set; } = "";
    [Key("status")] public string Status { get; set; } = "";
    [Key("started_tick")] public long StartedTick { get; set; }
    [Key("ended_tick")] public long? EndedTick { get; set; }
    [Key("ended_by")] public string? EndedBy { get; set; }
    [Key("objectives")] public ObjectiveDto[] Objectives { get; set; } = Array.Empty<ObjectiveDto>();
}

[MessagePackObject]
public sealed class ObjectiveDto
{
    [Key("id")] public string Id { get; set; } = "";
    [Key("status")] public string Status { get; set; } = "";
    [Key("activated_tick")] public long ActivatedTick { get; set; }
    [Key("ended_tick")] public long? EndedTick { get; set; }
    [Key("progress")] public int Progress { get; set; }
}

[MessagePackObject]
public sealed class RelationshipDto
{
    [Key("npc_id")] public string NpcId { get; set; } = "";
    [Key("dimension")] public string Dimension { get; set; } = "";
    [Key("value")] public int Value { get; set; }
}

[MessagePackObject]
public sealed class ConversationDto
{
    [Key("dialogue_id")] public string DialogueId { get; set; } = "";
    [Key("heard")] public string[] Heard { get; set; } = Array.Empty<string>();
}

[MessagePackObject]
public sealed class EffectDto
{
    [Key("effect_id")] public string EffectId { get; set; } = "";
    [Key("stacks")] public int Stacks { get; set; }
    [Key("expires_tick")] public long ExpiresTick { get; set; }
    [Key("next_tick_at")] public long NextTickAt { get; set; }
}

[MessagePackObject]
public sealed class EquipmentDto
{
    [Key("slot")] public string Slot { get; set; } = "";
    [Key("item_id")] public string ItemId { get; set; } = "";
}

[MessagePackObject]
public sealed class DiscoveryDto
{
    [Key("location_id")] public string LocationId { get; set; } = "";
    [Key("method")] public string Method { get; set; } = "";
    [Key("tick")] public long Tick { get; set; }
}

[MessagePackObject]
public sealed class InventoryDto
{
    [Key("item_id")] public string ItemId { get; set; } = "";
    [Key("def_id")] public string DefId { get; set; } = "";
    [Key("count")] public int Count { get; set; }

    /// <summary>Required from schema 9: the stack's quality (-1 crude, 0 standard, 1 fine). The 8 -> 9 step gives older stacks 0.</summary>
    [Key("quality")] public int? Quality { get; set; }
}

[MessagePackObject]
public sealed class CellsSectionDto
{
    [Key("records")] public CellDto[] Records { get; set; } = Array.Empty<CellDto>();
}

[MessagePackObject]
public sealed class CellDto
{
    [Key("cell_key")] public string CellKey { get; set; } = "";
    [Key("baseline_hash")] public string BaselineHash { get; set; } = "";
    [Key("dirty_reasons")] public string[] DirtyReasons { get; set; } = Array.Empty<string>();
    [Key("flags")] public FlagDto[] Flags { get; set; } = Array.Empty<FlagDto>();
    [Key("harvested_nodes")] public NodeDto[] HarvestedNodes { get; set; } = Array.Empty<NodeDto>();
    [Key("population_alive")] public PopulationDto[] PopulationAlive { get; set; } = Array.Empty<PopulationDto>();
}

[MessagePackObject]
public sealed class FlagDto
{
    [Key("name")] public string Name { get; set; } = "";
    [Key("value")] public long Value { get; set; }
}

[MessagePackObject]
public sealed class NodeDto
{
    [Key("node_key")] public string NodeKey { get; set; } = "";
    [Key("last_harvest_tick")] public long LastHarvestTick { get; set; }
    [Key("harvest_seq")] public int HarvestSeq { get; set; }
}

[MessagePackObject]
public sealed class PopulationDto
{
    [Key("population_id")] public string PopulationId { get; set; } = "";
    [Key("alive")] public int Alive { get; set; }
}

[MessagePackObject]
public sealed class EntitiesSectionDto
{
    [Key("records")] public EntityDto[] Records { get; set; } = Array.Empty<EntityDto>();

    /// <summary>Persistent instances no baseline slot generates (schema 3).</summary>
    [Key("created")] public CreatedDto[] Created { get; set; } = Array.Empty<CreatedDto>();

    /// <summary>The baseline hash of every cell hosting a record: each record is proven against its host cell.</summary>
    [Key("baselines")] public CellBaselineDto[] Baselines { get; set; } = Array.Empty<CellBaselineDto>();

    /// <summary>Changed world containers with their whole contents. Required from schema 6.</summary>
    [Key("containers")] public ContainerDto[]? Containers { get; set; }

    /// <summary>Spawners' creatures that left their baseline. Required from schema 8.</summary>
    [Key("creatures")] public CreatureDto[]? Creatures { get; set; }

    /// <summary>The sounds made on the last tick that creatures hear on the next, in order. Required from schema 14.</summary>
    [Key("noises")] public NoiseDto[]? Noises { get; set; }

    /// <summary>Required from schema 15. The 14 -> 15 step gives older saves none: nothing was built before M7.</summary>
    [Key("pieces")] public PieceDto[]? Pieces { get; set; }

    /// <summary>Required from schema 15. The 14 -> 15 step gives older saves 0: nothing was built before M7.</summary>
    [Key("structure_seq")] public long? StructureSeq { get; set; }

    /// <summary>Required from schema 15. The 14 -> 15 step gives older saves none: no one was sent to work before M7.</summary>
    [Key("npc_errands")] public NpcErrandDto[]? NpcErrands { get; set; }
}

/// <summary>A player-placed piece (schema 15): its derived ID, definition, anchor cell and anchor (absolute mm), turn, owner, health, door.</summary>
[MessagePackObject]
public sealed class PieceDto
{
    [Key("instance_id")] public string InstanceId { get; set; } = "";
    [Key("def_id")] public string DefId { get; set; } = "";
    [Key("host_cell")] public string HostCell { get; set; } = "";
    [Key("x_mm")] public long XMm { get; set; }
    [Key("z_mm")] public long ZMm { get; set; }
    [Key("rotation")] public int Rotation { get; set; }
    [Key("owner")] public string Owner { get; set; } = "";
    [Key("health")] public int Health { get; set; }
    [Key("door_open")] public bool DoorOpen { get; set; }
}

/// <summary>A named NPC away from their place (schema 15): phase, work place and owner, pose, the route, and ticks without headway.</summary>
[MessagePackObject]
public sealed class NpcErrandDto
{
    [Key("npc_id")] public string NpcId { get; set; } = "";
    [Key("host_cell")] public string HostCell { get; set; } = "";
    [Key("phase")] public string Phase { get; set; } = "";
    [Key("piece_id")] public string? PieceId { get; set; }
    [Key("work_owner")] public string? WorkOwner { get; set; }
    [Key("x_mm")] public long XMm { get; set; }
    [Key("z_mm")] public long ZMm { get; set; }
    [Key("facing_mdeg")] public int FacingMdeg { get; set; }
    [Key("route")] public NavRouteDto? Route { get; set; }
    [Key("stuck_ticks")] public int StuckTicks { get; set; }
}

/// <summary>A sound waiting to be heard (schema 14): where, how far it carries, and - for a howl - whose kind answers it.</summary>
[MessagePackObject]
public sealed class NoiseDto
{
    [Key("x_mm")] public long XMm { get; set; }
    [Key("z_mm")] public long ZMm { get; set; }
    [Key("radius_mm")] public long RadiusMm { get; set; }
    [Key("call")] public bool Call { get; set; }
    [Key("caller_kind")] public string? CallerKind { get; set; }
}

/// <summary>What a creature's next ticks depend on beyond its body and mind (schema 14): each only while it still matters.</summary>
[MessagePackObject]
public sealed class CreatureContinuationDto
{
    [Key("next_charge_tick")] public long NextChargeTick { get; set; }
    [Key("stagger_immune_until")] public long StaggerImmuneUntil { get; set; }
    [Key("staggered_tick")] public long? StaggeredTick { get; set; }
    [Key("stagger_lasts_ticks")] public int StaggerLastsTicks { get; set; }
}

[MessagePackObject]
public sealed class CreatureDto
{
    [Key("key")] public string Key { get; set; } = "";
    [Key("def_id")] public string DefId { get; set; } = "";
    [Key("instance_id")] public string InstanceId { get; set; } = "";
    [Key("host_cell")] public string HostCell { get; set; } = "";
    [Key("generation")] public int Generation { get; set; }
    [Key("condition")] public string Condition { get; set; } = "";
    [Key("x_mm")] public long XMm { get; set; }
    [Key("z_mm")] public long ZMm { get; set; }
    [Key("facing_mdeg")] public int FacingMdeg { get; set; }
    [Key("health")] public int Health { get; set; }
    [Key("died_tick")] public long DiedTick { get; set; }
    [Key("respawn_tick")] public long RespawnTick { get; set; }
    [Key("mind")] public string Mind { get; set; } = "";
    [Key("awareness")] public int Awareness { get; set; }
    [Key("knows")] public bool Knows { get; set; }
    [Key("known_x_mm")] public long KnownXMm { get; set; }
    [Key("known_z_mm")] public long KnownZMm { get; set; }
    [Key("last_seen_tick")] public long LastSeenTick { get; set; }
    [Key("search_until")] public long SearchUntil { get; set; }
    [Key("has_called")] public bool HasCalled { get; set; }

    /// <summary>Required from schema 14. The 13 -> 14 step gives older saves none: the charge, the stagger and the stun were not kept.</summary>
    [Key("continuation")] public CreatureContinuationDto? Continuation { get; set; }
}

[MessagePackObject]
public sealed class ContainerDto
{
    [Key("key")] public string Key { get; set; } = "";
    [Key("instance_id")] public string InstanceId { get; set; } = "";
    [Key("host_cell")] public string HostCell { get; set; } = "";
    [Key("items")] public ContainerItemDto[] Items { get; set; } = Array.Empty<ContainerItemDto>();
}

[MessagePackObject]
public sealed class ContainerItemDto
{
    [Key("item_id")] public string ItemId { get; set; } = "";
    [Key("def_id")] public string DefId { get; set; } = "";
    [Key("count")] public int Count { get; set; }

    /// <summary>Required from schema 9. The 8 -> 9 step gives older stacks 0.</summary>
    [Key("quality")] public int? Quality { get; set; }
}

[MessagePackObject]
public sealed class CreatedDto
{
    [Key("instance_id")] public string InstanceId { get; set; } = "";
    [Key("def_id")] public string DefId { get; set; } = "";
    [Key("host_cell")] public string HostCell { get; set; } = "";
    [Key("x_cm")] public int XCm { get; set; }
    [Key("z_cm")] public int ZCm { get; set; }

    /// <summary>Required from schema 6: a dropped stack keeps its count. The 5 -> 6 step gives older records 1.</summary>
    [Key("count")] public int? Count { get; set; }

    /// <summary>Required from schema 9. The 8 -> 9 step gives older records 0.</summary>
    [Key("quality")] public int? Quality { get; set; }
}

[MessagePackObject]
public sealed class CellBaselineDto
{
    [Key("cell_key")] public string CellKey { get; set; } = "";
    [Key("baseline_hash")] public string BaselineHash { get; set; } = "";
}

/// <summary>§5.3's record: instance_id, slot_key, generation_seq, def_id, dirty_mask, state.</summary>
[MessagePackObject]
public sealed class EntityDto
{
    [Key("instance_id")] public string InstanceId { get; set; } = "";
    [Key("slot_key")] public string SlotKey { get; set; } = "";
    [Key("generation_seq")] public int GenerationSeq { get; set; }
    [Key("def_id")] public string DefId { get; set; } = "";
    [Key("dirty_mask")] public int DirtyMask { get; set; }
    [Key("state")] public EntityStateDto State { get; set; } = new();
}

/// <summary>Only the diverged fields: a field that equals the baseline is nil.</summary>
[MessagePackObject]
public sealed class EntityStateDto
{
    [Key("alive")] public bool? Alive { get; set; }
    [Key("x_cm")] public int? XCm { get; set; }
    [Key("z_cm")] public int? ZCm { get; set; }
}

/// <summary>Encodes and decodes save sections. Output is byte-stable: equal input, equal bytes (T-03).</summary>
public static class SectionCodec
{
    // A save is untrusted input from disk: it may be damaged or hand-edited.
    private static readonly MessagePackSerializerOptions Options =
        MessagePackSerializerOptions.Standard.WithSecurity(MessagePackSecurity.UntrustedData);

    private static readonly JsonSerializerOptions ManifestJson = new() { WriteIndented = true };

    /// <summary>The MessagePack options every section is read and written with, shared with the migration chain.</summary>
    internal static MessagePackSerializerOptions MessagePackOptions => Options;

    public static byte[] EncodeManifest(SaveManifest manifest) => JsonSerializer.SerializeToUtf8Bytes(manifest, ManifestJson);

    public static SaveManifest DecodeManifest(byte[] bytes) =>
        JsonSerializer.Deserialize<SaveManifest>(bytes, ManifestJson)
        ?? throw new JsonException("manifest.json is empty");

    public static byte[] EncodePlayer(PlayerRecord player) => MessagePackSerializer.Serialize(new PlayerDto
    {
        InstanceId = player.Id.Value,
        Name = player.Name,
        XMm = player.XMm,
        YMm = player.YMm,
        ZMm = player.ZMm,
        AppearanceSeed = player.AppearanceSeed,
        Inventory = player.Inventory
            .Select(e => new InventoryDto { ItemId = e.ItemId.Value, DefId = e.DefId, Count = e.Count, Quality = e.Quality })
            .ToArray(),
        Progression = ProgressionCodec.ToDto(player.Progression),
        FacingMdeg = player.FacingMdeg,
        Discoveries = player.Discoveries
            .Select(d => new DiscoveryDto { LocationId = d.LocationId, Method = DiscoveryMethods.Key(d.Method), Tick = d.Tick })
            .ToArray(),
        Equipment = player.Equipment
            .Select(kv => new EquipmentDto { Slot = EquipSlots.Key(kv.Key), ItemId = kv.Value.Value })
            .ToArray(),
        Currency = player.Currency,
        Effects = player.Effects
            .Select(e => new EffectDto { EffectId = e.EffectId, Stacks = e.Stacks, ExpiresTick = e.ExpiresTick, NextTickAt = e.NextTickAt })
            .ToArray(),
        Relationships = player.Relationships
            .Select(r => new RelationshipDto { NpcId = r.NpcId, Dimension = r.Dimension, Value = r.Value })
            .ToArray(),
        Conversations = player.Conversations
            .Select(c => new ConversationDto { DialogueId = c.DialogueId, Heard = c.Heard.ToArray() })
            .ToArray(),
        Quests = player.Quests
            .Select(q => new QuestDto
            {
                QuestId = q.QuestId,
                Status = QuestKeys.Key(q.Status),
                StartedTick = q.StartedTick,
                EndedTick = q.EndedTick,
                EndedBy = q.EndedBy,
                Objectives = q.Objectives
                    .Select(o => new ObjectiveDto { Id = o.Id, Status = QuestKeys.Key(o.Status), ActivatedTick = o.ActivatedTick, EndedTick = o.EndedTick, Progress = o.Progress })
                    .ToArray(),
            })
            .ToArray(),
        Companions = player.Companions
            .Select(c => new CompanionDto
            {
                NpcId = c.NpcId,
                Order = CompanionKeys.Key(c.Order),
                Condition = CompanionKeys.Key(c.Condition),
                XMm = c.XMm,
                ZMm = c.ZMm,
                FacingMdeg = c.FacingMdeg,
                Health = c.Health,
                DownedTick = c.DownedTick,
                StuckTicks = c.StuckTicks,
                LastCombatTick = c.LastCombatTick,
                TrailMm = c.Trail.SelectMany(m => new[] { m.XMm, m.ZMm }).ToArray(),
                Route = RouteDto(c.Route),
            })
            .ToArray(),
        Posture = new PostureDto { Stance = StanceKeys.Key(player.Posture.Stance), Airborne = player.Posture.Airborne, AirMs = player.Posture.AirMs },
        Factions = new FactionsDto
        {
            NextActSeq = player.Factions.NextActSeq,
            Acts = player.Factions.Acts
                .Select(a => new ActDto { Seq = a.Seq, Kind = a.Kind, Subject = a.Subject, CellKey = a.CellKey, XMm = a.XMm, ZMm = a.ZMm, Tick = a.Tick })
                .ToArray(),
            Knowledge = player.Factions.Knowledge
                .Select(k => new KnowledgeDto { Knower = k.Knower, Act = k.Act, Identity = k.Identity, Source = k.Source, Via = k.Via, Tick = k.Tick, Delta = k.Delta })
                .ToArray(),
            Standing = player.Factions.Standing.Select(s => new StandingDto { FactionId = s.FactionId, Points = s.Points }).ToArray(),
        },
    }, Options);

    /// <summary>A route's saved shape (schema 15).</summary>
    private static NavRouteDto RouteDto(NavRoute route) => new()
    {
        Status = NavRoute.StatusKey(route.Status),
        GoalMm = new[] { route.GoalXMm, route.GoalZMm },
        CornersMm = route.Corners.SelectMany(c => new[] { c.XMm, c.ZMm }).ToArray(),
        PlannedTick = route.PlannedTick,
        Stamp = route.Stamp,
        WatchMm = new[] { route.Watch.MinXMm, route.Watch.MinZMm, route.Watch.MaxXMm, route.Watch.MaxZMm },
        Partial = route.Partial,
    };

    /// <summary>
    /// A saved route, only through <see cref="NavRoute"/>'s validating factories: the shape is checked here, and what the factory refuses is
    /// a decode failure for either host.
    /// </summary>
    private static NavRoute Route(NavRouteDto? dto, string what)
    {
        if (dto is null)
            throw new FormatException($"{what} has no route (required from schema 15)");
        if (dto.GoalMm is not { Length: 2 } || dto.WatchMm is not { Length: 4 } || dto.CornersMm is null || dto.CornersMm.Length % 2 != 0
            || dto.CornersMm.Length > 2 * NavRoute.MaxCorners)
            throw new FormatException($"{what} has a route of the wrong shape (goal_mm 2, watch_mm 4, corners_mm up to {2 * NavRoute.MaxCorners} numbers in pairs)");
        var goal = new NavPoint(dto.GoalMm[0], dto.GoalMm[1]);
        var watch = new NavRect(dto.WatchMm[0], dto.WatchMm[1], dto.WatchMm[2], dto.WatchMm[3]);
        var corners = Enumerable.Range(0, dto.CornersMm.Length / 2).Select(i => new NavPoint(dto.CornersMm[2 * i], dto.CornersMm[2 * i + 1])).ToImmutableArray();
        try
        {
            switch (dto.Status)
            {
                case "none":
                    if (NavRoute.ProblemOf(NavRouteStatus.None, goal.XMm, goal.ZMm, corners, dto.PlannedTick, dto.Stamp, watch, dto.Partial) is { } problem)
                        throw new ArgumentException(problem);
                    return NavRoute.None;
                case "active":
                    return NavRoute.Active(goal, corners, dto.PlannedTick, dto.Stamp, watch, dto.Partial);
                case "unreachable":
                    if (!corners.IsEmpty || dto.Partial)
                        throw new ArgumentException("an unreachable route has no corners and is not partial");
                    return NavRoute.Unreachable(goal, dto.PlannedTick, dto.Stamp, watch);
                default:
                    throw new FormatException($"{what} has route status '{dto.Status}'");
            }
        }
        catch (ArgumentException e)
        {
            throw new FormatException($"{what} has an invalid route: {e.Message}", e);
        }
    }

    /// <summary>The player's faction ledger (schema 15). Its keys are checked here; its order and ranges by <c>PlayerRecord</c>.</summary>
    private static FactionLedger Ledger(FactionsDto? dto)
    {
        if (dto is null)
            throw new FormatException("player.msgpack has no factions (required from schema 15)");
        if (dto.Acts is null || dto.Knowledge is null || dto.Standing is null)
            throw new FormatException("player.msgpack's factions lack acts, knowledge or standing");
        foreach (var act in dto.Acts.Where(a => !ActKinds.Built.Contains(a.Kind)))
            throw new FormatException($"player act {act.Seq} has kind '{act.Kind}'");
        foreach (var row in dto.Knowledge)
        {
            if (!Identities.All.Contains(row.Identity))
                throw new FormatException($"knowledge of act {row.Act} by {row.Knower} has identity '{row.Identity}'");
            if (!KnowledgeSources.All.Contains(row.Source))
                throw new FormatException($"knowledge of act {row.Act} by {row.Knower} has source '{row.Source}'");
        }
        return new FactionLedger(dto.NextActSeq,
            dto.Acts.Select(a => new ActRecord(a.Seq, a.Kind, a.Subject, a.CellKey, a.XMm, a.ZMm, a.Tick)).ToImmutableArray(),
            dto.Knowledge.Select(k => new FactionKnowledge(k.Knower, k.Act, k.Identity, k.Source, k.Via, k.Tick, k.Delta)).ToImmutableArray(),
            dto.Standing.Select(s => new FactionStanding(s.FactionId, s.Points)).ToImmutableArray());
    }

    /// <summary>A stack's saved quality: present from schema 9, and one of crude, standard or fine.</summary>
    private static int QualityOf(int? quality, string what) => quality switch
    {
        null => throw new FormatException($"{what} has no quality (required from schema 9)"),
        var q when UNNAMED.Domain.Crafting.Quality.IsValid(q.Value) => q.Value,
        var q => throw new FormatException($"{what} has quality {q}, which is not -1, 0 or 1"),
    };

    public static PlayerRecord DecodePlayer(byte[] bytes)
    {
        var dto = MessagePackSerializer.Deserialize<PlayerDto>(bytes, Options);
        var progression = dto.Progression ?? throw new FormatException("player.msgpack has no progression record (required from schema 4)");
        int facing = dto.FacingMdeg ?? throw new FormatException("player.msgpack has no facing (required from schema 5)");
        var discoveries = dto.Discoveries ?? throw new FormatException("player.msgpack has no discovery records (required from schema 5)");
        var equipment = dto.Equipment ?? throw new FormatException("player.msgpack has no equipment (required from schema 6)");
        long currency = dto.Currency ?? throw new FormatException("player.msgpack has no currency (required from schema 6)");
        var effects = dto.Effects ?? throw new FormatException("player.msgpack has no effects (required from schema 7)");
        var relationships = dto.Relationships ?? throw new FormatException("player.msgpack has no relationships (required from schema 10)");
        var conversations = dto.Conversations ?? throw new FormatException("player.msgpack has no conversations (required from schema 10)");
        var quests = dto.Quests ?? throw new FormatException("player.msgpack has no quests (required from schema 11)");
        var companions = dto.Companions ?? throw new FormatException("player.msgpack has no companions (required from schema 12)");
        var posture = dto.Posture ?? throw new FormatException("player.msgpack has no posture (required from schema 13)");
        var factions = Ledger(dto.Factions);
        var stance = posture.Stance switch
        {
            "standing" or "crouched" => StanceKeys.Parse(posture.Stance),
            _ => throw new FormatException($"player posture has stance '{posture.Stance}'"),
        };
        if (posture.AirMs < 0 || (!posture.Airborne && posture.AirMs != 0))
            throw new FormatException($"player posture has air_ms {posture.AirMs} while {(posture.Airborne ? "airborne" : "on the ground")}");
        return new PlayerRecord(EntityId.Parse(dto.InstanceId), dto.Name, dto.XMm, dto.YMm, dto.ZMm, dto.AppearanceSeed,
            dto.Inventory.Select(e => new InventoryEntry(EntityId.Parse(e.ItemId), e.DefId, e.Count) { Quality = QualityOf(e.Quality, $"carried {e.ItemId}") }),
            ProgressionCodec.FromDto(progression), facing,
            discoveries.Select(d => new DiscoveryRecord(d.LocationId, DiscoveryMethods.Parse(d.Method), d.Tick)),
            equipment.Select(e => KeyValuePair.Create(EquipSlots.Parse(e.Slot), EntityId.Parse(e.ItemId))), currency,
            effects.Select(e => new ActiveEffect(e.EffectId, e.Stacks, e.ExpiresTick, e.NextTickAt)),
            relationships.Select(r => new RelationshipValue(r.NpcId, r.Dimension, r.Value)),
            conversations.Select(c => new ConversationMemory(c.DialogueId, c.Heard.ToImmutableArray())),
            quests.Select(q => new QuestState(q.QuestId,
                QuestKeys.ParseQuest(q.Status) ?? throw new FormatException($"quest {q.QuestId} has status '{q.Status}'"),
                q.StartedTick, q.EndedTick, q.EndedBy,
                q.Objectives.Select(o => new ObjectiveState(o.Id,
                    QuestKeys.ParseObjective(o.Status) ?? throw new FormatException($"objective {o.Id} of {q.QuestId} has status '{o.Status}'"),
                    o.ActivatedTick, o.EndedTick, o.Progress)).ToImmutableArray())),
            companions.Select(Companion)) { Posture = new Posture(stance, posture.Airborne, posture.AirMs), Factions = factions };
    }

    private static CompanionRecord Companion(CompanionDto c)
    {
        if (c.TrailMm.Length % 2 != 0)
            throw new FormatException($"companion {c.NpcId}'s trail is x, z pairs; it has {c.TrailMm.Length} numbers");
        return new CompanionRecord(c.NpcId,
            CompanionKeys.ParseOrder(c.Order) ?? throw new FormatException($"companion {c.NpcId} has order '{c.Order}'"),
            CompanionKeys.ParseCondition(c.Condition) ?? throw new FormatException($"companion {c.NpcId} has condition '{c.Condition}'"),
            c.XMm, c.ZMm, c.FacingMdeg, c.Health)
        {
            DownedTick = c.DownedTick,
            StuckTicks = c.StuckTicks,
            LastCombatTick = c.LastCombatTick,
            Trail = Enumerable.Range(0, c.TrailMm.Length / 2).Select(i => new TrailMark(c.TrailMm[2 * i], c.TrailMm[2 * i + 1])).ToImmutableArray(),
            Route = Route(c.Route, $"companion {c.NpcId}"),
        };
    }

    public static byte[] EncodeCells(DeltaSnapshot snapshot) => MessagePackSerializer.Serialize(new CellsSectionDto
    {
        Records = snapshot.Cells.Select(c => new CellDto
        {
            CellKey = c.CellKey,
            BaselineHash = c.BaselineHash,
            DirtyReasons = c.DirtyReasons.ToArray(),
            Flags = c.Flags.Select(f => new FlagDto { Name = f.Key, Value = f.Value }).ToArray(),
            HarvestedNodes = c.HarvestedNodes
                .Select(n => new NodeDto { NodeKey = n.NodeKey, LastHarvestTick = n.LastHarvestTick, HarvestSeq = n.HarvestSeq })
                .ToArray(),
            PopulationAlive = c.PopulationAlive
                .Select(p => new PopulationDto { PopulationId = p.Key, Alive = p.Value })
                .ToArray(),
        }).ToArray(),
    }, Options);

    public static ImmutableArray<CellDeltaRecord> DecodeCells(byte[] bytes) =>
        MessagePackSerializer.Deserialize<CellsSectionDto>(bytes, Options).Records
            .Select(c => new CellDeltaRecord(
                c.CellKey,
                c.BaselineHash,
                c.DirtyReasons.ToImmutableArray(),
                c.Flags.Select(f => KeyValuePair.Create(f.Name, f.Value)).ToImmutableArray(),
                c.HarvestedNodes.Select(n => new NodeHarvest(n.NodeKey, n.LastHarvestTick, n.HarvestSeq)).ToImmutableArray(),
                c.PopulationAlive.Select(p => KeyValuePair.Create(p.PopulationId, p.Alive)).ToImmutableArray()))
            .ToImmutableArray();

    public static byte[] EncodeEntities(DeltaSnapshot snapshot)
    {
        var baselines = new SortedDictionary<string, string>(StringComparer.Ordinal);
        void Prove(string cell, string? hash, string label)
        {
            if (hash is null || (baselines.TryGetValue(cell, out string? known) && known != hash))
                throw new InvalidOperationException($"Entity record {label} carries no single baseline hash for its host cell {cell}");
            baselines[cell] = hash;
        }
        foreach (var e in snapshot.Entities)
            Prove(HostCell(e.SlotKey), e.BaselineHash, e.InstanceId.Value);
        foreach (var c in snapshot.Created)
            Prove(c.HostCell, c.BaselineHash, c.InstanceId.Value);
        foreach (var c in snapshot.Containers)
            Prove(c.HostCell, c.BaselineHash, c.InstanceId.Value);
        foreach (var c in snapshot.Creatures)
            Prove(c.HostCell, c.BaselineHash, c.InstanceId.Value);
        foreach (var p in snapshot.Pieces)
            Prove(p.HostCell, p.BaselineHash, p.InstanceId.Value);
        foreach (var e in snapshot.NpcErrands)
            Prove(e.HostCell, e.BaselineHash, $"npc errand {e.NpcId}");

        return MessagePackSerializer.Serialize(new EntitiesSectionDto
        {
            Records = snapshot.Entities.Select(e => new EntityDto
            {
                InstanceId = e.InstanceId.Value,
                SlotKey = e.SlotKey,
                GenerationSeq = e.GenerationSeq,
                DefId = e.DefId,
                DirtyMask = e.DirtyMask,
                State = new EntityStateDto { Alive = e.Alive, XCm = e.XCm, ZCm = e.ZCm },
            }).ToArray(),
            Created = snapshot.Created.Select(c => new CreatedDto
            {
                InstanceId = c.InstanceId.Value,
                DefId = c.DefId,
                HostCell = c.HostCell,
                XCm = c.XCm,
                ZCm = c.ZCm,
                Count = c.Count,
                Quality = c.Quality,
            }).ToArray(),
            Baselines = baselines.Select(kv => new CellBaselineDto { CellKey = kv.Key, BaselineHash = kv.Value }).ToArray(),
            Containers = snapshot.Containers.Select(c => new ContainerDto
            {
                Key = c.Key,
                InstanceId = c.InstanceId.Value,
                HostCell = c.HostCell,
                Items = c.Items.Select(i => new ContainerItemDto { ItemId = i.ItemId.Value, DefId = i.DefId, Count = i.Count, Quality = i.Quality }).ToArray(),
            }).ToArray(),
            Creatures = snapshot.Creatures.Select(c => new CreatureDto
            {
                Key = c.Key,
                DefId = c.DefId,
                InstanceId = c.InstanceId.Value,
                HostCell = c.HostCell,
                Generation = c.Generation,
                Condition = CreatureConditions.Key(c.Condition),
                XMm = c.XMm,
                ZMm = c.ZMm,
                FacingMdeg = c.FacingMdeg,
                Health = c.Health,
                DiedTick = c.DiedTick,
                RespawnTick = c.RespawnTick,
                Mind = CreatureMinds.Key(c.Mind),
                Awareness = c.Awareness,
                Knows = c.Knows,
                KnownXMm = c.KnownXMm,
                KnownZMm = c.KnownZMm,
                LastSeenTick = c.LastSeenTick,
                SearchUntil = c.SearchUntil,
                HasCalled = c.HasCalled,
                Continuation = new CreatureContinuationDto
                {
                    NextChargeTick = c.NextChargeTick,
                    StaggerImmuneUntil = c.StaggerImmuneUntil,
                    StaggeredTick = c.StaggeredTick,
                    StaggerLastsTicks = c.StaggerLastsTicks,
                },
            }).ToArray(),
            Noises = snapshot.Noises.Select(n => new NoiseDto { XMm = n.XMm, ZMm = n.ZMm, RadiusMm = n.RadiusMm, Call = n.Call, CallerKind = n.CallerKind })
                .ToArray(),
            Pieces = snapshot.Pieces.Select(p => new PieceDto
            {
                InstanceId = p.InstanceId.Value,
                DefId = p.DefId,
                HostCell = p.HostCell,
                XMm = p.XMm,
                ZMm = p.ZMm,
                Rotation = p.Rotation,
                Owner = p.Owner.Value,
                Health = p.HealthCurrent,
                DoorOpen = p.DoorOpen,
            }).ToArray(),
            StructureSeq = snapshot.StructureSequence,
            NpcErrands = snapshot.NpcErrands.Select(e => new NpcErrandDto
            {
                NpcId = e.NpcId,
                HostCell = e.HostCell,
                Phase = NpcErrandPhases.Key(e.Phase),
                PieceId = e.PieceId?.Value,
                WorkOwner = e.WorkOwner?.Value,
                XMm = e.XMm,
                ZMm = e.ZMm,
                FacingMdeg = e.FacingMdeg,
                Route = RouteDto(e.Route),
                StuckTicks = e.StuckTicks,
            }).ToArray(),
        }, Options);
    }

    /// <summary>
    /// The entities section, as the snapshot it saved (its cells are the cells section's): slot-keyed records, created instances, changed
    /// containers, creature records, placed pieces and NPC errands, each with its host cell's baseline hash; the sounds waiting to be
    /// heard; and the structure sequence.
    /// </summary>
    public static DeltaSnapshot DecodeEntitySection(byte[] bytes)
    {
        var section = MessagePackSerializer.Deserialize<EntitiesSectionDto>(bytes, Options);
        var baselines = section.Baselines.ToDictionary(b => b.CellKey, b => b.BaselineHash, StringComparer.Ordinal);
        var entities = section.Records
            .Select(e => new EntityDeltaRecord(
                EntityId.Parse(e.InstanceId), e.SlotKey, e.GenerationSeq, e.DefId,
                e.State.Alive, e.State.XCm, e.State.ZCm,
                baselines.GetValueOrDefault(HostCell(e.SlotKey))))
            .ToImmutableArray();
        var created = section.Created
            .Select(c => new CreatedEntityRecord(
                EntityId.Parse(c.InstanceId), c.DefId, c.HostCell, c.XCm, c.ZCm, baselines.GetValueOrDefault(c.HostCell))
            {
                Count = c.Count ?? throw new FormatException($"created instance {c.InstanceId} has no count (required from schema 6)"),
                Quality = QualityOf(c.Quality, $"created instance {c.InstanceId}"),
            })
            .ToImmutableArray();
        var containers = (section.Containers ?? throw new FormatException("entities.msgpack has no containers list (required from schema 6)"))
            .Select(c => new ContainerRecord(
                c.Key, EntityId.Parse(c.InstanceId), c.HostCell,
                c.Items.Select(i => new ContainerItem(EntityId.Parse(i.ItemId), i.DefId, i.Count) { Quality = QualityOf(i.Quality, $"{c.Key} item {i.ItemId}") })
                    .ToImmutableArray(),
                baselines.GetValueOrDefault(c.HostCell)))
            .ToImmutableArray();
        var creatures = (section.Creatures ?? throw new FormatException("entities.msgpack has no creatures list (required from schema 8)"))
            .Select(c =>
            {
                var continuation = c.Continuation ?? throw new FormatException($"creature {c.Key} has no continuation (required from schema 14)");
                return new CreatureRecord(c.Key, c.DefId, EntityId.Parse(c.InstanceId), c.HostCell, c.Generation, CreatureConditions.Parse(c.Condition),
                    c.XMm, c.ZMm, c.FacingMdeg, c.Health, c.DiedTick, c.RespawnTick, baselines.GetValueOrDefault(c.HostCell))
                {
                    Mind = CreatureMinds.Parse(c.Mind),
                    Awareness = c.Awareness,
                    Knows = c.Knows,
                    KnownXMm = c.KnownXMm,
                    KnownZMm = c.KnownZMm,
                    LastSeenTick = c.LastSeenTick,
                    SearchUntil = c.SearchUntil,
                    HasCalled = c.HasCalled,
                    NextChargeTick = continuation.NextChargeTick,
                    StaggerImmuneUntil = continuation.StaggerImmuneUntil,
                    StaggeredTick = continuation.StaggeredTick,
                    StaggerLastsTicks = continuation.StaggerLastsTicks,
                };
            })
            .ToImmutableArray();
        var noises = (section.Noises ?? throw new FormatException("entities.msgpack has no noises list (required from schema 14)"))
            .Select(n => new Noise(n.XMm, n.ZMm, n.RadiusMm, n.Call, n.CallerKind))
            .ToImmutableArray();
        var pieces = (section.Pieces ?? throw new FormatException("entities.msgpack has no pieces (required from schema 15)"))
            .Select(p => new PieceRecord(EntityId.Parse(p.InstanceId), p.DefId, p.HostCell, p.XMm, p.ZMm, p.Rotation, EntityId.Parse(p.Owner), p.Health,
                baselines.GetValueOrDefault(p.HostCell)) { DoorOpen = p.DoorOpen })
            .ToImmutableArray();
        long sequence = section.StructureSeq ?? throw new FormatException("entities.msgpack has no structure_seq (required from schema 15)");
        if (sequence < 0)
            throw new FormatException($"entities.msgpack has structure_seq {sequence}, below 0");
        var errands = (section.NpcErrands ?? throw new FormatException("entities.msgpack has no npc_errands (required from schema 15)"))
            .Select(e => new NpcErrandRecord(e.NpcId, e.HostCell, NpcErrandPhases.Parse(e.Phase), e.PieceId is null ? null : EntityId.Parse(e.PieceId),
                e.WorkOwner is null ? null : EntityId.Parse(e.WorkOwner), e.XMm, e.ZMm, e.FacingMdeg, baselines.GetValueOrDefault(e.HostCell))
            {
                Route = Route(e.Route, $"npc errand {e.NpcId}"),
                StuckTicks = e.StuckTicks,
            })
            .ToImmutableArray();
        return new DeltaSnapshot(ImmutableArray<CellDeltaRecord>.Empty, entities)
        {
            Created = created, Containers = containers, Creatures = creatures, Noises = noises,
            Pieces = pieces, StructureSequence = sequence, NpcErrands = errands,
        };
    }

    public static ImmutableArray<EntityDeltaRecord> DecodeEntities(byte[] bytes) => DecodeEntitySection(bytes).Entities;

    /// <summary>The cell key of a slot key: <c>&lt;cell_key&gt;.&lt;population_id&gt;.&lt;ordinal&gt;</c>, and a cell key has no '.'.</summary>
    public static string HostCell(string slotKey)
    {
        int dot = slotKey.IndexOf('.');
        return dot > 0 ? slotKey[..dot] : slotKey;
    }

    /// <summary>Manifest JSON options, shared with the migration chain, which edits the manifest as JSON.</summary>
    public static JsonSerializerOptions ManifestOptions => ManifestJson;
}
