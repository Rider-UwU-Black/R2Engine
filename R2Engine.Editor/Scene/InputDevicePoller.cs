using Silk.NET.Input;

namespace R2Engine.Editor.Scene;

public static class InputDevicePoller
{
    private sealed class DeviceState
    {
        public float[] Axes = Array.Empty<float>();
        public bool[] Buttons = Array.Empty<bool>();
    }

    private static readonly Dictionary<IJoystick, DeviceState> JoystickStates = new();
    private static readonly Dictionary<IGamepad, DeviceState> GamepadStates = new();

    public static void Poll(IInputContext? input)
    {
        if (WindowsLegacyJoystick.TryPoll(out string legacyName, out float[] legacyAxes, out bool[] legacyButtons))
        {
            RuntimeInput.SetControllerState(legacyName, legacyAxes, legacyButtons);
            return;
        }

        IJoystick? joystick = input?.Joysticks.FirstOrDefault(device =>
            device.IsConnected && (device.Axes.Count > 0 || device.Buttons.Count > 0 || device.Hats.Count > 0));
        if (joystick != null)
        {
            DeviceState state = TrackJoystick(joystick);
            RuntimeInput.SetControllerState(
                $"Raw Joystick: {joystick.Name} ({joystick.Axes.Count} axes, {joystick.Buttons.Count} buttons, {joystick.Hats.Count} hats)",
                state.Axes,
                state.Buttons);
            return;
        }

        IGamepad? gamepad = input?.Gamepads.FirstOrDefault(device => device.IsConnected);
        if (gamepad != null)
        {
            DeviceState state = TrackGamepad(gamepad);
            RuntimeInput.SetControllerState(
                $"Gamepad: {gamepad.Name} ({state.Axes.Length} axes, {state.Buttons.Length} buttons)",
                state.Axes,
                state.Buttons);
            return;
        }

        RuntimeInput.ClearControllerState();
    }

    private static DeviceState TrackJoystick(IJoystick joystick)
    {
        if (JoystickStates.TryGetValue(joystick, out DeviceState? existing))
            return existing;

        int rawAxisCount = joystick.Axes.Count;
        DeviceState state = new()
        {
            Axes = new float[rawAxisCount + joystick.Hats.Count * 2],
            Buttons = new bool[Math.Max(joystick.Buttons.Count, joystick.Buttons.Select(button => button.Index + 1).DefaultIfEmpty().Max())]
        };
        foreach (Axis axis in joystick.Axes)
            SetAxis(state, axis.Index, axis.Position);
        foreach (Button button in joystick.Buttons)
            SetButton(state, button.Index, button.Pressed);
        foreach (Hat hat in joystick.Hats)
            SetHat(state, rawAxisCount, hat);

        joystick.AxisMoved += (_, axis) => SetAxis(state, axis.Index, axis.Position);
        joystick.ButtonDown += (_, button) => SetButton(state, button.Index, true);
        joystick.ButtonUp += (_, button) => SetButton(state, button.Index, false);
        joystick.HatMoved += (_, hat) => SetHat(state, rawAxisCount, hat);
        JoystickStates[joystick] = state;
        return state;
    }

    private static DeviceState TrackGamepad(IGamepad gamepad)
    {
        if (GamepadStates.TryGetValue(gamepad, out DeviceState? existing))
            return existing;

        int stickAxisCount = gamepad.Thumbsticks.Count * 2;
        DeviceState state = new()
        {
            Axes = new float[stickAxisCount + gamepad.Triggers.Count],
            Buttons = new bool[Math.Max(gamepad.Buttons.Count, gamepad.Buttons.Select(button => button.Index + 1).DefaultIfEmpty().Max())]
        };
        foreach (Thumbstick stick in gamepad.Thumbsticks)
            SetStick(state, stick);
        foreach (Trigger trigger in gamepad.Triggers)
            SetAxis(state, stickAxisCount + trigger.Index, trigger.Position);
        foreach (Button button in gamepad.Buttons)
            SetButton(state, button.Index, button.Pressed);

        gamepad.ThumbstickMoved += (_, stick) => SetStick(state, stick);
        gamepad.TriggerMoved += (_, trigger) => SetAxis(state, stickAxisCount + trigger.Index, trigger.Position);
        gamepad.ButtonDown += (_, button) => SetButton(state, button.Index, true);
        gamepad.ButtonUp += (_, button) => SetButton(state, button.Index, false);
        GamepadStates[gamepad] = state;
        return state;
    }

    private static void SetStick(DeviceState state, Thumbstick stick)
    {
        SetAxis(state, stick.Index * 2, stick.X);
        SetAxis(state, stick.Index * 2 + 1, stick.Y);
    }

    private static void SetHat(DeviceState state, int rawAxisCount, Hat hat)
    {
        (float x, float y) = HatAxes(hat.Position);
        SetAxis(state, rawAxisCount + hat.Index * 2, x);
        SetAxis(state, rawAxisCount + hat.Index * 2 + 1, y);
    }

    private static void SetAxis(DeviceState state, int index, float value)
    {
        if (index >= 0 && index < state.Axes.Length) state.Axes[index] = value;
    }

    private static void SetButton(DeviceState state, int index, bool pressed)
    {
        if (index >= 0 && index < state.Buttons.Length) state.Buttons[index] = pressed;
    }

    private static (float X, float Y) HatAxes(Position2D position) => position switch
    {
        Position2D.Up => (0, 1), Position2D.Down => (0, -1),
        Position2D.Left => (-1, 0), Position2D.Right => (1, 0),
        Position2D.UpLeft => (-1, 1), Position2D.UpRight => (1, 1),
        Position2D.DownLeft => (-1, -1), Position2D.DownRight => (1, -1),
        _ => (0, 0)
    };
}
