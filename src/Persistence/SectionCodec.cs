// UNNAMED Persistence - section encoding (PERSISTENCE.md §5.1-§5.3)
// No Godot references - pure C#

using System.Collections.Immutable;
using System.Text.Json;
using MessagePack;
using UNNAMED.Domain;
using UNNAMED.Domain.Combat;
using UNNAMED.Domain.Companions;
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
            })
            .ToArray(),
        Posture = new PostureDto { Stance = StanceKeys.Key(player.Posture.Stance), Airborne = player.Posture.Airborne, AirMs = player.Posture.AirMs },
    }, Options);

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
            companions.Select(Companion)) { Posture = new Posture(stance, posture.Airborne, posture.AirMs) };
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
        void Prove(string cell, string? hash, EntityId instance)
        {
            if (hash is null || (baselines.TryGetValue(cell, out string? known) && known != hash))
                throw new InvalidOperationException($"Entity record {instance} carries no single baseline hash for its host cell {cell}");
            baselines[cell] = hash;
        }
        foreach (var e in snapshot.Entities)
            Prove(HostCell(e.SlotKey), e.BaselineHash, e.InstanceId);
        foreach (var c in snapshot.Created)
            Prove(c.HostCell, c.BaselineHash, c.InstanceId);
        foreach (var c in snapshot.Containers)
            Prove(c.HostCell, c.BaselineHash, c.InstanceId);
        foreach (var c in snapshot.Creatures)
            Prove(c.HostCell, c.BaselineHash, c.InstanceId);

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
            }).ToArray(),
        }, Options);
    }

    /// <summary>
    /// The entities section: slot-keyed records, created instances, changed containers and creature records, each with its
    /// host cell's baseline hash.
    /// </summary>
    public static (ImmutableArray<EntityDeltaRecord> Entities, ImmutableArray<CreatedEntityRecord> Created, ImmutableArray<ContainerRecord> Containers,
        ImmutableArray<CreatureRecord> Creatures) DecodeEntitySection(byte[] bytes)
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
            .Select(c => new CreatureRecord(c.Key, c.DefId, EntityId.Parse(c.InstanceId), c.HostCell, c.Generation, CreatureConditions.Parse(c.Condition),
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
            })
            .ToImmutableArray();
        return (entities, created, containers, creatures);
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
