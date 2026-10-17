// UNNAMED Persistence - the load sequence (PERSISTENCE.md §7.4, M2b §16)
// No Godot references - pure C#

using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MessagePack;
using UNNAMED.Domain.Combat;
using UNNAMED.Domain.Creatures;
using UNNAMED.Domain.Factions;
using UNNAMED.Domain.Economy;
using UNNAMED.Domain.Quests;
using UNNAMED.Domain.Social;
using UNNAMED.Persistence.Sections;
using UNNAMED.World;
using Registry = UNNAMED.EntityRegistry.EntityRegistry;

namespace UNNAMED.Persistence;

/// <summary>
/// The load sequence, as one pipeline shared by a load, a dry run and a migration. It reads files and
/// never writes them: a migrated save reaches disk only through the §7.1 write sequence.
/// Order (M2b §16): container, integrity, schema chain, definition IDs, baseline proof, invariants.
/// </summary>
internal static class SaveLoader
{
    public static MigrationReport NewReport(string saveName, LoadContext context) => new(saveName)
    {
        CurrentSchema = context.SchemaVersion,
        CurrentContentVersion = context.Content.Version,
        CurrentContentHash = context.Content.Hash,
        CurrentWorldgenVersion = context.Generator.WorldgenVersion,
        CurrentFingerprint = context.Generator.Fingerprint,
        CurrentRngContract = context.Generator.RngContractVersion,
    };

