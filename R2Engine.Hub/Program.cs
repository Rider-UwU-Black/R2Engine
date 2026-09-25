using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace R2Engine.Hub;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new HubForm());
    }
}

internal sealed class HubForm : Form
{
    private sealed record ProjectEntry(
        string Name,
        string Path,
        DateTime LastOpenedUtc,
        bool IsFavorite = false,
        string EditorVersion = "");

    private readonly string _engineRoot;
    private readonly string _editorPath;
    private readonly string _editorVersion;
    private readonly string _statePath;
    private readonly ListView _projects = new();
    private readonly TextBox _projectSearch = new();
    private readonly Label _empty = new();
    private readonly ImageList _projectRowHeight = new();
    private readonly Font _projectTitleFont = new("Segoe UI", 10f, FontStyle.Bold);
    private readonly Font _projectDetailFont = new("Segoe UI", 8.5f, FontStyle.Regular);
    private readonly Panel _sidebar = new();
    private readonly Panel _workspace = new();
    private readonly Panel _heading = new();
    private readonly Panel _content = new();
    private readonly Panel _resourcesView = new();
    private readonly Panel _creditsView = new();
    private readonly ContextMenuStrip _projectMenu = new();
    private readonly List<Button> _navigationButtons = new();
    private readonly Button _minimizeWindow = new();
    private readonly Button _closeWindow = new();
    private readonly PrivateFontCollection _iconFonts = new();
    private FontFamily? _iconFamily;
    private HubHeader? _brand;
    private DocumentationForm? _documentationWindow;
    private UserSettings _settings = new();
    private List<ProjectEntry> _entries = new();

    public HubForm()
    {
        _engineRoot = FindEngineRoot(AppContext.BaseDirectory);
        _editorPath = FindEditorExecutable(_engineRoot);
        _editorVersion = ReadExecutableVersion(_editorPath);
        _statePath = Path.Combine(R2UserData.Root, "hub-projects.json");
        _settings = UserSettings.Load();
        string iconFontPath = Path.Combine(AppContext.BaseDirectory, "Assets", "icon-works.ttf");
        if (File.Exists(iconFontPath))
        {
            _iconFonts.AddFontFile(iconFontPath);
            _iconFamily = _iconFonts.Families.FirstOrDefault();
        }

        Text = "R2Engine Project Hub";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.None;
        Size = new Size(920, 600);
        MinimumSize = new Size(920, 600);
        MaximumSize = new Size(920, 600);
        Padding = new Padding(1);
        BackColor = Color.FromArgb(184, 184, 184);
        ForeColor = Color.FromArgb(28, 28, 28);
        Font = new Font("Segoe UI", 9f);

        BuildInterface();
        ApplyRoundedWindowShape();
        ApplyTheme();
        LoadProjects();
    }

    private void BuildInterface()
    {
        _sidebar.Dock = DockStyle.Left;
        _sidebar.Width = 188;
        _sidebar.Padding = new Padding(0);
        _sidebar.Paint += (_, args) =>
            args.Graphics.DrawLine(new Pen(Color.FromArgb(45, 45, 45)), _sidebar.Width - 1, 0, _sidebar.Width - 1, _sidebar.Height);

        _brand = new HubHeader
        {
            Dock = DockStyle.None,
            Location = new Point(0, 0),
            Size = new Size(187, 108),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };
        _sidebar.Controls.Add(_brand);

        var navigation = new FlowLayoutPanel
        {
            Dock = DockStyle.None,
            Location = new Point(0, 108),
            Size = new Size(187, 180),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            Height = 180,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = new Padding(10, 10, 10, 0),
            BackColor = Color.Transparent
        };
        navigation.Controls.Add(MakeNavigationButton("Projects", (_, _) => ShowHubSection("Projects"), selected: true, icon: 'E'));
        navigation.Controls.Add(MakeNavigationButton("Resources", (_, _) => ShowHubSection("Resources"), icon: '<'));
        _sidebar.Controls.Add(navigation);

        var sidebarBottom = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 82,
            Padding = new Padding(10, 4, 10, 8),
            BackColor = Color.Transparent
        };
        var settingsButton = MakeNavigationButton("Settings", (_, _) => ShowSettings(), icon: 'j');
        settingsButton.Location = new Point(10, 4);
        var aboutButton = MakeNavigationButton("About", (_, _) => ShowAbout(), icon: 'r');
        aboutButton.Location = new Point(10, 39);
        aboutButton.Width = 80;
        var creditsButton = MakeNavigationButton("Credits", (_, _) => ShowHubSection("Credits"), icon: '!');
        creditsButton.Location = new Point(96, 39);
        creditsButton.Width = 80;
        sidebarBottom.Controls.Add(settingsButton);
        sidebarBottom.Controls.Add(aboutButton);
        sidebarBottom.Controls.Add(creditsButton);
        _sidebar.Controls.Add(sidebarBottom);

        _workspace.Dock = DockStyle.Fill;
        _heading.Dock = DockStyle.Top;
        _heading.Height = 60;
        var headingLabel = new Label
        {
            Text = "Projects",
            Location = new Point(16, 14),
            AutoSize = true,
            Font = new Font("Segoe UI", 16f, FontStyle.Bold),
            ForeColor = Color.FromArgb(38, 38, 38)
        };
        var newProjectButton = MakeButton("New Project", (_, _) => CreateProject());
        ApplyButtonIcon(newProjectButton, 'D');
        newProjectButton.AutoSize = false;
        newProjectButton.Size = new Size(132, 34);
        newProjectButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        var addProjectButton = MakeButton("Add", (_, _) => AddExisting());
        ApplyButtonIcon(addProjectButton, 'e');
        addProjectButton.AutoSize = false;
        addProjectButton.Size = new Size(86, 34);
        addProjectButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        var searchButton = new IconTextButton();
        ConfigureButton(searchButton, "", (_, _) => ToggleProjectSearch());
        searchButton.AutoSize = false;
        searchButton.Size = new Size(38, 34);
        ApplyButtonIcon(searchButton, 'K', iconOnly: true);
        searchButton.AccessibleName = "Search projects";
        searchButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _projectSearch.Visible = false;
        _projectSearch.PlaceholderText = "Search projects";
        _projectSearch.Size = new Size(190, 26);
        _projectSearch.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _projectSearch.TextChanged += (_, _) => RefreshList();
        _projectSearch.KeyDown += (_, args) =>
        {
            if (args.KeyCode != Keys.Escape) return;
            _projectSearch.Clear();
            _projectSearch.Visible = false;
            LayoutHeaderActions();
            args.SuppressKeyPress = true;
        };
        _heading.Controls.Add(headingLabel);
        _heading.Controls.Add(_projectSearch);
        _heading.Controls.Add(searchButton);
        _heading.Controls.Add(addProjectButton);
        _heading.Controls.Add(newProjectButton);
        _heading.Resize += (_, _) => LayoutHeaderActions();
        LayoutHeaderActions();

        void ToggleProjectSearch()
        {
            _projectSearch.Visible = !_projectSearch.Visible;
            if (_projectSearch.Visible)
            {
                _projectSearch.Focus();
                _projectSearch.SelectAll();
            }
            else
            {
                _projectSearch.Clear();
            }
            LayoutHeaderActions();
        }

        void LayoutHeaderActions()
        {
            newProjectButton.Location = new Point(_heading.ClientSize.Width - newProjectButton.Width - 78, 12);
            addProjectButton.Location = new Point(newProjectButton.Left - addProjectButton.Width - 8, 12);
            searchButton.Location = new Point(addProjectButton.Left - searchButton.Width - 8, 12);
            _projectSearch.Location = new Point(searchButton.Left - (_projectSearch.Visible ? _projectSearch.Width + 6 : 0), 16);
        }

        _projects.Dock = DockStyle.Fill;
        _projects.View = View.Details;
        _projects.FullRowSelect = true;
        _projects.MultiSelect = false;
        _projects.HideSelection = false;
        _projects.BackColor = Color.FromArgb(232, 232, 232);
        _projects.ForeColor = Color.FromArgb(30, 30, 30);
        _projects.BorderStyle = BorderStyle.Fixed3D;
        _projects.HeaderStyle = ColumnHeaderStyle.Nonclickable;
        _projects.OwnerDraw = true;
        _projectRowHeight.ImageSize = new Size(1, 58);
        _projectRowHeight.ColorDepth = ColorDepth.Depth32Bit;
        _projectRowHeight.Images.Add(new Bitmap(1, 58));
        _projects.SmallImageList = _projectRowHeight;
        _projects.Columns.Add("Project", 360);
        _projects.Columns.Add("Last Opened", 150);
        _projects.Columns.Add("Editor Version", 120);
        _projects.DrawColumnHeader += DrawProjectColumnHeader;
        _projects.DrawSubItem += DrawProjectSubItem;
        _projects.DrawItem += (_, _) => { };
        _projects.Resize += (_, _) => ResizeProjectColumns();
        _projects.DoubleClick += (_, _) => OpenSelected();
        _projects.KeyDown += (_, args) =>
        {
            if (args.KeyCode == Keys.Enter) OpenSelected();
            if (args.KeyCode == Keys.Delete) RemoveSelected();
        };

        _projectMenu.Opening += (_, args) =>
        {
            if (_projects.SelectedItems.Count == 0)
            {
                args.Cancel = true;
                return;
            }
            ProjectEntry entry = (ProjectEntry)_projects.SelectedItems[0].Tag!;
            _projectMenu.Items.Clear();
            _projectMenu.Items.Add("Open Project", null, (_, _) => OpenSelected());
            _projectMenu.Items.Add("Show in File Explorer", null, (_, _) => RevealSelected());
            _projectMenu.Items.Add(new ToolStripSeparator());
            _projectMenu.Items.Add("Set Project Display Name...", null, (_, _) => RenameSelectedProject());
            _projectMenu.Items.Add(entry.IsFavorite ? "Remove from Favorites" : "Add to Favorites", null,
                (_, _) => ToggleSelectedFavorite());
            _projectMenu.Items.Add(new ToolStripSeparator());
            _projectMenu.Items.Add("Remove from List", null, (_, _) => RemoveSelected());
        };
        _projects.ContextMenuStrip = _projectMenu;
        _projects.MouseDown += (_, args) =>
        {
            if (args.Button != MouseButtons.Right) return;
            ListViewHitTestInfo hit = _projects.HitTest(args.Location);
            if (hit.Item != null) hit.Item.Selected = true;
        };

        _empty.Text = "No projects yet. Create one or add an existing project folder.";
        _empty.Dock = DockStyle.Fill;
        _empty.TextAlign = ContentAlignment.MiddleCenter;
        _empty.ForeColor = Color.FromArgb(90, 90, 90);
        _empty.BackColor = Color.FromArgb(220, 220, 220);

        _content.Dock = DockStyle.Fill;
        _content.Padding = new Padding(14);
        _content.Controls.Add(_projects);
        _content.Controls.Add(_empty);

