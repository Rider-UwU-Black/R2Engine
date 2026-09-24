namespace R2Engine.Editor.Scene;

public abstract class ScriptBehaviour
{
    public GameObject GameObject { get; internal set; } = null!;
    public Transform Transform => GameObject.Transform;

    public T? GetComponent<T>() where T : class =>
        GameObject.GetRuntimeComponent<T>();

    public GameObject? FindGameObject(string name) =>
        ScriptRuntime.Find(name);

    public GameObject Instantiate(string name = "GameObject") =>
        ScriptRuntime.Instantiate(name);

    public void Destroy(GameObject gameObject) =>
        ScriptRuntime.Destroy(gameObject);

    public virtual void Start()
    {
    }

    public virtual void Update(float deltaTime)
    {
    }

    public virtual void OnDestroy()
    {
    }

    public virtual void OnCollisionEnter(GameObject other) { }
    public virtual void OnCollisionStay(GameObject other) { }
    public virtual void OnCollisionExit(GameObject other) { }
    public virtual void OnTriggerEnter(GameObject other) { }
    public virtual void OnTriggerStay(GameObject other) { }
    public virtual void OnTriggerExit(GameObject other) { }
    public virtual void OnAnimationEvent(string eventName) { }
}
