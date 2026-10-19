using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace CursorMaster;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        var infPath = args.FirstOrDefault(argument =>
            Path.GetExtension(argument).Equals(
                ".inf", StringComparison.OrdinalIgnoreCase));
        Application.Run(new MainForm(infPath));
    }
}

internal sealed class MainForm : Form
{
    private const string CursorRegistryPath = @"Control Panel\Cursors";
    private const int SpiSetCaretWidth = 0x2007;
    private const uint SpifUpdateIniFile = 0x01;
    private const uint SpifSendChange = 0x02;

    private static readonly CursorRole[] CursorRoles =
    [
        new("Arrow"),
        new("Help"),
        new("AppStarting"),
        new("Wait"),
        new("Crosshair"),
        new("IBeam"),
        new("NWPen"),
        new("No"),
        new("SizeNS"),
        new("SizeWE"),
        new("SizeNWSE"),
        new("SizeNESW"),
        new("SizeAll"),
        new("UpArrow"),
        new("Hand"),
        new("Pin"),
        new("Person")
    ];

    private static readonly Color Background = Color.FromArgb(17, 15, 29);
    private static readonly Color Surface = Color.FromArgb(29, 25, 45);
    private static readonly Color SurfaceLight = Color.FromArgb(43, 37, 63);
    private static readonly Color Accent = Color.FromArgb(255, 116, 177);
    private static readonly Color Cyan = Color.FromArgb(114, 224, 231);
    private static readonly Color Gold = Color.FromArgb(255, 204, 126);
    private static readonly Color Muted = Color.FromArgb(182, 174, 202);

    private readonly Label status = new();
    private readonly NumericUpDown caretWidth = new();
    private readonly TextBox packName = new();
    private readonly TextBox infPath = new();
    private readonly TextBox infSource = new();
    private readonly DataGridView infRoles = new();
    private readonly PictureBox infCursorPreview = new();
    private readonly Label infSchemeName = new();
    private readonly Label infPreviewStatus = new();
    private readonly Button importInfButton = new();
    private readonly SquareTabControl tabs = new(Background, Surface, Accent, Muted);
    private readonly SquareTabControl packStudioTabs = new(Background, Surface, Accent, Muted);
    private readonly System.Windows.Forms.Timer infPreviewTimer = new();
    private CursorInfPreview? currentInfPreview;
    private Bitmap? currentInfCursorPreview;
    private CursorPreviewAnimation? currentInfCursorAnimation;
    private int currentInfAnimationStep;
    private string? lastPackDirectory;

    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(
        uint action, uint parameter, IntPtr value, uint flags);

    public MainForm()
        : this(null)
    {
    }

