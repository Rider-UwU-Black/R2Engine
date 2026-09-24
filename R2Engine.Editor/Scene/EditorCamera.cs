using System.Numerics;
using ImGuiNET;
using R2Engine.Editor.Scene;

namespace R2Engine.Editor;

public class EditorCamera
{
    // =========================================================
    // Camera State
    // =========================================================

    public Vector3 Position =
        new Vector3(
            4.0f,
            3.0f,
            6.0f);

    public float Yaw =
        -34.0f;

    public float Pitch =
        -18.0f;

    // Point the editor camera is currently working around.
    private Vector3 _pivot =
        Vector3.Zero;

    // Distance from camera to pivot.
    private float _pivotDistance =
        8.0f;

    // =========================================================
    // Camera Settings
    // =========================================================

    public float MoveSpeed =
        4.0f;

    public float FastMoveMultiplier =
        4.0f;

    public float LookSensitivity =
        0.12f;

    public float OrbitSensitivity =
        0.18f;

    public float ZoomSensitivity =
        0.18f;

    public float PanSensitivity =
        0.0015f;

    public float MinimumPivotDistance =
        0.15f;

    public EditorCamera()
    {
        // Keep the initial position consistent with the orbit pivot,
        // distance, yaw, and pitch. Previously this only happened after
        // the first zoom/orbit input, leaving the default object off-screen.
        Position =
            _pivot -
            GetForward() *
            _pivotDistance;
    }

    // =========================================================
    // Update
    // =========================================================

    public void Update(
        bool sceneHovered,
        GameObject? selectedObject,
        bool panWithLeftMouse = false)
    {
        if (!sceneHovered)
            return;

        var io =
            ImGui.GetIO();

        // =====================================================
        // F - Focus Selected
        // =====================================================

        if (selectedObject != null &&
            ImGui.IsKeyPressed(
                ImGuiKey.F))
        {
            FocusObject(
                selectedObject);
        }

        // =====================================================
        // ALT + LEFT MOUSE - Orbit
        // =====================================================

        bool altHeld =
            ImGui.IsKeyDown(
                ImGuiKey.LeftAlt) ||
            ImGui.IsKeyDown(
                ImGuiKey.RightAlt);

        bool leftMouseHeld =
            ImGui.IsMouseDown(
                ImGuiMouseButton.Left);

        if (altHeld &&
            leftMouseHeld)
        {
            Orbit(
                io.MouseDelta);

            ImGui.SetMouseCursor(
                ImGuiMouseCursor.None);

            return;
        }

        if (panWithLeftMouse && leftMouseHeld)
        {
            Pan(io.MouseDelta);
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            return;
        }

        // =====================================================
        // MIDDLE MOUSE - Pan
        // =====================================================

        bool middleMouseHeld =
            ImGui.IsMouseDown(
                ImGuiMouseButton.Middle);

        if (middleMouseHeld)
        {
            Pan(
                io.MouseDelta);

            ImGui.SetMouseCursor(
                ImGuiMouseCursor.Hand);

            return;
        }

        // =====================================================
        // RIGHT MOUSE - Free Look + WASD
        // =====================================================

        bool rightMouseHeld =
            ImGui.IsMouseDown(
                ImGuiMouseButton.Right);

        if (rightMouseHeld)
        {
            FreeLook(
                io.MouseDelta);

            Fly(
                io.DeltaTime);

            ImGui.SetMouseCursor(
                ImGuiMouseCursor.None);
        }

        // =====================================================
        // SCROLL WHEEL - Zoom
        // =====================================================

        if (io.MouseWheel != 0.0f)
        {
            Zoom(
                io.MouseWheel);
        }
    }

    // =========================================================
    // Focus Selected
    // =========================================================

    public void FocusObject(
        GameObject gameObject)
    {
        _pivot =
            gameObject
                .Transform
                .WorldPosition;

        Vector3 scale =
            gameObject
                .Transform
                .WorldScale;

        float objectSize =
            MathF.Max(
                MathF.Abs(scale.X),
                MathF.Max(
                    MathF.Abs(scale.Y),
                    MathF.Abs(scale.Z)));

        _pivotDistance =
            MathF.Max(
                2.5f,
                objectSize * 3.0f);

        Position =
            _pivot -
            GetForward() *
            _pivotDistance;
    }

    // =========================================================
    // Orbit
    // =========================================================

    private void Orbit(
        Vector2 mouseDelta)
    {
        Yaw -=
            mouseDelta.X *
            OrbitSensitivity;

        Pitch -=
            mouseDelta.Y *
            OrbitSensitivity;

        ClampPitch();

        Position =
            _pivot -
            GetForward() *
            _pivotDistance;
    }

    // =========================================================
    // Pan
    // =========================================================

