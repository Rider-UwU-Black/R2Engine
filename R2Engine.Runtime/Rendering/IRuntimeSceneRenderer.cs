using R2Engine.Editor.Scene;

namespace R2Engine.Runtime.Rendering;

/// <summary>
/// Platform-neutral scene submission surface. A desktop backend may translate this
/// into OpenGL calls; a PS2 backend can consume the same scene/camera data without
/// exposing either API to the runtime.
/// </summary>
public interface IRuntimeSceneRenderer : IDisposable
{
    void RenderGame(
        Scene scene,
        GameObject cameraObject,
        Camera camera,
        int width,
        int height);

    void PresentGame(
        int sourceWidth,
        int sourceHeight,
        int destinationWidth,
        int destinationHeight);

    void RenderRuntimeUi(Scene scene, int width, int height, float pointerX, float pointerY, bool pointerDown, bool pointerReleased);
}
