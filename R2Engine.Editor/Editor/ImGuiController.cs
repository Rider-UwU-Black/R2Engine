using ImGuiNET;
using Silk.NET.Input;
using Silk.NET.OpenGL;
using Silk.NET.OpenGL.Extensions.ImGui;
using Silk.NET.Windowing;

namespace R2Engine.Editor;

public sealed class ImGuiController : IDisposable
{
    private readonly Silk.NET.OpenGL.Extensions.ImGui.ImGuiController _controller;

    public ImGuiController(
        GL gl,
        IWindow window,
        IInputContext input)
    {
        _controller = new Silk.NET.OpenGL.Extensions.ImGui.ImGuiController(
            gl,
            window,
            input,
            () =>
            {
                ImGui.GetIO().ConfigFlags |= ImGuiConfigFlags.DockingEnable;
            });
    }

    public void Update(float deltaTime)
    {
        _controller.Update(deltaTime);
    }

    public void Render()
    {
        _controller.Render();
    }

    public void Dispose()
    {
        _controller.Dispose();
    }
}