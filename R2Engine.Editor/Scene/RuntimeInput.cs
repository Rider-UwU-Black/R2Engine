using System.Numerics;

namespace R2Engine.Editor.Scene;

public enum RuntimeKey
{
    W, A, S, D, Q, E,
    Up, Down, Left, Right,
    Space, LeftShift, RightShift
}

public enum InputActionType { Button, Axis1D, Axis2D }

public sealed class InputActionBinding
{
    public string Name { get; set; } = "Action";
    public InputActionType Type { get; set; }
    public RuntimeKey Negative { get; set; }
    public RuntimeKey Positive { get; set; }
    public RuntimeKey Up { get; set; }
    public RuntimeKey Down { get; set; }
    public RuntimeKey Left { get; set; }
    public RuntimeKey Right { get; set; }
    public RuntimeKey Primary { get; set; }
    public RuntimeKey Secondary { get; set; }
    public int ControllerButton { get; set; } = -1;
    public int ControllerAxis { get; set; } = -1;
    public int ControllerAxisX { get; set; } = -1;
    public int ControllerAxisY { get; set; } = -1;
    public float DeadZone { get; set; } = 0.2f;
    public bool InvertControllerX { get; set; }
    public bool InvertControllerY { get; set; } = true;

    public static List<InputActionBinding> CreateDefaults() => new()
    {
        new() { Name = "Move", Type = InputActionType.Axis2D, Up = RuntimeKey.W, Down = RuntimeKey.S, Left = RuntimeKey.A, Right = RuntimeKey.D, ControllerAxisX = 0, ControllerAxisY = 1 },
        new() { Name = "Turn", Type = InputActionType.Axis1D, Negative = RuntimeKey.Left, Positive = RuntimeKey.Right, ControllerAxis = 2 },
        new() { Name = "Sprint", Type = InputActionType.Button, Primary = RuntimeKey.LeftShift, Secondary = RuntimeKey.RightShift, ControllerButton = 1 },
        new() { Name = "Jump", Type = InputActionType.Button, Primary = RuntimeKey.Space, Secondary = RuntimeKey.Space, ControllerButton = 0 }
    };
}

public static class RuntimeInput
{
    private static bool _inputEnabled = true;
    private static readonly HashSet<RuntimeKey> Down = new();
    private static readonly HashSet<RuntimeKey> Pressed = new();
    private static readonly HashSet<RuntimeKey> Released = new();
    private static Dictionary<string, InputActionBinding> _actions = new(StringComparer.OrdinalIgnoreCase);
    private static float[] _controllerAxes = Array.Empty<float>();
    private static bool[] _controllerButtons = Array.Empty<bool>();
    private static bool[] _previousControllerButtons = Array.Empty<bool>();
    public static string ControllerName { get; private set; } = "No controller";
    public static IReadOnlyList<float> ControllerAxes => _controllerAxes;
    public static IReadOnlyList<bool> ControllerButtons => _controllerButtons;

    public static bool InteractionConsumed { get; set; }
    public static string ActionHint(string name)
    {
        if (!_actions.TryGetValue(name,out var action)) return name;
        string pad=action.ControllerButton switch { 0 => "X", 1 => "Circle", 2 => "Square", 3 => "Triangle", _ => "Pad " + action.ControllerButton };
        return action.Primary + (action.ControllerButton >= 0 ? " / " + pad : "");
    }
    public static void Configure(IEnumerable<InputActionBinding> actions) =>
        _actions = new[] { new InputActionBinding { Name="Interact", Type=InputActionType.Button, Primary=RuntimeKey.E, Secondary=RuntimeKey.E, ControllerButton=0 } }.Concat(actions)
            .Where(action => !string.IsNullOrWhiteSpace(action.Name))
            .GroupBy(action => action.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.OrdinalIgnoreCase);

    public static bool IsDown(RuntimeKey key) => _inputEnabled && Down.Contains(key);
    public static bool WasPressed(RuntimeKey key) => _inputEnabled && Pressed.Contains(key);
    public static bool WasReleased(RuntimeKey key) => _inputEnabled && Released.Contains(key);

    public static void SetInputEnabled(bool enabled) => _inputEnabled = enabled;

    public static bool IsActionDown(string name) =>
        _actions.TryGetValue(name, out InputActionBinding? action) &&
        (IsDown(action.Primary) || IsDown(action.Secondary) || ControllerButtonDown(action.ControllerButton));

