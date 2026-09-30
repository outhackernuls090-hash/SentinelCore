using System;
using System.Drawing;
using System.Windows.Forms;

namespace SentinelConsole;

public static class TextInputDialog
{
    public static string? Prompt(string title, string label, string defaultValue = "")
    {
        using var form = new Form
        {
            Text = title,
            Width = 420,
            Height = 160,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false,
            MaximizeBox = false
        };

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(12) };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var lbl = new Label { Text = label, AutoSize = true, Margin = new Padding(0, 0, 0, 6) };
        var txt = new TextBox { Text = defaultValue, Dock = DockStyle.Top, Margin = new Padding(0, 0, 0, 12) };

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, AutoSize = true };
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Width = 80 };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Width = 80, Margin = new Padding(0, 0, 8, 0) };
        buttons.Controls.Add(ok);
        buttons.Controls.Add(cancel);

        layout.Controls.Add(lbl, 0, 0);
        layout.Controls.Add(txt, 0, 1);
        layout.Controls.Add(buttons, 0, 2);

        form.Controls.Add(layout);
        form.AcceptButton = ok;
        form.CancelButton = cancel;

        return form.ShowDialog() == DialogResult.OK ? txt.Text.Trim() : null;
    }
}

public static class CreateUserDialog
{
    public static (string name, string role)? Prompt()
    {
        using var form = new Form
        {
            Text = "Create User",
            Width = 380,
            Height = 200,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false,
            MaximizeBox = false
        };

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3, Padding = new Padding(12) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var name = new TextBox { Dock = DockStyle.Fill, Margin = new Padding(4) };
        var role = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(4) };
        role.Items.AddRange(new object[] { "Viewer", "Analyst", "Administrator", "Owner" });
        role.SelectedIndex = 0;

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, AutoSize = true };
        var ok = new Button { Text = "Create", DialogResult = DialogResult.OK, Width = 90 };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Width = 90, Margin = new Padding(0, 0, 8, 0) };
        buttons.Controls.Add(ok);
        buttons.Controls.Add(cancel);

        layout.Controls.Add(new Label { Text = "Name:", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(4, 10, 4, 4) }, 0, 0);
        layout.Controls.Add(name, 1, 0);
        layout.Controls.Add(new Label { Text = "Role:", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(4, 10, 4, 4) }, 0, 1);
        layout.Controls.Add(role, 1, 1);
        layout.Controls.Add(buttons, 1, 2);

        form.Controls.Add(layout);
        form.AcceptButton = ok;
        form.CancelButton = cancel;

        if (form.ShowDialog() != DialogResult.OK) return null;
        if (string.IsNullOrWhiteSpace(name.Text)) return null;
        return (name.Text.Trim(), role.SelectedItem?.ToString() ?? "Viewer");
    }
}

public static class KeyDisplayDialog
{
    public static void Show(string title, string apiKey)
    {
        using var form = new Form
        {
            Text = title,
            Width = 520,
            Height = 200,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false,
            MaximizeBox = false
        };

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(12) };
        var warn = new Label { Text = "Save this API key now - it will not be shown again.", AutoSize = true, ForeColor = Color.Firebrick, Margin = new Padding(0, 0, 0, 8) };
        var box = new TextBox { Text = apiKey, ReadOnly = true, Dock = DockStyle.Top, Font = new Font("Consolas", 10F) };
        var copy = new Button { Text = "Copy to clipboard", AutoSize = true, Margin = new Padding(0, 10, 0, 0) };
        copy.Click += (s, e) => { try { Clipboard.SetText(apiKey); } catch { } };
        var close = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, AutoSize = true };
        var ok = new Button { Text = "Close", DialogResult = DialogResult.OK, Width = 90 };
        close.Controls.Add(ok);

        layout.Controls.Add(warn, 0, 0);
        layout.Controls.Add(box, 0, 1);
        layout.Controls.Add(copy, 0, 2);
        layout.Controls.Add(close, 0, 3);

        form.Controls.Add(layout);
        form.AcceptButton = ok;
        form.ShowDialog();
    }
}
