using System.Numerics;
using ImGuiNET;
using R2Engine.Editor.Scene;

namespace R2Engine.Editor;

public enum TransformGizmoMode
{
    View,
    Move,
    Rotate,
    Scale,
    Rect,
    Transform
}

public enum TransformGizmoOrientation
{
    Global,
    Local
}

public class TransformGizmo
{
    private enum GizmoAxis
    {
        None,
        X,
        Y,
        Z
    }

    private const int RotationRingSegments =
        96;

    private GizmoAxis _activeAxis =
        GizmoAxis.None;

    private GameObject? _draggedObject;

    private Vector2 _previousMousePosition;

    private Vector3 _dragStartPosition;
    private Vector3 _dragStartWorldPosition;
    private Vector3 _dragStartRotation;
    private Vector3 _dragStartScale;

    private float _dragAmount;

    // Used by the rotation gizmo while dragging.
    private Vector2 _rotationDragDirection =
        Vector2.UnitX;

    // =========================================================
    // Tool State
    // =========================================================

    public TransformGizmoMode Mode { get; set; } =
        TransformGizmoMode.Move;

    public TransformGizmoOrientation Orientation { get; set; } =
        TransformGizmoOrientation.Global;

    public bool UsePivotPosition { get; set; } = true;

    public bool SnappingEnabled { get; set; } =
        false;

    public float MoveSnap { get; set; } =
        0.5f;

    public float RotationSnap { get; set; } =
        15.0f;

    public float ScaleSnap { get; set; } =
        0.1f;

    public bool IsDragging =>
        _activeAxis != GizmoAxis.None;

    // =========================================================
    // Update + Draw
    // =========================================================

    public bool UpdateAndDraw(
        GameObject? selectedObject,
        EditorCamera camera,
        Vector2 imagePosition,
        Vector2 imageSize,
        bool sceneHovered)
    {
        if (selectedObject == null)
        {
            StopDragging();

            return false;
        }

        if (imageSize.X <= 1.0f ||
            imageSize.Y <= 1.0f)
        {
            return false;
        }

        bool rightMouseHeld =
            ImGui.IsMouseDown(
                ImGuiMouseButton.Right);

        bool middleMouseHeld =
            ImGui.IsMouseDown(
                ImGuiMouseButton.Middle);

        bool altHeld =
            ImGui.IsKeyDown(
                ImGuiKey.LeftAlt) ||
            ImGui.IsKeyDown(
                ImGuiKey.RightAlt);

        // -----------------------------------------------------
        // W / E / R shortcuts
        // -----------------------------------------------------

        if (sceneHovered &&
            !IsDragging &&
            !rightMouseHeld &&
            !middleMouseHeld &&
            !altHeld &&
            !ImGui.GetIO().WantTextInput)
        {
            if (ImGui.IsKeyPressed(
                    ImGuiKey.W))
            {
                Mode =
                    TransformGizmoMode.Move;
            }

            if (ImGui.IsKeyPressed(
                    ImGuiKey.E))
            {
                Mode =
                    TransformGizmoMode.Rotate;
            }

            if (ImGui.IsKeyPressed(
                    ImGuiKey.R))
            {
                Mode =
                    TransformGizmoMode.Scale;
            }

            if (ImGui.IsKeyPressed(ImGuiKey.Q)) Mode = TransformGizmoMode.View;
            if (ImGui.IsKeyPressed(ImGuiKey.T)) Mode = TransformGizmoMode.Rect;
            if (ImGui.IsKeyPressed(ImGuiKey.Y)) Mode = TransformGizmoMode.Transform;
        }

        return Mode switch
        {
            TransformGizmoMode.Move =>
                UpdateMoveGizmo(
                    selectedObject,
                    camera,
                    imagePosition,
                    imageSize),

            TransformGizmoMode.Rotate =>
                UpdateRotateGizmo(
                    selectedObject,
                    camera,
                    imagePosition,
                    imageSize),

            TransformGizmoMode.Scale =>
                UpdateScaleGizmo(
                    selectedObject,
                    camera,
                    imagePosition,
                    imageSize),

            // The universal tool currently uses the move handles as its primary
            // manipulation handles while W/E/R remain available during editing.
            TransformGizmoMode.Transform =>
                UpdateMoveGizmo(
                    selectedObject,
                    camera,
                    imagePosition,
                    imageSize),

            _ => false
        };
    }

