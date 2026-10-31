// UNNAMED Application - the content half of a boot, built once per run and shared read-only.
// No Godot references - pure C#
//
// A boot is two halves: reading and validating the content pack and turning it into the immutable setups every rule reads, and then starting
// a world on those setups. The first half is the expensive one - 19.5 s of the 22 s content read is validation, on a warm disk - and it is the
// same answer for every boot with the same options. The second half is per world and must never be shared.
//
// So the first half is memoised here, keyed by everything that can change it. What is cached is immutable: records, frozen dictionaries and a
// deterministic generator. Nothing in it is mutated by a session, which is what makes sharing it safe - the test that proves that boots two
// sessions and mutates one.

using System.Collections.Concurrent;
using System.Collections.Immutable;
using UNNAMED.Domain.Guidance;
using UNNAMED.Persistence;
using UNNAMED.World;
using UNNAMED.World.Runtime;

namespace UNNAMED.Application;

/// <summary>The immutable product of reading a content pack: everything a session needs that is not the session's own state.</summary>
public sealed record ContentBoot(
    SimulationSetup Setup,
    ContentIdentity Content,
    ICellBaselineGenerator Generator,
    ImmutableArray<BaselineTransition> Transitions,
    IReadOnlyDictionary<string, string> DisplayNames,
    double ClockAtWorldStartHours,
    ImmutableSortedDictionary<string, HintDefinition> Hints);

/// <summary>
/// The per-run cache of <see cref="ContentBoot"/>. One entry per distinct boot request; concurrent asks for the same one build it once.
/// </summary>
public static class ContentBootCache
{
    private readonly record struct Key(string ContentRoot, string ContentVersion, string? RegionId, bool Barrowworks, bool Pets);

    private static readonly ConcurrentDictionary<Key, Lazy<ContentBoot>> Cache = new();

    /// <summary>The number of content packs built in this process: one per distinct request, no matter how many boots asked.</summary>
    public static int Built => Cache.Values.Count(v => v.IsValueCreated);

    /// <summary>The number of boots this process served from the cache instead of reading the pack again.</summary>
    private static long _served;

    public static long Served => Interlocked.Read(ref _served);

    /// <summary>
    /// The content half of a boot. <paramref name="build"/> runs at most once per key for the life of the process, and never concurrently for
    /// the same key.
    /// </summary>
    public static ContentBoot Get(GameOptions options, Func<ContentBoot> build)
    {
        var key = new Key(
            Path.GetFullPath(options.ContentRoot),
            options.ContentVersion ?? "",
            options.RegionId,
            options.Barrowworks,
            options.EnablePets);

        if (Cache.TryGetValue(key, out var existing))
        {
            Interlocked.Increment(ref _served);
            return existing.Value;
        }

        var created = Cache.GetOrAdd(key, _ => new Lazy<ContentBoot>(build, LazyThreadSafetyMode.ExecutionAndPublication));
        if (created.IsValueCreated && !ReferenceEquals(created, existing))
            Interlocked.Increment(ref _served);
        return created.Value;
    }

    /// <summary>Forget everything built: a test that changes the content on disk, and nothing else, needs this.</summary>
    public static void Clear() => Cache.Clear();
}