    public MainForm(string? initialInfPath)
    {
        Text = "Pointer Studio";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(900, 660);
        Size = new Size(1160, 820);
        BackColor = Background;
        ForeColor = Color.WhiteSmoke;
        Font = new Font("Segoe UI", 9);
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath)
            ?? throw new InvalidOperationException("Pointer Studio's application icon is missing.");
        BuildInterface();
        RegisterInfOpenHandler();
        infPreviewTimer.Tick += (_, _) => AdvanceInfCursorPreview();
        Disposed += (_, _) =>
        {
            ClearInfCursorPreview();
            infPreviewTimer.Dispose();
        };
        if (!string.IsNullOrWhiteSpace(initialInfPath))
            LoadInfPreview(initialInfPath);
    }

    private void BuildInterface()
    {
        var shell = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Background,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(22, 16, 22, 16)
        };
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 78));
        shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        Controls.Add(shell);

        var heading = new Panel { Dock = DockStyle.Fill, BackColor = Background };
        var title = new Label
        {
            Text = "✦  Pointer Studio",
            Font = new Font("Segoe UI Semibold", 22),
            ForeColor = Accent,
            AutoSize = true,
            Location = new Point(0, 2)
        };
        var subtitle = new Label
        {
            Text = "ANIME CURSOR ATELIER     ·     craft, preview & collect",
            ForeColor = Muted,
            AutoSize = true,
            Location = new Point(3, 45)
        };
        heading.Controls.Add(title);
        heading.Controls.Add(subtitle);
        var themeBadge = new Label
        {
            Text = "PIXEL MAGIC",
            Font = new Font("Segoe UI Semibold", 9),
            ForeColor = Cyan,
            TextAlign = ContentAlignment.MiddleRight,
            AutoSize = true,
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            Location = new Point(heading.ClientSize.Width - 120, 17)
        };
        heading.Controls.Add(themeBadge);
        heading.Resize += (_, _) =>
            themeBadge.Left = heading.ClientSize.Width - themeBadge.Width;
        shell.Controls.Add(heading, 0, 0);

        tabs.Dock = DockStyle.Fill;
        tabs.AddPage(BuildPackStudioTab());
        tabs.AddPage(BuildCompilerTab());
        tabs.AddPage(BuildTextTab());
        shell.Controls.Add(tabs, 0, 1);

        status.Dock = DockStyle.Fill;
        status.TextAlign = ContentAlignment.MiddleLeft;
        status.ForeColor = Muted;
        shell.Controls.Add(status, 0, 2);
    }

    private TabPage BuildInfTab()
    {
        var page = CreateTab("INF preview");
        var layout = WorkspaceLayout(page);

        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = Background,
            Padding = new Padding(0, 6, 0, 4)
        };
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.Controls.Add(content, 0, 0);

        var filePicker = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            BackColor = Background,
            Margin = new Padding(0)
        };
        filePicker.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        filePicker.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
        filePicker.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 105));
        filePicker.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 125));
        infPath.ReadOnly = true;
        infPath.Dock = DockStyle.Fill;
        infPath.BorderStyle = BorderStyle.FixedSingle;
        infPath.BackColor = SurfaceLight;
        infPath.ForeColor = Color.WhiteSmoke;
        infPath.Margin = new Padding(8, 10, 12, 8);
        filePicker.Controls.Add(infPath, 0, 0);
        filePicker.Controls.Add(CreateButton("Browse…", BrowseInfFile), 1, 0);
        filePicker.Controls.Add(CreateButton("Preview", PreviewSelectedInf, primary: true), 2, 0);
        var browsePreviewButton = CreateButton("Open cursor…", BrowsePreviewFile);
        browsePreviewButton.Width = 117;
        filePicker.Controls.Add(browsePreviewButton, 3, 0);
        content.Controls.Add(filePicker, 0, 0);

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            Size = new Size(900, 500),
            BackColor = Background,
            Panel1MinSize = 260,
            Panel2MinSize = 220,
            SplitterDistance = 440,
            Margin = new Padding(0, 8, 0, 8)
        };
        content.Controls.Add(split, 0, 1);

        infRoles.Dock = DockStyle.Fill;
        infRoles.BackgroundColor = Surface;
        infRoles.BorderStyle = BorderStyle.None;
        infRoles.AllowUserToAddRows = false;
        infRoles.AllowUserToDeleteRows = false;
        infRoles.AllowUserToResizeRows = false;
        infRoles.ReadOnly = true;
        infRoles.MultiSelect = false;
        infRoles.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        infRoles.RowHeadersVisible = false;
        infRoles.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        infRoles.EnableHeadersVisualStyles = false;
        infRoles.ColumnHeadersDefaultCellStyle.BackColor = SurfaceLight;
        infRoles.ColumnHeadersDefaultCellStyle.ForeColor = Color.WhiteSmoke;
        infRoles.DefaultCellStyle.BackColor = Surface;
        infRoles.DefaultCellStyle.ForeColor = Color.WhiteSmoke;
        infRoles.DefaultCellStyle.SelectionBackColor = Color.FromArgb(77, 43, 77);
        infRoles.DefaultCellStyle.SelectionForeColor = Color.WhiteSmoke;
        infRoles.Columns.Add("role", "Pointer");
        infRoles.Columns.Add("file", "Cursor file");
        infRoles.Columns.Add("state", "Preview");
        infRoles.Columns["role"]!.FillWeight = 35;
        infRoles.Columns["file"]!.FillWeight = 45;
        infRoles.Columns["state"]!.FillWeight = 20;
        infRoles.SelectionChanged += (_, _) => UpdateInfCursorPreview();
        split.Panel1.Controls.Add(infRoles);

        var preview = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            BackColor = Surface,
            Padding = new Padding(12)
        };
        preview.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        preview.RowStyles.Add(new RowStyle(SizeType.Absolute, 146));
        preview.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        preview.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        preview.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        split.Panel2.Controls.Add(preview);

        infSchemeName.Text = "Cursor preview";
        infSchemeName.Dock = DockStyle.Fill;
        infSchemeName.ForeColor = Color.WhiteSmoke;
        infSchemeName.Font = new Font("Segoe UI Semibold", 11);
        infSchemeName.AutoEllipsis = true;
        preview.Controls.Add(infSchemeName, 0, 0);

        infCursorPreview.Dock = DockStyle.Fill;
        infCursorPreview.SizeMode = PictureBoxSizeMode.CenterImage;
        infCursorPreview.BackColor = Surface;
        preview.Controls.Add(infCursorPreview, 0, 1);

        infPreviewStatus.Dock = DockStyle.Fill;
        infPreviewStatus.ForeColor = Muted;
        infPreviewStatus.TextAlign = ContentAlignment.MiddleLeft;
        infPreviewStatus.AutoEllipsis = true;
        preview.Controls.Add(infPreviewStatus, 0, 2);

        infSource.Dock = DockStyle.Fill;
        infSource.Multiline = true;
        infSource.ReadOnly = true;
        infSource.ScrollBars = ScrollBars.Vertical;
        infSource.BorderStyle = BorderStyle.FixedSingle;
        infSource.BackColor = Color.FromArgb(22, 23, 28);
        infSource.ForeColor = Muted;
        infSource.Font = new Font("Consolas", 8);
        preview.Controls.Add(infSource, 0, 3);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            BackColor = Background,
            Margin = new Padding(0)
        };
        importInfButton.Text = "Import & register cursor pack";
        importInfButton.AutoSize = true;
        importInfButton.Enabled = false;
        importInfButton.FlatStyle = FlatStyle.Flat;
        importInfButton.FlatAppearance.BorderColor = Accent;
        importInfButton.BackColor = Accent;
        importInfButton.ForeColor = Color.FromArgb(35, 20, 39);
        importInfButton.Font = new Font("Segoe UI Semibold", 9);
        importInfButton.Padding = new Padding(12, 5, 12, 5);
        importInfButton.Margin = new Padding(0, 4, 8, 4);
        importInfButton.Click += (_, _) => ImportCursorInf();
        actions.Controls.Add(importInfButton);
        actions.Controls.Add(CreateButton(
            "Set Pointer Studio as .inf default", SetInfAsDefault));
        actions.Controls.Add(CreateButton(
            "Default Apps settings", OpenInfDefaultAppsSettings));
        content.Controls.Add(actions, 0, 2);

        var footer = new Label
        {
            Text = "Preview reads the INF and cursor images only. Driver installation, scripts, and unrelated registry entries are never run.",
            ForeColor = Muted,
            Dock = DockStyle.Fill,
            AutoEllipsis = true
        };
        layout.Controls.Add(footer, 0, 1);
        return page;
    }

    private TabPage BuildPackStudioTab()
    {
        var page = CreateTab("Pack studio");
        var layout = PageLayout(page);
        AddIntro(layout, "Build a set. Preview the magic.",
            "Create a scheme from your current Windows cursors or inspect a downloaded INF pack.");

        packStudioTabs.Dock = DockStyle.Fill;
        packStudioTabs.AddPage(BuildPackTab());
        packStudioTabs.AddPage(BuildInfTab());
        layout.Controls.Add(packStudioTabs, 0, 1);

        var note = new Label
        {
            Text = "✦  PACK STUDIO  ·  INF files are inspected locally; setup commands are never run.",
            ForeColor = Cyan,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true
        };
        layout.Controls.Add(note, 0, 2);
        return page;
    }

    private TabPage BuildPackTab()
    {
        var page = CreateTab("Create pack");
        var layout = WorkspaceLayout(page);

        var card = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 0,
            GrowStyle = TableLayoutPanelGrowStyle.AddRows,
            BackColor = Surface,
            Padding = new Padding(18),
            Margin = new Padding(0, 8, 0, 12)
        };
        card.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.Controls.Add(card, 0, 0);

        AddCardLabel(card, "Pack name");
        packName.Dock = DockStyle.Fill;
        packName.BorderStyle = BorderStyle.FixedSingle;
        packName.BackColor = SurfaceLight;
        packName.ForeColor = Color.WhiteSmoke;
        packName.Margin = new Padding(8, 8, 8, 12);
        AddToCard(card, packName, 38);

        var details = new Label
        {
            Text = "Pointer Studio reads your current Windows cursor assignments and copies the "
                   + "available .cur/.ani files to %LOCALAPPDATA%\\PointerStudio\\Packs. "
                   + "Then it registers the set in Mouse Properties → Pointers → Scheme.",
            ForeColor = Muted,
            Dock = DockStyle.Fill,
            AutoEllipsis = true
        };
        AddToCard(card, details, 68);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            BackColor = Surface
        };
        actions.Controls.Add(CreateButton(
            "Create & register pack", CreateCursorPack, primary: true));
        actions.Controls.Add(CreateButton(
            "Open Mouse Properties", OpenMouseProperties));
        actions.Controls.Add(CreateButton(
            "Open pack folder", OpenPackFolder));
        AddToCard(card, actions, 56);

        var hint = new Label
        {
            Text = "✦  Default Windows cursors stay as defaults in the saved scheme.",
            ForeColor = Muted,
            Dock = DockStyle.Fill,
            AutoEllipsis = true
        };
        layout.Controls.Add(hint, 0, 1);
        return page;
    }

    private TabPage BuildCompilerTab()
    {
        var page = CreateTab("Cursor compiler");
        var layout = PageLayout(page);
        AddIntro(layout, "Choose your transformation",
            "Two local pipelines, one creative workspace. Large images are resized to the Windows cursor limit.");

        var cards = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Background,
            Padding = new Padding(0, 8, 0, 8)
        };
        cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        cards.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        cards.Controls.Add(BuildCompilerCard(animated: false), 0, 0);
        cards.Controls.Add(BuildCompilerCard(animated: true), 1, 0);
        layout.Controls.Add(cards, 0, 1);

        var footer = new Label
        {
            Text = "LOCAL ONLY  ·  Hotspots scale with resized art  ·  Nothing is uploaded",
            ForeColor = Cyan,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        };
        layout.Controls.Add(footer, 0, 2);
        return page;
    }

    private Control BuildCompilerCard(bool animated)
    {
        var accent = animated ? Accent : Cyan;
        var card = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 0,
            GrowStyle = TableLayoutPanelGrowStyle.AddRows,
            BackColor = Surface,
            Padding = new Padding(18),
            Margin = new Padding(0, 0, 10, 0)
        };
        card.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var banner = new Label
        {
            Text = animated ? "GIF   ✦   ANIMATED CURSOR" : "PNG   ✦   STATIC CURSOR",
            Dock = DockStyle.Fill,
            ForeColor = accent,
            Font = new Font("Segoe UI Semibold", 11),
            TextAlign = ContentAlignment.MiddleLeft
        };
        AddToCard(card, banner, 42);
        AddCardLabel(card, animated ? "GIF source" : "PNG source");
        var sourceRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            BackColor = Surface,
            Margin = new Padding(0, 4, 0, 10)
        };
        sourceRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        sourceRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
        var source = CreateTextBox();
        sourceRow.Controls.Add(source, 0, 0);
        sourceRow.Controls.Add(CreateButton("Browse", () => BrowseImage(source, animated)), 1, 0);
        AddToCard(card, sourceRow, 48);

        AddCardLabel(card, "Hotspot  ·  measured from top-left");
        var hotspotRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Surface,
            Padding = new Padding(0, 4, 0, 4),
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false
        };
        var hotspotX = CreateNumber(0, 0, ushort.MaxValue);
        var hotspotY = CreateNumber(0, 0, ushort.MaxValue);
        hotspotRow.Controls.Add(new Label { Text = "X", ForeColor = accent, AutoSize = true, Padding = new Padding(0, 7, 5, 0) });
        hotspotRow.Controls.Add(hotspotX);
        hotspotRow.Controls.Add(new Label { Text = "Y", ForeColor = accent, AutoSize = true, Padding = new Padding(14, 7, 5, 0) });
        hotspotRow.Controls.Add(hotspotY);
        AddToCard(card, hotspotRow, 42);
        var hotspotHint = new Label
        {
            Text = animated
                ? "GIF frame timing is preserved in the ANI. Hotspot follows any resize."
                : "Transparent artwork is preserved. Hotspot follows any resize.",
            ForeColor = Muted,
            Dock = DockStyle.Fill,
            AutoEllipsis = true
        };
        AddToCard(card, hotspotHint, 58);

        var convert = CreateButton(
            animated ? "Convert to ANI" : "Convert to CUR",
            () => ConvertFile(source.Text, animated, (int)hotspotX.Value, (int)hotspotY.Value),
            primary: true);
        convert.BackColor = accent;
        convert.FlatAppearance.BorderColor = accent;
        convert.Margin = new Padding(0, 8, 0, 0);
        AddToCard(card, convert, 52);
        card.Margin = new Padding(0, 0, 12, 0);
        return card;
    }

    private TabPage BuildTextTab()
    {
        var page = CreateTab("Text cursor");
        var layout = PageLayout(page);
        AddIntro(layout, "Text cursor accessibility",
            "Change the insertion caret thickness for your Windows account.");

        var card = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 1,
            RowCount = 0,
            GrowStyle = TableLayoutPanelGrowStyle.AddRows,
            BackColor = Surface,
            Padding = new Padding(18),
            Margin = new Padding(0, 8, 0, 12)
        };
        card.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.Controls.Add(card, 0, 1);

        AddCardLabel(card, "Caret thickness (1–20 pixels)");
        caretWidth.Minimum = 1;
        caretWidth.Maximum = 20;
        caretWidth.Value = 3;
        caretWidth.Width = 100;
        caretWidth.BackColor = SurfaceLight;
        caretWidth.ForeColor = Color.WhiteSmoke;
        caretWidth.Margin = new Padding(0, 10, 0, 18);
        AddToCard(card, caretWidth, 46);
        AddToCard(card, CreateButton("Apply thickness", ApplyCaretWidth, primary: true), 48);
        AddToCard(card, CreateButton(
            "Open Windows text cursor indicator settings",
            OpenTextCursorSettings), 48);
        return page;
    }

    private void RegisterInfOpenHandler()
    {
        try
        {
            var association = InfAssociationService.RegisterHandler();
            if (association.HasProtectedDefault &&
                !association.IsPointerStudioDefault)
            {
                SetStatus(
                    ".inf preview is registered. Windows currently protects "
                    + $"the default app ({association.CurrentDefault}); use Pack studio → INF preview to change it.");
            }
            else
            {
                SetStatus(".inf preview is registered for this Windows user.");
            }
        }
        catch (Exception exception) when (exception is IOException
                                        or UnauthorizedAccessException
                                        or System.Security.SecurityException
                                        or InvalidOperationException
                                        or Win32Exception)
        {
            status.ForeColor = Color.Salmon;
            status.Text = $"Could not register the .inf preview handler: {exception.Message}";
        }
    }

    private void BrowseInfFile()
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "Setup information (*.inf)|*.inf|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
            LoadInfPreview(dialog.FileName);
    }

    private void PreviewSelectedInf()
    {
        if (!string.IsNullOrWhiteSpace(infPath.Text))
            LoadInfPreview(infPath.Text);
    }

    private void BrowsePreviewFile()
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "Cursor and icon files (*.ani;*.cur;*.ico)|*.ani;*.cur;*.ico|"
                     + "Animated cursors (*.ani)|*.ani|"
                     + "Cursor files (*.cur)|*.cur|"
                     + "Icon files (*.ico)|*.ico",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
            LoadCursorFilePreview(dialog.FileName);
    }

    private void LoadCursorFilePreview(string path)
    {
        try
        {
            ClearInfCursorPreview();
            var animation = CursorPreviewRenderer.Open(path);
            try
            {
                currentInfCursorPreview = animation.Render(animation.Frames[0]);
                infCursorPreview.Image = currentInfCursorPreview;
                currentInfCursorAnimation = animation;
                infSchemeName.Text = Path.GetFileName(path);
                infPreviewStatus.Text = animation.IsAnimated
                    ? $"{Path.GetFileName(path)} · Animated"
                    : $"{Path.GetFileName(path)} · Preview";
                infPreviewStatus.ForeColor = Accent;
                if (animation.IsAnimated)
                {
                    infPreviewTimer.Interval = animation.Delays[0];
                    infPreviewTimer.Start();
                }
                SetStatus($"Previewing {Path.GetFileName(path)}.");
            }
            catch
            {
                animation.Dispose();
                throw;
            }
        }
        catch (Exception exception) when (exception is IOException
                                        or UnauthorizedAccessException
                                        or Win32Exception
                                        or ArgumentException
                                        or FormatException
                                        or System.Security.SecurityException
                                        or ExternalException)
        {
            ClearInfCursorPreview();
            infSchemeName.Text = Path.GetFileName(path);
            infPreviewStatus.Text = $"Cursor or icon preview unavailable: {exception.Message}";
            infPreviewStatus.ForeColor = Color.Salmon;
            SetStatus($"Could not preview {Path.GetFileName(path)}.");
        }
    }

    private void LoadInfPreview(string path)
    {
        try
        {
            var preview = CursorInfService.Parse(path);
            currentInfPreview = preview;
            infPath.Text = preview.InfPath;
            infSchemeName.Text = preview.IsCursorScheme
                ? preview.Name
                : Path.GetFileName(preview.InfPath);
            infPreviewStatus.Text = preview.Message
                ?? $"Found {preview.AvailableCursors.Count} cursor file(s). Select a role to preview its cursor.";
            infPreviewStatus.ForeColor = preview.CanInstall ? Accent : Color.Salmon;

            infRoles.Rows.Clear();
            foreach (var role in preview.Roles)
            {
                var file = role.SourcePath is null
                    ? role.Reference
                    : Path.GetFileName(role.SourcePath);
                var state = role.Reference.Length == 0
                    ? "Default"
                    : role.SourcePath is null ? "Missing" : "Ready";
                var rowIndex = infRoles.Rows.Add(role.Name, file, state);
                infRoles.Rows[rowIndex].Tag = role;
            }

            if (preview.Roles.Count == 0)
            {
                infRoles.Rows.Add(
                    "No supported cursor scheme",
                    "INF preview only",
                    "Not installable");
            }

            var sourceText = File.ReadAllText(preview.InfPath);
            const int previewTextLimit = 64 * 1024;
            infSource.Text = sourceText.Length <= previewTextLimit
                ? sourceText
                : sourceText[..previewTextLimit]
                  + Environment.NewLine
                  + "... preview truncated at 64 KB ...";
            importInfButton.Enabled = preview.CanInstall;
            UpdateInfCursorPreview();
            tabs.SelectedTab = tabs.TabPages.Cast<TabPage>()
                .First(page => page.Text == "Pack studio");
            packStudioTabs.SelectedTab = packStudioTabs.TabPages.Cast<TabPage>()
                .First(page => page.Text == "INF preview");
            SetStatus(preview.CanInstall
                ? $"Preview ready for cursor pack \"{preview.Name}\"."
                : preview.Message ?? "This INF does not contain an importable cursor scheme.");
        }
        catch (Exception exception) when (exception is IOException
                                        or UnauthorizedAccessException
                                        or ArgumentException
                                        or FormatException
                                        or System.Security.SecurityException)
        {
            currentInfPreview = null;
            importInfButton.Enabled = false;
            infRoles.Rows.Clear();
            infSource.Clear();
            ClearInfCursorPreview();
            ShowError("Could not preview this INF file.", exception);
        }
    }

    private void UpdateInfCursorPreview()
    {
        if (infRoles.CurrentRow?.Tag is not CursorInfRole role ||
            role.SourcePath is null)
        {
            infSchemeName.Text = currentInfPreview?.Name ?? "Cursor preview";
            ClearInfCursorPreview();
            return;
        }

        ClearInfCursorPreview();
        try
        {
            var animation = CursorPreviewRenderer.Open(role.SourcePath);
            try
            {
                var image = animation.Render(animation.Frames[0]);
                currentInfCursorAnimation = animation;
                currentInfAnimationStep = 0;
                currentInfCursorPreview = image;
                infCursorPreview.Image = image;
            }
            catch
            {
                animation.Dispose();
                throw;
            }

            var isAnimated = animation.IsAnimated;
            infPreviewTimer.Interval = animation.Delays[0];
            if (isAnimated)
                infPreviewTimer.Start();

            infSchemeName.Text = role.Name;
            infPreviewStatus.Text = isAnimated
                ? $"{Path.GetFileName(role.SourcePath)} · {role.RegistryName} · Animated"
                : $"{Path.GetFileName(role.SourcePath)} · {role.RegistryName}";
            infPreviewStatus.ForeColor = Accent;
        }
        catch (Exception exception) when (exception is IOException
                                        or UnauthorizedAccessException
                                        or Win32Exception
                                        or ArgumentException
                                        or FormatException
                                        or System.Security.SecurityException
                                        or ExternalException)
        {
            ClearInfCursorPreview();
            infSchemeName.Text = role.Name;
            infPreviewStatus.Text = $"Cursor preview unavailable: {exception.Message}";
            infPreviewStatus.ForeColor = Color.Salmon;
        }
    }

    private void AdvanceInfCursorPreview()
    {
        var animation = currentInfCursorAnimation;
        if (animation is null || !animation.IsAnimated)
        {
            infPreviewTimer.Stop();
            return;
        }

        currentInfAnimationStep =
            (currentInfAnimationStep + 1) % animation.Frames.Length;
        try
        {
            var image = animation.Render(animation.Frames[currentInfAnimationStep]);
            var previous = currentInfCursorPreview;
            currentInfCursorPreview = image;
            infCursorPreview.Image = image;
            previous?.Dispose();
            infPreviewTimer.Interval = animation.Delays[currentInfAnimationStep];
        }
        catch (Exception exception) when (exception is IOException
                                        or UnauthorizedAccessException
                                        or Win32Exception
                                        or ArgumentException
                                        or ExternalException)
        {
            infPreviewTimer.Stop();
            currentInfCursorAnimation = null;
            animation.Dispose();
            ClearInfCursorPreview();
            infPreviewStatus.Text =
                $"Cursor animation preview stopped: {exception.Message}";
            infPreviewStatus.ForeColor = Color.Salmon;
        }
    }

    private void ClearInfCursorPreview()
    {
        infPreviewTimer.Stop();
        currentInfCursorAnimation?.Dispose();
        currentInfCursorAnimation = null;
        infCursorPreview.Image = null;
        currentInfCursorPreview?.Dispose();
        currentInfCursorPreview = null;
        currentInfAnimationStep = 0;
    }

    private void ImportCursorInf()
    {
        var preview = currentInfPreview;
        if (preview is null || !preview.CanInstall)
        {
            SetStatus("Select a supported cursor INF with all referenced files present.");
            return;
        }

        try
        {
            if (CursorPackService.IsRegistered(preview.Name) &&
                MessageBox.Show(
                    this,
                    $"A cursor scheme named \"{preview.Name}\" already exists. Replace its scheme entry?",
                    "Replace cursor scheme",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning) != DialogResult.Yes)
                return;

            var packDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PointerStudio", "Packs");
            var pack = CursorPackService.Build(
                preview.Name, preview.AvailableCursors, packDirectory);
            try
            {
                CursorPackService.Register(pack);
            }
            catch
            {
                Directory.Delete(pack.DirectoryPath, recursive: true);
                throw;
            }

            lastPackDirectory = pack.DirectoryPath;
            SetStatus(
                $"Imported \"{pack.Name}\" safely and registered {pack.Files.Count} cursor file(s).");
            OpenMouseProperties();
        }
        catch (Exception exception) when (exception is IOException
                                        or UnauthorizedAccessException
                                        or FormatException
                                        or ArgumentException
                                        or InvalidOperationException
                                        or System.Security.SecurityException)
        {
            ShowError("Could not import the cursor pack.", exception);
        }
    }

    private void SetInfAsDefault()
    {
        try
        {
            var association = InfAssociationService.SetAsDefault();
            if (association.HasProtectedDefault &&
                !association.IsPointerStudioDefault)
            {
                InfAssociationService.OpenDefaultAppsSettings();
                MessageBox.Show(
                    this,
                    "Windows protects the current .inf default app, so Pointer Studio cannot replace it silently. "
                    + "In Default apps, search for “.inf” and choose Pointer Studio. "
                    + "After you confirm it once, future .inf files will open here.",
                    "Choose the .inf default app",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                SetStatus(
                    "Windows Default Apps is open. Choose Pointer Studio for .inf files to complete the association.");
            }
            else
            {
                SetStatus("Pointer Studio is now the current-user default for .inf files.");
            }
        }
        catch (Exception exception) when (exception is IOException
                                        or UnauthorizedAccessException
                                        or System.Security.SecurityException
                                        or InvalidOperationException
                                        or Win32Exception)
        {
            ShowError("Could not register Pointer Studio for .inf files.", exception);
        }
    }

    private void OpenInfDefaultAppsSettings()
    {
        try
        {
            InfAssociationService.OpenDefaultAppsSettings();
            SetStatus("Opened Windows Default Apps. Search for .inf to choose its default handler.");
        }
        catch (Exception exception) when (exception is Win32Exception
                                        or InvalidOperationException)
        {
            ShowError("Could not open Windows Default Apps settings.", exception);
        }
    }

    private void ApplyCaretWidth()
    {
        if (!SystemParametersInfo(
                SpiSetCaretWidth,
                (uint)caretWidth.Value,
                IntPtr.Zero,
                SpifUpdateIniFile | SpifSendChange))
        {
            ShowError("Windows did not accept the caret thickness.",
                new Win32Exception(Marshal.GetLastWin32Error()));
            return;
        }
        SetStatus($"Text caret thickness set to {(int)caretWidth.Value} pixels.");
    }

    private void CreateCursorPack()
    {
        try
        {
            var name = packName.Text.Trim();
            if (CursorPackService.IsRegistered(name) &&
                MessageBox.Show(
                    this,
                    $"A cursor scheme named \"{name}\" already exists. Replace its scheme entry?",
                    "Replace cursor scheme",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning) != DialogResult.Yes)
                return;

            var sourceAssignments = ReadCursorAssignments();
            var packsDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PointerStudio", "Packs");
            var pack = CursorPackService.Build(name, sourceAssignments, packsDirectory);
            try
            {
                CursorPackService.Register(pack);
            }
            catch
            {
                Directory.Delete(pack.DirectoryPath, recursive: true);
                throw;
            }

            lastPackDirectory = pack.DirectoryPath;
            SetStatus(
                $"Created \"{pack.Name}\" and registered it in Mouse Properties. "
                + $"Packed {pack.Files.Count} cursor file(s).");
            OpenMouseProperties();
        }
        catch (Exception exception) when (exception is IOException
                                        or UnauthorizedAccessException
                                        or ArgumentException
                                        or FormatException
                                        or InvalidOperationException
                                        or System.Security.SecurityException)
        {
            ShowError("Could not create the cursor pack.", exception);
        }
    }

    private static Dictionary<string, string> ReadCursorAssignments()
    {
        using var key = Registry.CurrentUser.OpenSubKey(CursorRegistryPath)
            ?? throw new IOException(
                "Could not read the current Windows cursor assignments.");
        return CursorRoles.ToDictionary(
            role => role.RegistryName,
            role => key.GetValue(role.RegistryName)?.ToString() ?? string.Empty,
            StringComparer.Ordinal);
    }

    private void OpenMouseProperties()
    {
        try
        {
            Process.Start(new ProcessStartInfo(
                "rundll32.exe", "shell32.dll,Control_RunDLL main.cpl,,1")
            {
                UseShellExecute = true
            });
            SetStatus("Opened Mouse Properties → Pointers.");
        }
        catch (Exception exception) when (exception is InvalidOperationException
                                        or Win32Exception
                                        or System.Security.SecurityException)
        {
            ShowError("Could not open Windows Mouse Properties.", exception);
        }
    }

    private void OpenPackFolder()
    {
        var folder = lastPackDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PointerStudio", "Packs");
        if (!Directory.Exists(folder))
        {
            SetStatus("Create a cursor pack first; no pack folder exists yet.");
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
        }
        catch (Exception exception) when (exception is InvalidOperationException
                                        or Win32Exception
                                        or System.Security.SecurityException)
        {
            ShowError("Could not open the cursor-pack folder.", exception);
        }
    }

    private void OpenTextCursorSettings()
    {
        try
        {
            Process.Start(new ProcessStartInfo("ms-settings:easeofaccess-cursor")
            {
                UseShellExecute = true
            });
            SetStatus("Opened Windows text cursor accessibility settings.");
        }
        catch (Exception exception) when (exception is InvalidOperationException
                                        or Win32Exception
                                        or System.Security.SecurityException)
        {
            ShowError("Could not open Windows Accessibility settings.", exception);
        }
    }

    private void ConvertFile(string source, bool animated, int hotspotX, int hotspotY)
    {
        if (string.IsNullOrWhiteSpace(source) || !File.Exists(source))
        {
            ShowError("Choose a source image first.",
                new FileNotFoundException("The selected image could not be found.", source));
            return;
        }

        using var dialog = new SaveFileDialog
        {
            AddExtension = true,
            DefaultExt = animated ? "ani" : "cur",
            Filter = animated
                ? "Animated cursor (*.ani)|*.ani"
                : "Windows cursor (*.cur)|*.cur",
            FileName = Path.GetFileNameWithoutExtension(source) + (animated ? ".ani" : ".cur"),
            OverwritePrompt = true
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        try
        {
            var result = animated
                ? CursorCompiler.GifToAni(source, hotspotX, hotspotY)
                : CursorCompiler.PngToCur(source, hotspotX, hotspotY);
            File.WriteAllBytes(dialog.FileName, result.Data);
            var resized = result.WasResized
                ? $" Image scaled from {result.SourceWidth} × {result.SourceHeight} to {result.Width} × {result.Height}; hotspot adjusted."
                : string.Empty;
            SetStatus($"Saved {Path.GetFileName(dialog.FileName)}.{resized}");
        }
        catch (Exception exception) when (exception is IOException
                                        or UnauthorizedAccessException
                                        or OutOfMemoryException
                                        or FormatException
                                        or ArgumentException)
        {
            ShowError("Conversion failed.", exception);
        }
    }

    private static void BrowseImage(TextBox target, bool animated)
    {
        using var dialog = new OpenFileDialog
        {
            Filter = animated
                ? "GIF animation (*.gif)|*.gif"
                : "PNG image (*.png)|*.png",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog() == DialogResult.OK)
            target.Text = dialog.FileName;
    }

    private TabPage CreateTab(string title) => new(title)
    {
        BackColor = Background,
        ForeColor = Color.WhiteSmoke,
        Padding = new Padding(14),
        BorderStyle = BorderStyle.None
    };

    private static TableLayoutPanel PageLayout(Control parent)
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = Background,
            Padding = new Padding(8, 12, 8, 8)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 88));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        parent.Controls.Add(layout);
        return layout;
    }

    private static void AddIntro(TableLayoutPanel layout, string title, string description)
    {
        var block = new Panel { Dock = DockStyle.Fill, BackColor = Background };
        block.Controls.Add(new Label
        {
            Text = "✧  POINTER STUDIO  /  CREATIVE WORKSPACE",
            Font = new Font("Segoe UI Semibold", 8),
            ForeColor = Cyan,
            AutoSize = true,
            Location = new Point(1, 0)
        });
        block.Controls.Add(new Label
        {
            Text = title,
            Font = new Font("Segoe UI Semibold", 16),
            ForeColor = Accent,
            AutoSize = true,
            Location = new Point(0, 18)
        });
        block.Controls.Add(new Label
        {
            Text = description,
            ForeColor = Muted,
            AutoSize = true,
            Location = new Point(1, 55)
        });
        layout.Controls.Add(block, 0, 0);
    }

    private static void AddCardLabel(TableLayoutPanel card, string text)
    {
        AddToCard(card, new Label
        {
            Text = text,
            ForeColor = Color.WhiteSmoke,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        }, 28);
    }

    private static void AddToCard(TableLayoutPanel card, Control control, int height)
    {
        var row = card.RowCount++;
        card.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        card.Controls.Add(control, 0, row);
    }

    private static Button CreateButton(string text, Action action, bool primary = false)
    {
        var button = new Button
        {
            Text = text,
            AutoSize = true,
            FlatStyle = FlatStyle.Flat,
            BackColor = primary ? Accent : SurfaceLight,
            ForeColor = primary ? Color.FromArgb(35, 20, 39) : Color.WhiteSmoke,
            Font = new Font("Segoe UI Semibold", 9),
            Padding = new Padding(12, 5, 12, 5),
            Margin = new Padding(0, 4, 8, 4),
            Cursor = Cursors.Hand
        };
        button.FlatAppearance.BorderColor = primary ? Accent : Color.FromArgb(70, Cyan);
        button.FlatAppearance.BorderSize = 1;
        button.Click += (_, _) => action();
        return button;
    }

    private static TableLayoutPanel WorkspaceLayout(Control parent)
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Background,
            Padding = new Padding(8, 8, 8, 8)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        parent.Controls.Add(layout);
        return layout;
    }

    private static TextBox CreateTextBox() => new()
    {
        Dock = DockStyle.Fill,
        BorderStyle = BorderStyle.FixedSingle,
        BackColor = SurfaceLight,
        ForeColor = Color.WhiteSmoke,
        Margin = new Padding(0, 4, 8, 4)
    };

    private static NumericUpDown CreateNumber(int value, int minimum, int maximum) => new()
    {
        Minimum = minimum,
        Maximum = maximum,
        Value = value,
        Width = 90,
        BackColor = SurfaceLight,
        ForeColor = Color.WhiteSmoke
    };

    private void SetStatus(string message)
    {
        status.ForeColor = Accent;
        status.Text = message;
    }

    private void ShowError(string message, Exception exception)
    {
        status.ForeColor = Color.Salmon;
        status.Text = message;
        MessageBox.Show(
            this,
            $"{message}{Environment.NewLine}{exception.Message}",
            "Pointer Studio",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
    }

    private sealed record CursorRole(string RegistryName);
}