    /// <summary>
    /// Load a save directory. Returns a null result when the report has blockers; throws for a save
    /// that is not readable as a save at all (§7.2). The report is filled in either way.
    /// </summary>
    public static LoadResult? Run(string directory, string saveName, LoadContext context, ImmutableArray<int> backups, MigrationReport report)
    {
        if (!Directory.Exists(directory))
            throw new SaveException($"There is no save at '{directory}'");
        var generator = context.Generator;

        // a. manifest.json: the container version must match exactly, else refuse.
        JsonObject manifestJson;
        try
        {
            manifestJson = JsonNode.Parse(File.ReadAllBytes(Path.Combine(directory, SaveFormat.Manifest))) as JsonObject
                ?? throw new JsonException("manifest.json is not a JSON object");
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            throw new SaveCorruptionException($"manifest.json in '{saveName}' is unreadable; the slot cannot be loaded", backups, e);
        }
        int saveFormat = RequiredInt(manifestJson, "save_format", saveName, backups);
        report.SourceSaveFormat = saveFormat;
        if (saveFormat != SaveFormat.Current)
            throw new SaveFormatMismatchException(saveFormat, SaveFormat.Current);
        int schema = RequiredInt(manifestJson, "schema_version", saveName, backups);
        report.SourceSchema = schema;
        report.SourceContentVersion = OptionalString(manifestJson, "content_version");
        report.SourceContentHash = OptionalString(manifestJson, "content_hash");
        report.SourceWorldgenVersion = OptionalInt(manifestJson, "worldgen_version");
        report.SourceFingerprint = OptionalString(manifestJson, "worldgen_fingerprint");
        report.SourceRngContract = OptionalInt(manifestJson, "rng_contract_version") ?? (schema == 1 ? RngContract.V1 : null);

        // b. sections.sha256, per section: quarantine what can be dropped; the rest is fatal.
        var expected = SaveIntegrity.TryReadRoot(directory);
        bool rederived = expected is null;   // §3.3: an unreadable integrity root is re-derived, and reported
        expected ??= SaveFormat.CheckedFiles.ToDictionary(f => f, f => SaveIntegrity.HashFileOrNull(Path.Combine(directory, f)) ?? "", StringComparer.Ordinal);
        var quarantined = new List<string>();
        foreach (string file in SaveFormat.CheckedFiles)
        {
            if (SaveIntegrity.HashFileOrNull(Path.Combine(directory, file)) == expected[file])
                continue;
            switch (file)
            {
                case SaveFormat.Manifest:
                    throw new SaveCorruptionException($"manifest.json in '{saveName}' fails its integrity check", backups);
                case SaveFormat.Player:
                    throw new SaveCorruptionException(
                        $"player.msgpack in '{saveName}' is corrupt. " + (backups.IsEmpty ? "No backup exists." : $"Backups available: {string.Join(", ", backups)}."),
                        backups);
                default:
                    quarantined.Add(SectionName(file));
                    break;
            }
        }
        if (rederived)
            report.Warnings.Add("sections.sha256 was unreadable and was re-derived from the files");
        foreach (string section in quarantined)
            report.Warnings.Add($"section '{section}' fails its integrity check and is loaded without (§7.2)");

        var sections = new Dictionary<string, byte[]?>(StringComparer.Ordinal);
        foreach (string file in new[] { SaveFormat.Player, SaveFormat.Cells, SaveFormat.Entities })
            sections[file] = quarantined.Contains(SectionName(file)) ? null : File.ReadAllBytes(Path.Combine(directory, file));

        // c. The schema migration chain, one version per step, in memory (§6.2).
        if (schema > context.SchemaVersion)
        {
            report.Blockers.Add($"schema {schema} is newer than this build, which reads up to schema {context.SchemaVersion}");
            return null;
        }
        if (schema < context.SchemaVersion)
        {
            if (!SchemaMigrations.TryChain(context.Migrations, schema, context.SchemaVersion, out var chain))
            {
                report.Blockers.Add($"no registered migration chain from schema {schema} to schema {context.SchemaVersion}");
                return null;
            }
            var document = new MigrationDocument(schema, manifestJson, sections);
            var environment = new MigrationEnvironment(generator);
            foreach (var step in chain)
            {
                try
                {
                    step.Apply(document, environment, report);
                }
                catch (Exception e) when (e is InvalidOperationException or FormatException or ArgumentException or MessagePackSerializationException)
                {
                    report.Blockers.Add($"migration schema {step.From} -> {step.To} failed: {e.Message}");
                }
                if (report.Blockers.Count > 0)
                    return null;
                document.Schema = step.To;
            }
            sections = document.Sections;
        }

        // d. Decode the current shape.
        SaveManifest manifest;
        try
        {
            manifest = manifestJson.Deserialize<SaveManifest>(SectionCodec.ManifestOptions)
                ?? throw new JsonException("manifest.json is empty");
            _ = manifest.SavedAt;   // a saved_utc that is not a moment is a corrupt manifest, not an absence of zero
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or NotSupportedException or FormatException)
        {
            throw new SaveCorruptionException($"manifest.json in '{saveName}' does not have the schema-{context.SchemaVersion} shape: {e.Message}", backups, e);
        }
        var cells = DecodeOrQuarantine(sections[SaveFormat.Cells], SaveFormat.Cells, SectionCodec.DecodeCells, quarantined, report);
        var (entities, created, containers, creatures, noises) = DecodeOrQuarantine(sections[SaveFormat.Entities], SaveFormat.Entities,
            SectionCodec.DecodeEntitySection, quarantined, report, (ImmutableArray<EntityDeltaRecord>.Empty, ImmutableArray<CreatedEntityRecord>.Empty,
                ImmutableArray<ContainerRecord>.Empty, ImmutableArray<CreatureRecord>.Empty, ImmutableArray<Noise>.Empty));
        // The economy (schema 22): its markets and goods are matched against content when the world starts (EconomyEngine.Reconcile),
        // which drops what content no longer defines, so they take no part in the definition-ID pass.
        var (economy, merchants) = DecodeOrQuarantine(quarantined.Contains(SectionName(SaveFormat.Entities)) ? null : sections[SaveFormat.Entities],
            SaveFormat.Entities, SectionCodec.DecodeEconomy, quarantined, report, ((EconomyState?)null, ImmutableArray<MerchantState>.Empty));
        // NPC life (schema 26): reconciled against the routines when the world starts, which drops what content no longer defines, so it
        // takes no part in the definition-ID pass either.
        var npcLife = DecodeOrQuarantine(quarantined.Contains(SectionName(SaveFormat.Entities)) ? null : sections[SaveFormat.Entities],
            SaveFormat.Entities, SectionCodec.DecodeNpcLife, quarantined, report, (NpcLifeState?)null);
        // Adventurers (schema 29): reconciled against config.adventurers when the world starts, outside the definition-ID pass too.
        var adventurers = DecodeOrQuarantine(quarantined.Contains(SectionName(SaveFormat.Entities)) ? null : sections[SaveFormat.Entities],
            SaveFormat.Entities, SectionCodec.DecodeAdventurers, quarantined, report, (AdventurerState?)null);
        var delta = new DeltaSnapshot(cells, entities)
        {
            Created = created, Containers = containers, Creatures = creatures, Noises = noises, Economy = economy, Merchants = merchants,
            NpcLife = npcLife, Adventurers = adventurers,
        };
        PlayerRecord player;
        try
        {
            player = SectionCodec.DecodePlayer(sections[SaveFormat.Player]!);
        }
        catch (Exception e) when (e is MessagePackSerializationException or FormatException or ArgumentException)
        {
            throw new SaveCorruptionException($"player.msgpack in '{saveName}' could not be decoded", backups, e);
        }

        // e. Definition IDs (§6.3): a changed content pack resolves every stored ID, or the load stops.
        if (manifest.ContentHash != context.Content.Hash)
            (player, delta) = ResolveDefinitions(player, delta, context.Content, report);
        if (report.Blockers.Count > 0)
            return null;

        // f. The generator contract. A different epoch needs a registered worldgen migration.
        if (manifest.WorldgenVersion != generator.WorldgenVersion || manifest.RngContractVersion != generator.RngContractVersion)
        {
            report.Blockers.Add(
                $"the save is worldgen {manifest.WorldgenVersion} (RNG contract {manifest.RngContractVersion}); this build is worldgen " +
                $"{generator.WorldgenVersion} (RNG contract {generator.RngContractVersion}), and no migration is registered between them");
            return null;
        }
        if (manifest.WorldgenFingerprint != generator.Fingerprint)
            report.Warnings.Add("the world generator changed since this save (worldgen_fingerprint differs); every changed cell is checked against its own baseline hash");

        // g. Baseline proof: every changed cell's delta targets exactly the baseline generated now.
        ulong seed = WorldSeed.Parse(manifest.WorldSeed);
        delta = ProveBaselines(delta, seed, manifest.WorldgenFingerprint, context, report);
        if (report.Blockers.Count > 0)
            return null;

        // h-i. Regenerate, apply the cell delta, merge entities on slot key. Invalid records are reported.
        var world = WorldDelta.FromSnapshot(generator, seed, context.Registry, delta, out var rejected);
        foreach (var r in rejected)
            report.Warnings.Add($"invalid {r.Section} record '{r.Key}' dropped: {r.Reason}");

        var quarantinedSections = quarantined.Distinct().OrderBy(s => s, StringComparer.Ordinal).ToImmutableArray();
        return new LoadResult(
            manifest with { Flags = new ManifestFlags { QuarantinedSections = quarantinedSections } },
            player, world, quarantinedSections, rejected, rederived, report);
    }