    // =========================================================
    // Move Gizmo
    // =========================================================

    private bool UpdateMoveGizmo(
        GameObject selectedObject,
        EditorCamera camera,
        Vector2 imagePosition,
        Vector2 imageSize)
    {
        if (!TryGetAxisScreenPositions(
                selectedObject,
                camera,
                imagePosition,
                imageSize,
                out Vector2 center,
                out Vector2 xEnd,
                out Vector2 yEnd,
                out Vector2 zEnd,
                out float worldAxisLength))
        {
            return false;
        }

        Vector2 mouse =
            ImGui.GetMousePos();

        GizmoAxis hoveredAxis =
            FindHoveredAxis(
                mouse,
                center,
                xEnd,
                yEnd,
                zEnd);

        BeginDragIfNeeded(
            selectedObject,
            hoveredAxis);

        if (IsDragging &&
            !ImGui.IsMouseDown(
                ImGuiMouseButton.Left))
        {
            StopDragging();
        }

        if (IsDragging &&
            _draggedObject ==
            selectedObject)
        {
            ApplyAxisMove(
                selectedObject,
                center,
                xEnd,
                yEnd,
                zEnd,
                worldAxisLength);

            ImGui.SetMouseCursor(
                ImGuiMouseCursor.ResizeAll);
        }

        DrawAxisGizmo(
            center,
            xEnd,
            yEnd,
            zEnd,
            hoveredAxis,
            false);

        return
            hoveredAxis != GizmoAxis.None ||
            IsDragging;
    }

    // =========================================================
    // Scale Gizmo
    // =========================================================

    private bool UpdateScaleGizmo(
        GameObject selectedObject,
        EditorCamera camera,
        Vector2 imagePosition,
        Vector2 imageSize)
    {
        if (!TryGetAxisScreenPositions(
                selectedObject,
                camera,
                imagePosition,
                imageSize,
                out Vector2 center,
                out Vector2 xEnd,
                out Vector2 yEnd,
                out Vector2 zEnd,
                out float worldAxisLength))
        {
            return false;
        }

        Vector2 mouse =
            ImGui.GetMousePos();

        GizmoAxis hoveredAxis =
            FindHoveredAxis(
                mouse,
                center,
                xEnd,
                yEnd,
                zEnd);

        BeginDragIfNeeded(
            selectedObject,
            hoveredAxis);

        if (IsDragging &&
            !ImGui.IsMouseDown(
                ImGuiMouseButton.Left))
        {
            StopDragging();
        }

        if (IsDragging &&
            _draggedObject ==
            selectedObject)
        {
            ApplyAxisScale(
                selectedObject,
                center,
                xEnd,
                yEnd,
                zEnd);

            ImGui.SetMouseCursor(
                ImGuiMouseCursor.ResizeAll);
        }

        DrawAxisGizmo(
            center,
            xEnd,
            yEnd,
            zEnd,
            hoveredAxis,
            true);

        return
            hoveredAxis != GizmoAxis.None ||
            IsDragging;
    }

    // =========================================================
    // 3D Rotate Gizmo
    // =========================================================