        _resourcesView.Dock = DockStyle.Fill;
        _resourcesView.Visible = false;
        var resourcesTitle = new Label
        {
            Text = "Resources",
            Location = new Point(28, 22),
            AutoSize = true,
            Font = new Font("Segoe UI", 16f, FontStyle.Bold)
        };
        var resourcesDescription = new Label
        {
            Text = "Documentation, guides, examples, and useful places for learning R2Engine.",
            Location = new Point(30, 62),
            AutoSize = true
        };
        var resourceTiles = new Panel
        {
            Location = new Point(24, 102),
            Size = new Size(650, 410),
            Anchor = AnchorStyles.Top | AnchorStyles.Left,
            Padding = new Padding(0),
            BackColor = Color.Transparent
        };
        AddResourceTile(resourceTiles, 0, 0, 'B', "Full Documentation",
            "Browse all engine, editor, gameplay, and PS2 guides.", () => OpenDocumentation());
        AddResourceTile(resourceTiles, 1, 0, 'R', "Getting Started",
            "Open the beginner introduction and first-game learning path.", () => OpenDocumentation("Welcome to R2Engine"));
        AddResourceTile(resourceTiles, 0, 1, 'h', "PS2 Development",
            "Console setup, exporting, testing, saves, and performance.", () => OpenDocumentation("Build and Test on PS2"));
        AddResourceTile(resourceTiles, 1, 1, 'V', "Examples & Tutorials",
            "Follow small, practical tutorials for common game features.", () => OpenDocumentation("Examples and Mini Tutorials"));
        AddResourceTile(resourceTiles, 0, 2, 'N', "Community",
            "Community links and shared resources will live here later.", null);
        _resourcesView.Controls.Add(resourcesTitle);
        _resourcesView.Controls.Add(resourcesDescription);
        _resourcesView.Controls.Add(resourceTiles);

        _creditsView.Dock = DockStyle.Fill;
        _creditsView.Visible = false;
        _creditsView.AutoScroll = true;
        _creditsView.Controls.Add(BuildCreditsContent());

        _workspace.Controls.Add(_content);
        _workspace.Controls.Add(_resourcesView);
        _workspace.Controls.Add(_creditsView);
        _workspace.Controls.Add(_heading);
        Controls.Add(_workspace);
        Controls.Add(_sidebar);