    /// <summary>A dry run: the whole load, into a throwaway registry, reporting instead of throwing.</summary>
    public static MigrationReport Plan(string directory, string saveName, LoadContext context)
    {
        var report = NewReport(saveName, context);
        try
        {
            Run(directory, saveName, context with { Registry = new Registry() }, ImmutableArray<int>.Empty, report);
        }
        catch (SaveException e)
        {
            report.Blockers.Add(e.Message);
        }
        return report;
    }

    private static (PlayerRecord, DeltaSnapshot) ResolveDefinitions(
        PlayerRecord player, DeltaSnapshot delta, ContentIdentity content, MigrationReport report)
    {
        string? Resolve(string id, string referencedBy)
        {
            var resolution = content.Resolve(id);
            switch (resolution.Outcome)
            {
                case IdOutcome.Current:
                    return id;
                case IdOutcome.Renamed:
                    report.CountAlias(id, resolution.CurrentId!);
                    return resolution.CurrentId;
                case IdOutcome.Replaced:
                    report.CountReplacement(id, resolution.CurrentId!);
                    return resolution.CurrentId;
                case IdOutcome.Discarded:
                    report.CountDiscard(id);
                    report.Loss.Add($"{referencedBy}: '{id}' was removed with no replacement; dropped");
                    return null;
                default:
                    report.Blockers.Add(
                        $"unresolved definition ID '{id}' ({referencedBy}): it is not defined and content/_aliases.yaml " +
                        $"does not map it ({resolution.Path})");
                    return null;
            }
        }

        var inventory = new List<InventoryEntry>();
        foreach (var entry in player.Inventory)
        {
            string? id = Resolve(entry.DefId, $"player inventory item {entry.ItemId}");
            if (id is not null)
                inventory.Add(entry with { DefId = id });
        }

        var cells = delta.Cells.Select(cell =>
        {
            var flags = new List<KeyValuePair<string, long>>();
            foreach (var (flag, value) in cell.Flags)
            {
                string? id = Resolve(flag, $"world flag in cell {cell.CellKey}");
                if (id is not null)
                    flags.Add(KeyValuePair.Create(id, value));
            }
            return cell with { Flags = flags.OrderBy(f => f.Key, StringComparer.Ordinal).ToImmutableArray() };
        }).ToImmutableArray();

        var entities = ImmutableArray.CreateBuilder<EntityDeltaRecord>();
        foreach (var record in delta.Entities)
        {
            string? id = Resolve(record.DefId, $"entity {record.InstanceId} in slot {record.SlotKey}");
            if (id is not null)
                entities.Add(record with { DefId = id });
        }

        var created = ImmutableArray.CreateBuilder<CreatedEntityRecord>();
        foreach (var record in delta.Created)
        {
            string? id = Resolve(record.DefId, $"created instance {record.InstanceId} in cell {record.HostCell}");
            if (id is not null)
                created.Add(record with { DefId = id });
        }

        // Items in changed world containers (schema 6). An item whose definition was removed leaves its container.
        var containers = delta.Containers.Select(container =>
        {
            var items = ImmutableArray.CreateBuilder<ContainerItem>();
            foreach (var item in container.Items)
            {
                string? id = Resolve(item.DefId, $"item {item.ItemId} in container {container.Key}");
                if (id is not null)
                    items.Add(item with { DefId = id });
            }
            return container with { Items = items.ToImmutable() };
        }).ToImmutableArray();

        // Skills, known techniques, first-time records and kill records name definitions too (schema 4).
        var progression = player.Progression.RewriteDefinitionIds((id, role) => Resolve(id, $"player {role}"));

        // Discovered locations (schema 5). Two records that resolve to one place keep the earliest discovery.
        var discoveries = new SortedDictionary<string, DiscoveryRecord>(StringComparer.Ordinal);
        foreach (var record in player.Discoveries)
        {
            if (Resolve(record.LocationId, "player discovery") is not { } id)
                continue;
            if (!discoveries.TryGetValue(id, out var existing) || record.Tick < existing.Tick)
                discoveries[id] = record with { LocationId = id };
        }

        // Active effects (schema 7). Two that resolve to one effect keep the later deadline and the higher stack count.
        var effects = new SortedDictionary<string, ActiveEffect>(StringComparer.Ordinal);
        foreach (var effect in player.Effects)
        {
            if (Resolve(effect.EffectId, "player effect") is not { } id)
                continue;
            effects[id] = effects.TryGetValue(id, out var existing)
                ? existing with { Stacks = Math.Max(existing.Stacks, effect.Stacks), ExpiresTick = Math.Max(existing.ExpiresTick, effect.ExpiresTick) }
                : effect with { EffectId = id };
        }

        // Creature records (schema 8). A creature whose definition was removed is gone from the save; its spawner decides afresh.
        var creatures = ImmutableArray.CreateBuilder<CreatureRecord>();
        foreach (var record in delta.Creatures)
        {
            string? id = Resolve(record.DefId, $"creature {record.Key}");
            if (id is not null)
                creatures.Add(record with { DefId = id });
        }

        // A howl waiting to be heard names its caller's kind (schema 14); one whose kind was removed has no one left to answer it.
        var noises = ImmutableArray.CreateBuilder<Noise>();
        foreach (var noise in delta.Noises)
        {
            if (noise.CallerKind is null)
                noises.Add(noise);
            else if (Resolve(noise.CallerKind, "a creature's call") is { } kind)
                noises.Add(noise with { CallerKind = kind });
        }

        // Relationships and conversation memory (schema 10): an NPC or conversation that was removed takes its record with it.
        var relationships = new SortedDictionary<(string, string), RelationshipValue>();
        foreach (var value in player.Relationships)
        {
            if (Resolve(value.NpcId, "player relationship") is not { } id)
                continue;
            relationships[(id, value.Dimension)] = value with { NpcId = id };
        }
        var conversations = new SortedDictionary<string, ConversationMemory>(StringComparer.Ordinal);
        foreach (var memory in player.Conversations)
        {
            if (Resolve(memory.DialogueId, "player conversation") is not { } id)
                continue;
            conversations[id] = conversations.TryGetValue(id, out var heard)
                ? heard with { Heard = heard.Heard.Union(memory.Heard, StringComparer.Ordinal).ToImmutableArray() }
                : memory with { DialogueId = id };
        }

        // Quests (schema 11): a quest that was removed takes its record with it. Two records that resolve to one quest keep the one
        // that went further (completed or failed over active), then the earlier start.
        var quests = new SortedDictionary<string, QuestState>(StringComparer.Ordinal);
        foreach (var quest in player.Quests)
        {
            if (Resolve(quest.QuestId, "player quest") is not { } id)
                continue;
            var resolved = quest with { QuestId = id };
            if (!quests.TryGetValue(id, out var existing)
                || (existing.Status == QuestStatus.Active && resolved.Status != QuestStatus.Active)
                || (existing.Status == QuestStatus.Active) == (resolved.Status == QuestStatus.Active) && resolved.StartedTick < existing.StartedTick)
                quests[id] = resolved;
        }

        // Companions (schema 12): a companion whose NPC was removed goes with it; two records that resolve to one NPC keep the first. A
        // hireling's contract (schema 20) names a recipe and the materials they were handed: a removed recipe leaves them with no work, and
        // removed materials are lost; two that resolve to one are summed.
        var companions = new SortedDictionary<string, CompanionRecord>(StringComparer.Ordinal);
        foreach (var companion in player.Companions)
        {
            if (Resolve(companion.NpcId, "player companion") is not { } id)
                continue;
            var contract = companion.Contract;
            if (contract is not null)
            {
                string? recipe = contract.RecipeId is { } r ? Resolve(r, "player companion (contract recipe)") : null;
                var supply = ImmutableSortedDictionary.Create<string, int>(StringComparer.Ordinal);
                foreach (var (item, count) in contract.Supply)
                {
                    if (Resolve(item, "player companion (contract supply)") is { } current)
                        supply = supply.SetItem(current, (int)Math.Min(int.MaxValue, (long)supply.GetValueOrDefault(current) + count));
                }
                contract = contract with { RecipeId = recipe, WorkSite = recipe is null && contract.RecipeId is not null ? null : contract.WorkSite, Supply = supply };
            }
            companions.TryAdd(id, companion with { NpcId = id, Contract = contract });
        }

        // The party's ledger (schema 20): someone recovering or fallen whose NPC was removed goes with them; a party member is never also in it.
        var recovering = new SortedDictionary<string, RecoveryRecord>(StringComparer.Ordinal);
        foreach (var recovery in player.Party.Recovering)
        {
            if (Resolve(recovery.NpcId, "player party (recovering)") is { } id && !companions.ContainsKey(id))
                recovering.TryAdd(id, recovery with { NpcId = id });
        }
        var fallen = new SortedSet<string>(StringComparer.Ordinal);
        foreach (string npc in player.Party.Fallen)
        {
            if (Resolve(npc, "player party (fallen)") is { } id && !companions.ContainsKey(id) && !recovering.ContainsKey(id))
                fallen.Add(id);
        }

        // Deeds (schema 16): a deed whose subject or NPC was removed goes with it; two tallies that resolve to one are summed, keeping
        // the later tick.
        var deeds = new SortedDictionary<string, DeedTally>(StringComparer.Ordinal);
        foreach (var deed in player.Deeds)
        {
            if (Resolve(deed.SubjectId, $"player deed ({DeedKeys.Key(deed.Kind)})") is not { } subject)
                continue;
            string? npc = null;
            if (deed.NpcId is not null && (npc = Resolve(deed.NpcId, "player deed (delivered to)")) is null)
                continue;
            var resolved = deed with { SubjectId = subject, NpcId = npc };
            deeds[resolved.Key] = deeds.TryGetValue(resolved.Key, out var known)
                ? known with { Count = (int)Math.Min(int.MaxValue, (long)known.Count + resolved.Count), LastTick = Math.Max(known.LastTick, resolved.LastTick) }
                : resolved;
        }

        // The faction ledger (schema 20): an act whose subject was removed goes, with what any faction knew of it; a faction removed takes
        // its knowledge and standing with it; a member removed leaves the knowledge, told by no one now named. Two factions that resolve to
        // one keep the first row for an act and sum their standing, within the range.
        var ledger = player.Factions;
        var acts = new List<ActRecord>();
        foreach (var act in ledger.Acts)
        {
            if (Resolve(act.Subject, $"player act {act.Seq} ({act.Kind})") is { } subject)
                acts.Add(act with { Subject = subject });
        }
        var kept = acts.Select(a => a.Seq).ToHashSet();
        var knowledge = new SortedDictionary<(string Knower, long Act), FactionKnowledge>(
            Comparer<(string Knower, long Act)>.Create((a, b) => string.CompareOrdinal(a.Knower, b.Knower) is var c && c != 0 ? c : a.Act.CompareTo(b.Act)));
        foreach (var row in ledger.Knowledge.Where(k => kept.Contains(k.Act)))
        {
            if (Resolve(row.Knower, $"faction knowledge of act {row.Act}") is not { } knower)
                continue;
            string? via = row.Via is { } told ? Resolve(told, $"faction knowledge of act {row.Act} (told to)") : null;
            knowledge.TryAdd((knower, row.Act), row with { Knower = knower, Via = via });
        }
        var standing = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var row in ledger.Standing)
        {
            if (Resolve(row.FactionId, "player standing") is { } faction)
                standing[faction] = Math.Clamp(standing.GetValueOrDefault(faction) + row.Points, FactionLedger.MinPoints, FactionLedger.MaxPoints);
        }
        var factions = new FactionLedger(ledger.NextActSeq, acts.ToImmutableArray(), knowledge.Values.ToImmutableArray(),
            standing.Where(kv => kv.Value != 0).Select(kv => new FactionStanding(kv.Key, kv.Value)).ToImmutableArray());

