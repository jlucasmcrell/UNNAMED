using UNNAMED.Content;

namespace UNNAMED.Content.Tests;

/// <summary>
/// The game's content, loaded once for the whole suite.
/// <para>
/// A full load of content/ costs about a minute, and the read-only tests ask the same questions of the same tree - so they ask one
/// loader. Tests that need a <em>changed</em> tree build their own (see <c>EditedContent</c>); this is only for reading.
/// </para>
/// </summary>
internal static class GameContentFixture
{
    private static readonly Lazy<ContentLoader> Shared = new(() =>
    {
        var loader = new ContentLoader();
        if (!loader.LoadAll(Path.Combine(RepoPaths.Root(), "content")))
            throw new InvalidOperationException("the game's content does not load:\n" + string.Join("\n", loader.Errors));
        return loader;
    });

    public static ContentLoader Of() => Shared.Value;
}