    private bool UpdateRotateGizmo(
        GameObject selectedObject,
        EditorCamera camera,
        Vector2 imagePosition,
        Vector2 imageSize)
    {
        Vector3 centerWorld =
            selectedObject
                .Transform
                .WorldPosition;

        if (!ProjectWorldToScreen(
                centerWorld,
                camera,
                imagePosition,
                imageSize,
                out Vector2 centerScreen))
        {
            return false;
        }

        float cameraDistance =
            Vector3.Distance(
                camera.Position,
                centerWorld);

        float ringRadius =
            Math.Clamp(
                cameraDistance * 0.14f,
                0.35f,
                6.0f);

        GetWorldAxes(
            selectedObject,
            out Vector3 xAxis,
            out Vector3 yAxis,
            out Vector3 zAxis);

        Vector2 mouse =
            ImGui.GetMousePos();

        GizmoAxis hoveredAxis =
            GizmoAxis.None;

        float closestDistance =
            8.0f;

        Vector2 hoverTangent =
            Vector2.UnitX;

        if (!IsDragging)
        {
            TestRotationRing(
                mouse,
                centerWorld,
                xAxis,
                ringRadius,
                camera,
                imagePosition,
                imageSize,
                GizmoAxis.X,
                ref hoveredAxis,
                ref closestDistance,
                ref hoverTangent);

            TestRotationRing(
                mouse,
                centerWorld,
                yAxis,
                ringRadius,
                camera,
                imagePosition,
                imageSize,
                GizmoAxis.Y,
                ref hoveredAxis,
                ref closestDistance,
                ref hoverTangent);

            TestRotationRing(
                mouse,
                centerWorld,
                zAxis,
                ringRadius,
                camera,
                imagePosition,
                imageSize,
                GizmoAxis.Z,
                ref hoveredAxis,
                ref closestDistance,
                ref hoverTangent);
        }

        if (hoveredAxis !=
                GizmoAxis.None &&
            ImGui.IsMouseClicked(
                ImGuiMouseButton.Left))
        {
            BeginRotationDrag(
                selectedObject,
                hoveredAxis,
                hoverTangent);
        }

        if (IsDragging &&
            !ImGui.IsMouseDown(
                ImGuiMouseButton.Left))
        {
            StopDragging();
        }

        if (IsDragging &&
            _draggedObject ==
            selectedObject)
        {
            ApplyRotationDrag(
                selectedObject);

            ImGui.SetMouseCursor(
                ImGuiMouseCursor.ResizeAll);
        }

        DrawRotationRing(
            centerWorld,
            xAxis,
            ringRadius,
            camera,
            imagePosition,
            imageSize,
            GizmoAxis.X,
            hoveredAxis);

        DrawRotationRing(
            centerWorld,
            yAxis,
            ringRadius,
            camera,
            imagePosition,
            imageSize,
            GizmoAxis.Y,
            hoveredAxis);

        DrawRotationRing(
            centerWorld,
            zAxis,
            ringRadius,
            camera,
            imagePosition,
            imageSize,
            GizmoAxis.Z,
            hoveredAxis);

        DrawRotationCenter(
            centerScreen);

        return
            hoveredAxis != GizmoAxis.None ||
            IsDragging;
    }

    // =========================================================
    // Rotation Ring Picking
    // =========================================================

    private void TestRotationRing(
        Vector2 mouse,
        Vector3 center,
        Vector3 axis,
        float radius,
        EditorCamera camera,
        Vector2 imagePosition,
        Vector2 imageSize,
        GizmoAxis gizmoAxis,
        ref GizmoAxis hoveredAxis,
        ref float closestDistance,
        ref Vector2 hoverTangent)
    {
        GetCircleBasis(
            axis,
            out Vector3 basisA,
            out Vector3 basisB);

        bool previousValid =
            false;

        Vector2 previousScreen =
            Vector2.Zero;

        for (
            int i = 0;
            i <= RotationRingSegments;
            i++)
        {
            float angle =
                (i /
                 (float)RotationRingSegments) *
                MathF.PI *
                2.0f;

            Vector3 worldPoint =
                center +
                (
                    basisA *
                    MathF.Cos(angle) +
                    basisB *
                    MathF.Sin(angle)
                ) *
                radius;

            bool valid =
                ProjectWorldToScreen(
                    worldPoint,
                    camera,
                    imagePosition,
                    imageSize,
                    out Vector2 currentScreen);

            if (valid &&
                previousValid)
            {
                float distance =
                    DistanceToSegment(
                        mouse,
                        previousScreen,
                        currentScreen);

                if (distance <
                    closestDistance)
                {
                    closestDistance =
                        distance;

                    hoveredAxis =
                        gizmoAxis;

                    Vector2 tangent =
                        currentScreen -
                        previousScreen;

                    if (tangent.LengthSquared() >
                        0.001f)
                    {
                        hoverTangent =
                            Vector2.Normalize(
                                tangent);
                    }
                }
            }

            previousValid =
                valid;

            previousScreen =
                currentScreen;
        }
    }

    // =========================================================
    // Rotation Dragging
    // =========================================================