        // The quick bar (schema 23): a slot whose item, formula or technique was removed is emptied, and two that resolve to one keep the
        // first; what was cleared off the bar stays cleared under its current ID, unless it is on the bar.
        var bar = player.QuickBar.ToBuilder();
        for (int slot = 0; slot < bar.Count; slot++)
        {
            if (bar[slot] is { } held && Resolve(held, $"quick slot {slot + 1}") is var id)
                bar[slot] = id is not null && bar.Take(slot).All(earlier => earlier != id) ? id : null;
        }
        var cleared = new SortedSet<string>(StringComparer.Ordinal);
        foreach (string off in player.QuickBarCleared)
        {
            if (Resolve(off, "quick bar (cleared)") is { } id && !bar.Contains(id))
                cleared.Add(id);
        }

        // The party's sheets (schema 25): a sheet whose NPC was removed goes with them, and what they held with it; a piece whose item was
        // removed goes; the progression is passed through the pass as the character's is. Two sheets that resolve to one NPC keep the first.
        var sheets = new SortedDictionary<string, PartySheet>(StringComparer.Ordinal);
        foreach (var sheet in player.PartySheets)
        {
            if (Resolve(sheet.NpcId, "player party sheet") is not { } id || sheets.ContainsKey(id))
                continue;
            InventoryEntry? Item(InventoryEntry entry) =>
                Resolve(entry.DefId, $"player party sheet {id} item {entry.ItemId}") is { } def ? entry with { DefId = def } : null;
            sheets[id] = sheet with
            {
                NpcId = id,
                Progression = sheet.Progression.RewriteDefinitionIds((ref_, role) => Resolve(ref_, $"party sheet {id} {role}")),
                Gear = sheet.Gear.Select(g => (g.Key, Item: Item(g.Value.Item), g.Value.Loaned)).Where(g => g.Item is not null)
                    .ToImmutableSortedDictionary(g => g.Key, g => new PartyGear(g.Item!, g.Loaned)),
                Belongings = sheet.Belongings.Select(Item).OfType<InventoryEntry>().ToImmutableArray(),
            };
        }