internal static class CursorCompiler
{
    private const int MaxFrames = 512;
    private const int GifFrameDelayProperty = 0x5100;
    private static ReadOnlySpan<byte> PngSignature => [137, 80, 78, 71, 13, 10, 26, 10];

    public static CursorCompilation PngToCur(string path, int hotspotX, int hotspotY)
    {
        var png = File.ReadAllBytes(path);
        if (png.Length < PngSignature.Length ||
            !png.AsSpan(0, PngSignature.Length).SequenceEqual(PngSignature))
            throw new FormatException("The selected file is not a PNG image.");

        using var image = System.Drawing.Image.FromFile(path);
        ValidateHotspot(image.Width, image.Height, hotspotX, hotspotY);
        using var source = new Bitmap(image);
        using var prepared = PrepareFrame(source, hotspotX, hotspotY);
        var resultPng = png;
        if (prepared.WasResized)
        {
            using var output = new MemoryStream();
            prepared.Bitmap.Save(output, ImageFormat.Png);
            resultPng = output.ToArray();
        }

        var data = BuildCur(
            resultPng, prepared.Width, prepared.Height,
            prepared.HotspotX, prepared.HotspotY);
        return new CursorCompilation(
            data, prepared.SourceWidth, prepared.SourceHeight,
            prepared.Width, prepared.Height, prepared.WasResized);
    }

