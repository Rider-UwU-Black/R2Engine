using System.Numerics;

namespace R2Engine.Editor.Scene;

public class EnvironmentSettings
{
    public Vector3 BackgroundColor = new(0.08f, 0.09f, 0.12f);
    public Vector3 AmbientColor = new(0.22f, 0.22f, 0.22f);
    public bool FogEnabled;
    public Vector3 FogColor = new(0.45f, 0.5f, 0.58f);
    public float FogStart = 20.0f;
    public float FogEnd = 80.0f;
}

public class Scene
{
    public string Name { get; set; }
    public CanvasNavigation MenuNavigation { get; } = new();

    public List<GameObject> GameObjects { get; } =
        new();

    public EnvironmentSettings Environment { get; } =
        new();

    public Scene(
        string name)
    {
        Name = name;
    }

    public GameObject CreateGameObject(
        string name)
    {
        string uniqueName =
            GetUniqueName(
                name);

        var gameObject =
            new GameObject(
                uniqueName);

        gameObject.Scene = this;

        GameObjects.Add(
            gameObject);

        return gameObject;
    }

    public void AddGameObject(
        GameObject gameObject)
    {
        gameObject.Scene = this;
        GameObjects.Add(
            gameObject);
    }

    public void DeleteGameObject(
        GameObject gameObject)
    {
        foreach (GameObject child in gameObject.Children.ToArray())
            DeleteGameObject(child);

        GameObjects.Remove(
            gameObject);

        gameObject.Scene = null;
    }

    public GameObject? FindGameObject(string name) =>
        GameObjects.FirstOrDefault(
            gameObject => string.Equals(gameObject.Name, name, StringComparison.Ordinal));

    public void Clear()
    {
        GameObjects.Clear();
    }

    public string GetUniqueName(
        string baseName)
    {
        bool baseNameExists =
            GameObjects.Any(
                gameObject =>
                    gameObject.Name ==
                    baseName);

        if (!baseNameExists)
            return baseName;

        int number =
            1;

        while (true)
        {
            string candidate =
                $"{baseName} ({number})";

            bool candidateExists =
                GameObjects.Any(
                    gameObject =>
                        gameObject.Name ==
                        candidate);

            if (!candidateExists)
                return candidate;

            number++;
        }
    }
}
