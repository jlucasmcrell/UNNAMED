// UNNAMED Application tests - the content half of a boot, shared by a whole collection.
// No Godot references - pure C#
//
// A boot is 36 s on this machine and 35.9 s of it is reading and validating the content pack; the world a test then starts costs under half
// a second. This fixture boots once for its collection and hands out the immutable result, so a collection of thirty tests pays the content
// read once instead of thirty times. It hands out nothing mutable: `ContentBoot` is records and frozen dictionaries, and every test still gets
// its own session, profile and world.

namespace UNNAMED.Application.Tests;

/// <summary>The one booted content a collection shares. Nothing here is per test.</summary>
public sealed class SharedContent
{
    public SharedContent()
    {
        using var profile = new TempProfile();
        var options = Options(profile.Root);
        Boot = ContentBootCache.Get(options, () => GameSession.ReadContent(options));
    }

    public ContentBoot Boot { get; }

    /// <summary>The content request every test in the collection shares: the repo's pack, the default region.</summary>
    public static GameOptions Options(string profileRoot) =>
        new(Path.Combine(Harness.RepoRoot(), "content"), profileRoot);
}

[CollectionDefinition(SharedContentCollection.Name)]
public sealed class SharedContentCollection : ICollectionFixture<SharedContent>
{
    public const string Name = "shared booted content";
}