    public static CursorCompilation GifToAni(string path, int hotspotX, int hotspotY)
    {
        using var image = System.Drawing.Image.FromFile(path);
        if (image.RawFormat.Guid != ImageFormat.Gif.Guid)
            throw new FormatException("The selected file is not a GIF animation.");

        ValidateHotspot(image.Width, image.Height, hotspotX, hotspotY);
        var sourceWidth = image.Width;
        var sourceHeight = image.Height;
        var frameCount = image.GetFrameCount(FrameDimension.Time);
        if (frameCount > MaxFrames)
            throw new FormatException($"GIFs are limited to {MaxFrames} frames.");

        var delays = ReadGifFrameDelays(image, frameCount);
        var cursorFrames = new List<(byte[] Cursor, int Delay)>(frameCount);
        PreparedFrame? firstPrepared = null;
        for (var i = 0; i < frameCount; i++)
        {
            image.SelectActiveFrame(FrameDimension.Time, i);
            using var source = new Bitmap(image);
            using var prepared = PrepareFrame(source, hotspotX, hotspotY);
            firstPrepared ??= prepared;

            using var png = new MemoryStream();
            prepared.Bitmap.Save(png, ImageFormat.Png);
            var cursor = BuildCur(
                png.ToArray(),
                prepared.Width,
                prepared.Height,
                prepared.HotspotX,
                prepared.HotspotY);
            cursorFrames.Add((
                cursor,
                Math.Max(1, (int)Math.Round(delays[i] * 60.0 / 1000.0))));
        }

        using var chunks = new MemoryStream();
        using (var writer = new BinaryWriter(chunks, System.Text.Encoding.ASCII, leaveOpen: true))
        {
            WriteChunk(writer, "anih", BuildAnimationHeader(cursorFrames.Count));
            using var rates = new MemoryStream();
            using (var rateWriter = new BinaryWriter(rates, System.Text.Encoding.ASCII, leaveOpen: true))
            {
                foreach (var frame in cursorFrames)
                    rateWriter.Write((uint)frame.Delay);
            }
            WriteChunk(writer, "rate", rates.ToArray());

            using var frameList = new MemoryStream();
            using (var listWriter = new BinaryWriter(frameList, System.Text.Encoding.ASCII, leaveOpen: true))
            {
                WriteFourCc(listWriter, "fram");
                foreach (var frame in cursorFrames)
                    WriteChunk(listWriter, "icon", frame.Cursor);
            }
            WriteChunk(writer, "LIST", frameList.ToArray());
        }

        using var riff = new MemoryStream();
        using (var writer = new BinaryWriter(riff, System.Text.Encoding.ASCII, leaveOpen: true))
        {
            WriteFourCc(writer, "RIFF");
            writer.Write(checked((uint)(chunks.Length + 4)));
            WriteFourCc(writer, "ACON");
            chunks.Position = 0;
            chunks.CopyTo(riff);
        }
        var first = firstPrepared
            ?? throw new FormatException("The GIF contains no frames.");
        return new CursorCompilation(
            riff.ToArray(), sourceWidth, sourceHeight,
            first.Width, first.Height,
            sourceWidth != first.Width || sourceHeight != first.Height);
    }

