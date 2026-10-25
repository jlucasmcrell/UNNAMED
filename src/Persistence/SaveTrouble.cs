// UNNAMED Persistence - what went wrong with a save, in a player's words, and a report they can copy (PJ10 §6.3, §6.3.1; SYS-134, SYS-148)
// No Godot references - pure C#

using System.Text;

namespace UNNAMED.Persistence;

/// <summary>The kinds of trouble a player is told about apart, because each asks something different of them.</summary>
public enum SaveTroubleKind
{
    /// <summary>The drive is full (or the account's quota is spent): free space; the game tries again.</summary>
    DiskFull,

    /// <summary>Another program holds a save file (cloud sync, a virus scanner, a second copy of the game): wait; the game tries again.</summary>
    InUse,

    /// <summary>The game may not write to, or read, its saves folder.</summary>
    NotAllowed,

    /// <summary>A save's files do not match their recorded hashes: its backups may load.</summary>
    Damaged,

    /// <summary>A newer build of the game wrote the save; this one cannot read it.</summary>
    NewerBuild,

    /// <summary>The save is older than any this build reads.</summary>
    TooOld,

    /// <summary>The save cannot be proven compatible with this build (an unresolved ID, a changed world with no transition).</summary>
    Refused,

    /// <summary>The save is not readable as a save at all (no manifest, no player).</summary>
    Unreadable,

    Other,
}

/// <summary>
/// One save's trouble as the saves list, the title and the save indicator say it: a headline, what the player can do, and the technical
/// detail kept for the report. Never a raw exception message on its own (PJ10 §6.3.1), and never a guess presented as certain: anything not
/// recognised is <see cref="SaveTroubleKind.Other"/>, with the detail in the report.
/// </summary>
public sealed record SaveTrouble(SaveTroubleKind Kind, string Headline, string Advice, string Detail)
{
    /// <summary>Why a save could not be written. The previous save in the slot is untouched whatever happened (§7.1).</summary>
    public static SaveTrouble OfSave(Exception error)
    {
        var kind = KindOf(error);
        string detail = Details(error);
        return kind switch
        {
            SaveTroubleKind.DiskFull => new(kind, "Not saved: the disk is full.",
                "Free some space on the drive that holds the saves folder. The game tries again shortly, and your last save is untouched.", detail),
            SaveTroubleKind.InUse => new(kind, "Not saved: another program is holding a save file.",
                "Cloud sync or a virus scanner can hold a file for a moment. The game tries again shortly, and your last save is untouched.", detail),
            SaveTroubleKind.NotAllowed => new(kind, "Not saved: the game may not write to its saves folder.",
                "Check that the saves folder exists and that your account may write to it. Your last save is untouched.", detail),
            _ => new(SaveTroubleKind.Other, "Not saved: something went wrong while writing the save.",
                "Your last save is untouched. Copy the report and send it with your feedback.", detail),
        };
    }

    /// <summary>Why a save could not be loaded. A refused or damaged save is never changed by trying (§7.2, M2b §8).</summary>
    public static SaveTrouble OfLoad(Exception error)
    {
        string detail = Details(error);
        return error switch
        {
            SaveFormatMismatchException => Words(SaveTroubleKind.NewerBuild, detail),
            SaveCompatibilityException refused when refused.Report.Blockers.Any(b => b.Contains("is newer than this build", StringComparison.Ordinal))
                => Words(SaveTroubleKind.NewerBuild, detail),
            SaveCompatibilityException => Words(SaveTroubleKind.Refused, detail),
            SaveCorruptionException corrupt when corrupt.Message.Contains("integrity check", StringComparison.Ordinal)
                                               || corrupt.Message.Contains(" is corrupt", StringComparison.Ordinal) => Words(SaveTroubleKind.Damaged, detail),
            SaveCorruptionException => Words(SaveTroubleKind.Unreadable, detail),
            _ => KindOf(error) is SaveTroubleKind.Other ? Words(SaveTroubleKind.Other, detail) : Words(KindOf(error), detail),
        };
    }

    /// <summary>A save summary's <see cref="SaveSummary.Problem"/> (what the list knows without loading it), in the same words.</summary>
    public static SaveTrouble OfProblem(string problem) => Words(problem switch
    {
        _ when problem.StartsWith("it is damaged", StringComparison.Ordinal) => SaveTroubleKind.Damaged,
        _ when problem.StartsWith("it was written by a newer build", StringComparison.Ordinal) => SaveTroubleKind.NewerBuild,
        _ when problem.StartsWith("save format", StringComparison.Ordinal) => SaveTroubleKind.NewerBuild,
        _ when problem.StartsWith("schema", StringComparison.Ordinal) && problem.Contains("older", StringComparison.Ordinal) => SaveTroubleKind.TooOld,
        _ when problem.StartsWith("its manifest cannot be read", StringComparison.Ordinal) => SaveTroubleKind.Unreadable,
        _ => SaveTroubleKind.Other,
    }, problem);