    private void Pan(
        Vector2 mouseDelta)
    {
        Vector3 right =
            GetRight();

        Vector3 up =
            GetUp();

        float distanceScale =
            MathF.Max(
                _pivotDistance,
                0.5f);

        float amount =
            PanSensitivity *
            distanceScale;

        Vector3 movement =
            (-right *
             mouseDelta.X *
             amount)
            +
            (up *
             mouseDelta.Y *
             amount);

        Position +=
            movement;

        _pivot +=
            movement;
    }

    // =========================================================
    // Zoom
    // =========================================================

    private void Zoom(
        float wheelDelta)
    {
        float zoomAmount =
            _pivotDistance *
            ZoomSensitivity *
            wheelDelta;

        float newDistance =
            _pivotDistance -
            zoomAmount;

        newDistance =
            Math.Clamp(
                newDistance,
                MinimumPivotDistance,
                10000.0f);

        _pivotDistance =
            newDistance;

        Position =
            _pivot -
            GetForward() *
            _pivotDistance;
    }

    // =========================================================
    // Free Look
    // =========================================================

    private void FreeLook(
        Vector2 mouseDelta)
    {
        Yaw -=
            mouseDelta.X *
            LookSensitivity;

        Pitch -=
            mouseDelta.Y *
            LookSensitivity;

        ClampPitch();

        _pivot =
            Position +
            GetForward() *
            _pivotDistance;
    }

    // =========================================================
    // WASD Fly
    // =========================================================

    private void Fly(
        float deltaTime)
    {
        float speed =
            MoveSpeed;

        bool shiftHeld =
            ImGui.IsKeyDown(
                ImGuiKey.LeftShift) ||
            ImGui.IsKeyDown(
                ImGuiKey.RightShift);

        if (shiftHeld)
        {
            speed *=
                FastMoveMultiplier;
        }

        float distanceMultiplier =
            Math.Clamp(
                _pivotDistance * 0.25f,
                0.35f,
                6.0f);

        speed *=
            distanceMultiplier;

        float amount =
            speed *
            deltaTime;

        Vector3 forward =
            GetForward();

        Vector3 right =
            GetRight();

        Vector3 worldUp =
            Vector3.UnitY;

        Vector3 movement =
            Vector3.Zero;

        if (ImGui.IsKeyDown(
                ImGuiKey.W))
        {
            movement +=
                forward;
        }

        if (ImGui.IsKeyDown(
                ImGuiKey.S))
        {
            movement -=
                forward;
        }

        if (ImGui.IsKeyDown(
                ImGuiKey.D))
        {
            movement +=
                right;
        }

        if (ImGui.IsKeyDown(
                ImGuiKey.A))
        {
            movement -=
                right;
        }

        if (ImGui.IsKeyDown(
                ImGuiKey.E))
        {
            movement +=
                worldUp;
        }

        if (ImGui.IsKeyDown(
                ImGuiKey.Q))
        {
            movement -=
                worldUp;
        }

        // Prevent faster diagonal movement.
        if (movement.LengthSquared() > 0.0f)
        {
            movement =
                Vector3.Normalize(
                    movement);

            movement *=
                amount;

            Position +=
                movement;

            _pivot +=
                movement;
        }
    }

    // =========================================================
    // Camera Direction Vectors
    // =========================================================

    private Vector3 GetForward()
    {
        float yawRadians =
            DegreesToRadians(
                Yaw);

        float pitchRadians =
            DegreesToRadians(
                Pitch);

        // IMPORTANT:
        //
        // The negative sine here matches the yaw convention
        // used by SceneRenderer's view transformation.
        //
        // Previously this was positive Sin(Yaw), which meant
        // the renderer and camera controller disagreed about
        // what direction the camera was actually facing.
        Vector3 forward =
            new Vector3(
                -MathF.Sin(yawRadians) *
                MathF.Cos(pitchRadians),

                MathF.Sin(pitchRadians),

                -MathF.Cos(yawRadians) *
                MathF.Cos(pitchRadians));

        return Vector3.Normalize(
            forward);
    }

    private Vector3 GetRight()
    {
        Vector3 forward =
            GetForward();

        Vector3 right =
            Vector3.Cross(
                forward,
                Vector3.UnitY);

        if (right.LengthSquared() <
            0.000001f)
        {
            return Vector3.UnitX;
        }

        return Vector3.Normalize(
            right);
    }

    private Vector3 GetUp()
    {
        Vector3 right =
            GetRight();

        Vector3 forward =
            GetForward();

        return Vector3.Normalize(
            Vector3.Cross(
                right,
                forward));
    }

    // =========================================================
    // Helpers
    // =========================================================

    private void ClampPitch()
    {
        Pitch =
            Math.Clamp(
                Pitch,
                -89.0f,
                89.0f);
    }

    private static float DegreesToRadians(
        float degrees)
    {
        return degrees *
               (MathF.PI / 180.0f);
    }
}
