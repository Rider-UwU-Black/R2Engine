using System.Numerics;
using Silk.NET.OpenAL;

using R2Engine.Runtime.Platform;
using R2Engine.Runtime.Audio;

namespace R2Engine.Editor.Scene;

public sealed unsafe class AudioSystem : IRuntimeAudioSystem
{
    private readonly ALContext _alc;
    private readonly Device* _device;
    private readonly Context* _context;
    private readonly AL _al;
    private readonly Dictionary<string, uint> _buffers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, uint[]> _variantBuffers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _lastVariant = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<uint> _oneShots = new();
    private readonly Dictionary<AudioSource, RuntimeSourceState> _sources = new();
    private Vector3 _listenerPosition;
    private bool _hasListener;
    public AudioSystem()
    {
        _alc = ALContext.GetApi(soft: true);
        _device = _alc.OpenDevice("");
        if (_device == null)
            throw new InvalidOperationException("No OpenAL audio device is available.");
        _context = _alc.CreateContext(_device, null);
        if (_context == null || !_alc.MakeContextCurrent(_context))
        {
            if (_context != null)
                _alc.DestroyContext(_context);
            _alc.CloseDevice(_device);
            throw new InvalidOperationException("OpenAL could not create an audio context.");
        }
        _al = AL.GetApi();
        _al.DistanceModel(DistanceModel.InverseDistanceClamped);
    }

    public void Update(Scene scene)
    {
        foreach (AudioSource removed in _sources.Keys.Where(source => source.GameObject.Scene != scene).ToArray())
            Release(removed);

        GameObject? cameraObject = scene.GameObjects.FirstOrDefault(item =>
            item.IsActiveInHierarchy && item.GetComponent<Camera>()?.IsPrimary == true);
        GameObject? listener = cameraObject?.GetComponent<Camera>()?.AudioListenerOverride;
        if (listener?.IsActiveInHierarchy != true)
            listener = cameraObject;
        if (listener != null)
        {
            Vector3 position = listener.Transform.WorldPosition;
            _listenerPosition = position;
            _hasListener = true;
            _al.SetListenerProperty(ListenerVector3.Position, position.X, position.Y, position.Z);
        }
        else
        {
            _hasListener = false;
        }

        foreach (AudioSource source in scene.GameObjects
                     .Where(item => item.IsActiveInHierarchy)
                     .SelectMany(item => item.Components).OfType<AudioSource>())
        {
            EnsureSource(source);
            ApplyProperties(source);
            RuntimeSourceState state = StateFor(source);
            if (!state.StartHandled)
            {
                state.StartHandled = true;
                if (source.PlayOnStart)
                    Play(source);
            }
        }

        for (int index = _oneShots.Count - 1; index >= 0; index--)
        {
            _al.GetSourceProperty(_oneShots[index], GetSourceInteger.SourceState, out int state);
            if (state != (int)SourceState.Playing)
            {
                _al.DeleteSource(_oneShots[index]);
                _oneShots.RemoveAt(index);
            }
        }
    }

    public void Play(AudioSource source)
    {
        EnsureSource(source);
        ApplyProperties(source);
        uint handle = StateFor(source).Handle;
        if (handle != 0)
            _al.SourcePlay(handle);
    }

    public void PlayOneShot(AudioSource source)
    {
        uint buffer = GetBuffer(source.ClipPath, source.Spatial);
        if (buffer == 0)
            return;
        uint oneShot = _al.GenSource();
        _al.SetSourceProperty(oneShot, SourceInteger.Buffer, buffer);
        ApplyProperties(source, oneShot, looping: false);
        _al.SourcePlay(oneShot);
        _oneShots.Add(oneShot);
    }