        ConfigureWindowButton(_minimizeWindow, "−", (_, _) => WindowState = FormWindowState.Minimized);
        ConfigureWindowButton(_closeWindow, "×", (_, _) => Close());
        Controls.Add(_minimizeWindow);
        Controls.Add(_closeWindow);
        PositionWindowButtons();
        _minimizeWindow.BringToFront();
        _closeWindow.BringToFront();
        Resize += (_, _) => PositionWindowButtons();
        EnableWindowDragging(_brand);
        EnableWindowDragging(_heading);
    }

    private Button MakeButton(string text, EventHandler action)
    {
        var button = new IconTextButton();
        ConfigureButton(button, text, action);
        return button;
    }

    private static void ConfigureWindowButton(Button button, string text, EventHandler action)
    {
        button.Text = text;
        button.Size = new Size(31, 27);
        button.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;
        button.Font = new Font("Segoe UI", 11f);
        button.TabStop = false;
        button.Click += action;
    }

    private void PositionWindowButtons()
    {
        _minimizeWindow.Location = new Point(ClientSize.Width - 62, 0);
        _closeWindow.Location = new Point(ClientSize.Width - 31, 0);
    }

    private void EnableWindowDragging(Control? control)
    {
        if (control == null) return;
        control.MouseDown += (_, args) =>
        {
            if (args.Button != MouseButtons.Left) return;
            NativeMethods.ReleaseCapture();
            NativeMethods.SendMessage(Handle, 0x00A1, (IntPtr)2, IntPtr.Zero);
        };
    }

    private static void ConfigureButton(Button button, string text, EventHandler action)
    {
        button.Text = text;
        button.AutoSize = true;
        button.Height = 30;
        button.Padding = new Padding(9, 1, 9, 1);
        button.FlatStyle = FlatStyle.Standard;
        button.BackColor = Color.FromArgb(210, 210, 210);
        button.ForeColor = Color.FromArgb(25, 25, 25);
        button.Click += action;
    }

    private Button MakeNavigationButton(string text, EventHandler action, bool selected = false, char? icon = null)
    {
        var button = new IconTextButton
        {
            Text = text,
            Width = 166,
            Height = 32,
            TextAlign = ContentAlignment.MiddleCenter,
            Padding = new Padding(0),
            FlatStyle = FlatStyle.Flat,
            UseVisualStyleBackColor = false,
            BackColor = selected ? Color.FromArgb(112, 132, 158) : Color.FromArgb(72, 72, 72),
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 9f, FontStyle.Bold),
            Margin = new Padding(0, 0, 0, 3),
            Cursor = Cursors.Hand
        };
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.BorderColor = Color.FromArgb(38, 38, 38);
        button.FlatAppearance.MouseOverBackColor = selected
            ? Color.FromArgb(122, 143, 170)
            : Color.FromArgb(88, 88, 88);
        button.FlatAppearance.MouseDownBackColor = Color.FromArgb(55, 70, 88);
        button.Click += action;
        if (icon.HasValue) ApplyButtonIcon(button, icon.Value);
        button.Tag = selected ? "selected-navigation" : "navigation";
        _navigationButtons.Add(button);
        return button;
    }

    private void ApplyButtonIcon(Button button, char glyph, bool iconOnly = false)
    {
        if (_iconFamily == null) return;
        if (!iconOnly)
            button.Font = new Font(button.Font.FontFamily, button.Font.Size, FontStyle.Bold);
        button.Image?.Dispose();
        var image = new Bitmap(20, 20);
        using (Graphics graphics = Graphics.FromImage(image))
        using (var brush = new SolidBrush(button.ForeColor))
        using (var outline = new Pen(button.ForeColor, 0.9f) { LineJoin = LineJoin.Round })
        using (var path = new GraphicsPath())
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            path.AddString(glyph.ToString(), _iconFamily, (int)FontStyle.Regular, 16f,
                PointF.Empty, StringFormat.GenericTypographic);
            RectangleF bounds = path.GetBounds();
            float scale = Math.Min(15.5f / Math.Max(1f, bounds.Width), 15.5f / Math.Max(1f, bounds.Height));
            using var transform = new Matrix();
            transform.Translate(-bounds.X, -bounds.Y);
            transform.Scale(scale, scale, MatrixOrder.Append);
            transform.Translate((image.Width - bounds.Width * scale) * 0.5f,
                (image.Height - bounds.Height * scale) * 0.5f, MatrixOrder.Append);
            path.Transform(transform);
            graphics.FillPath(brush, path);
            graphics.DrawPath(outline, path);
        }
        button.Image = image;
        if (button is IconTextButton iconButton)
            iconButton.SetIconOnly(iconOnly);
        else
        {
            button.ImageAlign = iconOnly ? ContentAlignment.MiddleCenter : ContentAlignment.MiddleLeft;
            button.TextImageRelation = TextImageRelation.Overlay;
        }
    }

    private void ShowSettings()
    {
        using var dialog = new HubSettingsForm(_settings, RemoveMissingProjects, _engineRoot, OpenProjectFolder);
        dialog.ShowDialog(this);
        _settings = dialog.Settings;
        _settings.Save();
        ApplyTheme();
    }

    private void ShowHubSection(string section)
    {
        bool showProjects = section == "Projects";
        _heading.Visible = showProjects;
        _content.Visible = showProjects;
        _resourcesView.Visible = section == "Resources";
        _creditsView.Visible = section == "Credits";
        if (_resourcesView.Visible) _resourcesView.BringToFront();
        if (_creditsView.Visible) _creditsView.BringToFront();
        foreach (Button button in _navigationButtons)
        {
            bool selected = button.Text == section;
            button.Tag = selected ? "selected-navigation" : "navigation";
            button.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
        }
        ApplyTheme();
    }

    private Control BuildCreditsContent()
    {
        var content = new Panel { Dock = DockStyle.Top, Height = 720, Padding = new Padding(30, 22, 30, 20) };
        var title = new Label
        {
            Text = "Credits",
            Location = new Point(28, 22),
            AutoSize = true,
            Font = new Font("Segoe UI", 16f, FontStyle.Bold)
        };
        var intro = new Label
        {
            Text = "R2Engine is built with the following open-source software.",
            Location = new Point(30, 62),
            MaximumSize = new Size(650, 0),
            AutoSize = true
        };
        content.Controls.Add(title);
        content.Controls.Add(intro);

        (string Name, string License, string Url)[] credits =
        [
            ("Silk.NET", "MIT License", "https://github.com/dotnet/Silk.NET"),
            ("ImGui.NET and Dear ImGui", "MIT License", "https://github.com/ImGuiNET/ImGui.NET"),
            ("AssimpNetter and Assimp", "BSD-style licenses", "https://github.com/Saalvage/AssimpNetter"),
            ("StbImageSharp and stb_image", "MIT License / Unlicense", "https://github.com/StbSharp/StbImageSharp"),
            ("Six Labors ImageSharp, Drawing, and Fonts", "Six Labors Split License", "https://github.com/SixLabors/ImageSharp/blob/main/LICENSE"),
            ("Microsoft Roslyn and System.Text.Json", "MIT License", "https://github.com/dotnet/roslyn"),
            ("OpenAL Soft", "LGPL 2.0 or later", "https://github.com/kcat/openal-soft"),
            ("GLFW", "zlib License", "https://github.com/glfw/glfw"),
            ("PS2SDK", "Academic Free License 2.0", "https://github.com/ps2dev/ps2sdk"),
            ("gsKit", "Academic Free License 2.0", "https://github.com/ps2dev/gsKit"),
            ("Icon-Works", "SIL Open Font License 1.1", "https://www.1001fonts.com/icon-works-font.html")
        ];

        int y = 110;
        foreach ((string name, string license, string url) in credits)
        {
            var nameLabel = new LinkLabel
            {
                Text = name,
                Location = new Point(30, y),
                AutoSize = true,
                Font = new Font("Segoe UI", 10f, FontStyle.Bold),
                Tag = url
            };
            nameLabel.LinkClicked += (_, _) => OpenWebLink((string)nameLabel.Tag!);
            var licenseLabel = new Label
            {
                Text = license,
                Location = new Point(30, y + 23),
                AutoSize = true
            };
            content.Controls.Add(nameLabel);
            content.Controls.Add(licenseLabel);
            y += 54;
        }
        return content;
    }

    private static void OpenWebLink(string url)
    {
        Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
    }

    private ResourceTile MakeResourceTile(char icon, string title, string description, Action? action)
    {
        var tile = new ResourceTile(icon, _iconFamily, title, description, action != null)
        {
            Size = new Size(286, 112),
            Margin = new Padding(6)
        };
        if (action != null)
            tile.Click += (_, _) => action();
        return tile;
    }

    private void AddResourceTile(
        Panel container,
        int column,
        int row,
        char icon,
        string title,
        string description,
        Action? action)
    {
        ResourceTile tile = MakeResourceTile(icon, title, description, action);
        tile.Size = new Size(310, 112);
        tile.Location = new Point(column * 322, row * 124);
        tile.Margin = new Padding(0);
        container.Controls.Add(tile);
    }

    private void OpenDocumentation(string? preferredDocument = null)
    {
        if (_documentationWindow == null || _documentationWindow.IsDisposed)
        {
            _documentationWindow = new DocumentationForm(_engineRoot, _settings.DarkTheme, preferredDocument);
            _documentationWindow.FormClosed += (_, _) => _documentationWindow = null;
            _documentationWindow.Show(this);
        }
        else
        {
            _documentationWindow.SelectDocument(preferredDocument);
            if (_documentationWindow.WindowState == FormWindowState.Minimized)
                _documentationWindow.WindowState = FormWindowState.Normal;
            _documentationWindow.BringToFront();
            _documentationWindow.Activate();
        }
    }

    private int RemoveMissingProjects()
    {
        int removed = _entries.RemoveAll(entry => !Directory.Exists(entry.Path));
        if (removed > 0)
        {
            SaveProjects();
            RefreshList();
        }
        return removed;
    }

    private void ApplyTheme()
    {
        bool dark = _settings.DarkTheme;
        Color window = dark ? Color.FromArgb(38, 38, 38) : Color.FromArgb(184, 184, 184);
        Color surface = dark ? Color.FromArgb(48, 48, 48) : Color.FromArgb(232, 232, 232);
        Color raised = dark ? Color.FromArgb(58, 58, 58) : Color.FromArgb(205, 205, 205);
        Color text = dark ? Color.FromArgb(235, 235, 235) : Color.FromArgb(28, 28, 28);
        BackColor = window;
        ForeColor = text;
        _sidebar.BackColor = dark ? Color.FromArgb(28, 28, 28) : Color.FromArgb(72, 72, 72);
        _workspace.BackColor = window;
        _content.BackColor = window;
        _heading.BackColor = raised;
        _resourcesView.BackColor = window;
        ApplyResourceTheme(_resourcesView, dark, text);
        _creditsView.BackColor = window;
        ApplyCreditsTheme(_creditsView, dark, text);
        foreach (Control control in _heading.Controls)
        {
            control.ForeColor = text;
            if (control is Button button)
                button.BackColor = dark ? Color.FromArgb(72, 72, 72) : Color.FromArgb(210, 210, 210);
            else if (control is TextBox textBox)
                textBox.BackColor = dark ? Color.FromArgb(55, 55, 55) : Color.White;
        }
        _projects.BackColor = surface;
        _projects.ForeColor = text;
        _empty.BackColor = surface;
        _empty.ForeColor = dark ? Color.FromArgb(175, 175, 175) : Color.FromArgb(90, 90, 90);
        _projectMenu.Renderer = new ToolStripProfessionalRenderer(new HubColorTable(dark));
        _projectMenu.BackColor = surface;
        _projectMenu.ForeColor = text;
        foreach (Button button in _navigationButtons)
        {
            bool selected = Equals(button.Tag, "selected-navigation");
            button.BackColor = selected ? Color.FromArgb(76, 108, 145) : _sidebar.BackColor;
            button.ForeColor = Color.White;
            button.FlatAppearance.BorderColor = dark
                ? Color.FromArgb(12, 12, 12)
                : Color.FromArgb(48, 48, 48);
            button.FlatAppearance.MouseOverBackColor = selected
                ? Color.FromArgb(88, 122, 162)
                : (dark ? Color.FromArgb(58, 58, 58) : Color.FromArgb(92, 92, 92));
        }
        _brand?.SetDarkTheme(dark);
        _minimizeWindow.BackColor = window;
        _minimizeWindow.ForeColor = text;
        _closeWindow.BackColor = window;
        _closeWindow.ForeColor = text;
        _minimizeWindow.FlatAppearance.MouseOverBackColor = dark
            ? Color.FromArgb(65, 65, 65) : Color.FromArgb(205, 205, 205);
        _closeWindow.FlatAppearance.MouseOverBackColor = Color.FromArgb(196, 52, 52);
        _projects.Invalidate();
        Invalidate(true);
    }

    private static void ApplyCreditsTheme(Control root, bool dark, Color text)
    {
        root.BackColor = dark ? Color.FromArgb(38, 38, 38) : Color.FromArgb(184, 184, 184);
        root.ForeColor = text;
        foreach (Control control in root.Controls)
        {
            control.ForeColor = text;
            if (control is LinkLabel link)
            {
                link.LinkColor = dark ? Color.FromArgb(104, 170, 235) : Color.FromArgb(25, 91, 160);
                link.ActiveLinkColor = dark ? Color.FromArgb(150, 200, 245) : Color.FromArgb(15, 65, 125);
                link.VisitedLinkColor = link.LinkColor;
            }
            ApplyCreditsTheme(control, dark, text);
        }
    }

    private static void ApplyResourceTheme(Control root, bool dark, Color text)
    {
        root.ForeColor = text;
        foreach (Control control in root.Controls)
        {
            control.ForeColor = text;
            if (control is ResourceTile tile)
                tile.SetDarkTheme(dark);
            ApplyResourceTheme(control, dark, text);
        }
    }

    private void LoadProjects()
    {
        try
        {
            if (File.Exists(_statePath))
                _entries = JsonSerializer.Deserialize<List<ProjectEntry>>(File.ReadAllText(_statePath)) ?? new();
        }
        catch
        {
            _entries = new();
        }

        string legacy = Path.Combine(_engineRoot, "R2Engine.Editor");
        if (Directory.Exists(Path.Combine(legacy, "Assets")) &&
            !_entries.Any(entry => SamePath(entry.Path, legacy)))
        {
            _entries.Add(new ProjectEntry("Current R2Engine Project", legacy, DateTime.UtcNow));
            SaveProjects();
        }

        RefreshList();
    }

    private void RefreshList()
    {
        _projects.Items.Clear();
        string search = _projectSearch.Text.Trim();
        foreach (ProjectEntry entry in _entries
                     .Where(entry => search.Length == 0 ||
                         entry.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                         entry.Path.Contains(search, StringComparison.OrdinalIgnoreCase))
                     .OrderByDescending(entry => entry.IsFavorite)
                     .ThenByDescending(entry => entry.LastOpenedUtc))
        {
            var item = new ListViewItem(entry.Name, 0) { Tag = entry };
            item.SubItems.Add(FormatRelativeTime(entry.LastOpenedUtc));
            item.SubItems.Add(string.IsNullOrWhiteSpace(entry.EditorVersion) ? _editorVersion : entry.EditorVersion);
            if (!Directory.Exists(entry.Path))
                item.ForeColor = Color.FromArgb(230, 110, 110);
            _projects.Items.Add(item);
        }
        _empty.Text = search.Length == 0
            ? "No projects yet. Create one or add an existing project folder."
            : $"No projects match '{search}'.";
        _empty.Visible = _projects.Items.Count == 0;
        _projects.Visible = !_empty.Visible;
        if (_projects.Items.Count > 0)
            _projects.Items[0].Selected = true;
        ResizeProjectColumns();
    }

    private void ResizeProjectColumns()
    {
        if (_projects.Columns.Count < 3 || _projects.ClientSize.Width <= 0)
            return;
        int available = Math.Max(360, _projects.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 6);
        _projects.Columns[0].Width = (int)(available * 0.56f);
        _projects.Columns[1].Width = (int)(available * 0.25f);
        _projects.Columns[2].Width = available - _projects.Columns[0].Width - _projects.Columns[1].Width;
    }

    private void DrawProjectColumnHeader(object? sender, DrawListViewColumnHeaderEventArgs e)
    {
        bool dark = _settings.DarkTheme;
        using var background = new SolidBrush(dark ? Color.FromArgb(55, 55, 55) : Color.FromArgb(198, 198, 198));
        using var border = new Pen(dark ? Color.FromArgb(82, 82, 82) : Color.FromArgb(145, 145, 145));
        e.Graphics.FillRectangle(background, e.Bounds);
        e.Graphics.DrawLine(border, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
        TextRenderer.DrawText(e.Graphics, e.Header?.Text ?? "", Font,
            new Rectangle(e.Bounds.X + 10, e.Bounds.Y, e.Bounds.Width - 12, e.Bounds.Height),
            dark ? Color.FromArgb(235, 235, 235) : Color.FromArgb(35, 35, 35), TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }

    private void DrawProjectSubItem(object? sender, DrawListViewSubItemEventArgs e)
    {
        if (e.Item == null || e.SubItem == null)
            return;
        bool selected = e.Item.Selected;
        bool dark = _settings.DarkTheme;
        Color backgroundColor = selected
            ? (dark ? Color.FromArgb(58, 83, 112) : Color.FromArgb(174, 199, 224))
            : (dark ? Color.FromArgb(48, 48, 48) : Color.FromArgb(232, 232, 232));
        using var background = new SolidBrush(backgroundColor);
        e.Graphics.FillRectangle(background, e.Bounds);

        if (selected)
        {
            using var selectionBorder = new Pen(Color.FromArgb(75, 116, 159));
            e.Graphics.DrawLine(selectionBorder, e.Bounds.Left, e.Bounds.Top,
                e.Bounds.Right, e.Bounds.Top);
            e.Graphics.DrawLine(selectionBorder, e.Bounds.Left, e.Bounds.Bottom - 1,
                e.Bounds.Right, e.Bounds.Bottom - 1);
        }

        Color primary = dark ? Color.FromArgb(238, 238, 238) : Color.FromArgb(28, 28, 28);
        Color secondary = dark ? Color.FromArgb(175, 175, 175) : Color.FromArgb(82, 82, 82);
        if (e.ColumnIndex == 0 && e.Item.Tag is ProjectEntry entry)
        {
            Rectangle titleBounds = new(e.Bounds.X + 12, e.Bounds.Y + 7, e.Bounds.Width - 20, 22);
            Rectangle pathBounds = new(e.Bounds.X + 12, e.Bounds.Y + 30, e.Bounds.Width - 20, 19);
            if (entry.IsFavorite)
            {
                TextRenderer.DrawText(e.Graphics, "★", _projectTitleFont,
                    new Rectangle(titleBounds.X, titleBounds.Y, 22, titleBounds.Height),
                    Color.FromArgb(190, 139, 35), TextFormatFlags.Left | TextFormatFlags.NoPadding);
                titleBounds = new Rectangle(titleBounds.X + 23, titleBounds.Y,
                    Math.Max(1, titleBounds.Width - 23), titleBounds.Height);
            }
            TextRenderer.DrawText(e.Graphics, entry.Name, _projectTitleFont, titleBounds, primary,
                TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
            TextRenderer.DrawText(e.Graphics, entry.Path, _projectDetailFont, pathBounds, secondary,
                TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        }
        else
        {
            Rectangle textBounds = new(e.Bounds.X + 10, e.Bounds.Y, e.Bounds.Width - 16, e.Bounds.Height);
            TextRenderer.DrawText(e.Graphics, e.SubItem.Text, _projectDetailFont, textBounds, primary,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }

    private static string FormatRelativeTime(DateTime utc)
    {
        TimeSpan elapsed = DateTime.UtcNow - utc;
        if (elapsed.TotalSeconds < 45) return "a few seconds ago";
        if (elapsed.TotalMinutes < 2) return "a minute ago";
        if (elapsed.TotalMinutes < 60) return $"{(int)elapsed.TotalMinutes} minutes ago";
        if (elapsed.TotalHours < 2) return "an hour ago";
        if (elapsed.TotalHours < 24) return $"{(int)elapsed.TotalHours} hours ago";
        if (elapsed.TotalDays < 2) return "yesterday";
        if (elapsed.TotalDays < 30) return $"{(int)elapsed.TotalDays} days ago";
        if (elapsed.TotalDays < 60) return "a month ago";
        if (elapsed.TotalDays < 365) return $"{(int)(elapsed.TotalDays / 30)} months ago";
        if (elapsed.TotalDays < 730) return "a year ago";
        return $"{(int)(elapsed.TotalDays / 365)} years ago";
    }

    private void CreateProject()
    {
        using var dialog = new NewProjectDialog(_settings.ResolvedProjectsFolder, _settings.DarkTheme);
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        string root = Path.GetFullPath(Path.Combine(dialog.ProjectLocation, dialog.ProjectName));
        if (Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).Any())
        {
            MessageBox.Show(this, "That project folder already exists and is not empty.", "Folder Is Not Empty",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        Directory.CreateDirectory(Path.Combine(root, "Assets", "Models"));
        Directory.CreateDirectory(Path.Combine(root, "Assets", "Textures"));
        Directory.CreateDirectory(Path.Combine(root, "Assets", "Materials"));
        Directory.CreateDirectory(Path.Combine(root, "Assets", "Prefabs"));
        Directory.CreateDirectory(Path.Combine(root, "Assets", "Scripts"));
        Directory.CreateDirectory(Path.Combine(root, "Assets", "Audio"));
        Directory.CreateDirectory(Path.Combine(root, "Assets", "Animations"));
        Directory.CreateDirectory(Path.Combine(root, "Assets", "Fonts"));
        Directory.CreateDirectory(Path.Combine(root, "Scenes"));
        File.WriteAllText(Path.Combine(root, "R2Project.json"), JsonSerializer.Serialize(new
        {
            Name = new DirectoryInfo(root).Name,
            FormatVersion = 1
        }, new JsonSerializerOptions { WriteIndented = true }));

        AddOrUpdate(new ProjectEntry(dialog.ProjectName, root, DateTime.UtcNow));
        OpenProject(root);
    }

    private void AddExisting()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Choose an R2Engine project folder containing Assets",
            UseDescriptionForTitle = true
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        string root = Path.GetFullPath(dialog.SelectedPath);
        if (!TryValidateProject(root, out string validationError))
        {
            MessageBox.Show(this, validationError, "Not an R2Engine Project",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        AddOrUpdate(new ProjectEntry(new DirectoryInfo(root).Name, root, DateTime.UtcNow,
            EditorVersion: _editorVersion));
    }

    private void OpenSelected()
    {
        if (_projects.SelectedItems.Count == 0 || _projects.SelectedItems[0].Tag is not ProjectEntry entry)
            return;
        OpenProject(entry.Path);
    }

    private void OpenProject(string root)
    {
        if (!TryValidateProject(root, out string validationError))
        {
            MessageBox.Show(this, validationError, "Project Cannot Be Opened",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (!File.Exists(_editorPath))
        {
            MessageBox.Show(this, $"The editor executable was not found:\n{_editorPath}", "Editor Missing",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        ProjectEntry? known = _entries.FirstOrDefault(entry => SamePath(entry.Path, root));
        AddOrUpdate(new ProjectEntry(known?.Name ?? new DirectoryInfo(root).Name, root, DateTime.UtcNow,
            known?.IsFavorite ?? false, _editorVersion));
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = _editorPath,
                Arguments = $"--project \"{root}\"",
                WorkingDirectory = root,
                UseShellExecute = true
            });
        }
        catch (System.ComponentModel.Win32Exception exception)
        {
            string message = exception.Message.Contains("Application Control policy", StringComparison.OrdinalIgnoreCase)
                ? "Windows Application Control blocked the R2Engine Editor.\n\n" +
                  "R2Engine preview releases are currently unsigned. If you trust this download, right-click the " +
                  "original R2Engine ZIP, choose Properties, check Unblock, apply the change, and then extract it again."
                : $"Windows could not start the R2Engine Editor.\n\n{exception.Message}";
            MessageBox.Show(this, message, "Editor Could Not Start", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        if (_settings.CloseHubAfterOpeningProject)
            Close();
    }

    private void RemoveSelected()
    {
        if (_projects.SelectedItems.Count == 0 || _projects.SelectedItems[0].Tag is not ProjectEntry entry)
            return;
        if (_settings.ConfirmBeforeRemovingProject &&
            MessageBox.Show(this,
                $"Remove '{entry.Name}' from the Hub?\n\nThis will not delete any project files.",
                "Remove Project", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;
        _entries.RemoveAll(candidate => SamePath(candidate.Path, entry.Path));
        SaveProjects();
        RefreshList();
    }

    private void RenameSelectedProject()
    {
        if (_projects.SelectedItems.Count == 0 || _projects.SelectedItems[0].Tag is not ProjectEntry entry)
            return;
        using var dialog = new ProjectNameDialog(entry.Name, _settings.DarkTheme);
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;
        string displayName = dialog.ProjectName.Trim();
        if (string.IsNullOrWhiteSpace(displayName))
            return;
        int index = _entries.FindIndex(candidate => SamePath(candidate.Path, entry.Path));
        if (index >= 0)
            _entries[index] = _entries[index] with { Name = displayName };
        SaveProjects();
        RefreshList();
        SelectProject(entry.Path);
    }

    private void ToggleSelectedFavorite()
    {
        if (_projects.SelectedItems.Count == 0 || _projects.SelectedItems[0].Tag is not ProjectEntry entry)
            return;
        int index = _entries.FindIndex(candidate => SamePath(candidate.Path, entry.Path));
        if (index >= 0)
            _entries[index] = _entries[index] with { IsFavorite = !entry.IsFavorite };
        SaveProjects();
        RefreshList();
        SelectProject(entry.Path);
    }

    private void SelectProject(string path)
    {
        foreach (ListViewItem item in _projects.Items)
        {
            if (item.Tag is ProjectEntry entry && SamePath(entry.Path, path))
            {
                item.Selected = true;
                item.EnsureVisible();
                break;
            }
        }
    }

    private void RevealSelected()
    {
        if (_projects.SelectedItems.Count == 0 || _projects.SelectedItems[0].Tag is not ProjectEntry entry)
            return;
        if (!Directory.Exists(entry.Path))
        {
            MessageBox.Show(this, "The project folder no longer exists.", "Project Missing",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"\"{entry.Path}\"",
            UseShellExecute = true
        });
    }

    private void OpenProjectFolder()
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"\"{_engineRoot}\"",
            UseShellExecute = true
        });
    }

    private void ShowAbout()
    {
        using var dialog = new AboutForm(_settings.DarkTheme);
        dialog.ShowDialog(this);
    }

    private void AddOrUpdate(ProjectEntry entry)
    {
        ProjectEntry? existing = _entries.FirstOrDefault(candidate => SamePath(candidate.Path, entry.Path));
        if (existing != null)
            entry = entry with { Name = existing.Name, IsFavorite = existing.IsFavorite };
        _entries.RemoveAll(candidate => SamePath(candidate.Path, entry.Path));
        _entries.Add(entry);
        SaveProjects();
        RefreshList();
    }

    private void SaveProjects()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_statePath)!);
        File.WriteAllText(_statePath, JsonSerializer.Serialize(_entries,
            new JsonSerializerOptions { WriteIndented = true }));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _projectRowHeight.Dispose();
            _projectTitleFont.Dispose();
            _projectDetailFont.Dispose();
        }
        base.Dispose(disposing);
    }

    protected override CreateParams CreateParams
    {
        get
        {
            const int classStyleDropShadow = 0x00020000;
            CreateParams parameters = base.CreateParams;
            parameters.ClassStyle |= classStyleDropShadow;
            return parameters;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        int roundedCorners = 2;
        NativeMethods.DwmSetWindowAttribute(Handle, 33, ref roundedCorners, sizeof(int));
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        ApplyRoundedWindowShape();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using GraphicsPath outline = CreateRoundedPath(new Rectangle(0, 0, Width - 1, Height - 1), 12);
        using var pen = new Pen(_settings.DarkTheme
            ? Color.FromArgb(78, 78, 78)
            : Color.FromArgb(125, 125, 125));
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.DrawPath(pen, outline);
    }

    private void ApplyRoundedWindowShape()
    {
        if (Width <= 1 || Height <= 1 || WindowState == FormWindowState.Maximized)
        {
            Region = null;
            return;
        }
        using GraphicsPath path = CreateRoundedPath(new Rectangle(0, 0, Width, Height), 12);
        Region? previous = Region;
        Region = new Region(path);
        previous?.Dispose();
    }

    private static GraphicsPath CreateRoundedPath(Rectangle bounds, int radius)
    {
        int diameter = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static bool SamePath(string left, string right) =>
        string.Equals(Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar),
            Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);

    private static bool TryValidateProject(string root, out string error)
    {
        if (!Directory.Exists(root))
        {
            error = "That project folder does not exist.";
            return false;
        }
        if (!Directory.Exists(Path.Combine(root, "Assets")))
        {
            error = "That folder does not contain an Assets folder.";
            return false;
        }
        string marker = Path.Combine(root, "R2Project.json");
        string legacySettings = Path.Combine(root, "ProjectSettings.json");
        if (!File.Exists(marker) && !File.Exists(legacySettings))
        {
            error = "That folder has an Assets folder, but no R2Project.json or legacy ProjectSettings.json file.";
            return false;
        }
        if (File.Exists(marker))
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(File.ReadAllText(marker));
                if (!document.RootElement.TryGetProperty("FormatVersion", out JsonElement version) ||
                    version.ValueKind != JsonValueKind.Number || version.GetInt32() != 1)
                {
                    error = "R2Project.json does not contain a supported project format version.";
                    return false;
                }
            }
            catch (Exception exception)
            {
                error = $"R2Project.json could not be read: {exception.Message}";
                return false;
            }
        }
        error = "";
        return true;
    }

    private static string ReadExecutableVersion(string executable)
    {
        try
        {
            Version? version = File.Exists(executable)
                ? FileVersionInfo.GetVersionInfo(executable).FileVersion is string value && Version.TryParse(value, out Version? parsed)
                    ? parsed
                    : null
                : null;
            return version == null ? "Unavailable" : $"{version.Major}.{version.Minor}.{version.Build}";
        }
        catch { return "Unavailable"; }
    }

    private static string FindEngineRoot(string start)
    {
        DirectoryInfo? directory = new(Path.GetFullPath(start));
        while (directory != null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "R2Engine.Editor")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate the R2Engine installation.");
    }

    private static string FindEditorExecutable(string engineRoot)
    {
        string[] candidates =
        {
            Path.Combine(engineRoot, "Editor", "R2Engine.Editor.exe"),
            Path.Combine(engineRoot, "R2Engine.Editor", "bin", "Debug", "net10.0", "R2Engine.Editor.exe"),
            Path.Combine(engineRoot, "R2Engine.Editor", "bin", "Release", "net10.0", "R2Engine.Editor.exe")
        };
        return candidates.FirstOrDefault(File.Exists) ?? candidates[0];
    }
}

internal static class NativeMethods
{
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    internal static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("dwmapi.dll")]
    internal static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int valueSize);
}

internal sealed class IconTextButton : Button
{
    private bool _hovered;
    private bool _pressed;
    private bool _iconOnly;

    internal void SetIconOnly(bool iconOnly)
    {
        _iconOnly = iconOnly;
        Invalidate();
    }

    public IconTextButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer, true);
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        _hovered = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hovered = false;
        _pressed = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs mevent)
    {
        _pressed = mevent.Button == MouseButtons.Left;
        Invalidate();
        base.OnMouseDown(mevent);
    }

    protected override void OnMouseUp(MouseEventArgs mevent)
    {
        _pressed = false;
        Invalidate();
        base.OnMouseUp(mevent);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Color fill = BackColor;
        if (_pressed && FlatAppearance.MouseDownBackColor != Color.Empty)
            fill = FlatAppearance.MouseDownBackColor;
        else if (_hovered && FlatAppearance.MouseOverBackColor != Color.Empty)
            fill = FlatAppearance.MouseOverBackColor;
        if (!Enabled) fill = ControlPaint.Light(fill, 0.08f);

        using (var brush = new SolidBrush(fill))
            e.Graphics.FillRectangle(brush, ClientRectangle);
        Color border = FlatAppearance.BorderColor == Color.Empty
            ? ControlPaint.Dark(fill)
            : FlatAppearance.BorderColor;
        ControlPaint.DrawBorder(e.Graphics, ClientRectangle, border, ButtonBorderStyle.Solid);

        if (Image != null)
        {
            int iconX = _iconOnly ? (ClientSize.Width - Image.Width) / 2 :
                (ClientSize.Width <= 90 ? 4 : 7);
            int iconY = (ClientSize.Height - Image.Height) / 2;
            e.Graphics.DrawImageUnscaled(Image, iconX, iconY);
        }

        if (!_iconOnly && !string.IsNullOrEmpty(Text))
        {
            int textLeft = Image == null ? 4 : (ClientSize.Width <= 90 ? 25 : 32);
            int rightInset = ClientSize.Width <= 90 ? 2 : 4;
            var textArea = new Rectangle(textLeft, 1,
                Math.Max(1, ClientSize.Width - textLeft - rightInset), ClientSize.Height - 2);
            Color textColor = Enabled ? ForeColor : SystemColors.GrayText;
            TextRenderer.DrawText(e.Graphics, Text, Font, textArea, textColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }

        if (Focused && ShowFocusCues)
        {
            Rectangle focus = ClientRectangle;
            focus.Inflate(-3, -3);
            ControlPaint.DrawFocusRectangle(e.Graphics, focus);
        }
    }
}

internal sealed class ResourceTile : Control
{
    private readonly char _icon;
    private readonly FontFamily? _iconFamily;
    private readonly string _title;
    private readonly string _description;
    private readonly bool _available;
    private bool _dark;
    private bool _hovered;

    public ResourceTile(char icon, FontFamily? iconFamily, string title, string description, bool available)
    {
        _icon = icon;
        _iconFamily = iconFamily;
        _title = title;
        _description = description;
        _available = available;
        DoubleBuffered = true;
        Cursor = available ? Cursors.Hand : Cursors.Default;
        TabStop = available;
        MouseEnter += (_, _) => { _hovered = true; Invalidate(); };
        MouseLeave += (_, _) => { _hovered = false; Invalidate(); };
    }

    public void SetDarkTheme(bool dark)
    {
        _dark = dark;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        Color fill = _dark
            ? (_hovered && _available ? Color.FromArgb(68, 68, 68) : Color.FromArgb(52, 52, 52))
            : (_hovered && _available ? Color.FromArgb(218, 225, 233) : Color.FromArgb(205, 205, 205));
        Color border = _dark ? Color.FromArgb(18, 18, 18) : Color.FromArgb(105, 105, 105);
        Color primary = _available
            ? (_dark ? Color.FromArgb(240, 240, 240) : Color.FromArgb(28, 28, 28))
            : (_dark ? Color.FromArgb(135, 135, 135) : Color.FromArgb(105, 105, 105));
        Color secondary = _dark ? Color.FromArgb(180, 180, 180) : Color.FromArgb(72, 72, 72);
        using var background = new SolidBrush(fill);
        using var outline = new Pen(border);
        e.Graphics.FillRectangle(background, ClientRectangle);
        e.Graphics.DrawRectangle(outline, 0, 0, Width - 1, Height - 1);
        using var titleFont = new Font("Segoe UI", 11f, FontStyle.Bold);
        using var detailFont = new Font("Segoe UI", 9f);
        int textLeft = 48;
        if (_iconFamily != null)
        {
            using var iconFont = new Font(_iconFamily, 17f, FontStyle.Regular, GraphicsUnit.Point);
            using var iconBrush = new SolidBrush(primary);
            e.Graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            foreach (PointF offset in new[]
                     {
                         new PointF(-0.5f, 0), new PointF(0.5f, 0),
                         new PointF(0, -0.4f), new PointF(0, 0.4f)
                     })
                e.Graphics.DrawString(_icon.ToString(), iconFont, iconBrush,
                    new RectangleF(13 + offset.X, 14 + offset.Y, 27, 30));
        }
        TextRenderer.DrawText(e.Graphics, _title, titleFont, new Rectangle(textLeft, 13, Width - textLeft - 14, 25), primary,
            TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        TextRenderer.DrawText(e.Graphics, _description, detailFont, new Rectangle(textLeft, 43, Width - textLeft - 14, 46), secondary,
            TextFormatFlags.Left | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        if (!_available)
            TextRenderer.DrawText(e.Graphics, "COMING LATER", detailFont,
                new Rectangle(Width - 116, Height - 24, 102, 18), secondary,
                TextFormatFlags.Right | TextFormatFlags.NoPrefix);
    }

    protected override void OnClick(EventArgs e)
    {
        if (_available) base.OnClick(e);
    }
}

internal sealed class DocumentationForm : Form
{
    private sealed record DocumentItem(string Title, string Path)
    {
        public override string ToString() => Title;
    }

    private readonly TreeView _documents = new();
    private readonly RichTextBox _content = new();
    private readonly string _docsRoot;

    public DocumentationForm(string engineRoot, bool dark, string? preferredDocument)
    {
        _docsRoot = Path.Combine(engineRoot, "Docs");
        Text = "R2Engine Documentation";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(1000, 620);
        Size = new Size(1120, 720);
        Font = new Font("Segoe UI", 9f);
        BackColor = dark ? Color.FromArgb(38, 38, 38) : Color.FromArgb(218, 218, 218);
        ForeColor = dark ? Color.FromArgb(238, 238, 238) : Color.FromArgb(28, 28, 28);

        var heading = new Label
        {
            Text = "R2Engine Documentation",
            Dock = DockStyle.Top,
            Height = 52,
            Padding = new Padding(16, 13, 0, 0),
            Font = new Font("Segoe UI", 15f, FontStyle.Bold),
            ForeColor = ForeColor
        };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(0),
            BackColor = dark ? Color.FromArgb(65, 65, 65) : Color.FromArgb(155, 155, 155)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 280f));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        var navigation = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(8),
            BackColor = dark ? Color.FromArgb(47, 47, 47) : Color.FromArgb(232, 232, 232)
        };
        var article = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(14),
            BackColor = dark ? Color.FromArgb(30, 30, 30) : Color.FromArgb(248, 248, 248)
        };
        _documents.Dock = DockStyle.Fill;
        _documents.BorderStyle = BorderStyle.None;
        _documents.HideSelection = false;
        _documents.FullRowSelect = true;
        _documents.ShowLines = false;
        _documents.ShowPlusMinus = true;
        _documents.ShowRootLines = false;
        _documents.ItemHeight = 23;
        _documents.Indent = 18;
        _documents.Scrollable = true;
        _documents.BackColor = dark ? Color.FromArgb(47, 47, 47) : Color.FromArgb(232, 232, 232);
        _documents.ForeColor = ForeColor;
        _documents.Font = new Font("Segoe UI", 9.5f);
        _content.Dock = DockStyle.Fill;
        _content.ReadOnly = true;
        _content.BorderStyle = BorderStyle.None;
        _content.BackColor = dark ? Color.FromArgb(30, 30, 30) : Color.FromArgb(248, 248, 248);
        _content.ForeColor = ForeColor;
        _content.Font = new Font("Segoe UI", 10f);
        _content.DetectUrls = false;
        _content.Cursor = Cursors.Arrow;
        navigation.Controls.Add(_documents);
        article.Controls.Add(_content);
        layout.Controls.Add(navigation, 0, 0);
        layout.Controls.Add(article, 1, 0);
        Controls.Add(layout);
        Controls.Add(heading);
        var openManualFolder = new Button
        {
            Text = "Open Manual Folder",
            Size = new Size(138, 29),
            Location = new Point(ClientSize.Width - 154, 11),
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            BackColor = dark ? Color.FromArgb(65, 65, 65) : Color.FromArgb(228, 228, 228),
            ForeColor = ForeColor
        };
        openManualFolder.Click += (_, _) => Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"\"{_docsRoot}\"",
            UseShellExecute = true
        });
        Controls.Add(openManualFolder);
        openManualFolder.BringToFront();

        LoadDocuments(engineRoot);
        _documents.AfterSelect += (_, _) => ShowSelectedDocument();
        if (_documents.Nodes.Count > 0)
        {
            TreeNode? preferred = null;
            if (!string.IsNullOrWhiteSpace(preferredDocument))
                preferred = FindDocumentNode(preferredDocument);
            TreeNode first = preferred ?? FirstDocumentNode()!;
            first.Parent?.Expand();
            _documents.SelectedNode = first;
        }
    }

    private void LoadDocuments(string engineRoot)
    {
        var files = new List<DocumentItem>();
        string docs = Path.Combine(engineRoot, "Docs");
        if (Directory.Exists(docs))
        {
            files.AddRange(Directory.EnumerateFiles(docs, "*.md", SearchOption.AllDirectories)
                .Select(path =>
                {
                    string section = Path.GetDirectoryName(Path.GetRelativePath(docs, path)) ?? "";
                    string prefix = section.Equals("Manual", StringComparison.OrdinalIgnoreCase)
                        ? "Manual — "
                        : "Feature Guide — ";
                    return new DocumentItem(prefix + PrettyDocumentName(Path.GetFileNameWithoutExtension(path)), path);
                }));
        }
        AddDocument(files, "R2Engine Runtime", Path.Combine(engineRoot, "R2Engine.Runtime", "README.md"));
        AddDocument(files, "PS2 README", Path.Combine(engineRoot, "R2Engine.PS2", "README.md"));
        AddDocument(files, "PS2 Scripting", Path.Combine(engineRoot, "R2Engine.PS2", "SCRIPTING.md"));
        foreach (DocumentItem item in files
                     .OrderBy(item => item.Title.StartsWith("Manual", StringComparison.Ordinal) ? 0 :
                         item.Title.StartsWith("Feature Guide", StringComparison.Ordinal) ? 1 : 2)
                     .ThenBy(item => item.Title.StartsWith("Manual", StringComparison.Ordinal)
                             ? Path.GetFileNameWithoutExtension(item.Path)
                             : item.Title,
                         StringComparer.OrdinalIgnoreCase))
        {
            string group = item.Title.StartsWith("Manual", StringComparison.Ordinal) ? "Manual" :
                item.Title.StartsWith("Feature Guide", StringComparison.Ordinal) ? "Feature Guides" :
                item.Title.StartsWith("PS2", StringComparison.Ordinal) ? "PS2 Development" : "Engine Reference";
            string pageTitle = item.Title.Replace("Manual — ", "", StringComparison.Ordinal)
                .Replace("Feature Guide — ", "", StringComparison.Ordinal);
            TreeNode? groupNode = _documents.Nodes.Cast<TreeNode>()
                .FirstOrDefault(node => node.Text.TrimEnd() == group);
            if (groupNode is null)
            {
                // TreeView measures node width using its regular font even when a node
                // supplies a bold NodeFont. Padding prevents the final glyph clipping.
                groupNode = new TreeNode(group + "   ") { NodeFont = new Font(_documents.Font, FontStyle.Bold) };
                _documents.Nodes.Add(groupNode);
            }
            groupNode.Nodes.Add(new TreeNode(pageTitle) { Tag = item });
        }
        if (_documents.Nodes.Count > 0)
            _documents.Nodes[0].Expand();
    }

    public void SelectDocument(string? preferredDocument)
    {
        if (string.IsNullOrWhiteSpace(preferredDocument))
            return;
        TreeNode? node = FindDocumentNode(preferredDocument);
        if (node is null) return;
        node.Parent?.Expand();
        _documents.SelectedNode = node;
    }

    private static void AddDocument(List<DocumentItem> files, string title, string path)
    {
        if (File.Exists(path)) files.Add(new DocumentItem(title, path));
    }

    private static string PrettyDocumentName(string name)
    {
        string withoutOrder = System.Text.RegularExpressions.Regex.Replace(name, @"^\d+\s*", "");
        return System.Text.RegularExpressions.Regex.Replace(withoutOrder, "([a-z])([A-Z])", "$1 $2");
    }

    private void ShowSelectedDocument()
    {
        if (_documents.SelectedNode?.Tag is not DocumentItem item) return;
        try
        {
            RenderMarkdown(File.ReadAllText(item.Path), Path.GetDirectoryName(item.Path)!);
            _content.SelectionStart = 0;
            _content.ScrollToCaret();
        }
        catch (Exception exception)
        {
            _content.Text = $"This document could not be opened.\n\n{exception.Message}";
        }
    }

    private TreeNode? FindDocumentNode(string title) => _documents.Nodes.Cast<TreeNode>()
        .SelectMany(group => group.Nodes.Cast<TreeNode>())
        .FirstOrDefault(node => node.Tag is DocumentItem item &&
            item.Title.Contains(title, StringComparison.OrdinalIgnoreCase));

    private TreeNode? FirstDocumentNode() => _documents.Nodes.Cast<TreeNode>()
        .SelectMany(group => group.Nodes.Cast<TreeNode>())
        .FirstOrDefault();

    private void RenderMarkdown(string markdown, string documentDirectory)
    {
        _content.SuspendLayout();
        _content.Clear();
        bool inCodeBlock = false;
        foreach (string sourceLine in markdown.Replace("\r\n", "\n").Split('\n'))
        {
            string line = sourceLine.TrimEnd();
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                inCodeBlock = !inCodeBlock;
                continue;
            }
            if (inCodeBlock)
            {
                AppendDocumentText(line + "\n", new Font("Consolas", 9.5f),
                    _content.ForeColor, _content.BackColor, 20, 4, 4);
                continue;
            }
            var imageMatch = System.Text.RegularExpressions.Regex.Match(
                line.Trim(), @"^!\[([^\]]*)\]\(([^\)]+)\)$");
            if (imageMatch.Success)
            {
                AppendDocumentImage(imageMatch.Groups[2].Value.Trim(), imageMatch.Groups[1].Value.Trim(), documentDirectory);
                continue;
            }
            if (line.StartsWith("### ", StringComparison.Ordinal))
            {
                AppendHeading(line[4..], 12f, 14, 4);
                continue;
            }
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                AppendHeading(line[3..], 15f, 20, 6);
                continue;
            }
            if (line.StartsWith("# ", StringComparison.Ordinal))
            {
                AppendHeading(line[2..], 20f, 8, 12);
                continue;
            }
            if (line.StartsWith("- ", StringComparison.Ordinal))
            {
                AppendDocumentText("•  " + CleanMarkdown(line[2..]) + "\n", new Font("Segoe UI", 10f),
                    _content.ForeColor, _content.BackColor, 34, 2, 3);
                continue;
            }
            if (System.Text.RegularExpressions.Regex.IsMatch(line, @"^\d+\.\s"))
            {
                AppendDocumentText(CleanMarkdown(line) + "\n", new Font("Segoe UI", 10f),
                    _content.ForeColor, _content.BackColor, 34, 2, 3);
                continue;
            }
            if (string.IsNullOrWhiteSpace(line))
            {
                AppendDocumentText("\n", new Font("Segoe UI", 5f), _content.ForeColor, _content.BackColor, 20, 0, 0);
                continue;
            }
            AppendDocumentText(CleanMarkdown(line) + "\n", new Font("Segoe UI", 10f),
                _content.ForeColor, _content.BackColor, 20, 2, 5);
        }
        _content.ResumeLayout();
    }

    private void AppendHeading(string text, float size, int spaceBefore, int spaceAfter)
    {
        AppendDocumentText(CleanMarkdown(text) + "\n", new Font("Segoe UI", size, FontStyle.Bold),
            _content.ForeColor, _content.BackColor, 20, spaceBefore, spaceAfter);
    }

    private void AppendDocumentText(
        string text,
        Font font,
        Color foreground,
        Color background,
        int leftIndent,
        int spaceBefore,
        int spaceAfter)
    {
        if (spaceBefore >= 10 && _content.TextLength > 0 && !_content.Text.EndsWith("\n\n", StringComparison.Ordinal))
            _content.AppendText("\n");
        int start = _content.TextLength;
        _content.AppendText(text);
        _content.Select(start, text.Length);
        _content.SelectionFont = font;
        _content.SelectionColor = foreground;
        _content.SelectionBackColor = background;
        _content.SelectionIndent = leftIndent;
        _content.SelectionRightIndent = 20;
        font.Dispose();
        if (spaceAfter >= 10 && !_content.Text.EndsWith("\n\n", StringComparison.Ordinal))
            _content.AppendText("\n");
    }

    private static string CleanMarkdown(string text)
    {
        string clean = text.Replace("**", "").Replace("__", "").Replace("`", "");
        clean = System.Text.RegularExpressions.Regex.Replace(clean, @"\[([^\]]+)\]\(([^\)]+)\)", "$1 — $2");
        return clean;
    }

    private void AppendDocumentImage(string source, string caption, string documentDirectory)
    {
        try
        {
            string path = Environment.ExpandEnvironmentVariables(source.Trim('"'));
            if (!Path.IsPathRooted(path))
                path = Path.GetFullPath(Path.Combine(documentDirectory, path));
            if (!File.Exists(path))
                throw new FileNotFoundException("Image not found.", path);

            using Image original = Image.FromFile(path);
            int availableWidth = Math.Max(160, _content.ClientSize.Width - 90);
            float scale = Math.Min(1f, availableWidth / (float)original.Width);
            int width = Math.Max(1, (int)MathF.Round(original.Width * scale));
            int height = Math.Max(1, (int)MathF.Round(original.Height * scale));
            using var rendered = new Bitmap(width, height);
            using (Graphics graphics = Graphics.FromImage(rendered))
            {
                graphics.Clear(_content.BackColor);
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.DrawImage(original, 0, 0, width, height);
            }
            using var stream = new MemoryStream();
            // RichEdit versions bundled with Windows do not consistently render
            // PNG data in SelectedRtf. A DIB is understood by every version we
            // support. BMP's first 14 bytes are its file header; the remaining
            // bytes are the DIB payload expected by \dibitmap0.
            rendered.Save(stream, System.Drawing.Imaging.ImageFormat.Bmp);
            byte[] bitmap = stream.ToArray();
            string hex = Convert.ToHexString(bitmap.AsSpan(14));
            int widthTwips = (int)MathF.Round(width * 1440f / 96f);
            int heightTwips = (int)MathF.Round(height * 1440f / 96f);
            _content.Select(_content.TextLength, 0);
            _content.SelectedRtf = $@"{{\rtf1\ansi\deff0{{\pict\dibitmap0\picw{width}\pich{height}\picwgoal{widthTwips}\pichgoal{heightTwips} {hex}}}}}";
            _content.AppendText("\n");
            if (!string.IsNullOrWhiteSpace(caption))
                AppendDocumentText(caption + "\n", new Font("Segoe UI", 9f, FontStyle.Italic),
                    _content.ForeColor, _content.BackColor, 20, 2, 10);
        }
        catch (Exception exception)
        {
            AppendDocumentText($"[Image unavailable: {caption}]\n{exception.Message}\n",
                new Font("Segoe UI", 9f, FontStyle.Italic), Color.FromArgb(190, 70, 70),
                _content.BackColor, 20, 2, 8);
        }
    }
}

