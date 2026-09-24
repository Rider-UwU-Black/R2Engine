namespace R2Engine.Editor.Scene;

public static class ScriptRuntime
{
    private static readonly List<GameObject> PendingDestroy = new();
    private static Scene? _scene;
    private static Action<string>? _logger;

    public static void Begin(Scene scene, Action<string> logger)
    {
        if (!ReferenceEquals(_scene, scene))
            PendingDestroy.Clear();

        _scene = scene;
        _logger = logger;
    }

    public static void End()
    {
        _scene = null;
        _logger = null;
        PendingDestroy.Clear();
    }

    public static GameObject? Find(string name) =>
        _scene?.FindGameObject(name);

    public static GameObject Instantiate(string name = "GameObject")
    {
        if (_scene == null)
            throw new InvalidOperationException("Instantiate is only available in Play Mode.");

        return _scene.CreateGameObject(name);
    }

    public static void Destroy(GameObject gameObject)
    {
        if (_scene != null && gameObject.Scene == _scene && !PendingDestroy.Contains(gameObject))
            PendingDestroy.Add(gameObject);
    }

    public static GameObject[] TakePendingDestroy()
    {
        GameObject[] pending = PendingDestroy.ToArray();
        PendingDestroy.Clear();
        return pending;
    }

    internal static void Log(string message) =>
        _logger?.Invoke(message);
}

public static class R2Debug
{
    public static void Log(object? message) =>
        ScriptRuntime.Log(message?.ToString() ?? "null");
}