    private static int[] ReadGifFrameDelays(System.Drawing.Image image, int frameCount)
    {
        try
        {
            var rawDelays = image.GetPropertyItem(GifFrameDelayProperty)?.Value;
            if (rawDelays is null)
                return Enumerable.Repeat(100, frameCount).ToArray();

            var delays = new int[frameCount];
            for (var i = 0; i < frameCount; i++)
            {
                var offset = i * sizeof(uint);
                var centiseconds = offset + sizeof(uint) <= rawDelays.Length
                    ? BitConverter.ToUInt32(rawDelays, offset)
                    : 10u;
                delays[i] = checked((int)Math.Max(1, centiseconds) * 10);
            }
            return delays;
        }
        catch (ArgumentException)
        {
            return Enumerable.Repeat(100, frameCount).ToArray();
        }
    }

    private static void ValidateHotspot(int width, int height, int hotspotX, int hotspotY)
    {
        if (hotspotX < 0 || hotspotY < 0 || hotspotX >= width || hotspotY >= height)
            throw new FormatException($"The hotspot must be inside the {width} × {height} image.");
    }

    private static PreparedFrame PrepareFrame(
        Bitmap source, int hotspotX, int hotspotY)
    {
        var scale = Math.Min(1d, Math.Min(256d / source.Width, 256d / source.Height));
        var width = Math.Max(1, (int)Math.Round(
            source.Width * scale, MidpointRounding.AwayFromZero));
        var height = Math.Max(1, (int)Math.Round(
            source.Height * scale, MidpointRounding.AwayFromZero));
        var scaledHotspotX = Math.Clamp(
            (int)Math.Round(hotspotX * scale, MidpointRounding.AwayFromZero),
            0, width - 1);
        var scaledHotspotY = Math.Clamp(
            (int)Math.Round(hotspotY * scale, MidpointRounding.AwayFromZero),
            0, height - 1);

        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
            graphics.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
            graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
            graphics.Clear(Color.Transparent);
            graphics.DrawImage(source, new Rectangle(0, 0, width, height));
        }