    private void BeginRotationDrag(
        GameObject selectedObject,
        GizmoAxis axis,
        Vector2 tangent)
    {
        _activeAxis =
            axis;

        _draggedObject =
            selectedObject;

        _dragStartPosition =
            selectedObject
                .Transform
                .Position;

        _dragStartWorldPosition =
            selectedObject
                .Transform
                .WorldPosition;

        _dragStartRotation =
            selectedObject
                .Transform
                .Rotation;

        _dragStartScale =
            selectedObject
                .Transform
                .Scale;

        _dragAmount =
            0.0f;

        _previousMousePosition =
            ImGui.GetMousePos();

        if (tangent.LengthSquared() >
            0.001f)
        {
            _rotationDragDirection =
                Vector2.Normalize(
                    tangent);
        }
        else
        {
            _rotationDragDirection =
                Vector2.UnitX;
        }
    }

    private void ApplyRotationDrag(
        GameObject selectedObject)
    {
        Vector2 mouseDelta =
            ImGui.GetIO()
                .MouseDelta;

        float pixelMovement =
            Vector2.Dot(
                mouseDelta,
                _rotationDragDirection);

        // This is deliberately based on pixels rather than
        // distance from the camera, so rotation feels
        // consistent whether you're zoomed in or out.
        const float degreesPerPixel =
            0.7f;

        _dragAmount +=
            pixelMovement *
            degreesPerPixel;

        float finalAmount =
            ShouldSnap()
                ? SnapValue(
                    _dragAmount,
                    RotationSnap)
                : _dragAmount;

        Vector3 rotation =
            _dragStartRotation;

        switch (_activeAxis)
        {
            case GizmoAxis.X:

                rotation.X +=
                    finalAmount;

                break;

            case GizmoAxis.Y:

                rotation.Y +=
                    finalAmount;

                break;

            case GizmoAxis.Z:

                rotation.Z +=
                    finalAmount;

                break;
        }

        selectedObject
            .Transform
            .Rotation =
            rotation;
    }

    // =========================================================
    // Rotation Ring Drawing
    // =========================================================

    private void DrawRotationRing(
        Vector3 center,
        Vector3 axis,
        float radius,
        EditorCamera camera,
        Vector2 imagePosition,
        Vector2 imageSize,
        GizmoAxis gizmoAxis,
        GizmoAxis hoveredAxis)
    {
        ImDrawListPtr drawList =
            ImGui.GetWindowDrawList();

        uint color =
            GetAxisColor(
                gizmoAxis,
                hoveredAxis,
                GetNormalAxisColor(
                    gizmoAxis));

        GetCircleBasis(
            axis,
            out Vector3 basisA,
            out Vector3 basisB);

        bool previousValid =
            false;

        Vector2 previousScreen =
            Vector2.Zero;

        for (
            int i = 0;
            i <= RotationRingSegments;
            i++)
        {
            float angle =
                (i /
                 (float)RotationRingSegments) *
                MathF.PI *
                2.0f;

            Vector3 worldPoint =
                center +
                (
                    basisA *
                    MathF.Cos(angle) +
                    basisB *
                    MathF.Sin(angle)
                ) *
                radius;

            bool valid =
                ProjectWorldToScreen(
                    worldPoint,
                    camera,
                    imagePosition,
                    imageSize,
                    out Vector2 currentScreen);

            if (valid &&
                previousValid)
            {
                drawList.AddLine(
                    previousScreen,
                    currentScreen,
                    color,
                    gizmoAxis ==
                    _activeAxis
                        ? 4.0f
                        : 2.5f);
            }

            previousValid =
                valid;

            previousScreen =
                currentScreen;
        }
    }

    private static void DrawRotationCenter(
        Vector2 center)
    {
        ImDrawListPtr drawList =
            ImGui.GetWindowDrawList();

        uint color =
            ImGui.ColorConvertFloat4ToU32(
                new Vector4(
                    0.9f,
                    0.9f,
                    0.9f,
                    0.8f));

        drawList.AddCircleFilled(
            center,
            3.0f,
            color);
    }

    private static void GetCircleBasis(
        Vector3 normal,
        out Vector3 basisA,
        out Vector3 basisB)
    {
        normal =
            Vector3.Normalize(
                normal);

        Vector3 helper =
            MathF.Abs(
                Vector3.Dot(
                    normal,
                    Vector3.UnitY)) <
            0.9f
                ? Vector3.UnitY
                : Vector3.UnitX;

        basisA =
            Vector3.Normalize(
                Vector3.Cross(
                    normal,
                    helper));

        basisB =
            Vector3.Normalize(
                Vector3.Cross(
                    normal,
                    basisA));
    }