        // The attack under way (schema 27): its weapon's or technique's ID passes through the pass, and one whose source was removed is
        // dropped, so the character loads at rest. Bare hands are no definition.
        var attack = player.Attack;
        if (attack is not null && UNNAMED.Domain.DefinitionId.IsValid(attack.Source))
            attack = Resolve(attack.Source, "player attack under way") is { } source ? attack with { Source = source } : null;

        // The party ledger is set aside while the party is rewritten: a rename may bring the two together, which the record refuses.
        var rewritten = player with { Party = PartyLedger.Empty, PartySheets = ImmutableArray<PartySheet>.Empty };
        rewritten = rewritten.WithInventory(inventory).WithProgression(progression).WithDiscoveries(discoveries.Values).WithEffects(effects.Values)
            .WithSocial(relationships.Values, conversations.Values).WithQuests(quests.Values).WithCompanions(companions.Values);
        return (rewritten.WithDeeds(deeds.Values).WithFactions(factions) with
            {
                Party = new PartyLedger(recovering.Values.ToImmutableArray(), fallen.ToImmutableArray()),
                QuickBar = bar.ToImmutable(), QuickBarCleared = cleared.ToImmutableArray(),
                PartySheets = sheets.Values.ToImmutableArray(),
                Attack = attack,
            },
            new DeltaSnapshot(cells, entities.ToImmutable())
            {
                Created = created.ToImmutable(), Containers = containers, Creatures = creatures.ToImmutable(), Noises = noises.ToImmutable(),
                Economy = delta.Economy, Merchants = delta.Merchants, NpcLife = delta.NpcLife, Adventurers = delta.Adventurers,
            });
    }

    private static DeltaSnapshot ProveBaselines(
        DeltaSnapshot delta, ulong seed, string saveFingerprint, LoadContext context, MigrationReport report)
    {
        var cache = new Dictionary<CellKey, CellBaseline>();
        CellBaseline Baseline(CellKey cell) =>
            cache.TryGetValue(cell, out var b) ? b : cache[cell] = context.Generator.Generate(seed, cell);

        var proven = new SortedSet<string>(StringComparer.Ordinal);
        var mismatched = new SortedDictionary<string, string>(StringComparer.Ordinal);
        void Check(string cellKey, string? saved)
        {
            if (!CellKey.TryParse(cellKey, out var cell))
                return;   // an unparseable key is rejected, with its reason, when the world is built
            string now = Baseline(cell).Digest;
            if (saved == now)
                proven.Add(cellKey);
            else
                mismatched.TryAdd(cellKey, $"saved baseline_hash {saved ?? "(none)"}, generated now {now}");
        }
        foreach (var record in delta.Cells)
            Check(record.CellKey, record.BaselineHash);
        foreach (var record in delta.Entities)
            Check(SectionCodec.HostCell(record.SlotKey), record.BaselineHash);
        foreach (var record in delta.Created)
            Check(record.HostCell, record.BaselineHash);
        foreach (var record in delta.Containers)
            Check(record.HostCell, record.BaselineHash);
        foreach (var record in delta.Creatures)
            Check(record.HostCell, record.BaselineHash);
        proven.ExceptWith(mismatched.Keys);
        report.CellsMatched = proven.Count;
        if (mismatched.Count == 0)
            return delta;

        var transition = context.Transitions.FirstOrDefault(t =>
            t.FromFingerprint == saveFingerprint && t.ToFingerprint == context.Generator.Fingerprint);
        if (transition is null)
        {
            foreach (var (cell, detail) in mismatched)
                report.CellsMismatched.Add($"{cell}: {detail}");
            report.Blockers.Add(
                $"{mismatched.Count} changed cell(s) were saved against a baseline this build no longer generates, and no transition " +
                $"is registered from worldgen fingerprint {saveFingerprint} to {context.Generator.Fingerprint}. " +
                "Their deltas are not applied to a different baseline.");
            return delta;
        }
        return SemanticRebase.Apply(transition, mismatched.Keys.ToHashSet(StringComparer.Ordinal), delta, Baseline, report);
    }

    private static ImmutableArray<T> DecodeOrQuarantine<T>(
        byte[]? bytes, string file, Func<byte[], ImmutableArray<T>> decode, List<string> quarantined, MigrationReport report) =>
        DecodeOrQuarantine(bytes, file, decode, quarantined, report, ImmutableArray<T>.Empty);

    private static T DecodeOrQuarantine<T>(
        byte[]? bytes, string file, Func<byte[], T> decode, List<string> quarantined, MigrationReport report, T empty)
    {
        if (bytes is null)
            return empty;
        try
        {
            return decode(bytes);
        }
        catch (Exception e) when (e is MessagePackSerializationException or FormatException or ArgumentException)
        {
            // Hash-valid but undecodable (for example written by a buggy build): drop it, and say so.
            quarantined.Add(SectionName(file));
            report.Warnings.Add($"section '{SectionName(file)}' could not be decoded and is loaded without: {e.Message}");
            return empty;
        }
    }

    internal static string SectionName(string file) => Path.GetFileNameWithoutExtension(file);

    private static int RequiredInt(JsonObject manifest, string field, string saveName, ImmutableArray<int> backups) =>
        OptionalInt(manifest, field)
        ?? throw new SaveCorruptionException($"manifest.json in '{saveName}' has no valid '{field}'", backups);

    private static int? OptionalInt(JsonObject manifest, string field) =>
        manifest[field] is JsonValue value && value.TryGetValue(out int number) ? number : null;

    private static string? OptionalString(JsonObject manifest, string field) =>
        manifest[field] is JsonValue value && value.TryGetValue(out string? text) ? text : null;
}

