using System.Buffers.Binary;
using System.Threading.Channels;
using Godot;

namespace UNNAMED.Presentation.Capture;

/// <summary>Real-time viewport + Master bus capture. Only readbacks run on the game thread; JPEG and disk work have a bounded queue.</summary>
public sealed partial class ClipRecorder : IDisposable
{
    public const int Fps = 30;
    private sealed record Packet(Image? Image, Vector2[] Audio, float Gain, int Frames);
    private readonly Channel<Packet> _queue = Channel.CreateBounded<Packet>(new BoundedChannelOptions(8)
        { SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.Wait });
    private readonly Viewport _viewport;
    private readonly AudioEffectCapture _audio = new() { BufferLength = 2 };
    private readonly int _bus, _width, _height, _mixRate;
    private readonly FileStream _output;
    private readonly Task<Result> _writing;
    private readonly ulong _started = Time.GetTicksUsec();
    private int _queuedFrames;
    private string? _problem;
    public string Path { get; }
    public bool Recording { get; private set; }
    public double Seconds => (Time.GetTicksUsec() - _started) / 1_000_000.0;
    public Result? Finished => Result.Poll(_writing, Path);

    public ClipRecorder(Viewport viewport, string directory)
    {
        _viewport = viewport;
        // Canvas stretch can keep a 1920-wide logical UI on a larger window. Record the actual F12 texture pixels.
        _width = viewport.GetTexture().GetWidth(); _height = viewport.GetTexture().GetHeight();
        _mixRate = (int)AudioServer.GetMixRate();
        _bus = AudioServer.GetBusIndex("Master");
        if (_bus < 0) throw new InvalidOperationException("The Master audio bus is missing.");
        Directory.CreateDirectory(directory);
        Path = System.IO.Path.Combine(directory, $"otherreach_clip_{DateTime.Now:yyyyMMdd_HHmmss_fff}.avi");
        _output = new FileStream(Path, FileMode.CreateNew, System.IO.FileAccess.ReadWrite, FileShare.Read);
        AudioServer.AddBusEffect(_bus, _audio);
        Recording = true;
        _writing = Task.Run(Write);
        RenderingServer.FramePostDraw += Capture;
    }

    private Vector2[] DrainAudio()
    {
        int count = _audio.GetFramesAvailable();
        return count > 0 ? _audio.GetBuffer(count) : [];
    }
    private int TargetFrames => Math.Max(1, checked((int)Math.Ceiling(Seconds * Fps)));
    // Bus effects run before the bus fader. Preserve the player's Master volume/mute in the local clip too.
    private float Gain => AudioServer.IsBusMute(_bus) ? 0 : Mathf.DbToLinear(AudioServer.GetBusVolumeDb(_bus));

    private void Capture()
    {
        if (!Recording) return;
        Image? image = null;
        try
        {
            if (_audio.GetDiscardedFrames() != 0) { Stop("Audio capture could not keep up."); return; }
            int target = TargetFrames;
            if (target > _queuedFrames)
            {
                image = _viewport.GetTexture().GetImage();
                if (image.GetWidth() != _width || image.GetHeight() != _height)
                {
                    image.Dispose(); image = null;
                    Stop("The window size changed; start another clip to keep recording.");
                    return;
                }
            }
            var packet = new Packet(image, DrainAudio(), Gain, target);
            if (!_queue.Writer.TryWrite(packet))
            {
                image?.Dispose(); image = null;
                Stop("The recording could not keep up with the game.");
                return;
            }
            image = null; // The worker owns and releases it now.
            _queuedFrames = target;
        }
        catch (Exception e)
        {
            image?.Dispose();
            Stop(e.Message);
        }
    }

    public void Stop(string? problem = null)
    {
        if (!Recording) return;
        Recording = false;
        _problem = problem;
        RenderingServer.FramePostDraw -= Capture;
        for (int i = AudioServer.GetBusEffectCount(_bus) - 1; i >= 0; i--)
            if (AudioServer.GetBusEffect(_bus, i).GetInstanceId() == _audio.GetInstanceId())
            {
                AudioServer.SetBusEffectEnabled(_bus, i, false);
                if (!_queue.Writer.TryWrite(new Packet(null, DrainAudio(), Gain, TargetFrames)))
                    _problem ??= "The recording stopped before its final audio block could be saved.";
                AudioServer.RemoveBusEffect(_bus, i);
                break;
            }
        _audio.Dispose();
        _queue.Writer.TryComplete();
    }

    private async Task<Result> Write()
    {
        AviClipWriter? movie = null;
        string? problem = null;
        bool finalised = false;
        try
        {
            movie = new AviClipWriter(_output, _width, _height, Fps, _mixRate);
            byte[]? previous = null;
            await foreach (var packet in _queue.Reader.ReadAllAsync())
            {
                byte[]? jpeg;
                try { jpeg = packet.Image?.SaveJpgToBuffer(.95f); }
                finally { packet.Image?.Dispose(); }
                // Keep wall-clock duration on a slow frame, rather than speeding up the clip and desynchronising its sound.
                while (movie.VideoFrames < packet.Frames && (previous is not null || jpeg is not null))
                    movie.Video(movie.VideoFrames + 1 < packet.Frames ? previous ?? jpeg! : jpeg ?? previous!);
                previous = jpeg ?? previous;
                var pcm = new byte[packet.Audio.Length * 4];
                for (int i = 0; i < packet.Audio.Length; i++)
                {
                    BinaryPrimitives.WriteInt16LittleEndian(pcm.AsSpan(i * 4), Sample(packet.Audio[i].X * packet.Gain));
                    BinaryPrimitives.WriteInt16LittleEndian(pcm.AsSpan(i * 4 + 2), Sample(packet.Audio[i].Y * packet.Gain));
                }
                movie.Audio(pcm);
            }
        }
        catch (Exception e) { problem = e.Message; }
        finally
        {
            _queue.Writer.TryComplete();
            while (_queue.Reader.TryRead(out var abandoned)) abandoned.Image?.Dispose();
            try { movie?.Dispose(); finalised = movie is not null; }
            catch (Exception e) { problem = e.Message; }
            try { _output.Dispose(); }
            catch (IOException e) { problem = e.Message; finalised = false; }
        }
        return new Result(Path, movie?.VideoFrames ?? 0, movie?.AudioFrames ?? 0, finalised, problem ?? _problem);
    }

    private static short Sample(float value) => (short)Math.Round(Math.Clamp(value, -1f, 1f) * short.MaxValue);

    public void Dispose()
    {
        Stop();
        try
        {
            if (!_writing.Wait(TimeSpan.FromSeconds(10)))
                GD.PushWarning($"Clip is still finishing after 10 seconds: {Path}");
            else
                GD.Print($"CLIP {System.Text.Json.JsonSerializer.Serialize(_writing.Result)}");
        }
        catch (Exception e)
        {
            // A recording failure must not interrupt the game's normal save/quit path.
            GD.PushWarning($"Clip could not finish: {Path}: {e.GetBaseException().Message}");
        }
    }
}
