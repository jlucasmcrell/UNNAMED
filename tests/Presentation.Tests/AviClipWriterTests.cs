using System.Buffers.Binary;
using System.Text;
using UNNAMED.Presentation.Capture;

namespace UNNAMED.Presentation.Tests;

public class AviClipWriterTests
{
    // Odd-length marker payload checks RIFF padding; real codec/decode is covered by the native capture proof.
    private static readonly byte[] Jpeg = [0xff, 0xd8, 0, 0xff, 0xd9];

    [Fact]
    public void InterleavedClipHasPlayableStreamHeadersExactPayloadsAndSeekableIndex()
    {
        using var output = new MemoryStream();
        using (var clip = new AviClipWriter(output, 1920, 1080, 30, 48000))
        {
            clip.Video(Jpeg); clip.Audio([1, 2, 3, 4]); clip.Video(Jpeg);
            Assert.Equal(2, clip.VideoFrames); Assert.Equal(1, clip.AudioFrames);
        }
        byte[] file = output.ToArray();
        Assert.Equal("RIFF", Text(file, 0)); Assert.Equal(file.Length - 8, (int)U32(file, 4)); Assert.Equal("AVI ", Text(file, 8));
        var chunks = Chunks(file, 12, file.Length).ToArray();
        int avih = chunks.Single(c => Text(file, c) == "avih") + 8;
        Assert.Equal(33333u, U32(file, avih)); Assert.Equal(2u, U32(file, avih + 16)); Assert.Equal(2u, U32(file, avih + 24));
        Assert.Equal(1920u, U32(file, avih + 32)); Assert.Equal(1080u, U32(file, avih + 36));
        var streams = chunks.Where(c => Text(file, c) == "strh").Select(c => c + 8).ToArray();
        Assert.Equal("vids", Text(file, streams[0])); Assert.Equal("MJPG", Text(file, streams[0] + 4));
        Assert.Equal(1u, U32(file, streams[0] + 20)); Assert.Equal(30u, U32(file, streams[0] + 24)); Assert.Equal(2u, U32(file, streams[0] + 32));
        Assert.Equal("auds", Text(file, streams[1])); Assert.Equal(4u, U32(file, streams[1] + 20));
        Assert.Equal(192000u, U32(file, streams[1] + 24)); Assert.Equal(1u, U32(file, streams[1] + 32));
        int movi = chunks.Single(c => Text(file, c) == "LIST" && Text(file, c + 8) == "movi") + 8;
        int index = chunks.Single(c => Text(file, c) == "idx1");
        Assert.Equal(48u, U32(file, index + 4));
        for (int i = 0; i < 3; i++)
        {
            int entry = index + 8 + i * 16, chunk = movi + (int)U32(file, entry + 8);
            Assert.Equal(Text(file, entry), Text(file, chunk));
            Assert.Equal(U32(file, entry + 12), U32(file, chunk + 4));
            Assert.Equal(i == 1 ? new byte[] { 1, 2, 3, 4 } : Jpeg, file.AsSpan(chunk + 8, (int)U32(file, chunk + 4)).ToArray());
        }
    }

    [Fact]
    public void InvalidSamplesDoNotCountAndFinishingTwiceDoesNotChangeTheFile()
    {
        using var output = new MemoryStream();
        var clip = new AviClipWriter(output, 16, 16, 60, 48000);
        Assert.Throws<ArgumentException>(() => clip.Video([0xff, 0xd8]));
        Assert.Throws<ArgumentException>(() => clip.Audio([1, 2, 3]));
        clip.Video(Jpeg); clip.Dispose();
        byte[] first = output.ToArray(); clip.Dispose(); Assert.Equal(first, output.ToArray());
        Assert.Throws<ObjectDisposedException>(() => clip.Video(Jpeg));
    }

    private static string Text(byte[] file, int at) => Encoding.ASCII.GetString(file, at, 4);
    [Fact]
    public void APartialDiskWriteLeavesOnlyCompletedFramesInTheFinishedClip()
    {
        using var broken = new FailOnceStream();
        using (var clip = new AviClipWriter(broken, 16, 16, 30, 48000))
        {
            clip.Video(Jpeg); broken.FailNext = true;
            Assert.Throws<IOException>(() => clip.Video(Jpeg));
            Assert.Equal(1, clip.VideoFrames);
            clip.Video(Jpeg);
        }
        using var reference = new MemoryStream();
        using (var clip = new AviClipWriter(reference, 16, 16, 30, 48000))
        { clip.Video(Jpeg); clip.Video(Jpeg); }
        Assert.Equal(reference.ToArray(), broken.ToArray());
    }

    private sealed class FailOnceStream : MemoryStream
    {
        public bool FailNext;
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            if (FailNext)
            {
                FailNext = false; base.Write(buffer[..Math.Min(2, buffer.Length)]);
                throw new IOException("Test disk full");
            }
            base.Write(buffer);
        }
    }

    private static uint U32(byte[] file, int at) => BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(at, 4));
    private static IEnumerable<int> Chunks(byte[] file, int from, int end)
    {
        for (int at = from; at + 8 <= end;)
        {
            int size = checked((int)U32(file, at + 4));
            Assert.InRange(at + 8 + size, at + 8, end);
            yield return at;
            if (Text(file, at) == "LIST" && Text(file, at + 8) != "movi")
                foreach (int child in Chunks(file, at + 12, at + 8 + size)) yield return child;
            at += 8 + size + (size & 1);
        }
    }
}
