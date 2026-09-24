using System.Globalization;
using System.Numerics;
using System.Reflection;

namespace R2Engine.Editor.Scene;

public static class ScriptFieldUtility
{
    public static bool IsSupported(Type type) =>
        type == typeof(float) ||
        type == typeof(int) ||
        type == typeof(bool) ||
        type == typeof(string) ||
        type == typeof(Vector2) ||
        type == typeof(Vector3) ||
        type == typeof(Vector4) ||
        type == typeof(GameObject);

    public static string Serialize(object? value, Type type)
    {
        if (type == typeof(float))
            return ((float)(value ?? 0.0f)).ToString("R", CultureInfo.InvariantCulture);

        if (type == typeof(int))
            return ((int)(value ?? 0)).ToString(CultureInfo.InvariantCulture);

        if (type == typeof(bool))
            return ((bool)(value ?? false)).ToString();

        if (type == typeof(string))
            return (string?)value ?? "";

        if (type == typeof(GameObject))
            return ((GameObject?)value)?.Name ?? "";

        if (type == typeof(Vector2))
        {
            Vector2 vector = (Vector2)(value ?? Vector2.Zero);
            return Join(vector.X, vector.Y);
        }

        if (type == typeof(Vector3))
        {
            Vector3 vector = (Vector3)(value ?? Vector3.Zero);
            return Join(vector.X, vector.Y, vector.Z);
        }

        if (type == typeof(Vector4))
        {
            Vector4 vector = (Vector4)(value ?? Vector4.Zero);
            return Join(vector.X, vector.Y, vector.Z, vector.W);
        }

        return "";
    }

    public static bool TryDeserialize(string text, Type type, out object? value)
    {
        value = null;

        if (type == typeof(float) && float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float floatValue))
            value = floatValue;
        else if (type == typeof(int) && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int intValue))
            value = intValue;
        else if (type == typeof(bool) && bool.TryParse(text, out bool boolValue))
            value = boolValue;
        else if (type == typeof(string))
            value = text;
        else if (type == typeof(Vector2) && TryParts(text, 2, out float[]? v2))
            value = new Vector2(v2[0], v2[1]);
        else if (type == typeof(Vector3) && TryParts(text, 3, out float[]? v3))
            value = new Vector3(v3[0], v3[1], v3[2]);
        else if (type == typeof(Vector4) && TryParts(text, 4, out float[]? v4))
            value = new Vector4(v4[0], v4[1], v4[2], v4[3]);

        return value != null;
    }

    public static void Apply(
        object instance,
        IReadOnlyDictionary<string, string> values,
        Scene? scene)
    {
        foreach (FieldInfo field in instance.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public))
        {
            if (!IsSupported(field.FieldType) ||
                !values.TryGetValue(field.Name, out string? serialized))
            {
                continue;
            }

            if (field.FieldType == typeof(GameObject))
            {
                field.SetValue(instance, scene?.FindGameObject(serialized));
                continue;
            }

            if (!TryDeserialize(serialized, field.FieldType, out object? value))
                continue;

            field.SetValue(instance, value);
        }
    }

    private static string Join(params float[] values) =>
        string.Join(",", values.Select(value => value.ToString("R", CultureInfo.InvariantCulture)));

    private static bool TryParts(string text, int count, out float[] values)
    {
        string[] parts = text.Split(',');
        values = new float[count];

        if (parts.Length != count)
            return false;

        for (int index = 0; index < count; index++)
        {
            if (!float.TryParse(parts[index], NumberStyles.Float, CultureInfo.InvariantCulture, out values[index]))
                return false;
        }

        return true;
    }
}