    // =========================================================
    // Move Dragging
    // =========================================================

    private void ApplyAxisMove(
        GameObject gameObject,
        Vector2 center,
        Vector2 xEnd,
        Vector2 yEnd,
        Vector2 zEnd,
        float worldAxisLength)
    {
        Vector2 mouseDelta =
            ImGui.GetIO()
                .MouseDelta;

        GetAxisInformation(
            gameObject,
            center,
            xEnd,
            yEnd,
            zEnd,
            out Vector2 screenAxis,
            out Vector3 worldAxis);

        float screenLength =
            screenAxis.Length();

        if (screenLength <
            0.001f)
        {
            return;
        }

        Vector2 screenDirection =
            screenAxis /
            screenLength;

        float pixelMovement =
            Vector2.Dot(
                mouseDelta,
                screenDirection);

        float worldUnitsPerPixel =
            worldAxisLength /
            screenLength;

        _dragAmount +=
            pixelMovement *
            worldUnitsPerPixel;

        float finalAmount =
            ShouldSnap()
                ? SnapValue(
                    _dragAmount,
                    MoveSnap)
                : _dragAmount;

        gameObject.Transform.SetWorldPosition(
            _dragStartWorldPosition + worldAxis * finalAmount);
    }

    // =========================================================
    // Scale Dragging
    // =========================================================

    private void ApplyAxisScale(
        GameObject gameObject,
        Vector2 center,
        Vector2 xEnd,
        Vector2 yEnd,
        Vector2 zEnd)
    {
        Vector2 mouseDelta =
            ImGui.GetIO()
                .MouseDelta;

        GetAxisInformation(
            gameObject,
            center,
            xEnd,
            yEnd,
            zEnd,
            out Vector2 screenAxis,
            out Vector3 worldAxis);

        float screenLength =
            screenAxis.Length();

        if (screenLength <
            0.001f)
        {
            return;
        }

        Vector2 screenDirection =
            screenAxis /
            screenLength;

        float pixelMovement =
            Vector2.Dot(
                mouseDelta,
                screenDirection);

        _dragAmount +=
            pixelMovement *
            0.01f;

        float finalAmount =
            ShouldSnap()
                ? SnapValue(
                    _dragAmount,
                    ScaleSnap)
                : _dragAmount;

        Vector3 scale =
            _dragStartScale;

        switch (_activeAxis)
        {
            case GizmoAxis.X:

                scale.X +=
                    finalAmount;

                scale.X =
                    MathF.Max(
                        0.01f,
                        scale.X);

                break;

            case GizmoAxis.Y:

                scale.Y +=
                    finalAmount;

                scale.Y =
                    MathF.Max(
                        0.01f,
                        scale.Y);

                break;

            case GizmoAxis.Z:

                scale.Z +=
                    finalAmount;

                scale.Z =
                    MathF.Max(
                        0.01f,
                        scale.Z);

                break;
        }

        gameObject
            .Transform
            .Scale =
            scale;
    }

    // =========================================================
    // Normal Move / Scale Drag Start
    // =========================================================

    private void BeginDragIfNeeded(
        GameObject selectedObject,
        GizmoAxis hoveredAxis)
    {
        if (hoveredAxis ==
                GizmoAxis.None ||
            !ImGui.IsMouseClicked(
                ImGuiMouseButton.Left))
        {
            return;
        }

        _activeAxis =
            hoveredAxis;

        _draggedObject =
            selectedObject;

        _previousMousePosition =
            ImGui.GetMousePos();

        _dragStartPosition =
            selectedObject
                .Transform
                .Position;

        _dragStartWorldPosition =
            selectedObject
                .Transform
                .WorldPosition;

        _dragStartRotation =
            selectedObject
                .Transform
                .Rotation;

        _dragStartScale =
            selectedObject
                .Transform
                .Scale;

        _dragAmount =
            0.0f;
    }

    // =========================================================
    // Orientation
    // =========================================================

