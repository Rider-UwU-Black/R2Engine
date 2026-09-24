using System.Numerics;
using System.Diagnostics;
using R2Engine.Editor.Scene;
using R2Engine.Runtime.Rendering;
using Silk.NET.OpenGL;

namespace R2Engine.Editor;

public sealed class SceneRenderer : IRuntimeSceneRenderer
{
    public sealed class RendererFrameStats
    {
        public int DrawCalls { get; internal set; }
        public int VisibleObjects { get; internal set; }
        public int CulledObjects { get; internal set; }
        public long Vertices { get; internal set; }
        public long Triangles { get; internal set; }
        public int Textures { get; internal set; }
        public long TextureVramBytes { get; internal set; }
        public int DirectionalLights { get; internal set; }
        public int PointLights { get; internal set; }
        public int SpotLights { get; internal set; }
        public int SkinnedMeshes { get; internal set; }
        public float CpuRenderMilliseconds { get; internal set; }
    }

    public RendererFrameStats LastFrameStats { get; } = new();
    private readonly HashSet<string> _frameTextures = new(StringComparer.OrdinalIgnoreCase);
    private sealed class GpuMesh
    {
        public uint VertexArray;
        public uint VertexBuffer;
        public uint IndexBuffer;
        public uint IndexCount;
    }

    private readonly GL _gl;

    private readonly TextureLoader _textureLoader;
    private RenderSettings _renderSettings = new();

    // =========================================================
    // Scene Framebuffer
    // =========================================================

    private uint _framebuffer;
    private uint _colorTexture;
    private uint _depthRenderbuffer;

    private int _framebufferWidth;
    private int _framebufferHeight;

    // =========================================================
    // Mesh Cache
    // =========================================================

    private readonly Dictionary<Mesh, GpuMesh> _gpuMeshes =
        new();

    // =========================================================
    // Grid
    // =========================================================

    private uint _gridVertexArray;
    private uint _gridVertexBuffer;

    private int _gridVertexCount;

    // =========================================================
    // Shaders
    // =========================================================

    private uint _meshShaderProgram;
    private uint _gridShaderProgram;
    private uint _uiShaderProgram;
    private uint _uiVertexArray;
    private uint _uiVertexBuffer;

    public uint ColorTexture =>
        _colorTexture;

    // =========================================================
    // Constructor
    // =========================================================

    public SceneRenderer(
        GL gl)
    {
        _gl =
            gl;

        _textureLoader =
            new TextureLoader(
                gl);

        CreateGrid();

        CreateMeshShader();
        CreateGridShader();
        CreateUiRenderer();
    }

    public void ConfigureRenderSettings(RenderSettings settings)
    {
        _renderSettings = settings ?? new RenderSettings();
        _textureLoader.Filtering = _renderSettings.TextureFiltering;
    }

    // =========================================================
    // Render Scene
    // =========================================================

    public void Render(
        Scene.Scene scene,
        EditorCamera editorCamera,
        GameObject? selectedObject,
        int width,
        int height)
    {
        long renderStart = Stopwatch.GetTimestamp();
        ResetFrameStats(scene);
        if (width <= 0 ||
            height <= 0)
        {
            return;
        }

        EnsureFramebuffer(
            width,
            height);

        _gl.BindFramebuffer(
            FramebufferTarget.Framebuffer,
            _framebuffer);

        _gl.Viewport(
            0,
            0,
            (uint)width,
            (uint)height);

        _gl.Enable(
            EnableCap.DepthTest);

        _gl.Disable(EnableCap.StencilTest);
        _gl.StencilMask(0xFF);

        _gl.ClearColor(
            scene.Environment.BackgroundColor.X,
            scene.Environment.BackgroundColor.Y,
            scene.Environment.BackgroundColor.Z,
            1.0f);

        _gl.Clear(
            (uint)(
                ClearBufferMask.ColorBufferBit |
                ClearBufferMask.DepthBufferBit |
                ClearBufferMask.StencilBufferBit));

        float aspect =
            (float)width /
            height;

        // -----------------------------------------------------
        // Grid
        // -----------------------------------------------------

        RenderGrid(
            editorCamera,
            aspect);

        // -----------------------------------------------------
        // Scene Meshes
        // -----------------------------------------------------

        _gl.UseProgram(
            _meshShaderProgram);

        SetCameraUniforms(
            _meshShaderProgram,
            editorCamera,
            aspect);

        List<GameObject> editorRenderObjects = new();
        foreach (GameObject gameObject in scene.GameObjects)
        {
            if (!gameObject.IsVisibleInEditor)
                continue;

            MeshRenderer? renderer = gameObject.GetComponent<MeshRenderer>();
            if (renderer == null)
                continue;

            if (_renderSettings.FrustumCulling &&
                !IsMeshVisible(renderer, gameObject, editorCamera.Position, editorCamera.Pitch,
                    editorCamera.Yaw, aspect, 58.1092f, 0.1f, 1000.0f))
            {
                LastFrameStats.CulledObjects++;
                continue;
            }

            editorRenderObjects.Add(gameObject);
        }

        IEnumerable<GameObject> orderedEditorObjects = editorRenderObjects
            .OrderBy(gameObject => gameObject.GetComponent<MeshRenderer>()!.Material.SurfaceMode == MaterialSurfaceMode.Transparent ? 1 : 0)
            .ThenByDescending(gameObject => gameObject.GetComponent<MeshRenderer>()!.Material.SurfaceMode == MaterialSurfaceMode.Transparent
                ? Vector3.DistanceSquared(editorCamera.Position, gameObject.Transform.WorldPosition)
                : 0.0f);

        foreach (
            GameObject gameObject
            in orderedEditorObjects)
        {
            if (!gameObject.IsVisibleInEditor)
                continue;

            MeshRenderer? meshRenderer =
                gameObject
                    .GetComponent<MeshRenderer>();

            if (meshRenderer ==
                null)
            {
                continue;
            }

            bool writeSelectionMask = ReferenceEquals(gameObject, selectedObject);
            if (writeSelectionMask)
            {
                _gl.Enable(EnableCap.StencilTest);
                _gl.StencilMask(0xFF);
                _gl.StencilFunc(StencilFunction.Always, 1, 0xFF);
                _gl.StencilOp(StencilOp.Keep, StencilOp.Keep, StencilOp.Replace);
            }

            RenderMesh(
                scene,
                gameObject,
                meshRenderer,
                false);

            if (writeSelectionMask)
                _gl.Disable(EnableCap.StencilTest);
        }

        // -----------------------------------------------------
        // Selection Outline
        // -----------------------------------------------------

        if (selectedObject !=
            null)
        {
            MeshRenderer? selectedMesh =
                selectedObject
                    .GetComponent<MeshRenderer>();

            if (selectedMesh !=
                null)
            {
                _gl.Enable(EnableCap.StencilTest);
                _gl.StencilMask(0x00);
                _gl.StencilFunc(StencilFunction.Notequal, 1, 0xFF);
                _gl.StencilOp(StencilOp.Keep, StencilOp.Keep, StencilOp.Keep);

                RenderMesh(
                    scene,
                    selectedObject,
                    selectedMesh,
                    true);

                _gl.StencilMask(0xFF);
                _gl.Disable(EnableCap.StencilTest);
            }
        }

        // -----------------------------------------------------
        // Restore State
        // -----------------------------------------------------

        _gl.PolygonMode(
            TriangleFace.FrontAndBack,
            PolygonMode.Fill);

        _gl.LineWidth(
            1.0f);

        _gl.Disable(
            EnableCap.StencilTest);

        _gl.BindVertexArray(
            0);

        _gl.BindTexture(
            TextureTarget.Texture2D,
            0);

        _gl.UseProgram(
            0);

        _gl.Disable(
            EnableCap.DepthTest);

        _gl.BindFramebuffer(
            FramebufferTarget.Framebuffer,
            0);
        LastFrameStats.CpuRenderMilliseconds = (float)Stopwatch.GetElapsedTime(renderStart).TotalMilliseconds;
    }

    public void RenderGame(
        Scene.Scene scene,
        GameObject cameraObject,
        Camera camera,
        int width,
        int height)
    {
        long renderStart = Stopwatch.GetTimestamp();
        ResetFrameStats(scene);
        if (width <= 0 || height <= 0)
            return;

        EnsureFramebuffer(width, height);

        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _framebuffer);
        _gl.Viewport(0, 0, (uint)width, (uint)height);
        _gl.Enable(EnableCap.DepthTest);
        _gl.ClearColor(
            scene.Environment.BackgroundColor.X,
            scene.Environment.BackgroundColor.Y,
            scene.Environment.BackgroundColor.Z,
            1.0f);
        _gl.Clear((uint)(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit));

        float aspect = (float)width / height;

        _gl.UseProgram(_meshShaderProgram);
        SetCameraUniforms(
            _meshShaderProgram,
            cameraObject.Transform.WorldPosition,
            cameraObject.Transform.WorldRotation.X,
            cameraObject.Transform.WorldRotation.Y,
            aspect,
            camera.FieldOfView,
            camera.NearClip,
            camera.FarClip);

        List<GameObject> gameRenderObjects = new();
        foreach (GameObject gameObject in scene.GameObjects)
        {
            if (!gameObject.IsActiveInHierarchy)
                continue;

            MeshRenderer? renderer = gameObject.GetComponent<MeshRenderer>();
            if (renderer == null)
                continue;

            if (_renderSettings.FrustumCulling &&
                !IsMeshVisible(renderer, gameObject, cameraObject.Transform.WorldPosition,
                    cameraObject.Transform.WorldRotation.X, cameraObject.Transform.WorldRotation.Y,
                    aspect, camera.FieldOfView, camera.NearClip, camera.FarClip))
            {
                LastFrameStats.CulledObjects++;
                continue;
            }

            gameRenderObjects.Add(gameObject);
        }

        IEnumerable<GameObject> orderedGameObjects = gameRenderObjects
            .OrderBy(gameObject => gameObject.GetComponent<MeshRenderer>()!.Material.SurfaceMode == MaterialSurfaceMode.Transparent ? 1 : 0)
            .ThenByDescending(gameObject => gameObject.GetComponent<MeshRenderer>()!.Material.SurfaceMode == MaterialSurfaceMode.Transparent
                ? Vector3.DistanceSquared(cameraObject.Transform.WorldPosition, gameObject.Transform.WorldPosition)
                : 0.0f);

