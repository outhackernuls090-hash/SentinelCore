using Sentinel.Shared;
using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace SentinelConsole;

public class DeviceView : UserControl
{
    public event Action? ConnectionInfoChanged;
    public string TabLabel { get; private set; } = "New Device";

    private static readonly string PinsPath = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SentinelCore", "console-pins.json");
    private static readonly PinStore Pins = new(PinsPath);

    private SentinelApi? _api;
    private string _role = "Viewer";
    private bool _connected;

    private readonly TextBox _txtHost = new() { Text = "https://localhost:47152" };
    private readonly TextBox _txtApiKey = new() { UseSystemPasswordChar = true };
    private readonly Button _btnConnect = new() { Text = "Connect" };
    private readonly Label _statusDot = new() { Text = "\u25CF", ForeColor = Color.Gray };
    private readonly Label _statusText = new() { Text = "Not connected", AutoEllipsis = true };

    private readonly Panel _sidebar = new();
    private readonly Panel _content = new();

    private readonly Panel _panelDashboard = new();
    private readonly Panel _panelIncidents = new();
    private readonly Panel _panelProcesses = new();
    private readonly Panel _panelNetwork = new();
    private readonly Panel _panelBlocked = new();
    private readonly Panel _panelAllowlist = new();
    private readonly Panel _panelAudit = new();
    private readonly Panel _panelUsers = new();
    private readonly Panel _panelTesting = new();

    private Button _navDashboard = new();
    private Button _navIncidents = new();
    private Button _navProcesses = new();
    private Button _navNetwork = new();
    private Button _navBlocked = new();
    private Button _navAllowlist = new();
    private Button _navAudit = new();
    private Button _navUsers = new();
    private Button _navTesting = new();

    private readonly DataGridView _gridIncidents = new();
    private readonly ListBox _timelineList = new();
    private readonly DataGridView _gridProcesses = new();
    private readonly DataGridView _gridNetwork = new();
    private readonly DataGridView _gridBlocked = new();
    private readonly ListBox _allowlistList = new();
    private readonly DataGridView _gridAudit = new();
    private readonly DataGridView _gridUsers = new();

    private readonly Label _lblDashHost = new();
    private readonly Label _lblDashRole = new();
    private readonly Label _lblDashCounts = new();
    private readonly Label _lblDashFlags = new();
    private readonly CheckBox _chkMaintenance = new() { Text = "Maintenance mode (suppress auto-block)" };
    private readonly Button _btnTestEmail = new() { Text = "Send Test Email" };
    private readonly Button _btnExportReport = new() { Text = "Export Incident Report (CSV)" };

    private readonly TextBox _txtSimIp = new() { Text = "203.0.113.55" };

    private readonly System.Windows.Forms.Timer _refreshTimer = new();
    private readonly CheckBox _chkAutoRefresh = new() { Text = "Auto-refresh (5s)", Checked = true };

    private static readonly Color BgDark = Color.FromArgb(30, 30, 34);
    private static readonly Color BgPanel = Color.FromArgb(45, 45, 50);
    private static readonly Color BgContent = Color.White;
    private static readonly Color AccentBlue = Color.FromArgb(0, 120, 215);
    private static readonly Color TextLight = Color.White;

    public DeviceView()
    {
        Dock = DockStyle.Fill;
        BackColor = BgContent;
        Font = new Font("Segoe UI", 9F);

        var topBar = BuildTopBar();
        BuildSidebar();
        BuildContentPanels();
        var body = BuildBodyLayout();
        BuildRootLayout(topBar, body);
        ShowSection(_panelDashboard, _navDashboard);

        _refreshTimer.Interval = 5000;
        _refreshTimer.Tick += async (s, e) => { if (_chkAutoRefresh.Checked && _connected) await RefreshAllAsync(); };
        _refreshTimer.Start();
    }

