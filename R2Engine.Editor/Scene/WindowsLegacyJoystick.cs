using System.Runtime.InteropServices;

namespace R2Engine.Editor.Scene;

internal static class WindowsLegacyJoystick
{
    private const uint JoyReturnAll = 0x000000FF;
    private const uint JoyPovCentered = 0x0000FFFF;

    [StructLayout(LayoutKind.Sequential)]
    private struct JoyInfoEx
    {
        public uint Size;
        public uint Flags;
        public uint X;
        public uint Y;
        public uint Z;
        public uint R;
        public uint U;
        public uint V;
        public uint Buttons;
        public uint ButtonNumber;
        public uint Pov;
        public uint Reserved1;
        public uint Reserved2;
    }

    [DllImport("winmm.dll")]
    private static extern uint joyGetNumDevs();

    [DllImport("winmm.dll")]
    private static extern uint joyGetPosEx(uint joystickId, ref JoyInfoEx info);

    public static bool TryPoll(out string name, out float[] axes, out bool[] buttons)
    {
        name = "";
        axes = Array.Empty<float>();
        buttons = Array.Empty<bool>();
        if (!OperatingSystem.IsWindows())
            return false;

        uint deviceCount = Math.Min(joyGetNumDevs(), 16u);
        for (uint deviceId = 0; deviceId < deviceCount; deviceId++)
        {
            JoyInfoEx info = new()
            {
                Size = (uint)Marshal.SizeOf<JoyInfoEx>(),
                Flags = JoyReturnAll
            };
            if (joyGetPosEx(deviceId, ref info) != 0)
                continue;

            axes = new[]
            {
                Normalize(info.X), Normalize(info.Y), Normalize(info.Z),
                Normalize(info.R), Normalize(info.U), Normalize(info.V),
                PovX(info.Pov), PovY(info.Pov)
            };
            buttons = Enumerable.Range(0, 32)
                .Select(index => (info.Buttons & (1u << index)) != 0)
                .ToArray();
            name = $"Windows Legacy Joystick {deviceId} (WinMM, 8 axes, 32 buttons)";
            return true;
        }

        return false;
    }

    private static float Normalize(uint value) =>
        Math.Clamp((value / 65535.0f) * 2.0f - 1.0f, -1.0f, 1.0f);

    private static float PovX(uint pov)
    {
        if (pov == JoyPovCentered) return 0.0f;
        float radians = pov / 100.0f * (MathF.PI / 180.0f);
        return MathF.Sin(radians);
    }

    private static float PovY(uint pov)
    {
        if (pov == JoyPovCentered) return 0.0f;
        float radians = pov / 100.0f * (MathF.PI / 180.0f);
        return MathF.Cos(radians);
    }
}