        foreach (GameObject gameObject in orderedGameObjects)
        {
            if (!gameObject.IsActiveInHierarchy)
                continue;

            MeshRenderer? meshRenderer = gameObject.GetComponent<MeshRenderer>();
            if (meshRenderer != null)
                RenderMesh(scene, gameObject, meshRenderer, false);
        }

        _gl.PolygonMode(TriangleFace.FrontAndBack, PolygonMode.Fill);
        _gl.LineWidth(1.0f);
        _gl.BindVertexArray(0);
        _gl.BindTexture(TextureTarget.Texture2D, 0);
        _gl.UseProgram(0);
        _gl.Disable(EnableCap.DepthTest);
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        LastFrameStats.CpuRenderMilliseconds = (float)Stopwatch.GetElapsedTime(renderStart).TotalMilliseconds;
    }

    public void PresentGame(int sourceWidth, int sourceHeight, int destinationWidth, int destinationHeight)
    {
        _gl.BindFramebuffer(FramebufferTarget.ReadFramebuffer, _framebuffer);
        _gl.BindFramebuffer(FramebufferTarget.DrawFramebuffer, 0);
        _gl.ClearColor(0.0f, 0.0f, 0.0f, 1.0f);
        _gl.Clear((uint)ClearBufferMask.ColorBufferBit);

        float scale = MathF.Min(
            destinationWidth / (float)Math.Max(1, sourceWidth),
            destinationHeight / (float)Math.Max(1, sourceHeight));
        int presentedWidth = Math.Max(1, (int)MathF.Round(sourceWidth * scale));
        int presentedHeight = Math.Max(1, (int)MathF.Round(sourceHeight * scale));
        int offsetX = (destinationWidth - presentedWidth) / 2;
        int offsetY = (destinationHeight - presentedHeight) / 2;
        _gl.BlitFramebuffer(
            0, 0, sourceWidth, sourceHeight,
            offsetX, offsetY, offsetX + presentedWidth, offsetY + presentedHeight,
            ClearBufferMask.ColorBufferBit,
            BlitFramebufferFilter.Nearest);
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
    }

    public unsafe void RenderRuntimeUi(Scene.Scene scene, int width, int height, float pointerX, float pointerY, bool pointerDown, bool pointerReleased)
    {
        Canvas[] canvases = scene.GameObjects.Where(item => item.IsActiveInHierarchy).Select(item => item.GetComponent<Canvas>())
            .Where(canvas => canvas?.IsVisible == true).Cast<Canvas>().ToArray();
        if (canvases.Length == 0) return;
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _framebuffer);
        _gl.Viewport(0, 0, (uint)width, (uint)height);
        _gl.Disable(EnableCap.DepthTest);
        _gl.Enable(EnableCap.Blend);
        _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        _gl.UseProgram(_uiShaderProgram);
        _gl.BindVertexArray(_uiVertexArray);

        foreach (Canvas canvas in canvases)
        {
        Vector2 reference = new(MathF.Max(1, canvas.ReferenceResolution.X), MathF.Max(1, canvas.ReferenceResolution.Y));
        Vector2 pointer = new(pointerX * reference.X, pointerY * reference.Y);
        foreach (GameObject item in scene.GameObjects.Where(item => item.IsActiveInHierarchy && ReferenceEquals(Canvas.FindOwner(item), canvas)).OrderBy(item =>
                     item.GetComponent<UIPanel>()?.SortOrder ?? item.GetComponent<UIButton>()?.SortOrder ?? item.GetComponent<UIText>()?.SortOrder ?? item.GetComponent<UIImage>()?.SortOrder ?? int.MinValue))
        {
            UIPanel? panel = item.GetComponent<UIPanel>();
            UIButton? button = item.GetComponent<UIButton>();
            UIText? label = item.GetComponent<UIText>();
            UIImage? image = item.GetComponent<UIImage>();
            if (panel != null) DrawUiRect(panel.Anchor * reference + panel.Offset, panel.Size, panel.Color, reference);
            else if (button != null)
            {
            bool externallyFocused = button.IsHovered;
            bool externallyPressed = button.IsPressed;
            bool externallyClicked = button.WasClicked;
            button.WasClicked = false;
            Vector2 center = button.Anchor * reference + button.Offset;
            Vector2 half = button.Size * .5f;
            bool hovered = button.Interactable && (externallyFocused || (pointer.X >= center.X - half.X && pointer.X <= center.X + half.X && pointer.Y >= center.Y - half.Y && pointer.Y <= center.Y + half.Y));
            button.IsHovered = hovered;
            button.IsPressed = externallyPressed || (hovered && pointerDown);
            button.WasClicked = externallyClicked || (hovered && pointerReleased);
            Vector4 color = button.IsPressed ? button.PressedColor : hovered ? button.HoverColor : button.NormalColor;
            string sprite = button.IsPressed && !string.IsNullOrWhiteSpace(button.PressedSprite) ? button.PressedSprite : hovered && !string.IsNullOrWhiteSpace(button.HoverSprite) ? button.HoverSprite : button.NormalSprite;
            DrawUiRect(center, button.Size, color, reference, sprite, button.SpriteBorders);
            DrawUiText(button.Text, center, MathF.Max(4, button.FontSize), Vector4.One, reference, button.FontPath);
            if (button.WasClicked) Canvas.ApplyCanvasAction(scene, button);
            }
            else if (label != null)
            DrawUiText(label.Text, label.Anchor * reference + label.Offset, label.FontSize, label.Color, reference, label.FontPath);
            else if (image != null) DrawUiRect(image.Anchor * reference + image.Offset, image.Size, image.Tint, reference, image.TexturePath, image.SpriteBorders, sourceRect: image.SourceRect);
        }
        }

        _gl.BindVertexArray(0);
        _gl.UseProgram(0);
        _gl.Disable(EnableCap.Blend);
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
    }

    private unsafe void DrawUiRect(Vector2 center, Vector2 size, Vector4 color, Vector2 reference, string? texturePath = null, Vector4 borders = default, Vector4? region = null, Vector4 sourceRect = default)
    {
        Vector4 sourceUv = UiNineSlice.UvRegion(texturePath, sourceRect);
        if (borders != Vector4.Zero && _textureLoader.GetTexture(texturePath).HasValue)
        {
            foreach (var quad in UiNineSlice.Quads(center - size * .5f, center + size * .5f, borders, UiNineSlice.UvBorders(texturePath, borders, sourceRect), Vector2.One))
            {
                Vector2 uvMin = UiNineSlice.MapUv(quad.UvMin, sourceUv), uvMax = UiNineSlice.MapUv(quad.UvMax, sourceUv);
                DrawUiRect((quad.Min + quad.Max) * .5f, quad.Max - quad.Min, color, reference, texturePath,
                    default, new Vector4(uvMin.X, uvMin.Y, uvMax.X, uvMax.Y));
            }
            return;
        }
        Vector4 uv = region ?? sourceUv;
        float l = (center.X - size.X * .5f) / reference.X * 2 - 1;
        float r = (center.X + size.X * .5f) / reference.X * 2 - 1;
        float t = 1 - (center.Y - size.Y * .5f) / reference.Y * 2;
        float b = 1 - (center.Y + size.Y * .5f) / reference.Y * 2;
        float[] vertices = { l,b,uv.X,1-uv.W, r,b,uv.Z,1-uv.W, r,t,uv.Z,1-uv.Y, l,b,uv.X,1-uv.W, r,t,uv.Z,1-uv.Y, l,t,uv.X,1-uv.Y };
        fixed (float* data = vertices)
        {
            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _uiVertexBuffer);
            _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(vertices.Length * sizeof(float)), data, BufferUsageARB.StreamDraw);
        }
        int location = _gl.GetUniformLocation(_uiShaderProgram, "uColor");
        _gl.Uniform4(location, color.X, color.Y, color.Z, color.W);
        uint? texture = _textureLoader.GetTexture(texturePath);
        _gl.Uniform1(_gl.GetUniformLocation(_uiShaderProgram, "uUseTexture"), texture.HasValue ? 1 : 0);
        _gl.ActiveTexture(TextureUnit.Texture0); _gl.BindTexture(TextureTarget.Texture2D, texture ?? 0);
        _gl.DrawArrays(PrimitiveType.Triangles, 0, 6);
    }

    private void DrawUiText(string text, Vector2 center, float height, Vector4 color, Vector2 reference, string? fontPath=null)
    {
        if(!string.IsNullOrWhiteSpace(fontPath))
        {
            try
            {
                FontAsset font=FontAsset.Load(AssetDatabase.ToAbsolutePath(fontPath));
                string customValue=text ?? string.Empty;
                float glyphWidth=height*font.CellWidth/font.CellHeight;
                float customStartX=center.X-customValue.Length*glyphWidth*.5f;
                for(int i=0;i<customValue.Length;i++)
                {
                    int code=customValue[i]; if(code<font.FirstCharacter || code>=font.FirstCharacter+font.CharacterCount) code='?';
                    int index=code-font.FirstCharacter, column=index%font.Columns, row=index/font.Columns;
                    DrawUiRect(new(customStartX+(i+.5f)*glyphWidth,center.Y),new(glyphWidth,height),color,reference,
                        font.AtlasPath,sourceRect:new Vector4(column*font.CellWidth+.5f,row*font.CellHeight+.5f,
                            MathF.Max(1,font.CellWidth-1),MathF.Max(1,font.CellHeight-1)));
                }
                return;
            }
            catch { /* Missing/corrupt custom fonts retain the built-in fallback. */ }
        }
        string value = (text ?? string.Empty).ToUpperInvariant();
        float cell = height / 7f;
        float width = value.Length * 6 * cell;
        float startX = center.X - width * .5f;
        float startY = center.Y - height * .5f;
        for (int i = 0; i < value.Length; i++)
        {
            string[] rows = Glyph(value[i]);
            for (int y = 0; y < 7; y++) for (int x = 0; x < 5; x++)
                if (rows[y][x] == '1') DrawUiRect(new(startX + (i * 6 + x + .5f) * cell, startY + (y + .5f) * cell), new(cell, cell), color, reference);
        }
    }

    private static string[] Glyph(char c) => c switch
    {
        'A' => ["01110","10001","10001","11111","10001","10001","10001"], 'B' => ["11110","10001","10001","11110","10001","10001","11110"],
        'D' => ["11110","10001","10001","10001","10001","10001","11110"], 'E' => ["11111","10000","10000","11110","10000","10000","11111"],
        'L' => ["10000","10000","10000","10000","10000","10000","11111"], 'N' => ["10001","11001","10101","10011","10001","10001","10001"],
        'O' => ["01110","10001","10001","10001","10001","10001","01110"], 'S' => ["01111","10000","10000","01110","00001","00001","11110"],
        'T' => ["11111","00100","00100","00100","00100","00100","00100"], 'U' => ["10001","10001","10001","10001","10001","10001","01110"],
        'V' => ["10001","10001","10001","10001","10001","01010","00100"],
        'W' => ["10001","10001","10001","10101","10101","10101","01010"],
        _ => ["00000","00000","00000","00000","00000","00000","00000"]
    };

    private unsafe void CreateUiRenderer()
    {
        _uiShaderProgram = CreateProgram("#version 330 core\nlayout(location=0) in vec2 p; layout(location=1) in vec2 u; out vec2 uv; void main(){uv=u;gl_Position=vec4(p,0,1);}", "#version 330 core\nin vec2 uv; out vec4 c; uniform vec4 uColor; uniform sampler2D uTexture; uniform int uUseTexture; void main(){c=uColor*(uUseTexture!=0?texture(uTexture,uv):vec4(1));}");
        _uiVertexArray = _gl.GenVertexArray();
        _uiVertexBuffer = _gl.GenBuffer();
        _gl.BindVertexArray(_uiVertexArray);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _uiVertexBuffer);
        _gl.EnableVertexAttribArray(0);
        _gl.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), null);
        _gl.EnableVertexAttribArray(1); _gl.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), (void*)(2 * sizeof(float)));
        _gl.BindVertexArray(0);
    }

    // =========================================================
    // Mesh Rendering
    // =========================================================

    private void RenderMesh(
        Scene.Scene scene,
        GameObject gameObject,
        MeshRenderer meshRenderer,
        bool selectedOutline)
    {
        if (!string.IsNullOrWhiteSpace(meshRenderer.MeshPath) &&
            !File.Exists(AssetDatabase.ToAbsolutePath(meshRenderer.MeshPath)))
        {
            return;
        }

        Mesh mesh =
            meshRenderer.MeshData;

        bool isSkeletal = string.Equals(
            Path.GetExtension(meshRenderer.MeshPath),
            ".r2skel",
            StringComparison.OrdinalIgnoreCase);
        if (isSkeletal)
            ApplyCpuSkinning(scene, gameObject, meshRenderer.MeshPath!, mesh);

        GpuMesh gpuMesh =
            GetOrUploadMesh(
                mesh);

        if (isSkeletal)
            UpdateGpuMeshVertices(gpuMesh, mesh.VertexData);

        LastFrameStats.DrawCalls += meshRenderer.MaterialSlotCount;
        LastFrameStats.Triangles += Enumerable.Range(0, meshRenderer.MaterialSlotCount)
            .Sum(slot => meshRenderer.GetSubmesh(slot).IndexCount / 3);
        LastFrameStats.Vertices += mesh.VertexData.Length / Mesh.FloatsPerVertex;
        if (!selectedOutline)
        {
            LastFrameStats.VisibleObjects++;
            if (isSkeletal) LastFrameStats.SkinnedMeshes++;
            foreach (Material material in Enumerable.Range(0, meshRenderer.MaterialSlotCount).Select(meshRenderer.GetMaterial))
            {
                string? texturePath = material.TexturePath;
                if (!string.IsNullOrWhiteSpace(texturePath) && _frameTextures.Add(texturePath))
                {
                    LastFrameStats.Textures = _frameTextures.Count;
                    LastFrameStats.TextureVramBytes += _textureLoader.GetEstimatedVramBytes(texturePath);
                }
            }
        }

        for (int slot = 0; slot < meshRenderer.MaterialSlotCount; slot++)
            RenderGpuMesh(
                scene,
                gameObject,
                meshRenderer.GetMaterial(slot),
                meshRenderer.GetSubmesh(slot),
                gpuMesh,
                selectedOutline);
    }

    private void ResetFrameStats(Scene.Scene scene)
    {
        LastFrameStats.DrawCalls = 0;
        LastFrameStats.VisibleObjects = 0;
        LastFrameStats.CulledObjects = 0;
        LastFrameStats.Vertices = 0;
        LastFrameStats.Triangles = 0;
        LastFrameStats.Textures = 0;
        LastFrameStats.TextureVramBytes = 0;
        LastFrameStats.SkinnedMeshes = 0;
        LastFrameStats.CpuRenderMilliseconds = 0.0f;
        _frameTextures.Clear();
        LastFrameStats.DirectionalLights = scene.GameObjects.Count(item =>
            item.IsActiveInHierarchy && item.GetComponent<Light>()?.Type == LightType.Directional);
        LastFrameStats.PointLights = scene.GameObjects.Count(item =>
            item.IsActiveInHierarchy && item.GetComponent<Light>()?.Type == LightType.Point);
        LastFrameStats.SpotLights = scene.GameObjects.Count(item =>
            item.IsActiveInHierarchy && item.GetComponent<Light>()?.Type == LightType.Spot);
    }

    private static bool IsMeshVisible(
        MeshRenderer renderer,
        GameObject gameObject,
        Vector3 cameraPosition,
        float cameraPitchDegrees,
        float cameraYawDegrees,
        float aspect,
        float fieldOfViewDegrees,
        float nearClip,
        float farClip)
    {
        if (!string.IsNullOrWhiteSpace(renderer.MeshPath) &&
            !File.Exists(AssetDatabase.ToAbsolutePath(renderer.MeshPath)))
            return true;

        Mesh mesh = renderer.MeshData;
        Vector3 scale = gameObject.Transform.WorldScale;
        Vector3 scaledCenter = mesh.BoundsCenter * scale;
        const float degreesToRadians = MathF.PI / 180.0f;
        Matrix4x4 objectRotation =
            Matrix4x4.CreateRotationX(gameObject.Transform.WorldRotation.X * degreesToRadians) *
            Matrix4x4.CreateRotationY(gameObject.Transform.WorldRotation.Y * degreesToRadians) *
            Matrix4x4.CreateRotationZ(gameObject.Transform.WorldRotation.Z * degreesToRadians);
        Vector3 worldCenter = Vector3.Transform(scaledCenter, objectRotation) + gameObject.Transform.WorldPosition;

        float largestScale = MathF.Max(MathF.Abs(scale.X), MathF.Max(MathF.Abs(scale.Y), MathF.Abs(scale.Z)));
        float radius = mesh.BoundsSize.Length() * 0.5f * largestScale * 1.15f;
        radius = MathF.Max(radius, 0.01f);

        Vector3 cameraSpace = worldCenter - cameraPosition;
        float yaw = -cameraYawDegrees * degreesToRadians;
        float cy = MathF.Cos(yaw);
        float sy = MathF.Sin(yaw);
        cameraSpace = new Vector3(
            cy * cameraSpace.X + sy * cameraSpace.Z,
            cameraSpace.Y,
            -sy * cameraSpace.X + cy * cameraSpace.Z);

        float pitch = -cameraPitchDegrees * degreesToRadians;
        float cx = MathF.Cos(pitch);
        float sx = MathF.Sin(pitch);
        cameraSpace = new Vector3(
            cameraSpace.X,
            cx * cameraSpace.Y - sx * cameraSpace.Z,
            sx * cameraSpace.Y + cx * cameraSpace.Z);

        float depth = -cameraSpace.Z;
        float safeNear = MathF.Max(0.001f, nearClip);
        float safeFar = MathF.Max(safeNear + 0.001f, farClip);
        if (depth + radius < safeNear || depth - radius > safeFar)
            return false;

        float tanHalfVertical = MathF.Tan(Math.Clamp(fieldOfViewDegrees, 1.0f, 179.0f) * degreesToRadians * 0.5f);
        float sideDepth = MathF.Max(depth, safeNear);
        float verticalLimit = sideDepth * tanHalfVertical + radius;
        float horizontalLimit = sideDepth * tanHalfVertical * MathF.Max(aspect, 0.001f) + radius;
        return MathF.Abs(cameraSpace.X) <= horizontalLimit &&
               MathF.Abs(cameraSpace.Y) <= verticalLimit;
    }

    private static void ApplyCpuSkinning(
        Scene.Scene scene,
        GameObject characterRoot,
        string assetPath,
        Mesh mesh)
    {
        SkeletalAsset asset = MeshAssetCache.GetSkeletalAsset(assetPath);
        int boneCount = asset.Bones.Count;
        Matrix4x4[] bindGlobal = new Matrix4x4[boneCount];
        Matrix4x4[] currentGlobal = new Matrix4x4[boneCount];
        Matrix4x4[] skinMatrices = new Matrix4x4[boneCount];

        Dictionary<int, GameObject> boneObjects = new();
        foreach (GameObject candidate in scene.GameObjects.Where(
                     gameObject => IsDescendantOf(gameObject, characterRoot)))
        {
            SkeletonBone? marker = candidate.GetComponent<SkeletonBone>();
            int boneIndex = marker?.BoneIndex ?? asset.Bones.FindIndex(
                bone => string.Equals(bone.Name, candidate.Name, StringComparison.Ordinal));
            if (boneIndex >= 0 && boneIndex < boneCount)
                boneObjects.TryAdd(boneIndex, candidate);
        }

        for (int index = 0; index < boneCount; index++)
        {
            SkeletalBone bone = asset.Bones[index];
            Matrix4x4 bindLocal = bone.LocalBindTransform;
            Matrix4x4 currentLocal = boneObjects.TryGetValue(index, out GameObject? boneObject)
                ? CreateLocalMatrix(boneObject.Transform)
                : bindLocal;

            if (bone.ParentIndex >= 0)
            {
                bindGlobal[index] = bindLocal * bindGlobal[bone.ParentIndex];
                currentGlobal[index] = currentLocal * currentGlobal[bone.ParentIndex];
            }
            else
            {
                bindGlobal[index] = bindLocal;
                currentGlobal[index] = currentLocal;
            }

            skinMatrices[index] = Matrix4x4.Invert(bindGlobal[index], out Matrix4x4 inverseBind)
                ? inverseBind * currentGlobal[index]
                : Matrix4x4.Identity;
        }

        for (int vertexIndex = 0; vertexIndex < asset.Vertices.Count; vertexIndex++)
        {
            SkinnedVertex source = asset.Vertices[vertexIndex];
            Vector3 position = Vector3.Zero;
            Vector3 normal = Vector3.Zero;
            float totalWeight = 0.0f;
            for (int influence = 0; influence < 4; influence++)
            {
                float weight = source.BoneWeights[influence];
                int boneIndex = source.BoneIndices[influence];
                if (weight <= 0.0f || boneIndex < 0 || boneIndex >= boneCount)
                    continue;

                Matrix4x4 skin = skinMatrices[boneIndex];
                position += Vector3.Transform(source.Position, skin) * weight;
                normal += Vector3.TransformNormal(source.Normal, skin) * weight;
                totalWeight += weight;
            }

            if (totalWeight <= 0.00001f)
            {
                position = source.Position;
                normal = source.Normal;
            }
            else if (normal.LengthSquared() > 0.000001f)
            {
                normal = Vector3.Normalize(normal);
            }

            int offset = vertexIndex * Mesh.FloatsPerVertex;
            mesh.VertexData[offset] = position.X;
            mesh.VertexData[offset + 1] = position.Y;
            mesh.VertexData[offset + 2] = position.Z;
            mesh.VertexData[offset + 3] = normal.X;
            mesh.VertexData[offset + 4] = normal.Y;
            mesh.VertexData[offset + 5] = normal.Z;
        }
    }

    private static bool IsDescendantOf(GameObject gameObject, GameObject root)
    {
        for (GameObject? current = gameObject.Parent; current != null; current = current.Parent)
        {
            if (ReferenceEquals(current, root))
                return true;
        }
        return false;
    }

    private static Matrix4x4 CreateLocalMatrix(Transform transform)
    {
        const float degreesToRadians = MathF.PI / 180.0f;
        Quaternion rotation = Quaternion.CreateFromYawPitchRoll(
            transform.Rotation.Y * degreesToRadians,
            transform.Rotation.X * degreesToRadians,
            transform.Rotation.Z * degreesToRadians);
        return Matrix4x4.CreateScale(transform.Scale) *
               Matrix4x4.CreateFromQuaternion(rotation) *
               Matrix4x4.CreateTranslation(transform.Position);
    }

    private unsafe void UpdateGpuMeshVertices(GpuMesh gpuMesh, float[] vertexData)
    {
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, gpuMesh.VertexBuffer);
        fixed (float* data = vertexData)
        {
            _gl.BufferSubData(
                BufferTargetARB.ArrayBuffer,
                0,
                (nuint)(vertexData.Length * sizeof(float)),
                data);
        }
    }

    private unsafe void RenderGpuMesh(
        Scene.Scene scene,
        GameObject gameObject,
        Material material,
        MeshSubmesh submesh,
        GpuMesh gpuMesh,
        bool selectedOutline)
    {
        Vector3 position =
            gameObject
                .Transform
                .WorldPosition;

        Vector3 rotation =
            gameObject
                .Transform
                .WorldRotation;

        Vector3 scale =
            gameObject
                .Transform
                .WorldScale;

        // -----------------------------------------------------
        // Transform
        // -----------------------------------------------------

        SetVector3Uniform(
            _meshShaderProgram,
            "uPosition",
            position);

        SetVector3Uniform(
            _meshShaderProgram,
            "uRotation",
            rotation);

        SetVector3Uniform(
            _meshShaderProgram,
            "uScale",
            scale);

        // -----------------------------------------------------
        // Base Color
        // -----------------------------------------------------

        SetVector4Uniform(
            _meshShaderProgram,
            "uBaseColor",
            material.BaseColor);

        // -----------------------------------------------------
        // PS2-style directional vertex lighting
        // -----------------------------------------------------

        SetLightingUniforms(
            scene, gameObject.Layer, gameObject.IsStatic);

        _gl.Uniform1(
            _gl.GetUniformLocation(_meshShaderProgram, "uLightingMode"),
            (int)material.LightingMode);

        _gl.Uniform1(
            _gl.GetUniformLocation(_meshShaderProgram, "uSurfaceMode"),
            (int)material.SurfaceMode);

        _gl.Uniform1(
            _gl.GetUniformLocation(_meshShaderProgram, "uAlphaCutoff"),
            material.AlphaCutoff);

        _gl.Uniform2(
            _gl.GetUniformLocation(_meshShaderProgram, "uTextureTiling"),
            material.TextureTiling.X,
            material.TextureTiling.Y);

        _gl.Uniform2(
            _gl.GetUniformLocation(_meshShaderProgram, "uTextureOffset"),
            material.TextureOffset.X,
            material.TextureOffset.Y);

        _gl.Uniform1(
            _gl.GetUniformLocation(_meshShaderProgram, "uFogEnabled"),
            _renderSettings.EnableFog && scene.Environment.FogEnabled && material.ReceiveFog ? 1 : 0);
        SetVector3Uniform(_meshShaderProgram, "uFogColor", scene.Environment.FogColor);
        _gl.Uniform1(_gl.GetUniformLocation(_meshShaderProgram, "uFogStart"), scene.Environment.FogStart);
        _gl.Uniform1(_gl.GetUniformLocation(_meshShaderProgram, "uFogEnd"), scene.Environment.FogEnd);

        // -----------------------------------------------------
        // Texture
        // -----------------------------------------------------

        uint? texture =
            _textureLoader.GetTexture(
                material.TexturePath);

        bool hasTexture =
            texture.HasValue;

        int useTextureLocation =
            _gl.GetUniformLocation(
                _meshShaderProgram,
                "uUseTexture");

        _gl.Uniform1(
            useTextureLocation,
            hasTexture
                ? 1
                : 0);

        if (hasTexture)
        {
            _gl.ActiveTexture(
                TextureUnit.Texture0);

            _gl.BindTexture(
                TextureTarget.Texture2D,
                texture!.Value);

            int textureLocation =
                _gl.GetUniformLocation(
                    _meshShaderProgram,
                    "uTexture");

            _gl.Uniform1(
                textureLocation,
                0);
        }
        else
        {
            _gl.BindTexture(
                TextureTarget.Texture2D,
                0);
        }

        // -----------------------------------------------------
        // Selection Outline
        // -----------------------------------------------------

        int outlineLocation =
            _gl.GetUniformLocation(
                _meshShaderProgram,
                "uSelectedOutline");

        _gl.Uniform1(
            outlineLocation,
            selectedOutline
                ? 1
                : 0);

        int outlineWidthLocation =
            _gl.GetUniformLocation(
                _meshShaderProgram,
                "uOutlineWidth");

        _gl.Uniform1(
            outlineWidthLocation,
            selectedOutline
                ? 0.015f
                : 0.0f);

        if (selectedOutline)
        {
            _gl.PolygonMode(
                TriangleFace.FrontAndBack,
                PolygonMode.Fill);

            _gl.Enable(
                EnableCap.CullFace);

            _gl.CullFace(
                TriangleFace.Front);
        }
        else
        {
            _gl.PolygonMode(
                TriangleFace.FrontAndBack,
                PolygonMode.Fill);

            if (material.DoubleSided)
            {
                _gl.Disable(EnableCap.CullFace);
            }
            else
            {
                _gl.Enable(EnableCap.CullFace);
                _gl.CullFace(TriangleFace.Back);
            }

            if (material.SurfaceMode == MaterialSurfaceMode.Transparent)
            {
                _gl.Enable(EnableCap.Blend);
                _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
                _gl.DepthMask(false);
            }
            else
            {
                _gl.Disable(EnableCap.Blend);
                _gl.DepthMask(true);
            }
        }

        // -----------------------------------------------------
        // Draw
        // -----------------------------------------------------

        _gl.BindVertexArray(
            gpuMesh.VertexArray);

        _gl.DrawElements(
            PrimitiveType.Triangles,
            (uint)submesh.IndexCount,
            DrawElementsType.UnsignedInt,
            (void*)(submesh.IndexStart * sizeof(uint)));

        _gl.DepthMask(true);
        _gl.Disable(EnableCap.Blend);
        _gl.Disable(EnableCap.CullFace);
    }

    // =========================================================
    // GPU Mesh Upload
    // =========================================================

    private GpuMesh GetOrUploadMesh(
        Mesh mesh)
    {
        if (_gpuMeshes.TryGetValue(
                mesh,
                out GpuMesh? existing))
        {
            return existing;
        }

        GpuMesh uploaded =
            UploadMesh(
                mesh);

        _gpuMeshes.Add(
            mesh,
            uploaded);

        return uploaded;
    }

    private unsafe GpuMesh UploadMesh(
        Mesh mesh)
    {
        GpuMesh gpuMesh =
            new();

        gpuMesh.VertexArray =
            _gl.GenVertexArray();

        gpuMesh.VertexBuffer =
            _gl.GenBuffer();

        gpuMesh.IndexBuffer =
            _gl.GenBuffer();

        gpuMesh.IndexCount =
            mesh.IndexCount;

        _gl.BindVertexArray(
            gpuMesh.VertexArray);

        _gl.BindBuffer(
            BufferTargetARB.ArrayBuffer,
            gpuMesh.VertexBuffer);

        fixed (
            float* vertexData =
                mesh.VertexData)
        {
            _gl.BufferData(
                BufferTargetARB.ArrayBuffer,
                (nuint)(
                    mesh.VertexData.Length *
                    sizeof(float)),
                vertexData,
                BufferUsageARB.StaticDraw);
        }

        _gl.BindBuffer(
            BufferTargetARB.ElementArrayBuffer,
            gpuMesh.IndexBuffer);

        fixed (
            uint* indexData =
                mesh.Indices)
        {
            _gl.BufferData(
                BufferTargetARB.ElementArrayBuffer,
                (nuint)(
                    mesh.Indices.Length *
                    sizeof(uint)),
                indexData,
                BufferUsageARB.StaticDraw);
        }

        uint stride =
            Mesh.FloatsPerVertex *
            sizeof(float);

        // Position
        _gl.EnableVertexAttribArray(
            0);

        _gl.VertexAttribPointer(
            0,
            3,
            VertexAttribPointerType.Float,
            false,
            stride,
            (void*)0);

        // Normal
        _gl.EnableVertexAttribArray(
            1);

        _gl.VertexAttribPointer(
            1,
            3,
            VertexAttribPointerType.Float,
            false,
            stride,
            (void*)(
                3 *
                sizeof(float)));

        // UV
        _gl.EnableVertexAttribArray(
            2);

        _gl.VertexAttribPointer(
            2,
            2,
            VertexAttribPointerType.Float,
            false,
            stride,
            (void*)(
                6 *
                sizeof(float)));

        _gl.BindVertexArray(
            0);

        _gl.BindBuffer(
            BufferTargetARB.ArrayBuffer,
            0);

        return gpuMesh;
    }

    // =========================================================
    // Scene Light Gizmos
    // =========================================================

    public bool TryWorldToViewport(
        EditorCamera editorCamera,
        Vector3 worldPosition,
        int width,
        int height,
        out Vector2 screenPosition)
    {
        screenPosition =
            Vector2.Zero;

        if (width <= 0 ||
            height <= 0)
        {
            return false;
        }

        Vector3 position =
            worldPosition -
            editorCamera.Position;

        float cameraPitch =
            DegreesToRadians(
                -editorCamera.Pitch);

        float cameraYaw =
            DegreesToRadians(
                -editorCamera.Yaw);

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

        // Behind or effectively on the camera plane.
        if (position.Z >=
            -0.01f)
        {
            return false;
        }

        float aspect =
            (float)width /
            height;

        const float fieldScale =
            1.8f;

        float ndcX =
            (
                position.X *
                fieldScale /
                aspect
            ) /
            -position.Z;

        float ndcY =
            (
                position.Y *
                fieldScale
            ) /
            -position.Z;

        // Give the editor a little margin before dropping gizmos.
        if (ndcX < -1.2f ||
            ndcX > 1.2f ||
            ndcY < -1.2f ||
            ndcY > 1.2f)
        {
            return false;
        }

        screenPosition =
            new Vector2(
                (
                    ndcX *
                    0.5f +
                    0.5f
                ) *
                width,
                (
                    1.0f -
                    (
                        ndcY *
                        0.5f +
                        0.5f
                    )
                ) *
                height);

        return true;
    }

    public Vector3 GetLightDirection(
        GameObject lightObject)
    {
        return GetTransformForward(
            lightObject.Transform);
    }

    // =========================================================
    // Scene Picking
    // =========================================================

    public GameObject? PickGameObject(
        Scene.Scene scene,
        EditorCamera editorCamera,
        float mouseX,
        float mouseY,
        int width,
        int height)
    {
        if (width <= 0 ||
            height <= 0)
        {
            return null;
        }

        float ndcX =
            (2.0f *
             mouseX /
             width) -
            1.0f;

        float ndcY =
            1.0f -
            (2.0f *
             mouseY /
             height);

        float aspect =
            (float)width /
            height;

        const float fieldScale =
            1.8f;

        Vector3 forward =
            GetCameraForward(
                editorCamera);

        Vector3 right =
            Vector3.Cross(
                forward,
                Vector3.UnitY);

        if (right.LengthSquared() <
            0.000001f)
        {
            right =
                Vector3.UnitX;
        }
        else
        {
            right =
                Vector3.Normalize(
                    right);
        }

        Vector3 up =
            Vector3.Normalize(
                Vector3.Cross(
                    right,
                    forward));

        Vector3 rayDirection =
            forward
            +
            right *
            (
                ndcX *
                aspect /
                fieldScale
            )
            +
            up *
            (
                ndcY /
                fieldScale
            );

        rayDirection =
            Vector3.Normalize(
                rayDirection);

        Vector3 rayOrigin =
            editorCamera.Position;

        GameObject? closestObject =
            null;

        float closestDistance =
            float.MaxValue;

        foreach (
            GameObject gameObject
            in scene.GameObjects)
        {
            if (!gameObject.IsVisibleInEditor || gameObject.IsLockedInEditor)
                continue;

            MeshRenderer? meshRenderer =
                gameObject
                    .GetComponent<MeshRenderer>();

            if (meshRenderer ==
                null)
            {
                continue;
            }

            Mesh mesh =
                meshRenderer.MeshData;

            if (!RayIntersectsMesh(
                    rayOrigin,
                    rayDirection,
                    gameObject,
                    mesh,
                    out float distance))
            {
                continue;
            }

            if (distance <
                closestDistance)
            {
                closestDistance =
                    distance;

                closestObject =
                    gameObject;
            }
        }

        return closestObject;
    }

    // =========================================================
    // Generic Mesh Bounds Picking
    // =========================================================

    private static bool RayIntersectsMeshBounds(
        Vector3 rayOrigin,
        Vector3 rayDirection,
        GameObject gameObject,
        Mesh mesh,
        out float distance)
    {
        distance =
            0.0f;

        Matrix4x4 modelMatrix =
            CreateModelMatrix(
                gameObject.Transform);

        if (!Matrix4x4.Invert(
                modelMatrix,
                out Matrix4x4 inverseModel))
        {
            return false;
        }

        Vector3 localOrigin =
            Vector3.Transform(
                rayOrigin,
                inverseModel);

        Vector3 localDirection =
            Vector3.TransformNormal(
                rayDirection,
                inverseModel);

        if (!RayIntersectsAabb(
                localOrigin,
                localDirection,
                mesh.BoundsMin,
                mesh.BoundsMax,
                out float localDistance))
        {
            return false;
        }

        Vector3 localHitPoint =
            localOrigin +
            localDirection *
            localDistance;

        Vector3 worldHitPoint =
            Vector3.Transform(
                localHitPoint,
                modelMatrix);

        distance =
            Vector3.Distance(
                rayOrigin,
                worldHitPoint);

        return true;
    }

    private static bool RayIntersectsMesh(
        Vector3 rayOrigin,
        Vector3 rayDirection,
        GameObject gameObject,
        Mesh mesh,
        out float distance)
    {
        distance = 0.0f;
        Matrix4x4 modelMatrix = CreateModelMatrix(gameObject.Transform);
        if (!Matrix4x4.Invert(modelMatrix, out Matrix4x4 inverseModel)) return false;
        Vector3 localOrigin = Vector3.Transform(rayOrigin, inverseModel);
        Vector3 localDirection = Vector3.TransformNormal(rayDirection, inverseModel);
        if (!RayIntersectsAabb(localOrigin, localDirection, mesh.BoundsMin, mesh.BoundsMax, out _))
            return false;

        float closestLocal = float.MaxValue;
        for (int index = 0; index + 2 < mesh.Indices.Length; index += 3)
        {
            Vector3 a = MeshVertex(mesh, mesh.Indices[index]);
            Vector3 b = MeshVertex(mesh, mesh.Indices[index + 1]);
            Vector3 c = MeshVertex(mesh, mesh.Indices[index + 2]);
            if (RayIntersectsTriangle(localOrigin, localDirection, a, b, c, out float triangleDistance) &&
                triangleDistance < closestLocal)
                closestLocal = triangleDistance;
        }
        if (closestLocal == float.MaxValue) return false;
        Vector3 worldHit = Vector3.Transform(localOrigin + localDirection * closestLocal, modelMatrix);
        distance = Vector3.Distance(rayOrigin, worldHit);
        return true;
    }

    private static Vector3 MeshVertex(Mesh mesh, uint index)
    {
        int offset = checked((int)index * Mesh.FloatsPerVertex);
        return new Vector3(mesh.VertexData[offset], mesh.VertexData[offset + 1], mesh.VertexData[offset + 2]);
    }

    private static bool RayIntersectsTriangle(
        Vector3 origin, Vector3 direction, Vector3 a, Vector3 b, Vector3 c, out float distance)
    {
        distance = 0.0f;
        Vector3 edge1 = b - a;
        Vector3 edge2 = c - a;
        Vector3 p = Vector3.Cross(direction, edge2);
        float determinant = Vector3.Dot(edge1, p);
        if (MathF.Abs(determinant) < 0.0000001f) return false;
        float inverse = 1.0f / determinant;
        Vector3 t = origin - a;
        float u = Vector3.Dot(t, p) * inverse;
        if (u < 0.0f || u > 1.0f) return false;
        Vector3 q = Vector3.Cross(t, edge1);
        float v = Vector3.Dot(direction, q) * inverse;
        if (v < 0.0f || u + v > 1.0f) return false;
        distance = Vector3.Dot(edge2, q) * inverse;
        return distance >= 0.0f;
    }

    // =========================================================
    // Model Matrix
    // =========================================================

    private static Matrix4x4 CreateModelMatrix(
        Transform transform)
    {
        Matrix4x4 scaleMatrix =
            Matrix4x4.CreateScale(
                transform.WorldScale);

        float rotationX =
            DegreesToRadians(
                transform.WorldRotation.X);

        float rotationY =
            DegreesToRadians(
                transform.WorldRotation.Y);

        float rotationZ =
            DegreesToRadians(
                transform.WorldRotation.Z);

        Matrix4x4 rotationMatrixX =
            Matrix4x4.CreateRotationX(
                rotationX);

        Matrix4x4 rotationMatrixY =
            Matrix4x4.CreateRotationY(
                rotationY);

        Matrix4x4 rotationMatrixZ =
            Matrix4x4.CreateRotationZ(
                rotationZ);

        Matrix4x4 translationMatrix =
            Matrix4x4.CreateTranslation(
                transform.WorldPosition);

        return
            scaleMatrix *
            rotationMatrixX *
            rotationMatrixY *
            rotationMatrixZ *
            translationMatrix;
    }

    // =========================================================
    // Ray vs AABB
    // =========================================================

    private static bool RayIntersectsAabb(
        Vector3 origin,
        Vector3 direction,
        Vector3 min,
        Vector3 max,
        out float distance)
    {
        distance =
            0.0f;

        float tMin =
            0.0f;

        float tMax =
            float.MaxValue;

        if (!IntersectAxis(
                origin.X,
                direction.X,
                min.X,
                max.X,
                ref tMin,
                ref tMax))
        {
            return false;
        }

        if (!IntersectAxis(
                origin.Y,
                direction.Y,
                min.Y,
                max.Y,
                ref tMin,
                ref tMax))
        {
            return false;
        }

        if (!IntersectAxis(
                origin.Z,
                direction.Z,
                min.Z,
                max.Z,
                ref tMin,
                ref tMax))
        {
            return false;
        }

        distance =
            tMin;

        return true;
    }

    private static bool IntersectAxis(
        float origin,
        float direction,
        float min,
        float max,
        ref float tMin,
        ref float tMax)
    {
        const float epsilon =
            0.000001f;

        if (MathF.Abs(
                direction) <
            epsilon)
        {
            return
                origin >= min &&
                origin <= max;
        }

        float inverseDirection =
            1.0f /
            direction;

        float t1 =
            (min - origin) *
            inverseDirection;

        float t2 =
            (max - origin) *
            inverseDirection;

        if (t1 >
            t2)
        {
            (t1, t2) =
                (t2, t1);
        }

        tMin =
            MathF.Max(
                tMin,
                t1);

        tMax =
            MathF.Min(
                tMax,
                t2);

        return
            tMax >=
            tMin;
    }

    // =========================================================
    // Grid
    // =========================================================

    private unsafe void CreateGrid()
    {
        const int gridHalfSize =
            20;

        List<float> vertices =
            new();

        for (
            int i = -gridHalfSize;
            i <= gridHalfSize;
            i++)
        {
            vertices.Add(i);
            vertices.Add(0.0f);
            vertices.Add(-gridHalfSize);

            vertices.Add(i);
            vertices.Add(0.0f);
            vertices.Add(gridHalfSize);

            vertices.Add(-gridHalfSize);
            vertices.Add(0.0f);
            vertices.Add(i);

            vertices.Add(gridHalfSize);
            vertices.Add(0.0f);
            vertices.Add(i);
        }

        float[] vertexData =
            vertices.ToArray();

        _gridVertexCount =
            vertexData.Length /
            3;

        _gridVertexArray =
            _gl.GenVertexArray();

        _gridVertexBuffer =
            _gl.GenBuffer();

        _gl.BindVertexArray(
            _gridVertexArray);

        _gl.BindBuffer(
            BufferTargetARB.ArrayBuffer,
            _gridVertexBuffer);

        fixed (
            float* data =
                vertexData)
        {
            _gl.BufferData(
                BufferTargetARB.ArrayBuffer,
                (nuint)(
                    vertexData.Length *
                    sizeof(float)),
                data,
                BufferUsageARB.StaticDraw);
        }

        _gl.EnableVertexAttribArray(
            0);

        _gl.VertexAttribPointer(
            0,
            3,
            VertexAttribPointerType.Float,
            false,
            3 *
            sizeof(float),
            (void*)0);

        _gl.BindVertexArray(
            0);

        _gl.BindBuffer(
            BufferTargetARB.ArrayBuffer,
            0);
    }

    private void RenderGrid(
        EditorCamera editorCamera,
        float aspect)
    {
        _gl.UseProgram(
            _gridShaderProgram);

        SetCameraUniforms(
            _gridShaderProgram,
            editorCamera,
            aspect);

        _gl.BindVertexArray(
            _gridVertexArray);

        _gl.LineWidth(
            1.0f);

        _gl.DrawArrays(
            PrimitiveType.Lines,
            0,
            (uint)_gridVertexCount);

        _gl.BindVertexArray(
            0);
    }

    // =========================================================
    // PS2-Style Scene Lighting
    // =========================================================

    private const int MaxLocalLights =
        3;

    private void SetLightingUniforms(
        Scene.Scene scene,
        int objectLayer,
        bool objectIsStatic)
    {
        int localLightLimit = Math.Clamp(_renderSettings.MaxLocalLights, 0, MaxLocalLights);
        GameObject? directionalObject =
            null;

        Light? directionalLight =
            null;

        List<(GameObject GameObject, Light Light)> localLights =
            new();

        foreach (
            GameObject gameObject
            in scene.GameObjects)
        {
            Light? light =
                gameObject
                    .GetComponent<Light>();

            if (light ==
                null)
            {
                continue;
            }

            bool realtimeContribution = light.RealtimeEnabled &&
                (light.RealtimeLayerMask & (1u << Math.Clamp(objectLayer, 0, 31))) != 0;
            bool bakedPreviewContribution = objectIsStatic && light.BakeOnExport;
            if (!realtimeContribution && !bakedPreviewContribution)
                continue;

            if (light.Type ==
                LightType.Directional)
            {
                if (directionalLight ==
                    null)
                {
                    directionalObject =
                        gameObject;

                    directionalLight =
                        light;
                }

                continue;
            }

            if (localLights.Count <
                localLightLimit)
            {
                localLights.Add(
                    (
                        gameObject,
                        light
                    ));
            }
        }

        SetVector3Uniform(
            _meshShaderProgram,
            "uAmbientColor",
            scene.Environment.AmbientColor);

        // -----------------------------------------------------
        // Directional Light
        // -----------------------------------------------------

        int directionalEnabledLocation =
            _gl.GetUniformLocation(
                _meshShaderProgram,
                "uDirectionalEnabled");

        if (directionalObject !=
                null &&
            directionalLight !=
                null)
        {
            _gl.Uniform1(
                directionalEnabledLocation,
                1);

            Vector3 directionToLight =
                -GetTransformForward(
                    directionalObject.Transform);

            SetVector3Uniform(
                _meshShaderProgram,
                "uDirectionalDirection",
                directionToLight);

            SetVector3Uniform(
                _meshShaderProgram,
                "uDirectionalColor",
                directionalLight.Color);

            int intensityLocation =
                _gl.GetUniformLocation(
                    _meshShaderProgram,
                    "uDirectionalIntensity");

            _gl.Uniform1(
                intensityLocation,
                MathF.Max(
                    0.0f,
                    directionalLight.Intensity));
        }
        else
        {
            _gl.Uniform1(
                directionalEnabledLocation,
                0);

            SetVector3Uniform(
                _meshShaderProgram,
                "uDirectionalDirection",
                Vector3.UnitY);

            SetVector3Uniform(
                _meshShaderProgram,
                "uDirectionalColor",
                Vector3.One);

            int intensityLocation =
                _gl.GetUniformLocation(
                    _meshShaderProgram,
                    "uDirectionalIntensity");

            _gl.Uniform1(
                intensityLocation,
                0.0f);
        }

        // -----------------------------------------------------
        // Local Lights
        // -----------------------------------------------------
        //
        // Type 0 = Point
        // Type 1 = Spot
        //
        // Point + Spot share one small budget so the eventual
        // PS2 renderer has a predictable per-mesh light count.
        // -----------------------------------------------------

        int localCountLocation =
            _gl.GetUniformLocation(
                _meshShaderProgram,
                "uLocalLightCount");

        _gl.Uniform1(
            localCountLocation,
            localLights.Count);

        for (
            int i = 0;
            i < MaxLocalLights;
            i++)
        {
            int type =
                0;

            Vector3 position =
                Vector3.Zero;

            Vector3 color =
                Vector3.One;

            Vector3 direction =
                new Vector3(
                    0.0f,
                    0.0f,
                    -1.0f);

            float intensity =
                0.0f;

            float range =
                1.0f;

            float spotCos =
                -1.0f;

            if (i <
                localLights.Count)
            {
                GameObject lightObject =
                    localLights[i]
                        .GameObject;

                Light light =
                    localLights[i]
                        .Light;

                type =
                    light.Type ==
                        LightType.Spot
                        ? 1
                        : 0;

                position =
                    lightObject
                        .Transform
                        .WorldPosition;

                color =
                    light.Color;

                direction =
                    GetTransformForward(
                        lightObject.Transform);

                intensity =
                    MathF.Max(
                        0.0f,
                        light.Intensity);

                range =
                    MathF.Max(
                        0.001f,
                        light.Range);

                float clampedAngle =
                    Math.Clamp(
                        light.SpotAngle,
                        1.0f,
                        179.0f);

                spotCos =
                    MathF.Cos(
                        DegreesToRadians(
                            clampedAngle *
                            0.5f));
            }

            int typeLocation =
                _gl.GetUniformLocation(
                    _meshShaderProgram,
                    $"uLocalLightType[{i}]");

            _gl.Uniform1(
                typeLocation,
                type);

            SetVector3Uniform(
                _meshShaderProgram,
                $"uLocalLightPosition[{i}]",
                position);

            SetVector3Uniform(
                _meshShaderProgram,
                $"uLocalLightColor[{i}]",
                color);

            SetVector3Uniform(
                _meshShaderProgram,
                $"uLocalLightDirection[{i}]",
                direction);

            int intensityLocation =
                _gl.GetUniformLocation(
                    _meshShaderProgram,
                    $"uLocalLightIntensity[{i}]");

            _gl.Uniform1(
                intensityLocation,
                intensity);

            int rangeLocation =
                _gl.GetUniformLocation(
                    _meshShaderProgram,
                    $"uLocalLightRange[{i}]");

            _gl.Uniform1(
                rangeLocation,
                range);

            int spotCosLocation =
                _gl.GetUniformLocation(
                    _meshShaderProgram,
                    $"uLocalLightSpotCos[{i}]");

            _gl.Uniform1(
                spotCosLocation,
                spotCos);
        }
    }

    private static Vector3 GetTransformForward(
        Transform transform)
    {
        Vector3 forward =
            new Vector3(
                0.0f,
                0.0f,
                -1.0f);

        float rotationX =
            DegreesToRadians(
                transform.WorldRotation.X);

        float rotationY =
            DegreesToRadians(
                transform.WorldRotation.Y);

        float rotationZ =
            DegreesToRadians(
                transform.WorldRotation.Z);

        Matrix4x4 rotationMatrix =
            Matrix4x4.CreateRotationX(
                rotationX) *
            Matrix4x4.CreateRotationY(
                rotationY) *
            Matrix4x4.CreateRotationZ(
                rotationZ);

        forward =
            Vector3.TransformNormal(
                forward,
                rotationMatrix);

        if (forward.LengthSquared() <
            0.000001f)
        {
            return new Vector3(
                0.0f,
                0.0f,
                -1.0f);
        }

        return Vector3.Normalize(
            forward);
    }

    // =========================================================
    // Uniform Helpers
    // =========================================================

    private void SetCameraUniforms(
        uint shaderProgram,
        EditorCamera editorCamera,
        float aspect)
    {
        SetCameraUniforms(
            shaderProgram,
            editorCamera.Position,
            editorCamera.Pitch,
            editorCamera.Yaw,
            aspect,
            58.1092f,
            0.1f,
            1000.0f);
    }

    private void SetCameraUniforms(
        uint shaderProgram,
        Vector3 position,
        float pitch,
        float yaw,
        float aspect,
        float fieldOfView,
        float nearClip,
        float farClip)
    {
        int aspectLocation =
            _gl.GetUniformLocation(
                shaderProgram,
                "uAspect");

        _gl.Uniform1(
            aspectLocation,
            aspect);

        SetVector3Uniform(
            shaderProgram,
            "uCameraPosition",
            position);

        int cameraRotationLocation =
            _gl.GetUniformLocation(
                shaderProgram,
                "uCameraRotation");

        _gl.Uniform2(
            cameraRotationLocation,
            pitch,
            yaw);

        _gl.Uniform1(
            _gl.GetUniformLocation(shaderProgram, "uFieldScale"),
            1.0f / MathF.Tan(Math.Clamp(fieldOfView, 1.0f, 179.0f) * MathF.PI / 360.0f));

        _gl.Uniform1(
            _gl.GetUniformLocation(shaderProgram, "uNearPlane"),
            Math.Max(0.001f, nearClip));

        _gl.Uniform1(
            _gl.GetUniformLocation(shaderProgram, "uFarPlane"),
            Math.Max(nearClip + 0.001f, farClip));
    }

    private void SetVector3Uniform(
        uint shaderProgram,
        string name,
        Vector3 value)
    {
        int location =
            _gl.GetUniformLocation(
                shaderProgram,
                name);

        _gl.Uniform3(
            location,
            value.X,
            value.Y,
            value.Z);
    }

    private void SetVector4Uniform(
        uint shaderProgram,
        string name,
        Vector4 value)
    {
        int location =
            _gl.GetUniformLocation(
                shaderProgram,
                name);

        _gl.Uniform4(
            location,
            value.X,
            value.Y,
            value.Z,
            value.W);
    }

    // =========================================================
    // Camera Math
    // =========================================================

    private static Vector3 GetCameraForward(
        EditorCamera camera)
    {
        float yaw =
            DegreesToRadians(
                camera.Yaw);

        float pitch =
            DegreesToRadians(
                camera.Pitch);

        Vector3 forward =
            new Vector3(
                -MathF.Sin(yaw) *
                MathF.Cos(pitch),

                MathF.Sin(pitch),

                -MathF.Cos(yaw) *
                MathF.Cos(pitch));

        return Vector3.Normalize(
            forward);
    }

    private static float DegreesToRadians(
        float degrees)
    {
        return
            degrees *
            (MathF.PI /
             180.0f);
    }

    // =========================================================
    // Framebuffer
    // =========================================================

    private void EnsureFramebuffer(
        int width,
        int height)
    {
        if (_framebuffer !=
                0 &&
            width ==
                _framebufferWidth &&
            height ==
                _framebufferHeight)
        {
            return;
        }

        DeleteFramebuffer();

        _framebufferWidth =
            width;

        _framebufferHeight =
            height;

        _framebuffer =
            _gl.GenFramebuffer();

        _gl.BindFramebuffer(
            FramebufferTarget.Framebuffer,
            _framebuffer);

        _colorTexture =
            _gl.GenTexture();

        _gl.BindTexture(
            TextureTarget.Texture2D,
            _colorTexture);

        unsafe
        {
            _gl.TexImage2D(
                TextureTarget.Texture2D,
                0,
                InternalFormat.Rgba8,
                (uint)width,
                (uint)height,
                0,
                PixelFormat.Rgba,
                PixelType.UnsignedByte,
                null);
        }

        int minFilter =
            (int)
            TextureMinFilter.Linear;

        int magFilter =
            (int)
            TextureMagFilter.Linear;

        _gl.TexParameterI(
            GLEnum.Texture2D,
            GLEnum.TextureMinFilter,
            ref minFilter);

        _gl.TexParameterI(
            GLEnum.Texture2D,
            GLEnum.TextureMagFilter,
            ref magFilter);

        _gl.FramebufferTexture2D(
            FramebufferTarget.Framebuffer,
            FramebufferAttachment.ColorAttachment0,
            TextureTarget.Texture2D,
            _colorTexture,
            0);

        _depthRenderbuffer =
            _gl.GenRenderbuffer();

        _gl.BindRenderbuffer(
            RenderbufferTarget.Renderbuffer,
            _depthRenderbuffer);

        _gl.RenderbufferStorage(
            RenderbufferTarget.Renderbuffer,
            InternalFormat.Depth24Stencil8,
            (uint)width,
            (uint)height);

        _gl.FramebufferRenderbuffer(
            FramebufferTarget.Framebuffer,
            FramebufferAttachment.DepthStencilAttachment,
            RenderbufferTarget.Renderbuffer,
            _depthRenderbuffer);

        GLEnum status =
            _gl.CheckFramebufferStatus(
                FramebufferTarget.Framebuffer);

        if (status !=
            GLEnum.FramebufferComplete)
        {
            throw new Exception(
                $"Scene framebuffer is incomplete: {status}");
        }

        _gl.BindTexture(
            TextureTarget.Texture2D,
            0);

        _gl.BindRenderbuffer(
            RenderbufferTarget.Renderbuffer,
            0);

        _gl.BindFramebuffer(
            FramebufferTarget.Framebuffer,
            0);
    }

    // =========================================================
    // Mesh Shader
    // =========================================================

    private void CreateMeshShader()
    {
        const string vertexShaderSource = """
            #version 330 core

            layout (location = 0)
            in vec3 aPosition;

            layout (location = 1)
            in vec3 aNormal;

            layout (location = 2)
            in vec2 aUV;

            out vec2 vUV;
            out vec3 vLighting;
            out float vFogDistance;

            uniform float uAspect;
            uniform float uFieldScale;
            uniform float uNearPlane;
            uniform float uFarPlane;

            uniform vec3 uPosition;
            uniform vec3 uRotation;
            uniform vec3 uScale;
            uniform float uOutlineWidth;

            uniform vec3 uCameraPosition;
            uniform vec2 uCameraRotation;

            uniform vec3 uAmbientColor;
            uniform int uLightingMode;

            uniform int uDirectionalEnabled;
            uniform vec3 uDirectionalDirection;
            uniform vec3 uDirectionalColor;
            uniform float uDirectionalIntensity;

            const int MAX_LOCAL_LIGHTS =
                3;

            uniform int uLocalLightCount;
            uniform int uLocalLightType[MAX_LOCAL_LIGHTS];
            uniform vec3 uLocalLightPosition[MAX_LOCAL_LIGHTS];
            uniform vec3 uLocalLightColor[MAX_LOCAL_LIGHTS];
            uniform vec3 uLocalLightDirection[MAX_LOCAL_LIGHTS];
            uniform float uLocalLightIntensity[MAX_LOCAL_LIGHTS];
            uniform float uLocalLightRange[MAX_LOCAL_LIGHTS];
            uniform float uLocalLightSpotCos[MAX_LOCAL_LIGHTS];

            const float PI =
                3.14159265359;

            vec3 RotateObject(
                vec3 value)
            {
                vec3 rotation =
                    uRotation *
                    (PI / 180.0);

                float cx =
                    cos(rotation.x);

                float sx =
                    sin(rotation.x);

                value =
                    vec3(
                        value.x,

                        cx * value.y -
                        sx * value.z,

                        sx * value.y +
                        cx * value.z);

                float cy =
                    cos(rotation.y);

                float sy =
                    sin(rotation.y);

                value =
                    vec3(
                        cy * value.x +
                        sy * value.z,

                        value.y,

                        -sy * value.x +
                        cy * value.z);

                float cz =
                    cos(rotation.z);

                float sz =
                    sin(rotation.z);

                value =
                    vec3(
                        cz * value.x -
                        sz * value.y,

                        sz * value.x +
                        cz * value.y,

                        value.z);

                return value;
            }

            vec3 ApplyCamera(
                vec3 position)
            {
                position -=
                    uCameraPosition;

                float cameraPitch =
                    -uCameraRotation.x *
                    (PI / 180.0);

                float cameraYaw =
                    -uCameraRotation.y *
                    (PI / 180.0);

                float cy =
                    cos(cameraYaw);

                float sy =
                    sin(cameraYaw);

                position =
                    vec3(
                        cy * position.x +
                        sy * position.z,

                        position.y,

                        -sy * position.x +
                        cy * position.z);

                float cx =
                    cos(cameraPitch);

                float sx =
                    sin(cameraPitch);

                position =
                    vec3(
                        position.x,

                        cx * position.y -
                        sx * position.z,

                        sx * position.y +
                        cx * position.z);

                return position;
            }

            vec4 Project(
                vec3 position)
            {
                float clipX =
                    (position.x *
                     uFieldScale) /
                    uAspect;

                float clipY =
                    position.y *
                    uFieldScale;

                float clipZ =
                    ((uFarPlane +
                      uNearPlane) /
                     (uNearPlane -
                      uFarPlane)) *
                    position.z
                    +
                    ((2.0 *
                      uFarPlane *
                      uNearPlane) /
                     (uNearPlane -
                      uFarPlane));

                float clipW =
                    -position.z;

                return vec4(
                    clipX,
                    clipY,
                    clipZ,
                    clipW);
            }

            void main()
            {
                vec3 position =
                    (aPosition + aNormal * uOutlineWidth) *
                    uScale;

                position =
                    RotateObject(
                        position);

                position +=
                    uPosition;

                vec3 cameraPosition =
                    ApplyCamera(
                        position);

                gl_Position =
                    Project(
                        cameraPosition);

                vFogDistance =
                    length(cameraPosition);

                vec3 worldNormal =
                    normalize(
                        RotateObject(
                            aNormal));

                vec3 lighting =
                    uAmbientColor;

                if (uDirectionalEnabled == 1)
                {
                    float directionalDiffuse =
                        max(
                            dot(
                                worldNormal,
                                normalize(
                                    uDirectionalDirection)),
                            0.0);

                    lighting +=
                        uDirectionalColor *
                        directionalDiffuse *
                        uDirectionalIntensity;
                }

                for (
                    int i = 0;
                    i < MAX_LOCAL_LIGHTS;
                    i++)
                {
                    if (i >=
                        uLocalLightCount)
                    {
                        break;
                    }

                    vec3 lightingSamplePosition =
                        uLightingMode == 2 ? uPosition : position;

                    vec3 toLight =
                        uLocalLightPosition[i] -
                        lightingSamplePosition;

                    float distanceToLight =
                        length(
                            toLight);

                    float range =
                        max(
                            uLocalLightRange[i],
                            0.001);

                    if (distanceToLight >=
                        range)
                    {
                        continue;
                    }

                    vec3 directionToLight =
                        normalize(
                            toLight);

                    float diffuse = uLightingMode == 2
                        ? 1.0
                        : max(
                            dot(
                                worldNormal,
                                directionToLight),
                            0.0);

                    float attenuation =
                        clamp(
                            1.0 -
                            (
                                distanceToLight /
                                range
                            ),
                            0.0,
                            1.0);

                    attenuation *=
                        attenuation;

                    float cone =
                        1.0;

                    if (uLocalLightType[i] == 1)
                    {
                        // Stored direction points outward from the
                        // light. Compare it against the direction
                        // from the light toward this vertex.
                        vec3 lightToVertex =
                            -directionToLight;

                        float coneDot =
                            dot(
                                normalize(
                                    uLocalLightDirection[i]),
                                lightToVertex);

                        cone =
                            coneDot >=
                                uLocalLightSpotCos[i]
                                ? 1.0
                                : 0.0;
                    }

                    lighting +=
                        uLocalLightColor[i] *
                        diffuse *
                        attenuation *
                        cone *
                        uLocalLightIntensity[i];
                }

                // Lighting is deliberately calculated per vertex.
                // The color is interpolated across each triangle.
                vLighting =
                    uLightingMode == 1
                        ? vec3(1.0)
                        : lighting;

                vUV =
                    aUV;
            }
            """;

        const string fragmentShaderSource = """
            #version 330 core

            in vec2 vUV;
            in vec3 vLighting;
            in float vFogDistance;

            out vec4 FragColor;

            uniform int uSelectedOutline;

            uniform vec4 uBaseColor;

            uniform int uUseTexture;
            uniform sampler2D uTexture;
            uniform vec2 uTextureTiling;
            uniform vec2 uTextureOffset;
            uniform int uSurfaceMode;
            uniform float uAlphaCutoff;
            uniform int uFogEnabled;
            uniform vec3 uFogColor;
            uniform float uFogStart;
            uniform float uFogEnd;

            void main()
            {
                if (uSelectedOutline == 1)
                {
                    FragColor =
                        vec4(
                            1.0,
                            0.65,
                            0.1,
                            1.0);

                    return;
                }

                vec4 surfaceColor =
                    uBaseColor;

                if (uUseTexture == 1)
                {
                    vec4 textureColor =
                        texture(
                            uTexture,
                            vUV * uTextureTiling + uTextureOffset);

                    surfaceColor *=
                        textureColor;
                }

                if (uSurfaceMode == 1 && surfaceColor.a < uAlphaCutoff)
                {
                    discard;
                }

                vec3 finalColor = surfaceColor.rgb * vLighting;

                if (uFogEnabled == 1)
                {
                    float fogRange = max(uFogEnd - uFogStart, 0.001);
                    float fogAmount = clamp((vFogDistance - uFogStart) / fogRange, 0.0, 1.0);
                    finalColor = mix(finalColor, uFogColor, fogAmount);
                }

                FragColor = vec4(finalColor, surfaceColor.a);
            }
            """;

        _meshShaderProgram =
            CreateProgram(
                vertexShaderSource,
                fragmentShaderSource);
    }

    // =========================================================
    // Grid Shader
    // =========================================================

    private void CreateGridShader()
    {
        const string vertexShaderSource = """
            #version 330 core

            layout (location = 0)
            in vec3 aPosition;

            uniform float uAspect;
            uniform float uFieldScale;
            uniform float uNearPlane;
            uniform float uFarPlane;

            uniform vec3 uCameraPosition;
            uniform vec2 uCameraRotation;

            const float PI =
                3.14159265359;

            void main()
            {
                vec3 position =
                    aPosition;

                position -=
                    uCameraPosition;

                float cameraPitch =
                    -uCameraRotation.x *
                    (PI / 180.0);

                float cameraYaw =
                    -uCameraRotation.y *
                    (PI / 180.0);

                float cy =
                    cos(cameraYaw);

                float sy =
                    sin(cameraYaw);

                position =
                    vec3(
                        cy * position.x +
                        sy * position.z,

                        position.y,

                        -sy * position.x +
                        cy * position.z);

                float cx =
                    cos(cameraPitch);

                float sx =
                    sin(cameraPitch);

                position =
                    vec3(
                        position.x,

                        cx * position.y -
                        sx * position.z,

                        sx * position.y +
                        cx * position.z);

                float clipX =
                    (position.x *
                     uFieldScale) /
                    uAspect;

                float clipY =
                    position.y *
                    uFieldScale;

                float clipZ =
                    ((uFarPlane +
                      uNearPlane) /
                     (uNearPlane -
                      uFarPlane)) *
                    position.z
                    +
                    ((2.0 *
                      uFarPlane *
                      uNearPlane) /
                     (uNearPlane -
                      uFarPlane));

                float clipW =
                    -position.z;

                gl_Position =
                    vec4(
                        clipX,
                        clipY,
                        clipZ,
                        clipW);
            }
            """;

        const string fragmentShaderSource = """
            #version 330 core

            out vec4 FragColor;

            void main()
            {
                FragColor =
                    vec4(
                        0.32,
                        0.34,
                        0.38,
                        1.0);
            }
            """;

        _gridShaderProgram =
            CreateProgram(
                vertexShaderSource,
                fragmentShaderSource);
    }

    // =========================================================
    // Shader Helpers
    // =========================================================

    private uint CreateProgram(
        string vertexShaderSource,
        string fragmentShaderSource)
    {
        uint vertexShader =
            CompileShader(
                ShaderType.VertexShader,
                vertexShaderSource);

        uint fragmentShader =
            CompileShader(
                ShaderType.FragmentShader,
                fragmentShaderSource);

        uint program =
            _gl.CreateProgram();

        _gl.AttachShader(
            program,
            vertexShader);

        _gl.AttachShader(
            program,
            fragmentShader);

        _gl.LinkProgram(
            program);

        _gl.GetProgram(
            program,
            ProgramPropertyARB.LinkStatus,
            out int linkStatus);

        if (linkStatus !=
            (int)GLEnum.True)
        {
            string error =
                _gl.GetProgramInfoLog(
                    program);

            throw new Exception(
                $"Shader program failed to link:\n{error}");
        }

        _gl.DetachShader(
            program,
            vertexShader);

        _gl.DetachShader(
            program,
            fragmentShader);

        _gl.DeleteShader(
            vertexShader);

        _gl.DeleteShader(
            fragmentShader);

        return program;
    }

    private uint CompileShader(
        ShaderType type,
        string source)
    {
        uint shader =
            _gl.CreateShader(
                type);

        _gl.ShaderSource(
            shader,
            source);

        _gl.CompileShader(
            shader);

        _gl.GetShader(
            shader,
            ShaderParameterName.CompileStatus,
            out int status);

        if (status !=
            (int)GLEnum.True)
        {
            string error =
                _gl.GetShaderInfoLog(
                    shader);

            throw new Exception(
                $"{type} failed to compile:\n{error}");
        }

        return shader;
    }

    // =========================================================
    // Cleanup
    // =========================================================

    private void DeleteFramebuffer()
    {
        if (_depthRenderbuffer !=
            0)
        {
            _gl.DeleteRenderbuffer(
                _depthRenderbuffer);

            _depthRenderbuffer =
                0;
        }

        if (_colorTexture !=
            0)
        {
            _gl.DeleteTexture(
                _colorTexture);

            _colorTexture =
                0;
        }

        if (_framebuffer !=
            0)
        {
            _gl.DeleteFramebuffer(
                _framebuffer);

            _framebuffer =
                0;
        }
    }

    private void DeleteGpuMeshes()
    {
        foreach (
            GpuMesh gpuMesh
            in _gpuMeshes.Values)
        {
            if (gpuMesh.IndexBuffer !=
                0)
            {
                _gl.DeleteBuffer(
                    gpuMesh.IndexBuffer);
            }

            if (gpuMesh.VertexBuffer !=
                0)
            {
                _gl.DeleteBuffer(
                    gpuMesh.VertexBuffer);
            }

            if (gpuMesh.VertexArray !=
                0)
            {
                _gl.DeleteVertexArray(
                    gpuMesh.VertexArray);
            }
        }

        _gpuMeshes.Clear();
    }

    public void ClearAssetCaches()
    {
        DeleteGpuMeshes();
        _textureLoader.Clear();
    }

    public void InvalidateTexture(string texturePath)
    {
        _textureLoader.Invalidate(texturePath);
    }

    public void Dispose()
    {
        DeleteFramebuffer();

        DeleteGpuMeshes();

        if (_uiVertexBuffer != 0) _gl.DeleteBuffer(_uiVertexBuffer);
        if (_uiVertexArray != 0) _gl.DeleteVertexArray(_uiVertexArray);
        if (_uiShaderProgram != 0) _gl.DeleteProgram(_uiShaderProgram);

        _textureLoader.Dispose();

        if (_gridVertexBuffer !=
            0)
        {
            _gl.DeleteBuffer(
                _gridVertexBuffer);
        }

        if (_gridVertexArray !=
            0)
        {
            _gl.DeleteVertexArray(
                _gridVertexArray);
        }

        if (_meshShaderProgram !=
            0)
        {
            _gl.DeleteProgram(
                _meshShaderProgram);
        }

        if (_gridShaderProgram !=
            0)
        {
            _gl.DeleteProgram(
                _gridShaderProgram);
        }
    }
}
