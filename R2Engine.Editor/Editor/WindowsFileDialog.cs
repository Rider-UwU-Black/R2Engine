using System.Runtime.InteropServices;

namespace R2Engine.Editor;

public static class WindowsFileDialog
{
    private const uint OFN_OVERWRITEPROMPT =
        0x00000002;

    private const uint OFN_NOCHANGEDIR =
        0x00000008;

    private const uint OFN_PATHMUSTEXIST =
        0x00000800;

    private const uint OFN_FILEMUSTEXIST =
        0x00001000;

    private const uint OFN_EXPLORER =
        0x00080000;

    [StructLayout(
        LayoutKind.Sequential,
        CharSet = CharSet.Unicode)]
    private struct OPENFILENAME
    {
        public int lStructSize;

        public IntPtr hwndOwner;
        public IntPtr hInstance;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string? lpstrFilter;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string? lpstrCustomFilter;

        public int nMaxCustFilter;
        public int nFilterIndex;

        public IntPtr lpstrFile;
        public int nMaxFile;

        public IntPtr lpstrFileTitle;
        public int nMaxFileTitle;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string? lpstrInitialDir;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string? lpstrTitle;

        public uint Flags;

        public short nFileOffset;
        public short nFileExtension;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string? lpstrDefExt;

        public IntPtr lCustData;
        public IntPtr lpfnHook;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string? lpTemplateName;

        public IntPtr pvReserved;
        public uint dwReserved;
        public uint FlagsEx;
    }