    private void GetWorldAxes(
        GameObject gameObject,
        out Vector3 xAxis,
        out Vector3 yAxis,
        out Vector3 zAxis)
    {
        if (Orientation ==
            TransformGizmoOrientation.Global)
        {
            xAxis =
                Vector3.UnitX;

            yAxis =
                Vector3.UnitY;

            zAxis =
                Vector3.UnitZ;

            return;
        }

        Vector3 rotation =
            gameObject
                .Transform
                .WorldRotation;

        float rotationX =
            DegreesToRadians(
                rotation.X);

        float rotationY =
            DegreesToRadians(
                rotation.Y);

        float rotationZ =
            DegreesToRadians(
                rotation.Z);

        Matrix4x4 rotationMatrix =
            Matrix4x4.CreateRotationX(
                rotationX) *
            Matrix4x4.CreateRotationY(
                rotationY) *
            Matrix4x4.CreateRotationZ(
                rotationZ);

        xAxis =
            Vector3.Normalize(
                Vector3.TransformNormal(
                    Vector3.UnitX,
                    rotationMatrix));

        yAxis =
            Vector3.Normalize(
                Vector3.TransformNormal(
                    Vector3.UnitY,
                    rotationMatrix));

        zAxis =
            Vector3.Normalize(
                Vector3.TransformNormal(
                    Vector3.UnitZ,
                    rotationMatrix));
    }

    // =========================================================
    // Snapping
    // =========================================================

    private bool ShouldSnap()
    {
        bool controlHeld =
            ImGui.IsKeyDown(
                ImGuiKey.LeftCtrl) ||
            ImGui.IsKeyDown(
                ImGuiKey.RightCtrl);

        return
            SnappingEnabled ||
            controlHeld;
    }

    private static float SnapValue(
        float value,
        float increment)
    {
        if (increment <=
            0.000001f)
        {
            return value;
        }

        return
            MathF.Round(
                value /
                increment) *
            increment;
    }

    // =========================================================
    // Axis Helpers
    // =========================================================

    private void GetAxisInformation(
        GameObject gameObject,
        Vector2 center,
        Vector2 xEnd,
        Vector2 yEnd,
        Vector2 zEnd,
        out Vector2 screenAxis,
        out Vector3 worldAxis)
    {
        GetWorldAxes(
            gameObject,
            out Vector3 xWorldAxis,
            out Vector3 yWorldAxis,
            out Vector3 zWorldAxis);

        switch (_activeAxis)
        {
            case GizmoAxis.X:

                screenAxis =
                    xEnd -
                    center;

                worldAxis =
                    xWorldAxis;

                break;

            case GizmoAxis.Y:

                screenAxis =
                    yEnd -
                    center;

                worldAxis =
                    yWorldAxis;

                break;

            case GizmoAxis.Z:

                screenAxis =
                    zEnd -
                    center;

                worldAxis =
                    zWorldAxis;

                break;

            default:

                screenAxis =
                    Vector2.Zero;

                worldAxis =
                    Vector3.Zero;

                break;
        }
    }

    private GizmoAxis FindHoveredAxis(
        Vector2 mouse,
        Vector2 center,
        Vector2 xEnd,
        Vector2 yEnd,
        Vector2 zEnd)
    {
        if (IsDragging)
            return GizmoAxis.None;

        const float hitRadius =
            8.0f;

        float bestDistance =
            hitRadius;

        GizmoAxis hovered =
            GizmoAxis.None;

        float xDistance =
            DistanceToSegment(
                mouse,
                center,
                xEnd);

        if (xDistance <
            bestDistance)
        {
            bestDistance =
                xDistance;

            hovered =
                GizmoAxis.X;
        }

        float yDistance =
            DistanceToSegment(
                mouse,
                center,
                yEnd);

        if (yDistance <
            bestDistance)
        {
            bestDistance =
                yDistance;

            hovered =
                GizmoAxis.Y;
        }

        float zDistance =
            DistanceToSegment(
                mouse,
                center,
                zEnd);

        if (zDistance <
            bestDistance)
        {
            hovered =
                GizmoAxis.Z;
        }

        return hovered;
    }

    // =========================================================
    // Axis Projection
    // =========================================================

