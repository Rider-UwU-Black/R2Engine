using System.Numerics;
using System.Text;

namespace R2Engine.Editor.Scene;

/// <summary>PS2 browser assets, independent of the checkpoint payload and slot paths.</summary>
public static class Ps2SavePresentation
{
    public static void Export(ProjectSettings settings, string projectRoot, string buildDirectory)
    {
        string title = string.IsNullOrWhiteSpace(settings.Ps2MemoryCardTitle)
            ? settings.ProductName : settings.Ps2MemoryCardTitle;
        byte[] system = CreateIconSys(title);
        byte[] identity = CreateIdentity(settings.Ps2SaveId, settings.Ps2ImportLegacySaves);
        byte[] icon;
        if (string.IsNullOrWhiteSpace(settings.Ps2MemoryCardIcon)) icon = CreateDefaultIcon();
        else
        {
            string path = Path.GetFullPath(Path.Combine(projectRoot, settings.Ps2MemoryCardIcon));
            if (!File.Exists(path)) throw new FileNotFoundException("PS2 memory-card icon was not found.", path);
            if (new FileInfo(path).Length > 256 * 1024)
                throw new InvalidDataException("PS2 memory-card icon must be at most 256 KiB.");
            icon = File.ReadAllBytes(path);
        }
        ValidateIcon(icon);
        string directory = Path.Combine(buildDirectory, "R2Data", "Save");
        Directory.CreateDirectory(directory);
        File.WriteAllBytes(Path.Combine(directory, "icon.sys"), system);
        File.WriteAllBytes(Path.Combine(directory, "save.icn"), icon);
        File.WriteAllBytes(Path.Combine(directory, "identity.bin"), identity);
    }

    public static byte[] CreateIdentity(string id, bool importLegacy)
    {
        if (string.IsNullOrEmpty(id) || id.Length > 20 || id.Any(c =>
            !(c is >= 'A' and <= 'Z' or >= '0' and <= '9' or '_' or '-')))
            throw new InvalidDataException("PS2 Save ID must contain 1-20 uppercase letters, digits, underscores or hyphens. Set it in Project Settings.");
        byte[] data = new byte[32];
        Encoding.ASCII.GetBytes("R2SI").CopyTo(data, 0);
        data[4] = importLegacy ? (byte)1 : (byte)0;
        Encoding.ASCII.GetBytes(id).CopyTo(data, 8);
        return data;
    }

    public static byte[] CreateIconSys(string title)
    {
        if (string.IsNullOrWhiteSpace(title)) title = "R2 Game";
        // The browser uses double-byte Shift-JIS, including full-width Latin text.
        string fullWidth = string.Concat(title.Select(c => c == ' ' ? '\u3000' :
            c >= '!' && c <= '~' ? (char)(c + 0xfee0) : c));
        if (fullWidth.Any(char.IsControl)) throw new InvalidDataException("Memory-card title must be one line.");
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        byte[] text;
        try
        {
            var encoding = Encoding.GetEncoding(932, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
            if (fullWidth.Any(c => encoding.GetByteCount(c.ToString()) != 2))
                throw new InvalidDataException("Memory-card title requires full-width Shift-JIS characters.");
            text = encoding.GetBytes(fullWidth);
        }
        catch (EncoderFallbackException) { throw new InvalidDataException("Memory-card title contains characters unavailable in Shift-JIS."); }
        if (text.Length > 64 || text.Length % 2 != 0 || fullWidth.Any(c => c >= '\uff61' && c <= '\uff9f'))
            throw new InvalidDataException("Memory-card title must fit 32 full-width characters (64 Shift-JIS bytes).");
        using var stream = new MemoryStream(new byte[964], true);
        using var writer = new BinaryWriter(stream);
        writer.Write(Encoding.ASCII.GetBytes("PS2D"));
        writer.Write((ushort)0); writer.Write((ushort)0); writer.Write(0u); writer.Write(0x60u);
        for (int i = 0; i < 4; i++) { writer.Write(18u); writer.Write(32u); writer.Write(56u); writer.Write(0u); }
        foreach (float value in new float[] { .5f,.5f,.5f,0, -.5f,.2f,.5f,0, 0,-.5f,-.5f,0 }) writer.Write(value);
        for (int i = 0; i < 3; i++) { writer.Write(.5f); writer.Write(.5f); writer.Write(.5f); writer.Write(0f); }
        writer.Write(.5f); writer.Write(.5f); writer.Write(.5f); writer.Write(0f);
        writer.Write(text);
        for (int i = 0; i < 3; i++) { stream.Position = 260 + i * 64; writer.Write(Encoding.ASCII.GetBytes("save.icn")); }
        return stream.ToArray();
    }

    // Original, low-poly teal gem. One static shape, with a tiny white RLE texture.
    public static byte[] CreateDefaultIcon()
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        writer.Write(0x10000u); writer.Write(1u); writer.Write(15u); writer.Write(1f); writer.Write(24u);
        Vector3[] ring = { new(1,0,0), new(0,0,1), new(-1,0,0), new(0,0,-1) };
        void Vector(Vector3 v) { writer.Write((short)(v.X*4096)); writer.Write((short)(v.Y*4096)); writer.Write((short)(v.Z*4096)); writer.Write((short)0); }
        for (int half = 0; half < 2; half++) for (int side = 0; side < 4; side++)
        {
            Vector3 a = new(0, half == 0 ? 1.3f : -1.3f, 0), b = ring[side], c = ring[(side+1)%4];
            if (half == 0) (b,c) = (c,b);
            Vector3 normal = Vector3.Normalize(Vector3.Cross(b-a,c-a));
            foreach (Vector3 vertex in new[] { a,b,c })
            { Vector(vertex); Vector(normal); writer.Write((short)0); writer.Write((short)0); writer.Write(0xffd0a030u); }
        }
        writer.Write(1u); writer.Write(31u); writer.Write(1f); writer.Write(0u); writer.Write(1u);
        writer.Write(0u); writer.Write(1u); writer.Write(0f); writer.Write(1f);
        writer.Write(4u); writer.Write((ushort)16384); writer.Write((ushort)0x7fff);
        return stream.ToArray();
    }

