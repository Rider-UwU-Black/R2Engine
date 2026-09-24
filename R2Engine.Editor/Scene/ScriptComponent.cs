namespace R2Engine.Editor.Scene;

public class ScriptComponent : Component
{
    public string ScriptPath = "";
    public Dictionary<string, string> FieldValues =
        new(StringComparer.Ordinal);

    // Inspector reassignment is different from renaming/moving the same asset.
    // A different script starts with its own defaults; dropping the same asset
    // again must not erase the user's configured values.
    public void AssignScriptAsset(string path)
    {
        if (path == null) throw new ArgumentNullException(nameof(path));
        if (!string.Equals(ScriptPath.Replace('\\', '/'), path.Replace('\\', '/'),
                StringComparison.OrdinalIgnoreCase))
            FieldValues.Clear();
        ScriptPath = path;
    }

    internal ScriptBehaviour? Instance;
    public bool HasRuntimeError;
    public bool IsInitialized => Instance != null;

    public void Initialize(Type scriptType)
    {
        Instance =
            (ScriptBehaviour?)Activator.CreateInstance(scriptType);

        if (Instance == null)
            throw new Exception($"Could not create script {scriptType.Name}.");

        Instance.GameObject = GameObject;
        HasRuntimeError = false;

        ScriptFieldUtility.Apply(Instance, FieldValues, GameObject.Scene);
        Instance.Start();
    }

    public void RuntimeUpdate(float deltaTime)
    {
        if (!HasRuntimeError)
            Instance?.Update(deltaTime);
    }

    public void Shutdown()
    {
        if (Instance != null && !HasRuntimeError)
            Instance.OnDestroy();

        Instance = null;
    }

    internal void NotifyCollision(GameObject other, bool trigger, bool entering)
    {
        if (Instance == null || HasRuntimeError)
            return;

        if (trigger)
        {
            if (entering) Instance.OnTriggerEnter(other);
            else Instance.OnTriggerExit(other);
        }
        else
        {
            if (entering) Instance.OnCollisionEnter(other);
            else Instance.OnCollisionExit(other);
        }
    }

    internal void NotifyCollisionStay(GameObject other, bool trigger)
    {
        if (Instance == null || HasRuntimeError)
            return;

        if (trigger)
            Instance.OnTriggerStay(other);
        else
            Instance.OnCollisionStay(other);
    }

    internal void NotifyAnimationEvent(string eventName)
    {
        if (Instance == null || HasRuntimeError)
            return;
        try
        {
            Instance.OnAnimationEvent(eventName);
        }
        catch (Exception exception)
        {
            HasRuntimeError = true;
            ScriptRuntime.Log(
                $"{GameObject.Name} script OnAnimationEvent({eventName}): {exception.GetBaseException().Message}");
        }
    }
}
