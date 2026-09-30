using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace SentinelConsole;

public class MainForm : Form
{
    private readonly FlowLayoutPanel _tabStrip = new();
    private readonly Panel _deviceHost = new();
    private readonly List<(Panel tabButton, DeviceView view)> _tabs = new();
    private Button? _btnAddTab;

    private static readonly Color BgDark = Color.FromArgb(24, 24, 27);
    private static readonly Color TabActive = Color.FromArgb(45, 45, 50);
    private static readonly Color TabInactive = Color.FromArgb(24, 24, 27);
    private static readonly Color TextLight = Color.White;

    public MainForm()
    {
        Text = "SentinelConsole - Admin Console v1.0";
        Width = 1320;
        Height = 820;
        MinimumSize = new Size(960, 620);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9F);

        BuildTabStrip();

        _deviceHost.Dock = DockStyle.Fill;

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(_tabStrip, 0, 0);
        layout.Controls.Add(_deviceHost, 0, 1);
        Controls.Add(layout);

        AddDeviceTab();
    }

    private void BuildTabStrip()
    {
        _tabStrip.Dock = DockStyle.Fill;
        _tabStrip.BackColor = BgDark;
        _tabStrip.FlowDirection = FlowDirection.LeftToRight;
        _tabStrip.WrapContents = false;
        _tabStrip.AutoScroll = true;
        _tabStrip.Padding = new Padding(4, 4, 4, 0);

        _btnAddTab = new Button
        {
            Text = "+",
            Width = 34,
            Height = 30,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(50, 50, 55),
            ForeColor = TextLight,
            Font = new Font("Segoe UI", 11F, FontStyle.Bold)
        };
        _btnAddTab.FlatAppearance.BorderSize = 0;
        _btnAddTab.Click += (s, e) => AddDeviceTab();

    }

    private void AddDeviceTab()
    {
        var view = new DeviceView();

        var chip = new Panel { Height = 30, Width = 150, Margin = new Padding(2, 0, 2, 0) };
        var title = new Label
        {
            Text = $"Device {_tabs.Count + 1}",
            Dock = DockStyle.Fill,
            ForeColor = TextLight,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(10, 0, 0, 0),
            AutoEllipsis = true,
            Cursor = Cursors.Hand
        };
        var close = new Label
        {
            Text = "\u2715",
            Dock = DockStyle.Right,
            Width = 26,
            ForeColor = Color.Gainsboro,
            TextAlign = ContentAlignment.MiddleCenter,
            Cursor = Cursors.Hand
        };
        chip.BackColor = TabInactive;
        title.BackColor = Color.Transparent;
        close.BackColor = Color.Transparent;
        chip.Controls.Add(title);
        chip.Controls.Add(close);

        title.Click += (s, e) => SelectTab(chip, view);
        chip.Click += (s, e) => SelectTab(chip, view);
        close.Click += (s, e) => CloseTab(chip, view);

        view.ConnectionInfoChanged += () =>
        {
            if (IsHandleCreated) BeginInvoke(new Action(() => title.Text = view.TabLabel));
        };

        _tabs.Add((chip, view));
        _deviceHost.Controls.Add(view);

        RebuildTabStrip();
        SelectTab(chip, view);
    }

    private void CloseTab(Panel chip, DeviceView view)
    {
        if (_tabs.Count <= 1)
        {
            MessageBox.Show("At least one device tab must stay open.");
            return;
        }

        var wasSelected = chip.BackColor == TabActive;
        _tabs.RemoveAll(t => t.tabButton == chip);
        _deviceHost.Controls.Remove(view);
        view.Dispose();

        RebuildTabStrip();

        if (wasSelected && _tabs.Count > 0)
            SelectTab(_tabs[0].tabButton, _tabs[0].view);
    }

    private void RebuildTabStrip()
    {
        _tabStrip.Controls.Clear();
        foreach (var (chip, _) in _tabs) _tabStrip.Controls.Add(chip);
        if (_btnAddTab != null) _tabStrip.Controls.Add(_btnAddTab);
    }

    private void SelectTab(Panel chip, DeviceView view)
    {
        foreach (var (c, v) in _tabs)
        {
            var isSelected = c == chip;
            c.BackColor = isSelected ? TabActive : TabInactive;
            v.Visible = isSelected;
        }
    }
}
