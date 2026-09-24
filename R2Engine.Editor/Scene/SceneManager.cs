namespace R2Engine.Editor.Scene;

public enum SceneLoadRequestKind
{
    Load,
    Restart
}

public readonly record struct SceneLoadRequest(
    SceneLoadRequestKind Kind,
    string SceneName,
    string DestinationMarker = "",
    string PlayerObjectName = "Player");

public static class SceneManager
{
    private static SceneLoadRequest? _pendingRequest;
    private static readonly HashSet<GameObject> PersistentObjects = new();

    public static void LoadScene(string sceneName)
    {
        if (string.IsNullOrWhiteSpace(sceneName))
            throw new ArgumentException("A scene name or path is required.", nameof(sceneName));
        _pendingRequest = new SceneLoadRequest(SceneLoadRequestKind.Load, sceneName);
    }

    public static void LoadSceneAt(string sceneName, string destinationMarker,
        string playerObjectName = "Player")
    {
        if (string.IsNullOrWhiteSpace(sceneName))
            throw new ArgumentException("A scene name or path is required.", nameof(sceneName));
        if (string.IsNullOrWhiteSpace(destinationMarker))
            throw new ArgumentException("A destination marker name is required.", nameof(destinationMarker));
        _pendingRequest = new SceneLoadRequest(SceneLoadRequestKind.Load, sceneName,
            destinationMarker.Trim(), playerObjectName?.Trim() ?? "");
    }

    public static void RestartScene() =>
        _pendingRequest = new SceneLoadRequest(SceneLoadRequestKind.Restart, "");

    public static void DontDestroyOnLoad(GameObject gameObject)
    {
        GameObject root = gameObject;
        while (root.Parent != null)
            root = root.Parent;
        PersistentObjects.Add(root);
    }

    public static SceneLoadRequest? TakePendingRequest()
    {
        SceneLoadRequest? request = _pendingRequest;
        _pendingRequest = null;
        return request;
    }

    public static bool IsPersistent(GameObject gameObject)
    {
        for (GameObject? current = gameObject; current != null; current = current.Parent)
        {
            if (PersistentObjects.Contains(current))
                return true;
        }
        return false;
    }

    public static void ClearRuntimeState()
    {
        _pendingRequest = null;
        PersistentObjects.Clear();
    }
}
