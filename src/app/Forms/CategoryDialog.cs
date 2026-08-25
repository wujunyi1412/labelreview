namespace LabelReviewer.Forms;

public sealed class CategoryDialog : Form
{
    private readonly ComboBox _reviewType = new();
    private readonly ComboBox _category = new();
    public string ReviewType => _reviewType.Text.Trim();
    public string Category => _category.Text.Trim();

    public CategoryDialog(IEnumerable<string> categories, string? preferredCategory,
        string? preferredReviewType)
    {
        Text = "选择标注标签";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(440, 240);
        MinimumSize = new Size(440, 240);
        Font = new Font("Microsoft YaHei UI", 9f);
        AutoScaleMode = AutoScaleMode.Dpi;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 6,
            Padding = new Padding(16)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var reviewTypeLabel = new Label
        {
            Text = "问题类型：",
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 6)
        };
        _reviewType.DropDownStyle = ComboBoxStyle.DropDownList;
        _reviewType.Dock = DockStyle.Top;
        _reviewType.Margin = new Padding(0, 0, 0, 14);
        _reviewType.Items.AddRange(["误检", "漏检"]);
        _reviewType.SelectedItem = preferredReviewType is "误检" or "漏检"
            ? preferredReviewType
            : "误检";

        var categoryLabel = new Label
        {
            Text = "类别（可直接输入新类别）：",
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 6)
        };
        _category.DropDownStyle = ComboBoxStyle.DropDown;
        _category.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
        _category.AutoCompleteSource = AutoCompleteSource.ListItems;
        _category.Dock = DockStyle.Top;
        _category.Margin = new Padding(0);
        _category.Items.AddRange(categories.Cast<object>().ToArray());
        _category.Text = preferredCategory ?? string.Empty;

        var ok = new Button { Text = "确定", DialogResult = DialogResult.OK, AutoSize = true };
        var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, AutoSize = true };
        ok.Click += (_, e) =>
        {
            if (Category.Length == 0)
            {
                MessageBox.Show(this, "请输入或选择一个类别。", "类别不能为空",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                DialogResult = DialogResult.None;
            }
        };

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Margin = new Padding(0, 12, 0, 0)
        };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);

        layout.Controls.Add(reviewTypeLabel, 0, 0);
        layout.Controls.Add(_reviewType, 0, 1);
        layout.Controls.Add(categoryLabel, 0, 2);
        layout.Controls.Add(_category, 0, 3);
        layout.Controls.Add(buttons, 0, 5);
        Controls.Add(layout);
        AcceptButton = ok;
        CancelButton = cancel;
        Shown += (_, _) => { _category.Focus(); _category.SelectAll(); };
    }
}