    public void PlayUiSound(string clipPath, bool splitVariants = true)
    {
        if (string.IsNullOrWhiteSpace(clipPath)) return;
        string absolutePath = AssetDatabase.ToAbsolutePath(clipPath);
        try
        {
            if (!_variantBuffers.TryGetValue(absolutePath, out uint[]? buffers))
            {
                WavData wav = WavData.Load(absolutePath);
                WavData[] variants = splitVariants ? wav.SplitOnSilence() : new[] { wav };
                buffers = variants.Select(variant =>
                {
                    uint buffer = _al.GenBuffer();
                    _al.BufferData(buffer, variant.Format, variant.Data, variant.SampleRate);
                    return buffer;
                }).ToArray();
                _variantBuffers[absolutePath] = buffers;
            }
            if (buffers.Length == 0) return;
            int previous = _lastVariant.GetValueOrDefault(absolutePath, -1);
            int choice = buffers.Length == 1 ? 0 : Random.Shared.Next(buffers.Length - 1);
            if (choice >= previous && previous >= 0) choice++;
            _lastVariant[absolutePath] = choice;
            uint oneShot = _al.GenSource();
            _al.SetSourceProperty(oneShot, SourceInteger.Buffer, buffers[choice]);
            _al.SetSourceProperty(oneShot, SourceBoolean.SourceRelative, true);
            _al.SetSourceProperty(oneShot, SourceFloat.Gain, 1.0f);
            _al.SourcePlay(oneShot);
            _oneShots.Add(oneShot);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"UI audio '{clipPath}' could not be played: {exception.GetBaseException().Message}");
        }
    }

    public void Pause(AudioSource source)
    {
        if (_sources.TryGetValue(source, out RuntimeSourceState? state) && state.Handle != 0)
            _al.SourcePause(state.Handle);
    }

    public void Stop(AudioSource source)
    {
        if (_sources.TryGetValue(source, out RuntimeSourceState? state) && state.Handle != 0)
            _al.SourceStop(state.Handle);
    }

    private void EnsureSource(AudioSource source)
    {
        RuntimeSourceState state = StateFor(source);
        if (state.Handle == 0)
        {
            state.Handle = _al.GenSource();
        }

        string runtimeKey = $"{source.ClipPath}|spatial={source.Spatial}";
        if (string.Equals(state.ClipKey, runtimeKey, StringComparison.OrdinalIgnoreCase))
            return;

        uint buffer = GetBuffer(source.ClipPath, source.Spatial);
        _al.SourceStop(state.Handle);
        _al.SetSourceProperty(state.Handle, SourceInteger.Buffer, buffer);
        state.ClipKey = runtimeKey;
    }

    private void ApplyProperties(AudioSource source) =>
        ApplyProperties(source, StateFor(source).Handle, source.Loop);

    private void ApplyProperties(AudioSource source, uint handle, bool looping)
    {
        if (handle == 0)
            return;
        float gain = Math.Clamp(source.Volume, 0.0f, 1.0f);
        if (source.Spatial && _hasListener)
        {
            float minimum = Math.Max(0.01f, source.MinDistance);
            float maximum = Math.Max(minimum + 0.01f, source.MaxDistance);
            float distance = Vector3.Distance(source.GameObject.Transform.WorldPosition, _listenerPosition);
            gain *= 1.0f - Math.Clamp((distance - minimum) / (maximum - minimum), 0.0f, 1.0f);
        }
        _al.SetSourceProperty(handle, SourceFloat.Gain, gain);
        _al.SetSourceProperty(handle, SourceFloat.Pitch, Math.Clamp(source.Pitch, 0.1f, 3.0f));
        _al.SetSourceProperty(handle, SourceBoolean.Looping, looping);
        _al.SetSourceProperty(handle, SourceBoolean.SourceRelative, !source.Spatial);
        Vector3 position = source.Spatial ? source.GameObject.Transform.WorldPosition : Vector3.Zero;
        _al.SetSourceProperty(handle, SourceVector3.Position, position.X, position.Y, position.Z);
        _al.SetSourceProperty(handle, SourceFloat.ReferenceDistance, Math.Max(0.01f, source.MinDistance));
        _al.SetSourceProperty(handle, SourceFloat.MaxDistance, Math.Max(source.MinDistance, source.MaxDistance));
        _al.SetSourceProperty(handle, SourceFloat.RolloffFactor, 0.0f);
    }

    private uint GetBuffer(string clipPath, bool spatial)
    {
        if (string.IsNullOrWhiteSpace(clipPath))
            return 0;
        string absolutePath = AssetDatabase.ToAbsolutePath(clipPath);
        string cacheKey = $"{absolutePath}|spatial={spatial}";
        if (_buffers.TryGetValue(cacheKey, out uint cached))
            return cached;

        try
        {
            WavData wav = WavData.Load(absolutePath);
            if (spatial && wav.Channels == 2)
                wav = wav.ToMono();
            uint buffer = _al.GenBuffer();
            _al.BufferData(buffer, wav.Format, wav.Data, wav.SampleRate);
            _buffers[cacheKey] = buffer;
            return buffer;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Audio clip '{clipPath}' could not be played: {exception.GetBaseException().Message}");
            _buffers[cacheKey] = 0;
            return 0;
        }
    }

    public void Release(AudioSource source)
    {
        if (_sources.TryGetValue(source, out RuntimeSourceState? state) && state.Handle != 0)
        {
            _al.SourceStop(state.Handle);
            _al.DeleteSource(state.Handle);
        }
        _sources.Remove(source);
    }

    public void Dispose()
    {
        foreach (uint source in _oneShots)
            _al.DeleteSource(source);
        foreach (AudioSource source in _sources.Keys.ToArray())
            Release(source);
        foreach (uint buffer in _buffers.Values)
            _al.DeleteBuffer(buffer);
        foreach (uint buffer in _variantBuffers.Values.SelectMany(value => value))
            _al.DeleteBuffer(buffer);
        _buffers.Clear();
        _variantBuffers.Clear();
        _oneShots.Clear();
        _al.Dispose();
        _alc.MakeContextCurrent(null);
        _alc.DestroyContext(_context);
        _alc.CloseDevice(_device);
        _alc.Dispose();
    }

    private RuntimeSourceState StateFor(AudioSource source)
    {
        if (!_sources.TryGetValue(source, out RuntimeSourceState? state))
        {
            state = new RuntimeSourceState();
            _sources.Add(source, state);
        }
        return state;
    }

    private sealed class RuntimeSourceState
    {
        public uint Handle;
        public string ClipKey = "";
        public bool StartHandled;
    }

    private sealed record WavData(byte[] Data, int SampleRate, BufferFormat Format, short Channels, short Bits)
    {
        public static WavData Load(string path)
        {
            using BinaryReader reader = new(RuntimePlatform.Files.OpenRead(path));
            if (new string(reader.ReadChars(4)) != "RIFF")
                throw new InvalidDataException("Audio clip is not a RIFF WAV file.");
            reader.ReadInt32();
            if (new string(reader.ReadChars(4)) != "WAVE")
                throw new InvalidDataException("Audio clip is not a WAV file.");

            short channels = 0;
            short bits = 0;
            int sampleRate = 0;
            byte[]? data = null;
            while (reader.BaseStream.Position + 8 <= reader.BaseStream.Length)
            {
                string chunk = new(reader.ReadChars(4));
                int length = reader.ReadInt32();
                if (chunk == "fmt ")
                {
                    short encoding = reader.ReadInt16();
                    if (encoding != 1)
                        throw new InvalidDataException("Only PCM WAV clips are currently supported.");
                    channels = reader.ReadInt16();
                    sampleRate = reader.ReadInt32();
                    reader.ReadInt32();
                    reader.ReadInt16();
                    bits = reader.ReadInt16();
                    reader.BaseStream.Position += length - 16;
                }
                else if (chunk == "data")
                    data = reader.ReadBytes(length);
                else
                    reader.BaseStream.Position += length;
                if ((length & 1) != 0)
                    reader.BaseStream.Position++;
            }

            BufferFormat format = (channels, bits) switch
            {
                (1, 8) => BufferFormat.Mono8,
                (1, 16) => BufferFormat.Mono16,
                (2, 8) => BufferFormat.Stereo8,
                (2, 16) => BufferFormat.Stereo16,
                _ => throw new InvalidDataException("WAV clips must be mono/stereo and 8-bit/16-bit PCM.")
            };
            return new WavData(
                data ?? throw new InvalidDataException("WAV clip has no data chunk."),
                sampleRate,
                format,
                channels,
                bits);
        }

        public WavData ToMono()
        {
            if (Channels != 2)
                return this;

            if (Bits == 8)
            {
                byte[] mono = new byte[Data.Length / 2];
                for (int source = 0, target = 0; source + 1 < Data.Length; source += 2, target++)
                    mono[target] = (byte)((Data[source] + Data[source + 1]) / 2);
                return new WavData(mono, SampleRate, BufferFormat.Mono8, 1, Bits);
            }

            byte[] mono16 = new byte[Data.Length / 2];
            for (int source = 0, target = 0; source + 3 < Data.Length; source += 4, target += 2)
            {
                short left = (short)(Data[source] | (Data[source + 1] << 8));
                short right = (short)(Data[source + 2] | (Data[source + 3] << 8));
                short mixed = (short)((left + right) / 2);
                mono16[target] = (byte)(mixed & 0xff);
                mono16[target + 1] = (byte)((mixed >> 8) & 0xff);
            }
            return new WavData(mono16, SampleRate, BufferFormat.Mono16, 1, Bits);
        }

        public WavData[] SplitOnSilence()
        {
            int bytesPerSample = Bits / 8;
            int frameSize = Channels * bytesPerSample;
            int frames = Data.Length / frameSize;
            if (frames == 0) return new[] { this };
            int peak = 1;
            for (int frame = 0; frame < frames; frame++)
                peak = Math.Max(peak, FrameAmplitude(frame, frameSize, bytesPerSample));
            int threshold = Math.Max(Bits == 8 ? 3 : 512, (int)(peak * 0.025f));
            int silenceFrames = Math.Max(1, SampleRate / 12);
            int padding = Math.Max(1, SampleRate / 100);
            List<(int Start, int End)> ranges = new();
            int start = -1, quiet = 0;
            for (int frame = 0; frame < frames; frame++)
            {
                bool active = FrameAmplitude(frame, frameSize, bytesPerSample) >= threshold;
                if (active) { if (start < 0) start = Math.Max(0, frame - padding); quiet = 0; }
                else if (start >= 0 && ++quiet >= silenceFrames)
                {
                    int end = Math.Min(frames, frame - quiet + padding);
                    if (end - start >= SampleRate / 50) ranges.Add((start, end));
                    start = -1; quiet = 0;
                }
            }
            if (start >= 0) ranges.Add((start, frames));
            if (ranges.Count <= 1) return new[] { this };
            return ranges.Select(range => new WavData(
                Data[(range.Start * frameSize)..(range.End * frameSize)], SampleRate, Format, Channels, Bits)).ToArray();
        }

        private int FrameAmplitude(int frame, int frameSize, int bytesPerSample)
        {
            int offset = frame * frameSize;
            int amplitude = 0;
            for (int channel = 0; channel < Channels; channel++)
            {
                int sampleOffset = offset + channel * bytesPerSample;
                int sample = Bits == 8
                    ? Math.Abs(Data[sampleOffset] - 128)
                    : Math.Abs(BitConverter.ToInt16(Data, sampleOffset));
                amplitude = Math.Max(amplitude, sample);
            }
            return amplitude;
        }
    }
}
