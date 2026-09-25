// UNNAMED Persistence - one game at a time on a save profile (the Phase-1 technical audit, M-07)
// No Godot references - pure C#

namespace UNNAMED.Persistence;

/// <summary>A second copy of the game asked for a profile that one already has open.</summary>
public sealed class ProfileInUseException : SaveException
{
    public ProfileInUseException(string profileRoot, Exception inner)
        : base($"Another copy of the game is using the saves in {profileRoot}. Close it first.", inner) => ProfileRoot = profileRoot;

    public string ProfileRoot { get; }
}

/// <summary>
/// Holds <c>&lt;profile&gt;/.lock</c> open, unshared, for as long as a game runs on the profile. A second process cannot open it, so it
/// refuses before its boot sweep runs - the sweep would otherwise discard the first game's staging directory mid-commit, and that
/// commit would fail. The operating system lets go when the process ends, however it ends; the file itself is harmless left behind.
/// </summary>
public sealed class ProfileLock : IDisposable
{
    public const string FileName = ".lock";

    private readonly FileStream _file;

    private ProfileLock(FileStream file) => _file = file;

    /// <summary>Take the profile, or throw <see cref="ProfileInUseException"/> when another game holds it.</summary>
    public static ProfileLock Acquire(string profileRoot)
    {
        string root = Path.GetFullPath(profileRoot);
        Directory.CreateDirectory(root);
        try
        {
            var file = new FileStream(Path.Combine(root, FileName), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            file.SetLength(0);
            file.Write(System.Text.Encoding.UTF8.GetBytes(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            file.Flush();
            return new ProfileLock(file);
        }
        catch (IOException e) when (IsHeldElsewhere(e))
        {
            throw new ProfileInUseException(root, e);
        }
    }

    // Windows: ERROR_SHARING_VIOLATION or ERROR_LOCK_VIOLATION. Elsewhere .NET keeps FileShare.None with flock, and a lock already held
    // fails with EWOULDBLOCK, raised with the raw errno as the HResult: 11 on Linux, 35 on macOS.
    private static bool IsHeldElsewhere(IOException e) =>
        OperatingSystem.IsWindows() ? (e.HResult & 0xFFFF) is 32 or 33 : e.HResult == (OperatingSystem.IsLinux() ? 11 : 35);

    public void Dispose() => _file.Dispose();
}
