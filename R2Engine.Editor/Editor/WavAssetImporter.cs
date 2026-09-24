using System.Text;

namespace R2Engine.Editor;

internal static class WavAssetImporter
{
    public static void ConvertToRuntimeWav(
        string sourcePath,
        string destinationPath,
        int outputBits = 16,
        int maximumSampleRate = 48000)
    {
        if (outputBits is not (8 or 16))
            throw new ArgumentOutOfRangeException(nameof(outputBits), "Audio output must be 8-bit or 16-bit PCM.");
        if (maximumSampleRate is not (22050 or 32000 or 44100 or 48000))
            throw new ArgumentOutOfRangeException(nameof(maximumSampleRate), "Unsupported audio sample rate.");

        byte[] source = File.ReadAllBytes(sourcePath);
        if (source.Length < 44 || Encoding.ASCII.GetString(source, 0, 4) != "RIFF" ||
            Encoding.ASCII.GetString(source, 8, 4) != "WAVE")
            throw new InvalidDataException("Audio must be a RIFF WAV file.");

        ushort encoding = 0, channels = 0, bits = 0;
        int sampleRate = 0, dataOffset = 0, dataLength = 0;
        for (int offset = 12; offset + 8 <= source.Length;)
        {
            string id = Encoding.ASCII.GetString(source, offset, 4);
            int length = BitConverter.ToInt32(source, offset + 4);
            int payload = offset + 8;
            if (length < 0 || payload + (long)length > source.Length)
                throw new InvalidDataException("WAV contains a damaged chunk.");
            if (id == "fmt " && length >= 16)
            {
                encoding = BitConverter.ToUInt16(source, payload);
                channels = BitConverter.ToUInt16(source, payload + 2);
                sampleRate = BitConverter.ToInt32(source, payload + 4);
                bits = BitConverter.ToUInt16(source, payload + 14);
            }
            else if (id == "data")
            {
                dataOffset = payload;
                dataLength = length;
            }
            offset = payload + length + (length & 1);
        }

        if (encoding is not (1 or 3) || channels is < 1 or > 2 || sampleRate <= 0 ||
            bits is not (8 or 16 or 24 or 32) || dataOffset == 0)
            throw new InvalidDataException("WAV import supports mono/stereo PCM (8/16/24/32-bit) or 32-bit float audio.");
        if (encoding == 3 && bits != 32)
            throw new InvalidDataException("Floating-point WAV audio must be 32-bit.");

        int bytesPerSample = bits / 8;
        int sourceFrameSize = bytesPerSample * channels;
        int sourceFrames = dataLength / sourceFrameSize;
        int outputRate = Math.Min(sampleRate, maximumSampleRate);
        int outputBytesPerSample = outputBits / 8;
        int outputFrames = Math.Max(1, (int)((long)sourceFrames * outputRate / sampleRate));
        byte[] output = new byte[checked(outputFrames * channels * outputBytesPerSample)];
        for (int frame = 0; frame < outputFrames; ++frame)
        {
            int sourceFrame = Math.Min(sourceFrames - 1, (int)((long)frame * sampleRate / outputRate));
            for (int channel = 0; channel < channels; ++channel)
            {
                int input = dataOffset + (sourceFrame * channels + channel) * bytesPerSample;
                short sample = DecodeSample(source, input, encoding, bits);
                int target = (frame * channels + channel) * outputBytesPerSample;
                if (outputBits == 8)
                    output[target] = (byte)Math.Clamp((sample >> 8) + 128, 0, 255);
                else
                {
                    output[target] = (byte)sample;
                    output[target + 1] = (byte)(sample >> 8);
                }
            }
        }

        string directory = Path.GetDirectoryName(destinationPath)!;
        Directory.CreateDirectory(directory);
        string temporary = destinationPath + ".importing";
        using (BinaryWriter writer = new(File.Create(temporary)))
        {
            writer.Write(Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(36 + output.Length);
            writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
            writer.Write(16);
            writer.Write((ushort)1);
            writer.Write(channels);
            writer.Write(outputRate);
            writer.Write(outputRate * channels * outputBytesPerSample);
            writer.Write((ushort)(channels * outputBytesPerSample));
            writer.Write((ushort)outputBits);
            writer.Write(Encoding.ASCII.GetBytes("data"));
            writer.Write(output.Length);
            writer.Write(output);
        }
        File.Move(temporary, destinationPath, true);
    }

    private static short DecodeSample(byte[] bytes, int offset, ushort encoding, ushort bits)
    {
        if (encoding == 3)
        {
            float value = Math.Clamp(BitConverter.ToSingle(bytes, offset), -1.0f, 1.0f);
            return (short)Math.Clamp((int)MathF.Round(value * 32767.0f), short.MinValue, short.MaxValue);
        }
        return bits switch
        {
            8 => (short)((bytes[offset] - 128) << 8),
            16 => BitConverter.ToInt16(bytes, offset),
            24 => (short)(((bytes[offset] | bytes[offset + 1] << 8 | bytes[offset + 2] << 16) << 8) >> 16),
            32 => (short)(BitConverter.ToInt32(bytes, offset) >> 16),
            _ => 0
        };
    }
}