internal sealed class ProjectNameDialog : Form
{
    private readonly TextBox _name = new();
    public string ProjectName => _name.Text;

    public ProjectNameDialog(string currentName, bool dark = false)
    {
        Text = "Set Project Display Name";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(390, 125);
        BackColor = dark ? Color.FromArgb(42, 42, 42) : Color.FromArgb(205, 205, 205);
        ForeColor = dark ? Color.FromArgb(238, 238, 238) : Color.FromArgb(28, 28, 28);
        Font = new Font("Segoe UI", 9f);

        var label = new Label
        {
            Text = "Project display name:",
            Location = new Point(14, 14),
            AutoSize = true
        };
        _name.Location = new Point(14, 38);
        _name.Size = new Size(360, 24);
        _name.Text = currentName;
        _name.SelectAll();

        var ok = new Button
        {
            Text = "OK",
            DialogResult = DialogResult.OK,
            Location = new Point(218, 80),
            Size = new Size(75, 28)
        };
        var cancel = new Button
        {
            Text = "Cancel",
            DialogResult = DialogResult.Cancel,
            Location = new Point(299, 80),
            Size = new Size(75, 28)
        };
        AcceptButton = ok;
        CancelButton = cancel;
        Controls.Add(label);
        Controls.Add(_name);
        Controls.Add(ok);
        Controls.Add(cancel);
        if (dark)
        {
            label.ForeColor = ForeColor;
            _name.BackColor = Color.FromArgb(58, 58, 58);
            _name.ForeColor = ForeColor;
        }
        Shown += (_, _) => _name.Focus();
    }
}

