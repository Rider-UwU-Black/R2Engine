using System.Numerics;

namespace R2Engine.Editor.Scene;

public class GameObject
{
    public Scene? Scene { get; internal set; }
    public string Name { get; set; }
    public string? PrefabPath { get; set; }
    public string? PrefabBaseline { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsActiveInHierarchy => IsActive && (Parent?.IsActiveInHierarchy ?? true);
    public bool IsVisibleInEditor { get; set; } = true;
    public bool IsLockedInEditor { get; set; }
    public bool IsStatic { get; set; }
    public int Layer { get; set; }
    public GameObject? Parent { get; private set; }

    public IEnumerable<GameObject> Children =>
        Scene?.GameObjects.Where(gameObject => ReferenceEquals(gameObject.Parent, this)) ??
        Enumerable.Empty<GameObject>();

    public Transform Transform { get; }

    public List<Component> Components { get; } = new();

    public GameObject(string name)
    {
        Name = name;

        Transform = AddComponent<Transform>();
    }

    public T AddComponent<T>()
        where T : Component, new()
    {
        var component = new T();

        component.GameObject = this;

        Components.Add(component);

        return component;
    }

    public T? GetComponent<T>()
        where T : Component
    {
        foreach (var component in Components)
        {
            if (component is T matchingComponent)
            {
                return matchingComponent;
            }
        }

        return null;
    }

    public T? GetRuntimeComponent<T>()
        where T : class
    {
        T? component = Components.OfType<T>().FirstOrDefault();

        if (component != null)
            return component;

        return Components
            .OfType<ScriptComponent>()
            .Select(script => script.Instance)
            .OfType<T>()
            .FirstOrDefault();
    }

    public bool RemoveComponent<T>()
        where T : Component
    {
        // Transform is required on every GameObject.
        if (typeof(T) == typeof(Transform))
            return false;

        T? component = GetComponent<T>();

        if (component == null)
            return false;

        Components.Remove(component);

        return true;
    }

    public bool SetParent(GameObject? parent, bool keepWorldPosition = true)
    {
        if (ReferenceEquals(parent, this) || (parent != null && parent.IsDescendantOf(this)))
            return false;

        Vector3 worldPosition = Transform.WorldPosition;
        Vector3 worldRotation = Transform.WorldRotation;
        Vector3 worldScale = Transform.WorldScale;
        Parent = parent;

        if (keepWorldPosition)
            Transform.SetWorldTransform(worldPosition, worldRotation, worldScale);

        return true;
    }

    private bool IsDescendantOf(GameObject possibleAncestor)
    {
        for (GameObject? current = Parent; current != null; current = current.Parent)
        {
            if (ReferenceEquals(current, possibleAncestor))
                return true;
        }

        return false;
    }
}