    [DllImport(
        "comdlg32.dll",
        CharSet = CharSet.Unicode,
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetOpenFileName(
        ref OPENFILENAME openFileName);

    [DllImport(
        "comdlg32.dll",
        CharSet = CharSet.Unicode,
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSaveFileName(
        ref OPENFILENAME openFileName);

    // =========================================================
    // Scene
    // =========================================================

    public static string? OpenScene()
    {
        string scenesDirectory =
            Path.Combine(
                AssetDatabase.ProjectRoot,
                "Scenes");

        Directory.CreateDirectory(
            scenesDirectory);

        return ShowDialog(
            saveDialog: false,
            title: "Open R2Engine Scene",
            initialFileName: "",
            initialDirectory: scenesDirectory,
            filter:
                "R2Engine Scene (*.r2scene)\0" +
                "*.r2scene\0" +
                "All Files (*.*)\0" +
                "*.*\0\0",
            defaultExtension: "r2scene");
    }

    public static string? SaveScene(
        string suggestedFileName)
    {
        string scenesDirectory =
            Path.Combine(
                AssetDatabase.ProjectRoot,
                "Scenes");

        Directory.CreateDirectory(
            scenesDirectory);

        return ShowDialog(
            saveDialog: true,
            title: "Save R2Engine Scene",
            initialFileName: suggestedFileName,
            initialDirectory: scenesDirectory,
            filter:
                "R2Engine Scene (*.r2scene)\0" +
                "*.r2scene\0" +
                "All Files (*.*)\0" +
                "*.*\0\0",
            defaultExtension: "r2scene");
    }

    // =========================================================
    // Texture
    // =========================================================

    public static string? OpenTexture()
    {
        AssetDatabase.Initialize();

        return ShowDialog(
            saveDialog: false,
            title: "Import Texture",
            initialFileName: "",
            initialDirectory:
                AssetDatabase.TexturesDirectory,
            filter:
                "Image Files (*.png;*.jpg;*.jpeg;*.bmp;*.tga)\0" +
                "*.png;*.jpg;*.jpeg;*.bmp;*.tga\0" +
                "PNG Image (*.png)\0" +
                "*.png\0" +
                "JPEG Image (*.jpg;*.jpeg)\0" +
                "*.jpg;*.jpeg\0" +
                "All Files (*.*)\0" +
                "*.*\0\0",
            defaultExtension: "");
    }

    // =========================================================
    // Model
    // =========================================================

    public static string? OpenModelFormat(string format)
    {
        AssetDatabase.Initialize();
        string normalized = format.ToLowerInvariant();
        (string label, string pattern, string extension) = normalized switch
        {
            "obj" => ("Wavefront OBJ", "*.obj", "obj"),
            "fbx" => ("FBX Character", "*.fbx", "fbx"),
            "glb" => ("Binary glTF", "*.glb", "glb"),
            "gltf" => ("glTF Model", "*.gltf", "gltf"),
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unsupported model format.")
        };

        return ShowDialog(
            saveDialog: false,
            title: $"Import {label}",
            initialFileName: "",
            initialDirectory: AssetDatabase.ModelsDirectory,
            filter: $"{label} ({pattern})\0{pattern}\0All Files (*.*)\0*.*\0\0",
            defaultExtension: extension);
    }

    // =========================================================
    // Generic Dialog
    // =========================================================

    public static string? OpenAudio()
    {
        AssetDatabase.Initialize();
        return ShowDialog(
            saveDialog: false,
            title: "Import WAV Audio",
            initialFileName: "",
            initialDirectory: AssetDatabase.AudioDirectory,
            filter: "WAV Audio (*.wav)\0*.wav\0All Files (*.*)\0*.*\0\0",
            defaultExtension: "wav");
    }

    public static string? OpenFont()
    {
        AssetDatabase.Initialize();
        return ShowDialog(false,"Import Font","",AssetDatabase.FontsDirectory,
            "Font Files (*.ttf;*.otf)\0*.ttf;*.otf\0TrueType Font (*.ttf)\0*.ttf\0OpenType Font (*.otf)\0*.otf\0\0","");
    }

    public static string? OpenScript()
    {
        AssetDatabase.Initialize();
        string scriptsDirectory = Path.Combine(AssetDatabase.AssetsRoot, "Scripts");
        Directory.CreateDirectory(scriptsDirectory);
        return ShowDialog(
            saveDialog: false,
            title: "Import C# Script",
            initialFileName: "",
            initialDirectory: scriptsDirectory,
            filter: "C# Script (*.cs)\0*.cs\0All Files (*.*)\0*.*\0\0",
            defaultExtension: "cs");
    }

    private static string? ShowDialog(
        bool saveDialog,
        string title,
        string initialFileName,
        string initialDirectory,
        string filter,
        string defaultExtension)
    {
        const int bufferSize =
            4096;

        IntPtr fileBuffer =
            Marshal.AllocHGlobal(
                bufferSize *
                sizeof(char));

        try
        {
            for (
                int i = 0;
                i < bufferSize;
                i++)
            {
                Marshal.WriteInt16(
                    fileBuffer,
                    i * sizeof(char),
                    0);
            }

            if (!string.IsNullOrWhiteSpace(
                    initialFileName))
            {
                char[] characters =
                    initialFileName.ToCharArray();

                int count =
                    Math.Min(
                        characters.Length,
                        bufferSize - 1);

                Marshal.Copy(
                    characters,
                    0,
                    fileBuffer,
                    count);
            }

            OPENFILENAME dialog =
                new()
                {
                    lStructSize =
                        Marshal.SizeOf<OPENFILENAME>(),

                    lpstrFilter =
                        filter,

                    nFilterIndex =
                        1,

                    lpstrFile =
                        fileBuffer,

                    nMaxFile =
                        bufferSize,

                    lpstrInitialDir =
                        initialDirectory,

                    lpstrTitle =
                        title,

                    lpstrDefExt =
                        defaultExtension,

                    Flags =
                        OFN_EXPLORER |
                        OFN_NOCHANGEDIR |
                        OFN_PATHMUSTEXIST
                };

            if (saveDialog)
            {
                dialog.Flags |=
                    OFN_OVERWRITEPROMPT;
            }
            else
            {
                dialog.Flags |=
                    OFN_FILEMUSTEXIST;
            }

            bool success =
                saveDialog
                    ? GetSaveFileName(
                        ref dialog)
                    : GetOpenFileName(
                        ref dialog);

            if (!success)
            {
                return null;
            }

            string? result =
                Marshal.PtrToStringUni(
                    fileBuffer);

            return string.IsNullOrWhiteSpace(
                    result)
                ? null
                : result;
        }
        finally
        {
            Marshal.FreeHGlobal(
                fileBuffer);
        }
    }
}
