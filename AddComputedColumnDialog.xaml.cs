using System.Windows;
using FlightAnalyzer.Services;

namespace FlightAnalyzer;

public partial class AddComputedColumnDialog : Window
{
    private readonly HashSet<string> _existingColumns;

    public string NewColumnName { get; private set; } = string.Empty;
    public string Formula { get; private set; } = string.Empty;

    public AddComputedColumnDialog(IEnumerable<string> existingColumns)
    {
        InitializeComponent();
        _existingColumns = new HashSet<string>(existingColumns, StringComparer.OrdinalIgnoreCase);
    }

    private void ColumnNameBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        ValidateInputs();
    }

    private void FormulaBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        ValidateInputs();
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
        NewColumnName = ColumnNameBox.Text.Trim();
        Formula = FormulaBox.Text.Trim();
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