internal sealed class AboutForm : Form
{
    private readonly Image? _logo;

    public AboutForm(bool dark)
    {
        Text = "About R2Engine";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(420, 300);
        Font = new Font("Segoe UI", 9f);
        BackColor = dark ? Color.FromArgb(38, 38, 38) : Color.FromArgb(218, 218, 218);
        ForeColor = dark ? Color.FromArgb(238, 238, 238) : Color.FromArgb(28, 28, 28);

        string logoPath = Path.Combine(AppContext.BaseDirectory, "Assets", "r2-logo.png");
        if (File.Exists(logoPath))
        {
            using Image source = Image.FromFile(logoPath);
            _logo = new Bitmap(source);
        }
        var logo = new PictureBox
        {
            Image = _logo,
            SizeMode = PictureBoxSizeMode.Zoom,
            Location = new Point(105, 22),
            Size = new Size(210, 112),
            BackColor = Color.Transparent
        };
        var name = new Label
        {
            Text = "R2Engine",
            Font = new Font("Segoe UI", 16f, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(157, 145),
            ForeColor = ForeColor
        };
        var description = new Label
        {
            Text = "A game engine and editor built for creating games\nwith native PlayStation 2 support.",
            TextAlign = ContentAlignment.MiddleCenter,
            Location = new Point(55, 184),
            Size = new Size(310, 44),
            ForeColor = ForeColor
        };
        var close = new Button
        {
            Text = "Close",
            DialogResult = DialogResult.OK,
            Location = new Point(168, 250),
            Size = new Size(84, 30),
            BackColor = dark ? Color.FromArgb(66, 66, 66) : Color.FromArgb(230, 230, 230),
            ForeColor = ForeColor
        };
        AcceptButton = close;
        CancelButton = close;
        Controls.AddRange([logo, name, description, close]);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _logo?.Dispose();
        base.Dispose(disposing);
    }
}

internal sealed class NewProjectDialog : Form
{
    private readonly TextBox _name = new();
    private readonly TextBox _location = new();
    public string ProjectName => _name.Text.Trim();
    public string ProjectLocation => _location.Text.Trim();

