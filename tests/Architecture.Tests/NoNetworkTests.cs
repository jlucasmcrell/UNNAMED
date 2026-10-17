using System.Text.RegularExpressions;

namespace UNNAMED.Architecture.Tests;

/// <summary>
/// The owner's rule (2026-10-17, restated for WP10's crash hooks): the game makes no network requests - no upload, telemetry or update
/// check, ever. No source under <c>src/</c>, the Godot add-ons included, may name a .NET or Godot networking type. A crash is written
/// to a local file and said at the next launch, nothing more.
/// </summary>
public class NoNetworkTests
{
    private static readonly (string Pattern, string What)[] Forbidden =
    {
        (@"\bHttpClient\b", "an HTTP client"),
        (@"\bHttpRequest\b", "a Godot HTTP request"),
        (@"\bHttpWebRequest\b|\bWebRequest\b|\bWebClient\b", "a web request"),
        (@"\bSystem\.Net\.(Sockets|Http|WebSockets|Mail)\b", "a networking namespace"),
        (@"\bTcpClient\b|\bTcpListener\b|\bUdpClient\b|\bSocket\b", "a socket"),
        (@"\bStreamPeerTcp\b|\bPacketPeerUdp\b|\bTcpServer\b|\bUdpServer\b|\bWebSocketPeer\b|\bENetMultiplayerPeer\b|\bWebRtc\w*\b", "a Godot peer"),
        (@"\bMultiplayerApi\b|\bMultiplayerPeer\b", "Godot multiplayer"),
        (@"\bHTTPRequest\b|\bHTTPClient\b|\bStreamPeerTCP\b|\bPacketPeerUDP\b|\bTCPServer\b|\bUDPServer\b|\bWebSocketPeer\b|\bENetMultiplayerPeer\b|\bWebRTC\w*\b", "a Godot networking class (GDScript)"),
    };

    [Fact]
    public void NoSource_SpeaksToTheNetwork()
    {
        string src = Path.Combine(RepositoryRoot(), "src");
        string sep = Path.DirectorySeparatorChar.ToString();
        var offenders = new List<string>();
        foreach (string file in Directory.EnumerateFiles(src, "*.*", SearchOption.AllDirectories)
                     .Where(f => f.EndsWith(".cs", StringComparison.Ordinal) || f.EndsWith(".gd", StringComparison.Ordinal))
                     .Where(f => !f.Contains($"{sep}obj{sep}", StringComparison.Ordinal) && !f.Contains($"{sep}bin{sep}", StringComparison.Ordinal)
                                 && !f.Contains($"{sep}.godot{sep}", StringComparison.Ordinal)))
        {
            string[] lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                string code = lines[i].Split("//")[0];
                if (code.TrimStart().StartsWith('#'))
                    continue;
                foreach (var (pattern, what) in Forbidden.Where(f => Regex.IsMatch(code, f.Pattern)))
                    offenders.Add($"{Path.GetRelativePath(src, file)}:{i + 1}: {what}: {lines[i].Trim()}");
            }
        }
        Assert.True(offenders.Count == 0, "The game must make no network requests:\n" + string.Join("\n", offenders));
    }

    private static string RepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "src", "UNNAMED.sln")))
                return dir.FullName;
        }
        throw new DirectoryNotFoundException("Repository root not found");
    }
}
