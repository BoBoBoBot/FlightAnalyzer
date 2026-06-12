using System.Windows;
using System.Windows.Controls;
using FlightAnalyzer.Services;

namespace FlightAnalyzer;

public partial class AddComputedColumnDialog : Window
{
    private readonly HashSet<string> _existingColumns;
    private readonly List<string> _allColumnNames;

    public string NewColumnName { get; private set; } = string.Empty;
    public string Formula { get; private set; } = string.Empty;

    public AddComputedColumnDialog(
        IEnumerable<string> existingColumns,
        IEnumerable<string> allColumnNames,
        string? existingName = null,
        string? existingFormula = null)
    {
        InitializeComponent();
        _existingColumns = new HashSet<string>(existingColumns, StringComparer.OrdinalIgnoreCase);
        _allColumnNames = allColumnNames.ToList();

        // 填充下拉框，禁用自身引用避免循环
        ColumnPicker.ItemsSource = _allColumnNames;
        if (!string.IsNullOrEmpty(existingName))
        {
            ColumnPicker.SelectedItem = _allColumnNames.FirstOrDefault(
                n => string.Equals(n, existingName, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrEmpty(existingName))
        {
            ColumnNameBox.Text = existingName;
            _existingColumns.Remove(existingName);
        }
        if (!string.IsNullOrEmpty(existingFormula))
        {
            FormulaBox.Text = existingFormula;
        }
    }

    private void ColumnNameBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        ValidateInputs();
    }

    private void FormulaBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        ValidateInputs();
    }

    /// <summary>下拉框选择列名时，将 ${列名} 插入公式栏光标位置</summary>
    private void ColumnPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // 下拉选择即自动插入（更符合直觉）
        if (ColumnPicker.SelectedItem is string colName)
        {
            InsertColumnRef(colName);
            // 插入后取消选中，允许重复选同一项
            ColumnPicker.SelectedIndex = -1;
        }
    }

    /// <summary>点击"插入"按钮，将下拉框选中列插入公式栏</summary>
    private void InsertColumnName_Click(object sender, RoutedEventArgs e)
    {
        if (ColumnPicker.SelectedItem is string colName)
        {
            InsertColumnRef(colName);
        }
    }

    /// <summary>在公式栏光标处插入 ${列名}</summary>
    private void InsertColumnRef(string colName)
    {
        string refStr = $"${{{colName}}}";
        int caret = FormulaBox.CaretIndex;
        FormulaBox.Text = FormulaBox.Text.Insert(caret, refStr);
        FormulaBox.CaretIndex = caret + refStr.Length;
        FormulaBox.Focus();
    }

    private void ValidateInputs()
    {
        string colName = ColumnNameBox.Text?.Trim() ?? "";
        string formula = FormulaBox.Text?.Trim() ?? "";

        if (string.IsNullOrEmpty(colName))
        {
            ErrorText.Text = "列名不能为空";
            OkButton.IsEnabled = false;
            return;
        }

        if (_existingColumns.Contains(colName))
        {
            ErrorText.Text = $"列名 \"{colName}\" 已存在";
            OkButton.IsEnabled = false;
            return;
        }

        if (string.IsNullOrEmpty(formula))
        {
            ErrorText.Text = "公式不能为空";
            OkButton.IsEnabled = false;
            return;
        }

        // 验证公式语法
        var (isValid, error) = FormulaParser.ValidateFormula(formula, _existingColumns);
        if (!isValid)
        {
            ErrorText.Text = error;
            OkButton.IsEnabled = false;
            return;
        }

        ErrorText.Text = "";
        OkButton.IsEnabled = true;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ColumnNameBox.Text))
        {
            ErrorText.Text = "列名不能为空";
            return;
        }
        NewColumnName = ColumnNameBox.Text.Trim();
        Formula = FormulaBox.Text.Trim();
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