    public static bool WasActionPressed(string name) =>
        _actions.TryGetValue(name, out InputActionBinding? action) &&
        (WasPressed(action.Primary) || WasPressed(action.Secondary) || ControllerButtonPressed(action.ControllerButton));

    public static bool WasActionReleased(string name) =>
        _actions.TryGetValue(name, out InputActionBinding? action) &&
        (WasReleased(action.Primary) || WasReleased(action.Secondary) || ControllerButtonReleased(action.ControllerButton));

    public static float GetAxis(string name)
    {
        if (!_actions.TryGetValue(name, out InputActionBinding? action)) return 0.0f;
        float keyboard = (IsDown(action.Positive) ? 1.0f : 0.0f) - (IsDown(action.Negative) ? 1.0f : 0.0f);
        float controller = ApplyDeadZone(ControllerAxis(action.ControllerAxis), action.DeadZone);
        return MathF.Abs(controller) > MathF.Abs(keyboard) ? controller : keyboard;
    }

    public static Vector2 GetVector2(string name)
    {
        if (!_actions.TryGetValue(name, out InputActionBinding? action)) return Vector2.Zero;
        Vector2 keyboard = new(
            (IsDown(action.Right) ? 1.0f : 0.0f) - (IsDown(action.Left) ? 1.0f : 0.0f),
            (IsDown(action.Up) ? 1.0f : 0.0f) - (IsDown(action.Down) ? 1.0f : 0.0f));
        Vector2 controller = new(
            ApplyDeadZone(ControllerAxis(action.ControllerAxisX), action.DeadZone) * (action.InvertControllerX ? -1.0f : 1.0f),
            ApplyDeadZone(ControllerAxis(action.ControllerAxisY), action.DeadZone) * (action.InvertControllerY ? -1.0f : 1.0f));
        return controller.LengthSquared() > keyboard.LengthSquared() ? controller : keyboard;
    }

    public static void BeginFrame()
    {
        InteractionConsumed=false;
        Pressed.Clear();
        Released.Clear();
        _previousControllerButtons = _controllerButtons.ToArray();
    }

    public static void SetControllerState(string name, IEnumerable<float> axes, IEnumerable<bool> buttons)
    {
        ControllerName = name;
        _controllerAxes = axes.ToArray();
        _controllerButtons = buttons.ToArray();
    }

    public static void ClearControllerState()
    {
        ControllerName = "No controller";
        _controllerAxes = Array.Empty<float>();
        _controllerButtons = Array.Empty<bool>();
        _previousControllerButtons = Array.Empty<bool>();
    }

    // Right-stick vertical axis; keyboard arrows provide the same orbit input.
    public static float GetCameraPitch()
    {
        float stick = -ApplyDeadZone(ControllerAxis(3), 0.2f);
        float keyboard = (IsDown(RuntimeKey.Up) ? 1f : 0f) - (IsDown(RuntimeKey.Down) ? 1f : 0f);
        return MathF.Abs(stick) > MathF.Abs(keyboard) ? stick : keyboard;
    }

    private static float ControllerAxis(int index) => _inputEnabled && index >= 0 && index < _controllerAxes.Length ? _controllerAxes[index] : 0.0f;
    private static bool ControllerButtonDown(int index) => _inputEnabled && index >= 0 && index < _controllerButtons.Length && _controllerButtons[index];
    private static bool ControllerButtonPressed(int index) => ControllerButtonDown(index) &&
        (index >= _previousControllerButtons.Length || !_previousControllerButtons[index]);
    private static bool ControllerButtonReleased(int index) => _inputEnabled && index >= 0 &&
        index < _controllerButtons.Length && index < _previousControllerButtons.Length &&
        _previousControllerButtons[index] && !_controllerButtons[index];
    private static float ApplyDeadZone(float value, float deadZone)
    {
        float magnitude = MathF.Abs(value);
        if (magnitude <= deadZone) return 0.0f;
        float normalized = (magnitude - deadZone) / Math.Max(0.0001f, 1.0f - deadZone);
        return value < 0.0f ? -normalized : normalized;
    }

    public static void SetKey(RuntimeKey key, bool isDown)
    {
        bool wasDown = Down.Contains(key);

        if (isDown)
        {
            Down.Add(key);
            if (!wasDown)
                Pressed.Add(key);
        }
        else
        {
            Down.Remove(key);
            if (wasDown)
                Released.Add(key);
        }
    }

    public static void Clear()
    {
        _inputEnabled = true;
        Down.Clear();
        Pressed.Clear();
        Released.Clear();
        ClearControllerState();
    }
}