    public NewProjectDialog(string defaultLocation, bool dark)
    {
        Text = "New R2Engine Project";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(560, 205);
        Font = new Font("Segoe UI", 9f);
        BackColor = dark ? Color.FromArgb(42, 42, 42) : Color.FromArgb(218, 218, 218);
        ForeColor = dark ? Color.FromArgb(238, 238, 238) : Color.FromArgb(28, 28, 28);

        Controls.Add(new Label { Text = "Project name", Location = new Point(16, 16), AutoSize = true });
        _name.Location = new Point(16, 39);
        _name.Size = new Size(528, 25);
        _name.Text = "New Project";
        Controls.Add(_name);

        Controls.Add(new Label { Text = "Location", Location = new Point(16, 78), AutoSize = true });
        _location.Location = new Point(16, 101);
        _location.Size = new Size(435, 25);
        _location.Text = defaultLocation;
        Controls.Add(_location);
        var browse = new Button { Text = "Browse...", Location = new Point(459, 99), Size = new Size(85, 29) };
        browse.Click += (_, _) => BrowseLocation();
        Controls.Add(browse);

        var preview = new Label
        {
            Location = new Point(16, 134),
            Size = new Size(528, 20),
            ForeColor = Color.FromArgb(85, 85, 85),
            AutoEllipsis = true
        };
        void UpdatePreview() => preview.Text = string.IsNullOrWhiteSpace(_name.Text)
            ? _location.Text
            : Path.Combine(_location.Text, _name.Text);
        _name.TextChanged += (_, _) => UpdatePreview();
        _location.TextChanged += (_, _) => UpdatePreview();
        UpdatePreview();
        Controls.Add(preview);

        var create = new Button { Text = "Create", DialogResult = DialogResult.OK, Location = new Point(378, 165), Size = new Size(80, 29) };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(464, 165), Size = new Size(80, 29) };
        AcceptButton = create;
        CancelButton = cancel;
        Controls.Add(create);
        Controls.Add(cancel);
        if (dark)
        {
            foreach (Control control in Controls)
            {
                control.ForeColor = ForeColor;
                if (control is TextBox)
                    control.BackColor = Color.FromArgb(58, 58, 58);
                else if (control is Button)
                    control.BackColor = Color.FromArgb(72, 72, 72);
            }
            preview.ForeColor = Color.FromArgb(175, 175, 175);
        }
        FormClosing += ValidateBeforeClosing;
        Shown += (_, _) => { _name.SelectAll(); _name.Focus(); };
    }

