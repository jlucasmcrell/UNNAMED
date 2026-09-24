// UNNAMED Persistence - where saves live (PERSISTENCE.md §3.2, RK-P06)
// No Godot references - pure C#

using System.Text.Json;

namespace UNNAMED.Persistence;

/// <summary>
/// Where saves live. §3.2: the save root "must be a location excluded from known cloud-sync roots
/// (OneDrive/Dropbox), or the game must detect and warn": a sync engine that holds, hydrates or
/// resurrects files defeats the commit protocol (RK-P06).
/// </summary>
public static class SaveLocation
{
    /// <summary>
    /// <c>&lt;local app data&gt;/&lt;gameFolder&gt;/saves/&lt;profile&gt;</c>. Local, not roaming, app data:
    /// OneDrive's folder redirection covers Desktop, Documents and Pictures, not AppData.
    /// </summary>
    public static string ProfileRoot(string gameFolder, string profile) =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), gameFolder, "saves", profile);

    /// <summary>The known cloud-sync root that contains <paramref name="path"/>, or null.</summary>
    public static string? CloudSyncRootOf(string path) => CloudSyncRootOf(path, KnownCloudSyncRoots());

    public static string? CloudSyncRootOf(string path, IEnumerable<string> syncRoots)
    {
        var comparison = OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        string full = WithSeparator(Path.GetFullPath(path));
        return syncRoots.FirstOrDefault(root => full.StartsWith(WithSeparator(Path.GetFullPath(root)), comparison));
    }

    /// <summary>OneDrive roots from the variables its client sets; Dropbox roots from its info.json.</summary>
    public static IReadOnlyList<string> KnownCloudSyncRoots()
    {
        var roots = new List<string>();
        foreach (string variable in new[] { "OneDrive", "OneDriveConsumer", "OneDriveCommercial" })
        {
            string? value = Environment.GetEnvironmentVariable(variable);
            if (!string.IsNullOrWhiteSpace(value))
                roots.Add(value);
        }
        foreach (var folder in new[] { Environment.SpecialFolder.ApplicationData, Environment.SpecialFolder.LocalApplicationData })
        {
            try
            {
                using var info = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(Environment.GetFolderPath(folder), "Dropbox", "info.json")));
                foreach (var account in info.RootElement.EnumerateObject())
                {
                    if (account.Value.ValueKind == JsonValueKind.Object
                        && account.Value.TryGetProperty("path", out var dropbox)
                        && dropbox.ValueKind == JsonValueKind.String
                        && !string.IsNullOrWhiteSpace(dropbox.GetString()))
                        roots.Add(dropbox.GetString()!);
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
            {
                // No Dropbox, or an unreadable info.json: nothing known to warn about.
            }
        }
        return roots.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string WithSeparator(string path) =>
        Path.EndsInDirectorySeparator(path) ? path : path + Path.DirectorySeparatorChar;
}