    private TableLayoutPanel BuildTopBar()
    {
        var bar = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = BgDark,
            ColumnCount = 7,
            RowCount = 1,
            Padding = new Padding(10, 0, 10, 0)
        };
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 58));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 240));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 68));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 24));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bar.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        Label MakeLabel(string text) => new()
        {
            Text = text,
            ForeColor = TextLight,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(4, 15, 8, 0)
        };

        _txtHost.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        _txtHost.Margin = new Padding(0, 12, 12, 0);
        _txtApiKey.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        _txtApiKey.Margin = new Padding(0, 12, 12, 0);

        _btnConnect.Anchor = AnchorStyles.Left;
        _btnConnect.Margin = new Padding(0, 9, 16, 0);
        _btnConnect.Width = 96;
        _btnConnect.Height = 28;
        _btnConnect.BackColor = AccentBlue;
        _btnConnect.ForeColor = TextLight;
        _btnConnect.FlatStyle = FlatStyle.Flat;
        _btnConnect.FlatAppearance.BorderSize = 0;
        _btnConnect.Click += async (s, e) => await ConnectAsync();

        _statusDot.Font = new Font("Segoe UI", 13F);
        _statusDot.Margin = new Padding(0, 13, 4, 0);
        _statusDot.AutoSize = true;

        _statusText.ForeColor = TextLight;
        _statusText.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        _statusText.Margin = new Padding(0, 15, 0, 0);
        _statusText.AutoEllipsis = true;

        bar.Controls.Add(MakeLabel("Agent:"), 0, 0);
        bar.Controls.Add(_txtHost, 1, 0);
        bar.Controls.Add(MakeLabel("API Key:"), 2, 0);
        bar.Controls.Add(_txtApiKey, 3, 0);
        bar.Controls.Add(_btnConnect, 4, 0);
        bar.Controls.Add(_statusDot, 5, 0);
        bar.Controls.Add(_statusText, 6, 0);

        return bar;
    }

    private void BuildSidebar()
    {
        _sidebar.Dock = DockStyle.Fill;
        _sidebar.BackColor = BgPanel;

        _navDashboard = MakeNavButton("Dashboard");
        _navIncidents = MakeNavButton("Incidents");
        _navProcesses = MakeNavButton("Processes");
        _navNetwork = MakeNavButton("Network");
        _navBlocked = MakeNavButton("Blocked IPs");
        _navAllowlist = MakeNavButton("Allowlist");
        _navAudit = MakeNavButton("Audit Log");
        _navUsers = MakeNavButton("Users");
        _navUsers.Visible = false;
        _navTesting = MakeNavButton("Local Testing");
        _navTesting.BackColor = Color.FromArgb(70, 55, 20);

        _navDashboard.Click += (s, e) => ShowSection(_panelDashboard, _navDashboard);
        _navIncidents.Click += (s, e) => ShowSection(_panelIncidents, _navIncidents);
        _navProcesses.Click += (s, e) => ShowSection(_panelProcesses, _navProcesses);
        _navNetwork.Click += (s, e) => ShowSection(_panelNetwork, _navNetwork);
        _navBlocked.Click += (s, e) => ShowSection(_panelBlocked, _navBlocked);
        _navAllowlist.Click += (s, e) => ShowSection(_panelAllowlist, _navAllowlist);
        _navAudit.Click += (s, e) => ShowSection(_panelAudit, _navAudit);
        _navUsers.Click += (s, e) => ShowSection(_panelUsers, _navUsers);
        _navTesting.Click += (s, e) => ShowSection(_panelTesting, _navTesting);

        _sidebar.Controls.Add(_navTesting);
        _sidebar.Controls.Add(_navUsers);
        _sidebar.Controls.Add(_navAudit);
        _sidebar.Controls.Add(_navAllowlist);
        _sidebar.Controls.Add(_navBlocked);
        _sidebar.Controls.Add(_navNetwork);
        _sidebar.Controls.Add(_navProcesses);
        _sidebar.Controls.Add(_navIncidents);
        _sidebar.Controls.Add(_navDashboard);
    }

    private Button MakeNavButton(string text) => new()
    {
        Text = text,
        Dock = DockStyle.Top,
        Height = 40,
        FlatStyle = FlatStyle.Flat,
        BackColor = BgPanel,
        ForeColor = TextLight,
        TextAlign = ContentAlignment.MiddleLeft,
        Padding = new Padding(16, 0, 0, 0),
        FlatAppearance = { BorderSize = 0 }
    };

    private void ShowSection(Panel target, Button navButton)
    {
        foreach (var p in new[] { _panelDashboard, _panelIncidents, _panelProcesses, _panelNetwork, _panelBlocked, _panelAllowlist, _panelAudit, _panelUsers, _panelTesting })
            p.Visible = p == target;

        foreach (var b in new[] { _navDashboard, _navIncidents, _navProcesses, _navNetwork, _navBlocked, _navAllowlist, _navAudit, _navUsers })
            b.BackColor = b == navButton ? AccentBlue : BgPanel;
        _navTesting.BackColor = navButton == _navTesting ? AccentBlue : Color.FromArgb(70, 55, 20);

        foreach (var g in new[] { _gridIncidents, _gridProcesses, _gridNetwork, _gridBlocked, _gridAudit, _gridUsers })
            ResetGridView(g);
    }

    private TableLayoutPanel BuildBodyLayout()
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(_sidebar, 0, 0);
        layout.Controls.Add(_content, 1, 0);
        return layout;
    }

    private void BuildRootLayout(Control topBar, Control body)
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.Controls.Add(topBar, 0, 0);
        root.Controls.Add(body, 0, 1);
        Controls.Add(root);
    }

    private void BuildContentPanels()
    {
        _content.Dock = DockStyle.Fill;
        _content.BackColor = BgContent;
        _content.Padding = new Padding(12);

        BuildDashboardPanel();
        BuildIncidentsPanel();
        BuildProcessesPanel();
        BuildGridOnlyPanel(_panelNetwork, _gridNetwork, "Network Connections", BuildNetworkColumns);
        BuildBlockedPanel();
        BuildAllowlistPanel();
        BuildAuditPanel();
        BuildUsersPanel();
        BuildTestingPanel();

        _content.Controls.Add(_panelTesting);
        _content.Controls.Add(_panelUsers);
        _content.Controls.Add(_panelAudit);
        _content.Controls.Add(_panelAllowlist);
        _content.Controls.Add(_panelBlocked);
        _content.Controls.Add(_panelNetwork);
        _content.Controls.Add(_panelProcesses);
        _content.Controls.Add(_panelIncidents);
        _content.Controls.Add(_panelDashboard);
    }

    private static Label SectionTitle(string text) => new()
    {
        Text = text,
        Dock = DockStyle.Top,
        Height = 32,
        Font = new Font("Segoe UI", 13F, FontStyle.Bold),
        ForeColor = Color.FromArgb(30, 30, 30)
    };

    private DataGridView StyleGrid(DataGridView grid)
    {
        grid.Dock = DockStyle.Fill;
        grid.ReadOnly = true;
        grid.AllowUserToAddRows = false;
        grid.AllowUserToDeleteRows = false;
        grid.AutoGenerateColumns = false;
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        grid.ScrollBars = ScrollBars.Vertical;
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        grid.MultiSelect = false;
        grid.RowHeadersVisible = false;
        grid.BackgroundColor = BgContent;
        grid.BorderStyle = BorderStyle.None;
        grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(245, 246, 248);
        grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(235, 236, 240);
        grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
        grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
        grid.EnableHeadersVisualStyles = false;
        return grid;
    }

    private static void ResetGridView(DataGridView grid)
    {
        try
        {
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            grid.ClearSelection();
            grid.CurrentCell = null;
            if (grid.Columns.Count > 0) grid.FirstDisplayedScrollingColumnIndex = 0;
            grid.Refresh();
        }
        catch
        {
        }
    }

    private void BuildGridOnlyPanel(Panel panel, DataGridView grid, string title, Action<DataGridView> columns)
    {
        panel.Dock = DockStyle.Fill;
        panel.Visible = false;
        panel.Padding = new Padding(12);
        StyleGrid(grid);
        columns(grid);
        panel.Controls.Add(grid);
        panel.Controls.Add(SectionTitle(title));
    }

    private static FlowLayoutPanel ActionRow() => new()
    {
        Dock = DockStyle.Bottom,
        FlowDirection = FlowDirection.LeftToRight,
        AutoSize = true,
        WrapContents = false,
        Padding = new Padding(0, 8, 0, 0)
    };

    private static Button ActionButton(string text, Color? fg = null) => new()
    {
        Text = text,
        AutoSize = true,
        Height = 30,
        Margin = new Padding(0, 0, 8, 0),
        Padding = new Padding(10, 0, 10, 0),
        ForeColor = fg ?? Color.Black
    };

    private void BuildDashboardPanel()
    {
        _panelDashboard.Dock = DockStyle.Fill;
        _panelDashboard.Padding = new Padding(12);

        var stack = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 1, AutoSize = true };
        stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _lblDashHost.AutoSize = true;
        _lblDashHost.Font = new Font("Segoe UI", 11F, FontStyle.Bold);
        _lblDashHost.Margin = new Padding(0, 8, 0, 4);
        _lblDashHost.Text = "Not connected";

        _lblDashRole.AutoSize = true;
        _lblDashRole.Margin = new Padding(0, 0, 0, 10);
        _lblDashRole.ForeColor = Color.DimGray;

        _lblDashCounts.AutoSize = true;
        _lblDashCounts.Font = new Font("Segoe UI", 10F);
        _lblDashCounts.Margin = new Padding(0, 0, 0, 4);

        _lblDashFlags.AutoSize = true;
        _lblDashFlags.ForeColor = Color.DimGray;
        _lblDashFlags.Margin = new Padding(0, 0, 0, 16);

        _chkMaintenance.AutoSize = true;
        _chkMaintenance.Margin = new Padding(0, 0, 0, 16);
        _chkMaintenance.CheckedChanged += async (s, e) => await ToggleMaintenanceAsync();

        var actions = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = false };
        _btnTestEmail.AutoSize = true;
        _btnTestEmail.Height = 32;
        _btnTestEmail.Margin = new Padding(0, 0, 10, 0);
        _btnTestEmail.Click += async (s, e) => await TestEmailAsync();

        _btnExportReport.AutoSize = true;
        _btnExportReport.Height = 32;
        _btnExportReport.Click += async (s, e) => await ExportReportAsync();

        actions.Controls.Add(_btnTestEmail);
        actions.Controls.Add(_btnExportReport);

        stack.Controls.Add(_lblDashHost, 0, 0);
        stack.Controls.Add(_lblDashRole, 0, 1);
        stack.Controls.Add(_lblDashCounts, 0, 2);
        stack.Controls.Add(_lblDashFlags, 0, 3);
        stack.Controls.Add(_chkMaintenance, 0, 4);
        stack.Controls.Add(actions, 0, 5);
        for (int i = 0; i < 6; i++) stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _panelDashboard.Controls.Add(stack);
        _panelDashboard.Controls.Add(SectionTitle("Dashboard"));
    }

    private async System.Threading.Tasks.Task ToggleMaintenanceAsync()
    {
        if (_api == null || !_connected || !_chkMaintenance.Focused) return;
        try
        {
            await _api.SetMaintenanceAsync(_chkMaintenance.Checked);
        }
        catch (Exception ex)
        {
            MessageBox.Show("Could not change maintenance mode: " + ex.Message);
        }
    }

    private async System.Threading.Tasks.Task TestEmailAsync()
    {
        if (_api == null) return;
        try
        {
            var res = await _api.TestEmailAsync();
            MessageBox.Show(res.Success ? "Test email sent." : "Failed - check SMTP settings in appsettings.json: " + res.Message);
        }
        catch (Exception ex)
        {
            MessageBox.Show("Failed: " + ex.Message);
        }
    }

    private async System.Threading.Tasks.Task ExportReportAsync()
    {
        if (_api == null) return;
        try
        {
            var csv = await _api.GetReportCsvAsync();
            using var dialog = new SaveFileDialog { Filter = "CSV files (*.csv)|*.csv", FileName = "sentinelcore-incidents.csv" };
            if (dialog.ShowDialog() == DialogResult.OK)
            {
                await System.IO.File.WriteAllTextAsync(dialog.FileName, csv);
                MessageBox.Show("Report exported.");
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show("Export failed: " + ex.Message);
        }
    }

    private void BuildIncidentsPanel()
    {
        _panelIncidents.Dock = DockStyle.Fill;
        _panelIncidents.Padding = new Padding(12);

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            FixedPanel = FixedPanel.None,
            Panel1MinSize = 100,
            Panel2MinSize = 80
        };
        var splitterDistanceSet = false;
        split.SizeChanged += (s, e) =>
        {
            if (splitterDistanceSet || split.Height < 250) return;
            try { split.SplitterDistance = split.Height - 180; splitterDistanceSet = true; } catch { }
        };

        StyleGrid(_gridIncidents);
        _gridIncidents.Columns.Add(new DataGridViewTextBoxColumn { Name = "LastSeen", HeaderText = "Last Seen", DataPropertyName = "LastSeen", MinimumWidth = 120, FillWeight = 13 });
        _gridIncidents.Columns.Add(new DataGridViewTextBoxColumn { Name = "Severity", HeaderText = "Severity", DataPropertyName = "Severity", MinimumWidth = 80, FillWeight = 8 });
        _gridIncidents.Columns.Add(new DataGridViewTextBoxColumn { Name = "State", HeaderText = "State", DataPropertyName = "State", MinimumWidth = 100, FillWeight = 10 });
        _gridIncidents.Columns.Add(new DataGridViewTextBoxColumn { Name = "Rule", HeaderText = "Rule", DataPropertyName = "Rule", MinimumWidth = 150, FillWeight = 17 });
        _gridIncidents.Columns.Add(new DataGridViewTextBoxColumn { Name = "SourceIp", HeaderText = "Source IP", DataPropertyName = "SourceIp", MinimumWidth = 110, FillWeight = 12 });
        _gridIncidents.Columns.Add(new DataGridViewTextBoxColumn { Name = "Count", HeaderText = "Count", DataPropertyName = "Count", MinimumWidth = 60, FillWeight = 6 });
        _gridIncidents.Columns.Add(new DataGridViewTextBoxColumn { Name = "Details", HeaderText = "Details", DataPropertyName = "Details", MinimumWidth = 200, FillWeight = 24 });
        _gridIncidents.CellFormatting += (s, e) =>
        {
            if (_gridIncidents.Columns[e.ColumnIndex].Name != "Severity" || e.Value == null) return;
            e.CellStyle!.ForeColor = e.Value.ToString() switch
            {
                "high" or "critical" => Color.Firebrick,
                "medium" => Color.DarkOrange,
                _ => Color.Black
            };
            e.CellStyle.Font = new Font(_gridIncidents.Font, FontStyle.Bold);
        };
        _gridIncidents.SelectionChanged += async (s, e) => await LoadTimelineAsync();

        var incidentActions = ActionRow();
        var btnAck = ActionButton("Acknowledge");
        btnAck.Click += async (s, e) => await SetIncidentStateAsync("investigating");
        var btnResolve = ActionButton("Resolve");
        btnResolve.Click += async (s, e) => await SetIncidentStateAsync("resolved");
        var btnFalsePositive = ActionButton("Mark False Positive");
        btnFalsePositive.Click += async (s, e) => await SetIncidentStateAsync("false_positive");
        var btnIgnore = ActionButton("Ignore");
        btnIgnore.Click += async (s, e) => await SetIncidentStateAsync("ignored");
        var btnRefresh = ActionButton("Refresh Now");
        btnRefresh.Click += async (s, e) => await RefreshAllAsync();
        _chkAutoRefresh.AutoSize = true;
        _chkAutoRefresh.Margin = new Padding(10, 6, 0, 0);
        incidentActions.Controls.Add(btnAck);
        incidentActions.Controls.Add(btnResolve);
        incidentActions.Controls.Add(btnFalsePositive);
        incidentActions.Controls.Add(btnIgnore);
        incidentActions.Controls.Add(btnRefresh);
        incidentActions.Controls.Add(_chkAutoRefresh);

        var gridHost = new Panel { Dock = DockStyle.Fill };
        gridHost.Controls.Add(_gridIncidents);
        gridHost.Controls.Add(incidentActions);
        split.Panel1.Controls.Add(gridHost);

        _timelineList.Dock = DockStyle.Fill;
        _timelineList.Font = new Font("Consolas", 9F);
        var timelineHost = new Panel { Dock = DockStyle.Fill };
        var timelineTitle = new Label { Text = "Timeline (select an incident above)", Dock = DockStyle.Top, Height = 24, ForeColor = Color.DimGray };
        timelineHost.Controls.Add(_timelineList);
        timelineHost.Controls.Add(timelineTitle);
        split.Panel2.Controls.Add(timelineHost);

        _panelIncidents.Controls.Add(split);
        _panelIncidents.Controls.Add(SectionTitle("Incidents"));
    }

    private async System.Threading.Tasks.Task LoadTimelineAsync()
    {
        if (_api == null) return;
        _timelineList.Items.Clear();
        if (_gridIncidents.CurrentRow?.DataBoundItem is not Incident i) return;
        try
        {
            var entries = await _api.GetTimelineAsync(i.Id);
            foreach (var e in entries)
                _timelineList.Items.Add($"{e.Timestamp:u}  {e.Text}");
        }
        catch
        {
        }
    }

    private async System.Threading.Tasks.Task SetIncidentStateAsync(string state)
    {
        if (_api == null) return;
        if (_gridIncidents.CurrentRow?.DataBoundItem is not Incident i) { MessageBox.Show("Select an incident first."); return; }
        try
        {
            await _api.SetIncidentStateAsync(i.Id, state);
            await RefreshAllAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show("Failed: " + ex.Message);
        }
    }

    private void BuildProcessesPanel()
    {
        _panelProcesses.Dock = DockStyle.Fill;
        _panelProcesses.Visible = false;
        _panelProcesses.Padding = new Padding(12);

        StyleGrid(_gridProcesses);
        _gridProcesses.Columns.Add(new DataGridViewTextBoxColumn { Name = "Pid", HeaderText = "PID", DataPropertyName = "Pid", MinimumWidth = 70, FillWeight = 7 });
        _gridProcesses.Columns.Add(new DataGridViewTextBoxColumn { Name = "ParentPid", HeaderText = "Parent PID", DataPropertyName = "ParentPid", MinimumWidth = 80, FillWeight = 8 });
        _gridProcesses.Columns.Add(new DataGridViewTextBoxColumn { Name = "Name", HeaderText = "Name", DataPropertyName = "Name", MinimumWidth = 140, FillWeight = 16 });
        _gridProcesses.Columns.Add(new DataGridViewTextBoxColumn { Name = "Path", HeaderText = "Path", DataPropertyName = "Path", MinimumWidth = 200, FillWeight = 40 });
        _gridProcesses.Columns.Add(new DataGridViewTextBoxColumn { Name = "Flag", HeaderText = "Flag", DataPropertyName = "Flag", MinimumWidth = 150, FillWeight = 20 });
        _gridProcesses.Columns.Add(new DataGridViewTextBoxColumn { Name = "StartTime", HeaderText = "Start Time", DataPropertyName = "StartTime", MinimumWidth = 130, FillWeight = 9 });
        _gridProcesses.CellFormatting += (s, e) =>
        {
            if (_gridProcesses.Columns[e.ColumnIndex].Name != "Flag" || e.Value == null || string.IsNullOrEmpty(e.Value.ToString())) return;
            e.CellStyle!.ForeColor = Color.DarkOrange;
            e.CellStyle.Font = new Font(_gridProcesses.Font, FontStyle.Bold);
        };

        var actions = ActionRow();
        var btnSuspend = ActionButton("Suspend Selected");
        btnSuspend.Click += async (s, e) => await SuspendSelectedAsync();
        var btnResume = ActionButton("Resume Selected");
        btnResume.Click += async (s, e) => await ResumeSelectedAsync();
        var btnTerminate = ActionButton("Terminate Selected", Color.Firebrick);
        btnTerminate.Click += async (s, e) => await TerminateSelectedAsync();
        actions.Controls.Add(btnSuspend);
        actions.Controls.Add(btnResume);
        actions.Controls.Add(btnTerminate);

        _panelProcesses.Controls.Add(_gridProcesses);
        _panelProcesses.Controls.Add(actions);
        _panelProcesses.Controls.Add(SectionTitle("Processes"));
    }

    private static void BuildNetworkColumns(DataGridView g)
    {
        g.Columns.Add(new DataGridViewTextBoxColumn { Name = "Protocol", HeaderText = "Proto", DataPropertyName = "Protocol", MinimumWidth = 60, FillWeight = 8 });
        g.Columns.Add(new DataGridViewTextBoxColumn { Name = "LocalAddress", HeaderText = "Local Address", DataPropertyName = "LocalAddress", MinimumWidth = 120, FillWeight = 22 });
        g.Columns.Add(new DataGridViewTextBoxColumn { Name = "LocalPort", HeaderText = "Local Port", DataPropertyName = "LocalPort", MinimumWidth = 80, FillWeight = 11 });
        g.Columns.Add(new DataGridViewTextBoxColumn { Name = "RemoteAddress", HeaderText = "Remote Address", DataPropertyName = "RemoteAddress", MinimumWidth = 120, FillWeight = 22 });
        g.Columns.Add(new DataGridViewTextBoxColumn { Name = "RemotePort", HeaderText = "Remote Port", DataPropertyName = "RemotePort", MinimumWidth = 80, FillWeight = 11 });
        g.Columns.Add(new DataGridViewTextBoxColumn { Name = "State", HeaderText = "State", DataPropertyName = "State", MinimumWidth = 90, FillWeight = 15 });
    }

    private async System.Threading.Tasks.Task SuspendSelectedAsync()
    {
        if (_api == null) return;
        if (_gridProcesses.CurrentRow?.DataBoundItem is not ProcessInfo p) { MessageBox.Show("Select a process first."); return; }
        if (MessageBox.Show($"Suspend {p.Name} (PID {p.Pid})?", "Confirm", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
        try { await _api.SuspendProcessAsync(p.Pid); await RefreshAllAsync(); }
        catch (Exception ex) { MessageBox.Show("Failed: " + ex.Message); }
    }

    private async System.Threading.Tasks.Task ResumeSelectedAsync()
    {
        if (_api == null) return;
        if (_gridProcesses.CurrentRow?.DataBoundItem is not ProcessInfo p) { MessageBox.Show("Select a process first."); return; }
        try { await _api.ResumeProcessAsync(p.Pid); await RefreshAllAsync(); }
        catch (Exception ex) { MessageBox.Show("Failed: " + ex.Message); }
    }

    private async System.Threading.Tasks.Task TerminateSelectedAsync()
    {
        if (_api == null) return;
        if (_gridProcesses.CurrentRow?.DataBoundItem is not ProcessInfo p) { MessageBox.Show("Select a process first."); return; }
        if (MessageBox.Show($"Terminate {p.Name} (PID {p.Pid})? This cannot be undone.", "Confirm", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        try { await _api.TerminateProcessAsync(p.Pid); await RefreshAllAsync(); }
        catch (Exception ex) { MessageBox.Show("Failed: " + ex.Message); }
    }

    private void BuildBlockedPanel()
    {
        _panelBlocked.Dock = DockStyle.Fill;
        _panelBlocked.Visible = false;
        _panelBlocked.Padding = new Padding(12);

        StyleGrid(_gridBlocked);
        _gridBlocked.Columns.Add(new DataGridViewTextBoxColumn { Name = "Ip", HeaderText = "IP Address", DataPropertyName = "Ip", MinimumWidth = 120, FillWeight = 18 });
        _gridBlocked.Columns.Add(new DataGridViewTextBoxColumn { Name = "BlockedAt", HeaderText = "Blocked At", DataPropertyName = "BlockedAt", MinimumWidth = 130, FillWeight = 20 });
        _gridBlocked.Columns.Add(new DataGridViewTextBoxColumn { Name = "ExpiresAt", HeaderText = "Expires At", DataPropertyName = "ExpiresAt", MinimumWidth = 130, FillWeight = 20 });
        _gridBlocked.Columns.Add(new DataGridViewTextBoxColumn { Name = "BlockedBy", HeaderText = "Blocked By", DataPropertyName = "BlockedBy", MinimumWidth = 100, FillWeight = 14 });
        _gridBlocked.Columns.Add(new DataGridViewTextBoxColumn { Name = "Reason", HeaderText = "Reason", DataPropertyName = "Reason", MinimumWidth = 160, FillWeight = 28 });

        var row = ActionRow();
        var txtIp = new TextBox { Width = 150, Margin = new Padding(0, 4, 8, 0) };
        txtIp.PlaceholderText = "IP address";
        var btnBlock = ActionButton("Block IP");
        btnBlock.Click += async (s, e) =>
        {
            if (_api == null || string.IsNullOrWhiteSpace(txtIp.Text)) return;
            try { await _api.BlockIpAsync(txtIp.Text.Trim(), "manual (console)", null); await RefreshAllAsync(); }
            catch (Exception ex) { MessageBox.Show("Failed: " + ex.Message); }
        };
        var btnUnblock = ActionButton("Unblock Selected");
        btnUnblock.Click += async (s, e) =>
        {
            if (_api == null) return;
            if (_gridBlocked.CurrentRow?.DataBoundItem is not BlockedIp b) { MessageBox.Show("Select a row first."); return; }
            try { await _api.UnblockIpAsync(b.Ip); await RefreshAllAsync(); }
            catch (Exception ex) { MessageBox.Show("Failed: " + ex.Message); }
        };
        row.Controls.Add(txtIp);
        row.Controls.Add(btnBlock);
        row.Controls.Add(btnUnblock);

        _panelBlocked.Controls.Add(_gridBlocked);
        _panelBlocked.Controls.Add(row);
        _panelBlocked.Controls.Add(SectionTitle("Blocked IPs"));
    }

    private void BuildAllowlistPanel()
    {
        _panelAllowlist.Dock = DockStyle.Fill;
        _panelAllowlist.Visible = false;
        _panelAllowlist.Padding = new Padding(12);

        _allowlistList.Dock = DockStyle.Fill;
        _allowlistList.Font = new Font("Consolas", 10F);

        var info = new Label
        {
            Text = "IPs here are exempt from brute-force / port-scan auto-blocking (e.g. your backup server, VPN gateway, or known scanners).",
            Dock = DockStyle.Top,
            Height = 34,
            ForeColor = Color.DimGray
        };

        var row = ActionRow();
        var btnAdd = ActionButton("Add Entry");
        btnAdd.Click += async (s, e) =>
        {
            if (_api == null) return;
            var entry = TextInputDialog.Prompt("Add Allowlist Entry", "IP address:");
            if (string.IsNullOrWhiteSpace(entry)) return;
            try { await _api.ChangeAllowlistAsync(entry.Trim(), false); await RefreshAllAsync(); }
            catch (Exception ex) { MessageBox.Show("Failed: " + ex.Message); }
        };
        var btnRemove = ActionButton("Remove Selected");
        btnRemove.Click += async (s, e) =>
        {
            if (_api == null || _allowlistList.SelectedItem == null) { MessageBox.Show("Select an entry first."); return; }
            try { await _api.ChangeAllowlistAsync(_allowlistList.SelectedItem.ToString()!, true); await RefreshAllAsync(); }
            catch (Exception ex) { MessageBox.Show("Failed: " + ex.Message); }
        };
        row.Controls.Add(btnAdd);
        row.Controls.Add(btnRemove);

        _panelAllowlist.Controls.Add(_allowlistList);
        _panelAllowlist.Controls.Add(row);
        _panelAllowlist.Controls.Add(info);
        _panelAllowlist.Controls.Add(SectionTitle("Allowlist"));
    }

    private void BuildAuditPanel()
    {
        _panelAudit.Dock = DockStyle.Fill;
        _panelAudit.Visible = false;
        _panelAudit.Padding = new Padding(12);

        StyleGrid(_gridAudit);
        _gridAudit.Columns.Add(new DataGridViewTextBoxColumn { Name = "Timestamp", HeaderText = "Timestamp", DataPropertyName = "Timestamp", MinimumWidth = 130, FillWeight = 18 });
        _gridAudit.Columns.Add(new DataGridViewTextBoxColumn { Name = "Actor", HeaderText = "Actor", DataPropertyName = "Actor", MinimumWidth = 90, FillWeight = 12 });
        _gridAudit.Columns.Add(new DataGridViewTextBoxColumn { Name = "Action", HeaderText = "Action", DataPropertyName = "Action", MinimumWidth = 130, FillWeight = 20 });
        _gridAudit.Columns.Add(new DataGridViewTextBoxColumn { Name = "Target", HeaderText = "Target", DataPropertyName = "Target", MinimumWidth = 130, FillWeight = 24 });
        _gridAudit.Columns.Add(new DataGridViewTextBoxColumn { Name = "Result", HeaderText = "Result", DataPropertyName = "Result", MinimumWidth = 90, FillWeight = 14 });
        _gridAudit.Columns.Add(new DataGridViewTextBoxColumn { Name = "Hash", HeaderText = "Hash", DataPropertyName = "Hash", MinimumWidth = 90, FillWeight = 12 });

        var row = ActionRow();
        var btnVerify = ActionButton("Verify Chain Integrity");
        btnVerify.Click += async (s, e) =>
        {
            if (_api == null) return;
            try
            {
                var v = await _api.VerifyAuditAsync();
                MessageBox.Show(v.Valid
                    ? $"Audit log intact - {v.Entries} entries verified, no tampering detected."
                    : $"WARNING: audit chain broken - {v.Message}", "Audit Verification",
                    MessageBoxButtons.OK, v.Valid ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
            }
            catch (Exception ex) { MessageBox.Show("Failed: " + ex.Message); }
        };
        row.Controls.Add(btnVerify);

        _panelAudit.Controls.Add(_gridAudit);
        _panelAudit.Controls.Add(row);
        _panelAudit.Controls.Add(SectionTitle("Audit Log"));
    }

    private void BuildUsersPanel()
    {
        _panelUsers.Dock = DockStyle.Fill;
        _panelUsers.Visible = false;
        _panelUsers.Padding = new Padding(12);

        StyleGrid(_gridUsers);
        _gridUsers.Columns.Add(new DataGridViewTextBoxColumn { Name = "Name", HeaderText = "Name", DataPropertyName = "Name", MinimumWidth = 120, FillWeight = 30 });
        _gridUsers.Columns.Add(new DataGridViewTextBoxColumn { Name = "Role", HeaderText = "Role", DataPropertyName = "Role", MinimumWidth = 110, FillWeight = 30 });
        _gridUsers.Columns.Add(new DataGridViewTextBoxColumn { Name = "Created", HeaderText = "Created", DataPropertyName = "Created", MinimumWidth = 140, FillWeight = 40 });

        var row = ActionRow();
        var btnCreate = ActionButton("Create User");
        btnCreate.Click += async (s, e) =>
        {
            if (_api == null) return;
            var result = CreateUserDialog.Prompt();
            if (result == null) return;
            try
            {
                var created = await _api.CreateUserAsync(result.Value.name, result.Value.role);
                KeyDisplayDialog.Show($"User '{created.Name}' created", created.ApiKey);
                await RefreshAllAsync();
            }
            catch (Exception ex) { MessageBox.Show("Failed: " + ex.Message); }
        };
        var btnRevoke = ActionButton("Revoke Selected", Color.Firebrick);
        btnRevoke.Click += async (s, e) =>
        {
            if (_api == null) return;
            if (_gridUsers.CurrentRow?.DataBoundItem is not UserInfo u) { MessageBox.Show("Select a user first."); return; }
            if (MessageBox.Show($"Revoke user '{u.Name}'? Their API key stops working immediately.", "Confirm", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            try { await _api.RevokeUserAsync(u.Name); await RefreshAllAsync(); }
            catch (Exception ex) { MessageBox.Show("Failed: " + ex.Message); }
        };
        row.Controls.Add(btnCreate);
        row.Controls.Add(btnRevoke);

        var info = new Label { Text = "Owner-only. Roles: Viewer (read-only), Analyst (+ incident triage), Administrator (+ response actions), Owner (+ user/policy management).", Dock = DockStyle.Top, Height = 34, ForeColor = Color.DimGray };

        _panelUsers.Controls.Add(_gridUsers);
        _panelUsers.Controls.Add(row);
        _panelUsers.Controls.Add(info);
        _panelUsers.Controls.Add(SectionTitle("Users"));
    }

    private void BuildTestingPanel()
    {
        _panelTesting.Dock = DockStyle.Fill;
        _panelTesting.Visible = false;
        _panelTesting.Padding = new Padding(12);

        var info = new Label
        {
            Text = "Fires synthetic events through the agent's real detection engine - no real attack needed.\nRequires \"testMode\": { \"enabled\": true } in the agent's appsettings.json.",
            Dock = DockStyle.Top,
            Height = 48,
            ForeColor = Color.DimGray
        };

        var row = ActionRow();
        var lblIp = new Label { Text = "Fake source IP:", AutoSize = true, Margin = new Padding(0, 8, 8, 0) };
        _txtSimIp.Width = 160;
        _txtSimIp.Margin = new Padding(0, 4, 12, 0);

        var btnBrute = ActionButton("Simulate Brute Force");
        btnBrute.Click += async (s, e) => await SimulateAsync("bruteforce");
        var btnScan = ActionButton("Simulate Port Scan");
        btnScan.Click += async (s, e) => await SimulateAsync("portscan");

        row.Controls.Add(lblIp);
        row.Controls.Add(_txtSimIp);
        row.Controls.Add(btnBrute);
        row.Controls.Add(btnScan);

        _panelTesting.Controls.Add(row);
        _panelTesting.Controls.Add(info);
        _panelTesting.Controls.Add(SectionTitle("Local Testing"));
    }

    private async System.Threading.Tasks.Task SimulateAsync(string kind)
    {
        if (_api == null) return;
        var ip = string.IsNullOrWhiteSpace(_txtSimIp.Text) ? "203.0.113.55" : _txtSimIp.Text.Trim();
        try
        {
            await _api.SimulateAsync(kind, ip);
            MessageBox.Show($"Simulated {kind} from {ip}. Check the Incidents tab.");
            await RefreshAllAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show("Failed: " + ex.Message);
        }
    }

    private async System.Threading.Tasks.Task ConnectAsync()
    {
        var host = _txtHost.Text.Trim();
        var key = _txtApiKey.Text.Trim();
        if (string.IsNullOrEmpty(host) || string.IsNullOrEmpty(key))
        {
            SetDisconnected("Enter Agent URL and API Key.");
            return;
        }

        _btnConnect.Enabled = false;
        try
        {
            var (health, fingerprint, error) = await SentinelApi.ProbeAsync(host);
            if (error != null)
            {
                SetDisconnected("Agent not reachable: " + error);
                return;
            }

            if (fingerprint != null)
            {
                var pinned = Pins.Get(host);
                if (pinned == null)
                {
                    var proceed = MessageBox.Show(
                        $"First connection to this agent.\n\nTLS certificate fingerprint:\n{PinStore.Format(fingerprint)}\n\nTrust this certificate?",
                        "Verify Certificate", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                    if (proceed != DialogResult.Yes) { SetDisconnected("Certificate not trusted."); return; }
                    Pins.Set(host, fingerprint);
                }
                else if (!string.Equals(pinned, fingerprint, StringComparison.OrdinalIgnoreCase))
                {
                    var proceed = MessageBox.Show(
                        $"WARNING: the certificate for this agent has changed since last time.\n\nPreviously: {PinStore.Format(pinned)}\nNow: {PinStore.Format(fingerprint)}\n\nThis can mean the agent was reinstalled, or that the connection is being intercepted. Trust the new certificate anyway?",
                        "Certificate Changed", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                    if (proceed != DialogResult.Yes) { SetDisconnected("Certificate mismatch - connection rejected."); return; }
                    Pins.Set(host, fingerprint);
                }
            }

            _api?.Dispose();
            _api = new SentinelApi(host, key, Pins.Get(host));

            var status = await _api.GetStatusAsync();
            _connected = true;
            _role = status.Role;
            _statusDot.ForeColor = Color.LimeGreen;
            _statusText.Text = $"Connected: {status.HostName} ({status.Platform}) - role {status.Role} - {status.ActiveIncidents} open incidents";
            TabLabel = status.HostName;
            _navUsers.Visible = _role == "Owner";
            _chkMaintenance.Checked = status.MaintenanceMode;
            ConnectionInfoChanged?.Invoke();
            await RefreshAllAsync();
        }
        catch (ApiException ex)
        {
            SetDisconnected($"Connect failed ({ex.StatusCode}): {ex.Message}");
        }
        catch (Exception ex)
        {
            SetDisconnected("Connect failed: " + ex.Message);
        }
        finally
        {
            _btnConnect.Enabled = true;
        }
    }

    private void SetDisconnected(string message)
    {
        _connected = false;
        _statusDot.ForeColor = Color.Firebrick;
        _statusText.Text = message;
        if (string.IsNullOrEmpty(TabLabel) || TabLabel == "New Device")
            TabLabel = "New Device";
        else if (!TabLabel.EndsWith(" (offline)"))
            TabLabel += " (offline)";
        ConnectionInfoChanged?.Invoke();
    }

    private async System.Threading.Tasks.Task RefreshAllAsync()
    {
        if (_api == null || !_connected) return;
        try
        {
            var incidents = await _api.GetIncidentsAsync();
            _gridIncidents.DataSource = incidents;
            ResetGridView(_gridIncidents);

            var processes = await _api.GetProcessesAsync();
            _gridProcesses.DataSource = processes.OrderBy(p => p.Name).ToList();
            ResetGridView(_gridProcesses);

            var network = await _api.GetNetworkAsync();
            _gridNetwork.DataSource = network;
            ResetGridView(_gridNetwork);

            var blocked = await _api.GetBlockedIpsAsync();
            _gridBlocked.DataSource = blocked;
            ResetGridView(_gridBlocked);

            var allow = await _api.GetAllowlistAsync();
            _allowlistList.Items.Clear();
            foreach (var entry in allow.Entries) _allowlistList.Items.Add(entry);

            var audit = await _api.GetAuditAsync();
            _gridAudit.DataSource = audit;
            ResetGridView(_gridAudit);

            if (_role == "Owner")
            {
                var users = await _api.GetUsersAsync();
                _gridUsers.DataSource = users;
                ResetGridView(_gridUsers);
            }

            var status = await _api.GetStatusAsync();
            _statusDot.ForeColor = Color.LimeGreen;
            _statusText.Text = $"Connected: {status.HostName} ({status.Platform}) - role {status.Role} - {status.ActiveIncidents} open incidents";
            TabLabel = status.HostName;
            ConnectionInfoChanged?.Invoke();

            _lblDashHost.Text = $"{status.HostName}   -   {status.Platform}, v{status.Version}";
            _lblDashRole.Text = $"Connected as {status.User} ({status.Role})   -   uptime {TimeSpan.FromSeconds(status.UptimeSeconds)}";
            _lblDashCounts.Text = $"Open incidents: {status.ActiveIncidents}      Blocked IPs: {status.BlockedIps}";
            _lblDashFlags.Text = $"Maintenance mode: {(status.MaintenanceMode ? "ON" : "off")}    Dry run: {(status.DryRun ? "ON" : "off")}    Test mode: {(status.TestMode ? "ON" : "off")}";
        }
        catch (Exception ex)
        {
            SetDisconnected("Refresh failed: " + ex.Message);
        }
    }
}