    private static SaveTrouble Words(SaveTroubleKind kind, string detail) => kind switch
    {
        SaveTroubleKind.Damaged => new(kind, "This save is damaged.",
            "Its kept copies are listed under it, and one of them may load. What survives of this one can still be restored.", detail),
        SaveTroubleKind.NewerBuild => new(kind, "This save was made by a newer version of the game.",
            "Update the game to load it. It is kept exactly as it is.", detail),
        SaveTroubleKind.TooOld => new(kind, "This save is older than this version of the game can read.",
            "It is kept exactly as it is. Copy the report and send it with your feedback.", detail),
        SaveTroubleKind.Refused => new(kind, "This save cannot be loaded by this version of the game.",
            "Nothing in it was changed. Copy the report and send it with your feedback.", detail),
        SaveTroubleKind.Unreadable => new(kind, "This save cannot be read.",
            "Its kept copies are listed under it, and one of them may load.", detail),
        SaveTroubleKind.DiskFull => new(kind, "This save could not be read: the disk is full.", "Free some space and try again.", detail),
        SaveTroubleKind.InUse => new(kind, "This save could not be read: another program is holding it.", "Wait a moment and try again.", detail),
        SaveTroubleKind.NotAllowed => new(kind, "This save could not be read: the game may not open its saves folder.",
            "Check that your account may read the saves folder.", detail),
        _ => new(SaveTroubleKind.Other, "This save could not be loaded.", "Copy the report and send it with your feedback.", detail),
    };

    /// <summary>The headline and the advice together: what a callout says.</summary>
    public string Sentence => $"{Headline} {Advice}";

    /// <summary>
    /// What "Copy report" puts on the clipboard: plain text a tester pastes into their feedback. Local only: nothing is sent anywhere. It
    /// names the save and the folder, never anything outside the game's own.
    /// </summary>
    public string Report(string what, string? folder, string build)
    {
        var text = new StringBuilder();
        text.Append("Otherreach save report\n");
        text.Append("Save: ").Append(what).Append('\n');
        text.Append("Problem: ").Append(Headline).Append(" (").Append(Key(Kind)).Append(")\n");
        if (folder is not null)
            text.Append("Saves folder: ").Append(folder).Append('\n');
        text.Append("Build: ").Append(build.Length > 0 ? build : "unknown").Append('\n');
        text.Append("Detail:\n").Append(Detail).Append('\n');
        return text.ToString();
    }

    public static string Key(SaveTroubleKind kind) => kind switch
    {
        SaveTroubleKind.DiskFull => "disk_full",
        SaveTroubleKind.InUse => "in_use",
        SaveTroubleKind.NotAllowed => "not_allowed",
        SaveTroubleKind.Damaged => "damaged",
        SaveTroubleKind.NewerBuild => "newer_build",
        SaveTroubleKind.TooOld => "too_old",
        SaveTroubleKind.Refused => "refused",
        SaveTroubleKind.Unreadable => "unreadable",
        _ => "other",
    };

    // Windows HRESULT codes (low word) and POSIX errno values the runtime reports for IO it could not do.
    private const int ErrorHandleDiskFull = 0x27, ErrorDiskFull = 0x70, ErrorSharingViolation = 0x20, ErrorLockViolation = 0x21;
    private const int Enospc = 28, Edquot = 122, Ebusy = 16, Etxtbsy = 26;

    /// <summary>The trouble an exception, or any exception inside it, names - judged by error code, never by the words alone.</summary>
    public static SaveTroubleKind KindOf(Exception error)
    {
        for (Exception? e = error; e is not null; e = e.InnerException)
        {
            if (e is UnauthorizedAccessException)
                return SaveTroubleKind.NotAllowed;
            if (e is not IOException io)
                continue;
            int code = io.HResult;
            int low = code & 0xFFFF;
            bool windows = (code & 0xFFFF0000) == 0x80070000;
            if (windows ? low is ErrorDiskFull or ErrorHandleDiskFull : code is Enospc or Edquot)
                return SaveTroubleKind.DiskFull;
            if (windows ? low is ErrorSharingViolation or ErrorLockViolation : code is Ebusy or Etxtbsy)
                return SaveTroubleKind.InUse;
        }
        return SaveTroubleKind.Other;
    }

    /// <summary>Every message down the chain, one a line, with its type and code: what a tester's report needs, and nothing more.</summary>
    private static string Details(Exception error)
    {
        var lines = new StringBuilder();
        for (Exception? e = error; e is not null; e = e.InnerException)
            lines.Append(e.GetType().Name).Append(" (0x").Append(e.HResult.ToString("X8", System.Globalization.CultureInfo.InvariantCulture)).Append("): ")
                .Append(e.Message.ReplaceLineEndings(" / ")).Append('\n');
        return lines.ToString().TrimEnd('\n');
    }
}