    private bool TryGetAxisScreenPositions(
        GameObject selectedObject,
        EditorCamera camera,
        Vector2 imagePosition,
        Vector2 imageSize,
        out Vector2 center,
        out Vector2 xEnd,
        out Vector2 yEnd,
        out Vector2 zEnd,
        out float worldAxisLength)
    {
        center =
            Vector2.Zero;

        xEnd =
            Vector2.Zero;

        yEnd =
            Vector2.Zero;

        zEnd =
            Vector2.Zero;

        worldAxisLength =
            1.0f;

        Vector3 objectPosition =
            selectedObject
                .Transform
                .WorldPosition;

        if (!UsePivotPosition && selectedObject.GetComponent<MeshRenderer>() is { } renderer)
        {
            Vector3 localCenter = renderer.MeshData.BoundsCenter * selectedObject.Transform.WorldScale;
            Vector3 rotation = selectedObject.Transform.WorldRotation * (MathF.PI / 180.0f);
            Quaternion orientation = Quaternion.CreateFromYawPitchRoll(rotation.Y, rotation.X, rotation.Z);
            objectPosition += Vector3.Transform(localCenter, orientation);
        }

        if (!ProjectWorldToScreen(
                objectPosition,
                camera,
                imagePosition,
                imageSize,
                out center))
        {
            return false;
        }

        float cameraDistance =
            Vector3.Distance(
                camera.Position,
                objectPosition);

        worldAxisLength =
            Math.Clamp(
                cameraDistance *
                0.14f,
                0.35f,
                6.0f);

        GetWorldAxes(
            selectedObject,
            out Vector3 xAxis,
            out Vector3 yAxis,
            out Vector3 zAxis);

        bool xVisible =
            ProjectWorldToScreen(
                objectPosition +
                xAxis *
                worldAxisLength,
                camera,
                imagePosition,
                imageSize,
                out xEnd);

        bool yVisible =
            ProjectWorldToScreen(
                objectPosition +
                yAxis *
                worldAxisLength,
                camera,
                imagePosition,
                imageSize,
                out yEnd);

        bool zVisible =
            ProjectWorldToScreen(
                objectPosition +
                zAxis *
                worldAxisLength,
                camera,
                imagePosition,
                imageSize,
                out zEnd);

        return
            xVisible ||
            yVisible ||
            zVisible;
    }

    // =========================================================
    // Move / Scale Drawing
    // =========================================================

    private void DrawAxisGizmo(
        Vector2 center,
        Vector2 xEnd,
        Vector2 yEnd,
        Vector2 zEnd,
        GizmoAxis hoveredAxis,
        bool scaleMode)
    {
        ImDrawListPtr drawList =
            ImGui.GetWindowDrawList();

        uint xColor =
            GetAxisColor(
                GizmoAxis.X,
                hoveredAxis,
                GetNormalAxisColor(
                    GizmoAxis.X));

        uint yColor =
            GetAxisColor(
                GizmoAxis.Y,
                hoveredAxis,
                GetNormalAxisColor(
                    GizmoAxis.Y));

        uint zColor =
            GetAxisColor(
                GizmoAxis.Z,
                hoveredAxis,
                GetNormalAxisColor(
                    GizmoAxis.Z));

        DrawAxis(
            drawList,
            center,
            xEnd,
            xColor,
            scaleMode);

        DrawAxis(
            drawList,
            center,
            yEnd,
            yColor,
            scaleMode);

        DrawAxis(
            drawList,
            center,
            zEnd,
            zColor,
            scaleMode);

        uint centerColor =
            ImGui.ColorConvertFloat4ToU32(
                new Vector4(
                    0.95f,
                    0.95f,
                    0.95f,
                    1.0f));

        drawList.AddCircleFilled(
            center,
            4.0f,
            centerColor);
    }

    private static void DrawAxis(
        ImDrawListPtr drawList,
        Vector2 start,
        Vector2 end,
        uint color,
        bool scaleMode)
    {
        drawList.AddLine(
            start,
            end,
            color,
            3.0f);

        if (scaleMode)
        {
            Vector2 min =
                end -
                new Vector2(
                    5.0f,
                    5.0f);

            Vector2 max =
                end +
                new Vector2(
                    5.0f,
                    5.0f);

            drawList.AddRectFilled(
                min,
                max,
                color);
        }
        else
        {
            drawList.AddCircleFilled(
                end,
                5.0f,
                color);
        }
    }

