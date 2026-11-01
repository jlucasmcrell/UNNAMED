using System.Text;

namespace UNNAMED.Presentation.Capture;

/// <summary>Local AVI: viewport JPEGs and 16-bit stereo PCM. No codec installation or external recorder required.</summary>
public sealed class AviClipWriter : IDisposable
{
    // ponytail: AVI 1.0 has 32-bit offsets; finish at 2 GB. OpenDML is warranted only for long recordings.
    public const long MaxBytes = 2_000_000_000;
    private readonly BinaryWriter _file;
    private readonly List<(string Id, uint Offset, uint Bytes)> _index = new();
    private readonly long _totalFrames, _videoLength, _audioLength, _movi;
    private bool _closed;
    public int VideoFrames { get; private set; }
    public long AudioFrames { get; private set; }

    public AviClipWriter(Stream output, int width, int height, int fps, int mixRate)
    {
        if (!output.CanSeek || !output.CanWrite || width is < 1 or > short.MaxValue || height is < 1 or > short.MaxValue
            || fps is < 1 or > 240 || mixRate is < 8000 or > 192000)
            throw new ArgumentException("A clip needs a seekable output, valid viewport size, frame rate and audio rate.");
        _file = new BinaryWriter(output, Encoding.ASCII, leaveOpen: true);
        Tag("RIFF"); _file.Write(0u); Tag("AVI ");
        long hdrl = List("hdrl");
        Tag("avih"); _file.Write(56u);
        _file.Write((uint)Math.Round(1_000_000.0 / fps)); _file.Write(0u); _file.Write(0u); _file.Write(0x110u);
        _totalFrames = Position; _file.Write(0u); _file.Write(0u); _file.Write(2u); _file.Write(0u);
        _file.Write((uint)width); _file.Write((uint)height); Zeros(4);
        long video = List("strl");
        Tag("strh"); _file.Write(56u); Tag("vids"); Tag("MJPG");
        _videoLength = StreamHeader(1, (uint)fps, 0);
        _file.Write((short)0); _file.Write((short)0); _file.Write((short)width); _file.Write((short)height);
        Tag("strf"); _file.Write(40u); _file.Write(40u); _file.Write(width); _file.Write(height);
        _file.Write((ushort)1); _file.Write((ushort)24); Tag("MJPG");
        _file.Write(checked((uint)(width * (long)height * 3))); Zeros(4);
        EndList(video);
        long audio = List("strl");
        Tag("strh"); _file.Write(56u); Tag("auds"); _file.Write(0u);
        _audioLength = StreamHeader(4, (uint)mixRate * 4, 4); Zeros(2);
        Tag("strf"); _file.Write(16u); _file.Write((ushort)1); _file.Write((ushort)2);
        _file.Write((uint)mixRate); _file.Write((uint)mixRate * 4); _file.Write((ushort)4); _file.Write((ushort)16);
        EndList(audio); EndList(hdrl);
        _movi = List("movi");
    }

    private long Position => _file.BaseStream.Position;
    private void Tag(string tag) => _file.Write(Encoding.ASCII.GetBytes(tag));
    private void Zeros(int count) { for (int i = 0; i < count; i++) _file.Write(0u); }
    private long List(string type) { Tag("LIST"); long size = Position; _file.Write(0u); Tag(type); return size; }
    private void EndList(long size) => Patch(size, checked((uint)(Position - size - 4)));
    private void Patch(long at, uint value)
    {
        long end = Position; _file.BaseStream.Position = at; _file.Write(value); _file.BaseStream.Position = end;
    }
    private long StreamHeader(uint scale, uint rate, uint sampleSize)
    {
        _file.Write(0u); _file.Write((ushort)0); _file.Write((ushort)0); _file.Write(0u);
        _file.Write(scale); _file.Write(rate); _file.Write(0u);
        long length = Position; _file.Write(0u); _file.Write(0u); _file.Write(uint.MaxValue); _file.Write(sampleSize);
        return length;
    }

    public void Video(ReadOnlySpan<byte> jpeg)
    {
        if (jpeg.Length < 4 || jpeg[0] != 0xff || jpeg[1] != 0xd8 || jpeg[^2] != 0xff || jpeg[^1] != 0xd9)
            throw new ArgumentException("A video frame must be a complete JPEG.");
        Chunk("00dc", jpeg); VideoFrames++;
    }

    public void Audio(ReadOnlySpan<byte> pcm)
    {
        if (pcm.Length % 4 != 0) throw new ArgumentException("Stereo PCM must contain complete 16-bit sample pairs.");
        if (pcm.Length == 0) return;
        Chunk("01wb", pcm); AudioFrames += pcm.Length / 4;
    }

    private void Chunk(string id, ReadOnlySpan<byte> bytes)
    {
        ObjectDisposedException.ThrowIf(_closed, this);
        // Reserve the index and RIFF padding before writing: a size-limit stop leaves a playable prefix.
        if (Position + bytes.Length + 9 + 8 + (_index.Count + 1L) * 16 > MaxBytes)
            throw new IOException("The clip reached 2 GB; start another clip to keep recording.");
        long start = Position;
        try
        {
            Tag(id); _file.Write((uint)bytes.Length); _file.Write(bytes);
            if ((bytes.Length & 1) != 0) _file.Write((byte)0);
        }
        catch (IOException)
        {
            _file.BaseStream.SetLength(start); _file.BaseStream.Position = start;
            throw;
        }
        _index.Add((id, checked((uint)(start - _movi - 4)), (uint)bytes.Length));
    }

    public void Dispose()
    {
        if (_closed) return;
        _closed = true;
        EndList(_movi);
        Tag("idx1"); _file.Write(checked((uint)_index.Count * 16));
        foreach (var item in _index)
        {
            Tag(item.Id); _file.Write(0x10u); _file.Write(item.Offset); _file.Write(item.Bytes);
        }
        Patch(_totalFrames, (uint)VideoFrames); Patch(_videoLength, (uint)VideoFrames); Patch(_audioLength, checked((uint)AudioFrames));
        Patch(4, checked((uint)(Position - 8)));
        _file.Flush(); _file.Dispose();
    }
}