/// <summary><c>sections.sha256</c>: build, read and verify the integrity root (§3.3, §7.1).</summary>
internal static class SaveIntegrity
{
    /// <summary>One <c>&lt;sha256&gt;  &lt;file&gt;</c> line per checked file, in canonical order (the sha256sum layout).</summary>
    public static byte[] BuildRoot(string directory)
    {
        var text = new StringBuilder();
        foreach (string file in SaveFormat.CheckedFiles)
            text.Append(HashFile(Path.Combine(directory, file))).Append("  ").Append(file).Append('\n');
        return Encoding.UTF8.GetBytes(text.ToString());
    }

    public static Dictionary<string, string>? TryReadRoot(string directory)
    {
        try
        {
            var entries = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string line in File.ReadAllLines(Path.Combine(directory, SaveFormat.IntegrityRoot)))
            {
                int gap = line.IndexOf("  ", StringComparison.Ordinal);
                if (gap != 64 || !line[..64].All(c => char.IsAsciiDigit(c) || c is >= 'a' and <= 'f'))
                    return null;
                entries[line[(gap + 2)..]] = line[..64];
            }
            return SaveFormat.CheckedFiles.All(entries.ContainsKey) ? entries : null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    /// <summary>Null when every checked file matches the integrity root; otherwise what failed.</summary>
    public static string? Verify(string directory)
    {
        var expected = TryReadRoot(directory);
        if (expected is null)
            return "integrity root missing or malformed";
        foreach (string file in SaveFormat.CheckedFiles)
        {
            if (HashFileOrNull(Path.Combine(directory, file)) != expected[file])
                return $"{file} does not match its recorded hash";
        }
        return null;
    }

    public static string RootDigest(string directory) =>
        "sha256:" + (HashFileOrNull(Path.Combine(directory, SaveFormat.IntegrityRoot)) ?? "missing");

    public static string HashFile(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

    public static string? HashFileOrNull(string path) => File.Exists(path) ? HashFile(path) : null;
}
