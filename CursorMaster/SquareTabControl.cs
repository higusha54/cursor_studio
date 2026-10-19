namespace CursorMaster;

internal sealed class SquareTabControl : UserControl
{
    private readonly List<TabPage> pages = [];
    private readonly List<Panel> pageViews = [];
    private readonly List<Button> tabButtons = [];
    private readonly Panel pageHost = new();
    private readonly FlowLayoutPanel navigation = new();
    private readonly Color backgroundColor;
    private readonly Color selectedColor;
    private readonly Color accentColor;
    private readonly Color mutedColor;
    private int selectedIndex = -1;

    public SquareTabControl(
        Color backgroundColor, Color selectedColor, Color accentColor, Color mutedColor)
    {
        this.backgroundColor = backgroundColor;
        this.selectedColor = selectedColor;
        this.accentColor = accentColor;
        this.mutedColor = mutedColor;

        BackColor = backgroundColor;
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = backgroundColor,
            ColumnCount = 1,
            RowCount = 2,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        navigation.Dock = DockStyle.Fill;
        navigation.BackColor = backgroundColor;
        navigation.FlowDirection = FlowDirection.LeftToRight;
        navigation.WrapContents = false;
        navigation.Margin = Padding.Empty;
        navigation.Padding = Padding.Empty;
        layout.Controls.Add(navigation, 0, 0);

        pageHost.Dock = DockStyle.Fill;
        pageHost.BackColor = backgroundColor;
        pageHost.Margin = Padding.Empty;
        layout.Controls.Add(pageHost, 0, 1);
        Controls.Add(layout);
    }

    public IReadOnlyList<TabPage> TabPages => pages;

    public TabPage? SelectedTab
    {
        get => selectedIndex >= 0 ? pages[selectedIndex] : null;
        set
        {
            if (value is null)
                return;
            var index = pages.IndexOf(value);
            if (index >= 0)
                SelectedIndex = index;
        }
    }

    public int SelectedIndex
    {
        get => selectedIndex;
        set
        {
            if (value < 0 || value >= pages.Count)
                throw new ArgumentOutOfRangeException(nameof(value));
            selectedIndex = value;
            for (var index = 0; index < pages.Count; index++)
            {
                pageViews[index].Visible = index == selectedIndex;
                tabButtons[index].BackColor = index == selectedIndex
                    ? selectedColor
                    : backgroundColor;
                tabButtons[index].ForeColor = index == selectedIndex
                    ? accentColor
                    : mutedColor;
                tabButtons[index].FlatAppearance.BorderColor = index == selectedIndex
                    ? accentColor
                    : selectedColor;
            }
        }
    }

    public void AddPage(TabPage page)
    {
        ArgumentNullException.ThrowIfNull(page);
        pages.Add(page);
        var pageView = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = page.BackColor,
            Padding = page.Padding,
            Visible = false
        };
        while (page.Controls.Count > 0)
        {
            var child = page.Controls[0];
            page.Controls.RemoveAt(0);
            pageView.Controls.Add(child);
        }
        pageViews.Add(pageView);
        pageHost.Controls.Add(pageView);

        var index = pages.Count - 1;
        var button = new Button
        {
            Text = page.Text,
            Width = 164,
            Height = 36,
            FlatStyle = FlatStyle.Flat,
            UseMnemonic = false,
            BackColor = backgroundColor,
            ForeColor = mutedColor,
            Font = new Font("Segoe UI Semibold", 9),
            Margin = new Padding(0, 0, 8, 0),
            Padding = new Padding(6, 0, 6, 0),
            Cursor = Cursors.Hand
        };
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.BorderColor = selectedColor;
        button.Click += (_, _) => SelectedIndex = index;
        tabButtons.Add(button);
        navigation.Controls.Add(button);

        if (selectedIndex < 0)
            SelectedIndex = 0;
    }
}
