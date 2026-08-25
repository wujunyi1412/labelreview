namespace LabelReviewer.Forms;

public sealed class CategoryInputDialog : Form
{
    private readonly TextBox _category = new();
    public string Category => _category.Text.Trim();

    public CategoryInputDialog()
    {
        Text = "添加类别";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(400, 155);
        MinimumSize = new Size(400, 155);
        Font = new Font("Microsoft YaHei UI", 9f);
        AutoScaleMode = AutoScaleMode.Dpi;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(16)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var label = new Label
        {
            Text = "新类别名称：",
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 6)
        };
        _category.Dock = DockStyle.Top;
        _category.Margin = new Padding(0);

        var ok = new Button { Text = "添加", DialogResult = DialogResult.OK, AutoSize = true };
        var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, AutoSize = true };
        ok.Click += (_, _) =>
        {
            if (Category.Length > 0) return;
            MessageBox.Show(this, "请输入类别名称。", "类别不能为空",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            DialogResult = DialogResult.None;
        };

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Margin = new Padding(0, 10, 0, 0)
        };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);

        layout.Controls.Add(label, 0, 0);
        layout.Controls.Add(_category, 0, 1);
        layout.Controls.Add(buttons, 0, 3);
        Controls.Add(layout);

        AcceptButton = ok;
        CancelButton = cancel;
        Shown += (_, _) => _category.Focus();
    }
}