    private void BrowseLocation()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Choose the parent folder for the new project",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true,
            SelectedPath = Directory.Exists(_location.Text) ? _location.Text : ""
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
            _location.Text = dialog.SelectedPath;
    }

    private void ValidateBeforeClosing(object? sender, FormClosingEventArgs e)
    {
        if (DialogResult != DialogResult.OK)
            return;
        if (string.IsNullOrWhiteSpace(ProjectName) ||
            ProjectName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            MessageBox.Show(this, "Enter a valid project name.", "Invalid Project Name",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            e.Cancel = true;
            return;
        }
        if (string.IsNullOrWhiteSpace(ProjectLocation))
        {
            MessageBox.Show(this, "Choose a location for the project.", "Project Location Required",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            e.Cancel = true;
        }
    }
}

internal static class R2UserData
{
    public static string Root { get; } = FindRoot();

    private static string FindRoot()
    {
        DirectoryInfo? directory = new(Path.GetFullPath(AppContext.BaseDirectory));
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, ".r2portable")))
                return Path.Combine(directory.FullName, "UserData");
            directory = directory.Parent;
        }
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "R2Engine");
    }
}

internal sealed class UserSettings
{
    public bool DarkTheme { get; set; }
    public string Pcsx2ExecutablePath { get; set; } = "";
    public string DefaultProjectsFolder { get; set; } = "";
    public bool CloseHubAfterOpeningProject { get; set; } = true;
    public bool ConfirmBeforeRemovingProject { get; set; } = true;
    public string Language { get; set; } = "English";

    public string ResolvedProjectsFolder => string.IsNullOrWhiteSpace(DefaultProjectsFolder)
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "R2Engine Projects")
        : Environment.ExpandEnvironmentVariables(DefaultProjectsFolder);

    public static string SettingsPath => Path.Combine(R2UserData.Root, "user-settings.json");

    public static UserSettings Load()
    {
        try
        {
            return File.Exists(SettingsPath)
                ? JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(SettingsPath)) ?? new UserSettings()
                : new UserSettings();
        }
        catch { return new UserSettings(); }
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this,
            new JsonSerializerOptions { WriteIndented = true }));
    }
}

internal sealed class HubSettingsForm : Form
{
    private readonly CheckBox _darkTheme = new();
    private readonly TextBox _pcsx2Path = new();
    private readonly TextBox _projectsFolder = new();
    private readonly CheckBox _closeAfterOpen = new();
    private readonly CheckBox _confirmRemove = new();
    private readonly ComboBox _language = new();
    private readonly Label _pcsx2Status = new();
    private readonly Panel _pageHost = new();
    private readonly Dictionary<Button, Control> _pages = new();
    private readonly bool _dark;
    public UserSettings Settings => new()
    {
        DarkTheme = _darkTheme.Checked,
        Pcsx2ExecutablePath = _pcsx2Path.Text.Trim(),
        DefaultProjectsFolder = _projectsFolder.Text.Trim(),
        CloseHubAfterOpeningProject = _closeAfterOpen.Checked,
        ConfirmBeforeRemovingProject = _confirmRemove.Checked,
        Language = _language.SelectedItem?.ToString() ?? "English"
    };

    public HubSettingsForm(
        UserSettings current,
        Func<int> removeMissingProjects,
        string engineRoot,
        Action openEngineFolder)
    {
        _dark = current.DarkTheme;
        Text = "R2Engine Settings";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(790, 500);
        Font = new Font("Segoe UI", 9f);
        BackColor = _dark ? Color.FromArgb(28, 28, 28) : Color.FromArgb(218, 218, 218);
        ForeColor = _dark ? Color.FromArgb(238, 238, 238) : Color.FromArgb(28, 28, 28);

        var header = new Panel { Dock = DockStyle.Top, Height = 62, BackColor = BackColor };
        header.Controls.Add(new Label
        {
            Text = "Settings",
            Font = new Font("Segoe UI", 17f, FontStyle.Bold),
            Location = new Point(20, 17),
            AutoSize = true,
            ForeColor = ForeColor
        });

        var navigation = new FlowLayoutPanel
        {
            Dock = DockStyle.Left,
            Width = 180,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = new Padding(10, 14, 10, 0),
            BackColor = _dark ? Color.FromArgb(25, 25, 25) : Color.FromArgb(202, 202, 202)
        };
        _pageHost.Dock = DockStyle.Fill;
        _pageHost.BackColor = BackColor;

        Panel projectsPage = MakePage("Projects", "Choose where projects are created and how the Hub manages them.");
        projectsPage.Controls.Add(MakeLabel("Default projects folder", 28, 94));
        _projectsFolder.Location = new Point(28, 118);
        _projectsFolder.Size = new Size(448, 25);
        _projectsFolder.Text = current.ResolvedProjectsFolder;
        var browseProjects = new Button { Text = "Browse...", Location = new Point(484, 116), Size = new Size(84, 29) };
        browseProjects.Click += (_, _) => BrowseForProjectsFolder();
        projectsPage.Controls.Add(_projectsFolder);
        projectsPage.Controls.Add(browseProjects);
        _closeAfterOpen.Text = "Close the Hub after opening a project";
        _closeAfterOpen.Checked = current.CloseHubAfterOpeningProject;
        _closeAfterOpen.Location = new Point(28, 158);
        _closeAfterOpen.AutoSize = true;
        _confirmRemove.Text = "Ask before removing a project from the Hub";
        _confirmRemove.Checked = current.ConfirmBeforeRemovingProject;
        _confirmRemove.Location = new Point(28, 184);
        _confirmRemove.AutoSize = true;
        var cleanMissing = new Button { Text = "Remove Missing Projects", Location = new Point(28, 228), Size = new Size(170, 30) };
        var cleanStatus = MakeLabel("Removes list entries whose folders no longer exist. Project files are never deleted.", 28, 266);
        cleanMissing.Click += (_, _) =>
        {
            int removed = removeMissingProjects();
            cleanStatus.Text = removed == 0 ? "No missing projects were found." :
                $"Removed {removed} missing project{(removed == 1 ? "" : "s")} from the list.";
        };
        projectsPage.Controls.Add(_closeAfterOpen);
        projectsPage.Controls.Add(_confirmRemove);
        projectsPage.Controls.Add(cleanMissing);
        projectsPage.Controls.Add(cleanStatus);

        Panel appearancePage = MakePage("Appearance", "Change how R2Engine looks on this computer.");
        _darkTheme.Text = "Use dark theme";
        _darkTheme.Checked = current.DarkTheme;
        _darkTheme.Location = new Point(28, 96);
        _darkTheme.AutoSize = true;
        appearancePage.Controls.Add(_darkTheme);
        appearancePage.Controls.Add(MakeLabel("Language", 28, 140));
        _language.DropDownStyle = ComboBoxStyle.DropDownList;
        _language.Items.Add("English");
        _language.SelectedIndex = 0;
        _language.Location = new Point(28, 164);
        _language.Size = new Size(230, 25);
        appearancePage.Controls.Add(_language);
        appearancePage.Controls.Add(MakeLabel("Additional languages can be added when translations become available.", 28, 198));

        Panel toolsPage = MakePage("External Tools", "Set paths for applications R2Engine launches during development.");
        toolsPage.Controls.Add(MakeLabel("PCSX2 executable", 28, 94));
        _pcsx2Path.Location = new Point(28, 118);
        _pcsx2Path.Size = new Size(354, 25);
        _pcsx2Path.Text = current.Pcsx2ExecutablePath;
        _pcsx2Path.PlaceholderText = "Leave blank to find PCSX2 automatically";
        var browse = new Button { Text = "Browse...", Location = new Point(390, 116), Size = new Size(84, 29) };
        browse.Click += (_, _) => { BrowseForPcsx2(); UpdatePcsx2Status(); };
        var testPcsx2 = new Button { Text = "Test", Location = new Point(484, 116), Size = new Size(84, 29) };
        testPcsx2.Click += (_, _) => UpdatePcsx2Status(showResult: true);
        toolsPage.Controls.Add(_pcsx2Path);
        toolsPage.Controls.Add(browse);
        toolsPage.Controls.Add(testPcsx2);
        _pcsx2Status.Location = new Point(28, 155);
        _pcsx2Status.AutoSize = true;
        toolsPage.Controls.Add(_pcsx2Status);
        toolsPage.Controls.Add(MakeLabel("Leave this blank and R2Engine will look in the standard installation locations.", 28, 184));
        _pcsx2Path.TextChanged += (_, _) => UpdatePcsx2Status();
        UpdatePcsx2Status();

        Panel advancedPage = MakePage("Advanced", "Maintenance and installation options for advanced users.");
        advancedPage.Controls.Add(MakeLabel("Engine installation folder", 28, 94));
        var enginePath = new Label
        {
            Text = engineRoot,
            Location = new Point(28, 118),
            Size = new Size(448, 25),
            BorderStyle = BorderStyle.FixedSingle,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(5, 0, 5, 0),
            AutoEllipsis = true
        };
        var openEngine = new Button { Text = "Open Folder", Location = new Point(484, 116), Size = new Size(84, 29) };
        openEngine.Click += (_, _) => openEngineFolder();
        advancedPage.Controls.Add(enginePath);
        advancedPage.Controls.Add(openEngine);
        advancedPage.Controls.Add(MakeLabel(
            "This contains the editor, engine source, console runtime, and build tools.", 28, 155));

        AddCategory(navigation, "Projects", projectsPage, selected: true);
        AddCategory(navigation, "Appearance", appearancePage);
        AddCategory(navigation, "External Tools", toolsPage);
        AddCategory(navigation, "Advanced", advancedPage);

        var footer = new Panel { Dock = DockStyle.Bottom, Height = 54, BackColor = BackColor };
        var close = new Button { Text = "Close", DialogResult = DialogResult.OK, Location = new Point(690, 12), Size = new Size(78, 29), Anchor = AnchorStyles.Top | AnchorStyles.Right };
        footer.Controls.Add(close);
        AcceptButton = close;
        CancelButton = close;
        Controls.Add(_pageHost);
        Controls.Add(navigation);
        Controls.Add(footer);
        Controls.Add(header);
        ApplyControlTheme(this);
        ShowPage(projectsPage);
        UpdatePcsx2Status();
    }