    public static void ValidateIcon(byte[] data)
    {
        try
        {
            if (data.Length > 256*1024) throw new InvalidDataException("Icon exceeds 256 KiB.");
            using var stream = new MemoryStream(data); using var reader = new BinaryReader(stream);
            if (reader.ReadUInt32() != 0x10000) throw new InvalidDataException("Not a native PS2 3D icon. PNG and Windows ICO files are not supported.");
            uint shapes=reader.ReadUInt32(), texture=reader.ReadUInt32(); reader.ReadUInt32(); uint vertices=reader.ReadUInt32();
            if (shapes is 0 or > 64 || vertices is 0 or > 12000 || vertices%3!=0 || (texture!=6 && texture!=7 && texture!=15))
                throw new InvalidDataException("Unsupported PS2 icon geometry or texture header.");
            long animation = 20L + vertices*(shapes*8L+16);
            if (animation+20>data.Length) throw new InvalidDataException("Truncated PS2 icon geometry.");
            stream.Position=animation;
            reader.ReadUInt32(); reader.ReadUInt32(); float speed=reader.ReadSingle(); reader.ReadUInt32(); uint frames=reader.ReadUInt32();
            if (!float.IsFinite(speed) || frames>1024) throw new InvalidDataException("Invalid icon animation.");
            for (uint i=0;i<frames;i++)
            {
                uint shape=reader.ReadUInt32(), keys=reader.ReadUInt32();
                if (shape>=shapes || keys>4096 || stream.Position+keys*8L>data.Length) throw new InvalidDataException("Invalid icon animation keys.");
                for (uint key=0;key<keys;key++) if (!float.IsFinite(reader.ReadSingle()) || !float.IsFinite(reader.ReadSingle()))
                    throw new InvalidDataException("Non-finite icon animation key.");
            }
            if (texture!=15)
            { if(stream.Length-stream.Position!=32768) throw new InvalidDataException("Icon needs a 128x128 16-bit texture."); }
            else
            {
                uint size=reader.ReadUInt32(); long end=stream.Position+size; int pixels=0;
                if(end!=stream.Length) throw new InvalidDataException("Invalid icon RLE length.");
                while(stream.Position<end)
                {
                    ushort run=reader.ReadUInt16(); int count=run<0xff00?run:65536-run;
                    if(count==0 || pixels+count>16384) throw new InvalidDataException("Invalid icon RLE run.");
                    int values=run<0xff00?1:count;
                    if(stream.Position+values*2L>end) throw new InvalidDataException("Truncated icon RLE data.");
                    stream.Position+=values*2L; pixels+=count;
                }
                if(pixels!=16384) throw new InvalidDataException("Incomplete icon texture.");
            }
        }
        catch (EndOfStreamException) { throw new InvalidDataException("Truncated PS2 memory-card icon."); }
    }
}
