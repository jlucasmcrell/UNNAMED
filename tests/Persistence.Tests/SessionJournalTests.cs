using System.Collections.Immutable;
using UNNAMED.Persistence;

namespace UNNAMED.Persistence.Tests;

/// <summary>
/// SYS-113: the session journal the save layout names. It is prose, so the rules are about the file, not about play - one object per line,
/// appended, and the layout's own tolerance on the way back in: a partial tail line is not corruption.
/// </summary>
public class SessionJournalTests
{
    private static string TempPath() =>
        Path.Combine(Path.GetTempPath(), "unnamed-journal-" + Guid.NewGuid().ToString("N"), SessionJournal.FileName);

    [Fact]
    public void WhatIsAppended_IsWhatIsReadBack_AndTheOrderHolds()
    {
        var path = TempPath();
        try
        {
            var first = new[] { new SessionJournalEntry(120, "arrived", "Came in out of the Ash Stand with three crows following.") };
            var second = new[] { new SessionJournalEntry(400, "fell", "The Custodian's housing turned and stopped.") };

            Assert.True(SessionJournal.Append(path, first) > 0);
            Assert.True(SessionJournal.Append(path, second) > 0);

            var read = SessionJournal.Read(path, out long partial, out var malformed);
            Assert.Equal(0, partial);
            Assert.Empty(malformed);
            Assert.Equal(first.Concat(second), read);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    [Fact]
    public void APartialTailLine_IsNotRead_AndIsNotCountedAsCorruption()
    {
        var path = TempPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        try
        {
            File.WriteAllBytes(path, SessionJournal.Encode(new[] { new SessionJournalEntry(7, "note", "short") }));

            // The session died mid-write: a line with no newline behind it.
            File.AppendAllText(path, "{\"tick\":8,\"kind\":\"note\",\"text\":\"never fi");

            var read = SessionJournal.Read(path, out long partial, out var malformed);
            Assert.Single(read);
            Assert.Equal(7, read[0].Tick);
            Assert.True(partial > 0);
            Assert.Empty(malformed);

            // §3.3's repair: the torn tail is cut off, and the journal goes on from a clean line.
            Assert.Equal(partial, SessionJournal.TruncatePartial(path));
            Assert.Equal(0, SessionJournal.TruncatePartial(path));   // nothing left to cut: the repair is idempotent

            SessionJournal.Append(path, new[] { new SessionJournalEntry(9, "note", "after the crash") });
            var healed = SessionJournal.Read(path, out long none, out var stillBad);
            Assert.Equal(0, none);
            Assert.Empty(stillBad);
            Assert.Equal(new long[] { 7, 9 }, healed.Select(e => e.Tick));
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    [Fact]
    public void ACompleteLineThatIsNotAJournalEntry_IsReported_NotDropped()
    {
        var path = TempPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        try
        {
            File.WriteAllText(path, "{\"tick\":1,\"kind\":\"note\",\"text\":\"fine\"}\nnot a journal line\n");
            var read = SessionJournal.Read(path, out long partial, out var malformed);

            Assert.Single(read);
            Assert.Equal(0, partial);
            Assert.Equal("not a journal line", Assert.Single(malformed));
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    [Fact]
    public void AnEmptyOrAbsentJournal_ReadsAsNothingRatherThanFailing()
    {
        var path = TempPath();
        var absent = SessionJournal.Read(path, out long noTail, out var noBad);
        Assert.Empty(absent);
        Assert.Equal(0, noTail);
        Assert.Empty(noBad);

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        try
        {
            File.WriteAllBytes(path, Array.Empty<byte>());
            Assert.Empty(SessionJournal.Read(path, out long zero, out _));
            Assert.Equal(0, zero);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    [Fact]
    public void TheFileIsUtf8WithoutAByteOrderMark_WithOneObjectToALine()
    {
        var bytes = SessionJournal.Encode(new[] { new SessionJournalEntry(0, "note", "a \"quoted\" line — and a dash") });
        Assert.NotEqual(0xEF, bytes[0]);
        Assert.Equal((byte)'\n', bytes[^1]);

        string text = System.Text.Encoding.UTF8.GetString(bytes);
        Assert.Single(text.TrimEnd('\n').Split('\n'));
        Assert.Contains("\\\"quoted\\\"", text);
    }

    [Fact]
    public void TheWriter_LeavesAFileTheReaderCanRead_AfterManyAppends()
    {
        var path = TempPath();
        try
        {
            for (int i = 0; i < 25; i++)
                SessionJournal.Append(path, new[] { new SessionJournalEntry(i * 20, "note", $"step {i}") });

            var read = SessionJournal.Read(path, out long partial, out var malformed);
            Assert.Equal(25, read.Length);
            Assert.Equal(0, partial);
            Assert.Empty(malformed);
            Assert.Equal(Enumerable.Range(0, 25).Select(i => (long)(i * 20)), read.Select(e => e.Tick));
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }
}