    private Panel MakePage(string title, string description)
    {
        var page = new Panel { Dock = DockStyle.Fill, BackColor = BackColor, Visible = false };
        page.Controls.Add(new Label { Text = title, Font = new Font("Segoe UI", 14f, FontStyle.Bold), Location = new Point(28, 20), AutoSize = true });
        page.Controls.Add(new Label { Text = description, Location = new Point(28, 56), AutoSize = true });
        _pageHost.Controls.Add(page);
        return page;
    }

    private Label MakeLabel(string text, int x, int y) => new()
    {
        Text = text,
        Location = new Point(x, y),
        AutoSize = true,
        ForeColor = _dark ? Color.FromArgb(190, 190, 190) : Color.FromArgb(70, 70, 70)
    };

    private void AddCategory(FlowLayoutPanel navigation, string text, Control page, bool selected = false)
    {
        var button = new Button
        {
            Text = text,
            Width = 158,
            Height = 36,
            FlatStyle = FlatStyle.Flat,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(10, 0, 0, 0),
            Margin = new Padding(0, 0, 0, 2),
            Font = new Font("Segoe UI", 9f, selected ? FontStyle.Bold : FontStyle.Regular)
        };
        button.FlatAppearance.BorderSize = 0;
        button.Click += (_, _) => ShowPage(page);
        _pages[button] = page;
        navigation.Controls.Add(button);
    }

    private void ShowPage(Control page)
    {
        foreach ((Button button, Control candidate) in _pages)
        {
            bool selected = ReferenceEquals(candidate, page);
            candidate.Visible = selected;
            button.BackColor = selected
                ? (_dark ? Color.FromArgb(52, 70, 91) : Color.FromArgb(145, 166, 190))
                : (_dark ? Color.FromArgb(25, 25, 25) : Color.FromArgb(202, 202, 202));
            button.Font = new Font("Segoe UI", 9f, selected ? FontStyle.Bold : FontStyle.Regular);
        }
        page.BringToFront();
    }

    private void ApplyControlTheme(Control root)
    {
        foreach (Control control in root.Controls)
        {
            control.ForeColor = ForeColor;
            if (control is TextBox or ComboBox)
                control.BackColor = _dark ? Color.FromArgb(55, 55, 55) : Color.White;
            else if (control is Button && !_pages.ContainsKey((Button)control))
                control.BackColor = _dark ? Color.FromArgb(65, 65, 65) : Color.FromArgb(225, 225, 225);
            ApplyControlTheme(control);
        }
    }

    private void BrowseForProjectsFolder()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Choose the folder where new R2Engine projects will be created",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true,
            SelectedPath = Directory.Exists(_projectsFolder.Text) ? _projectsFolder.Text : ""
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
            _projectsFolder.Text = dialog.SelectedPath;
    }

    private void BrowseForPcsx2()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Choose the PCSX2 executable",
            Filter = "PCSX2 executable (pcsx2*.exe)|pcsx2*.exe|Applications (*.exe)|*.exe|All files (*.*)|*.*",
            CheckFileExists = true
        };
        if (File.Exists(_pcsx2Path.Text))
            dialog.InitialDirectory = Path.GetDirectoryName(_pcsx2Path.Text);
        if (dialog.ShowDialog(this) == DialogResult.OK)
            _pcsx2Path.Text = dialog.FileName;
    }

    private void UpdatePcsx2Status(bool showResult = false)
    {
        string? resolved = ResolvePcsx2Path(_pcsx2Path.Text);
        if (resolved != null)
        {
            _pcsx2Status.Text = $"Ready: {resolved}";
            _pcsx2Status.ForeColor = _dark ? Color.FromArgb(120, 210, 145) : Color.FromArgb(30, 120, 55);
        }
        else
        {
            _pcsx2Status.Text = "PCSX2 could not be found.";
            _pcsx2Status.ForeColor = _dark ? Color.FromArgb(235, 125, 125) : Color.FromArgb(175, 38, 38);
        }
        if (showResult)
            _pcsx2Status.Text = resolved != null ? "PCSX2 configuration is valid." : "PCSX2 configuration is not valid.";
    }

    private static string? ResolvePcsx2Path(string configured)
    {
        if (!string.IsNullOrWhiteSpace(configured))
        {
            string expanded = Environment.ExpandEnvironmentVariables(configured.Trim());
            return File.Exists(expanded) ? Path.GetFullPath(expanded) : null;
        }
        string[] candidates =
        [
            @"E:\PCSX2\pcsx2-qt.exe",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "PCSX2", "pcsx2-qt.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PCSX2", "pcsx2-qt.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "PCSX2", "pcsx2-qt.exe")
        ];
        return candidates.FirstOrDefault(File.Exists);
    }
}

internal sealed class HubHeader : Panel
{
    private readonly Image? _logo;

    public HubHeader()
    {
        DoubleBuffered = true;
        BackColor = Color.FromArgb(172, 172, 172);
        string logoPath = Path.Combine(AppContext.BaseDirectory, "Assets", "r2-hub-logo.png");
        if (File.Exists(logoPath))
        {
            using Image source = Image.FromFile(logoPath);
            _logo = new Bitmap(source);
        }
    }

    public void SetDarkTheme(bool dark)
    {
        BackColor = dark ? Color.FromArgb(36, 36, 36) : Color.FromArgb(172, 172, 172);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        Rectangle bounds = ClientRectangle;
        using var gradient = new LinearGradientBrush(bounds,
            Color.FromArgb(82, 82, 82), Color.FromArgb(65, 65, 65), LinearGradientMode.Vertical);
        e.Graphics.FillRectangle(gradient, bounds);
        using var divider = new Pen(Color.FromArgb(42, 42, 42));
        e.Graphics.DrawLine(divider, 0, bounds.Height - 1, bounds.Width, bounds.Height - 1);

        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        if (_logo != null)
        {
            Rectangle source = new(96, 268, 1084, 558);
            const float targetWidth = 154.0f;
            float targetHeight = targetWidth * source.Height / source.Width;
            float x = (bounds.Width - targetWidth) * 0.5f;
            float y = (bounds.Height - targetHeight) * 0.5f;
            e.Graphics.DrawImage(_logo,
                new RectangleF(x, y, targetWidth, targetHeight), source, GraphicsUnit.Pixel);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _logo?.Dispose();
        base.Dispose(disposing);
    }
}

internal sealed class HubColorTable(bool dark) : ProfessionalColorTable
{
    public override Color MenuStripGradientBegin => dark ? Color.FromArgb(48, 48, 48) : Color.FromArgb(224, 224, 224);
    public override Color MenuStripGradientEnd => dark ? Color.FromArgb(48, 48, 48) : Color.FromArgb(205, 205, 205);
    public override Color ToolStripDropDownBackground => dark ? Color.FromArgb(48, 48, 48) : Color.FromArgb(232, 232, 232);
    public override Color ImageMarginGradientBegin => dark ? Color.FromArgb(42, 42, 42) : Color.FromArgb(218, 218, 218);
    public override Color ImageMarginGradientMiddle => ImageMarginGradientBegin;
    public override Color ImageMarginGradientEnd => ImageMarginGradientBegin;
    public override Color MenuItemSelected => dark ? Color.FromArgb(66, 91, 122) : Color.FromArgb(168, 194, 222);
    public override Color MenuItemBorder => Color.FromArgb(91, 116, 145);
    public override Color MenuBorder => Color.FromArgb(105, 105, 105);
    public override Color SeparatorDark => Color.FromArgb(145, 145, 145);
    public override Color SeparatorLight => Color.FromArgb(238, 238, 238);
    public override Color StatusStripGradientBegin => Color.FromArgb(214, 214, 214);
    public override Color StatusStripGradientEnd => Color.FromArgb(195, 195, 195);
}