    private static Vector4 GetNormalAxisColor(
        GizmoAxis axis)
    {
        return axis switch
        {
            GizmoAxis.X =>
                new Vector4(
                    0.95f,
                    0.18f,
                    0.18f,
                    1.0f),

            GizmoAxis.Y =>
                new Vector4(
                    0.25f,
                    0.90f,
                    0.25f,
                    1.0f),

            GizmoAxis.Z =>
                new Vector4(
                    0.20f,
                    0.45f,
                    1.0f,
                    1.0f),

            _ =>
                Vector4.One
        };
    }

    private uint GetAxisColor(
        GizmoAxis axis,
        GizmoAxis hoveredAxis,
        Vector4 normalColor)
    {
        if (_activeAxis ==
            axis)
        {
            return
                ImGui.ColorConvertFloat4ToU32(
                    new Vector4(
                        1.0f,
                        0.85f,
                        0.20f,
                        1.0f));
        }

        if (hoveredAxis ==
            axis)
        {
            return
                ImGui.ColorConvertFloat4ToU32(
                    new Vector4(
                        1.0f,
                        1.0f,
                        0.45f,
                        1.0f));
        }

        return
            ImGui.ColorConvertFloat4ToU32(
                normalColor);
    }

    // =========================================================
    // World -> Screen Projection
    // =========================================================

    private static bool ProjectWorldToScreen(
        Vector3 worldPosition,
        EditorCamera camera,
        Vector2 imagePosition,
        Vector2 imageSize,
        out Vector2 screenPosition)
    {
        screenPosition =
            Vector2.Zero;

        Vector3 position =
            worldPosition -
            camera.Position;

        float cameraPitch =
            -DegreesToRadians(
                camera.Pitch);

        float cameraYaw =
            -DegreesToRadians(
                camera.Yaw);

        float cy =
            MathF.Cos(
                cameraYaw);

        float sy =
            MathF.Sin(
                cameraYaw);

        position =
            new Vector3(
                cy * position.X +
                sy * position.Z,

                position.Y,

                -sy * position.X +
                cy * position.Z);

        float cx =
            MathF.Cos(
                cameraPitch);

        float sx =
            MathF.Sin(
                cameraPitch);

        position =
            new Vector3(
                position.X,

                cx * position.Y -
                sx * position.Z,

                sx * position.Y +
                cx * position.Z);

        float clipW =
            -position.Z;

        if (clipW <=
            0.01f)
        {
            return false;
        }

        float aspect =
            imageSize.X /
            imageSize.Y;

        const float fieldScale =
            1.8f;

        float clipX =
            position.X *
            fieldScale /
            aspect;

        float clipY =
            position.Y *
            fieldScale;

        float ndcX =
            clipX /
            clipW;

        float ndcY =
            clipY /
            clipW;

        screenPosition =
            new Vector2(
                imagePosition.X +
                ((ndcX * 0.5f) +
                 0.5f) *
                imageSize.X,

                imagePosition.Y +
                (1.0f -
                 ((ndcY * 0.5f) +
                  0.5f)) *
                imageSize.Y);

        return true;
    }

    // =========================================================
    // Mouse Hit Testing
    // =========================================================

    private static float DistanceToSegment(
        Vector2 point,
        Vector2 start,
        Vector2 end)
    {
        Vector2 segment =
            end -
            start;

        float lengthSquared =
            segment.LengthSquared();

        if (lengthSquared <=
            0.000001f)
        {
            return
                Vector2.Distance(
                    point,
                    start);
        }

        float t =
            Vector2.Dot(
                point - start,
                segment) /
            lengthSquared;

        t =
            Math.Clamp(
                t,
                0.0f,
                1.0f);

        Vector2 closest =
            start +
            segment *
            t;

        return
            Vector2.Distance(
                point,
                closest);
    }

    // =========================================================
    // Cleanup / Helpers
    // =========================================================

    private void StopDragging()
    {
        _activeAxis =
            GizmoAxis.None;

        _draggedObject =
            null;

        _dragAmount =
            0.0f;
    }

    private static float DegreesToRadians(
        float degrees)
    {
        return
            degrees *
            (MathF.PI / 180.0f);
    }
}