        return new PreparedFrame(
            bitmap, source.Width, source.Height, width, height,
            scaledHotspotX, scaledHotspotY,
            width != source.Width || height != source.Height);
    }

    private static byte[] BuildCur(byte[] png, int width, int height, int hotspotX, int hotspotY)
    {
        using var stream = new MemoryStream(22 + png.Length);
        using var writer = new BinaryWriter(stream);
        writer.Write((ushort)0);
        writer.Write((ushort)2);
        writer.Write((ushort)1);
        writer.Write((byte)(width == 256 ? 0 : width));
        writer.Write((byte)(height == 256 ? 0 : height));
        writer.Write((byte)0);
        writer.Write((byte)0);
        writer.Write((ushort)hotspotX);
        writer.Write((ushort)hotspotY);
        writer.Write((uint)png.Length);
        writer.Write((uint)22);
        writer.Write(png);
        return stream.ToArray();
    }

    private static byte[] BuildAnimationHeader(int frames)
    {
        using var stream = new MemoryStream(36);
        using var writer = new BinaryWriter(stream);
        writer.Write((uint)36);
        writer.Write((uint)frames);
        writer.Write((uint)frames);
        writer.Write((uint)0);
        writer.Write((uint)0);
        writer.Write((uint)0);
        writer.Write((uint)0);
        writer.Write((uint)6);
        writer.Write((uint)1);
        return stream.ToArray();
    }

    private static void WriteChunk(BinaryWriter writer, string name, byte[] data)
    {
        WriteFourCc(writer, name);
        writer.Write(checked((uint)data.Length));
        writer.Write(data);
        if ((data.Length & 1) != 0)
            writer.Write((byte)0);
    }

    private static void WriteFourCc(BinaryWriter writer, string name)
    {
        if (name.Length != 4)
            throw new ArgumentException("A RIFF chunk name must contain four characters.", nameof(name));
        writer.Write(System.Text.Encoding.ASCII.GetBytes(name));
    }

    private sealed record PreparedFrame(
        Bitmap Bitmap,
        int SourceWidth,
        int SourceHeight,
        int Width,
        int Height,
        int HotspotX,
        int HotspotY,
        bool WasResized) : IDisposable
    {
        public void Dispose() => Bitmap.Dispose();
    }
}

internal sealed record CursorCompilation(
    byte[] Data,
    int SourceWidth,
    int SourceHeight,
    int Width,
    int Height,
    bool WasResized);
