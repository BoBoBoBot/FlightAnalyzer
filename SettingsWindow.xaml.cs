using System.Windows;
using System.Windows.Controls;
using FlightAnalyzer.ViewModels;

namespace FlightAnalyzer;

public partial class SettingsWindow : Window
{
    private SettingsWindowViewModel _vm;

    public SettingsWindow(MainViewModel mainVm, MainWindow mainWindow)
    {
        _vm = new SettingsWindowViewModel(mainVm, mainWindow, this);
        DataContext = _vm;
        InitializeComponent();

        Loaded += (_, _) =>
        {
            foreach (var child in ThemePanel.Children)
            {
                if (child is RadioButton rb &&
                    string.Equals(rb.Content?.ToString(), _vm.ThemeMode, StringComparison.OrdinalIgnoreCase))
                {
                    rb.IsChecked = true;
                    break;
                }
            }
        };
    }

    private void ThemeRadio_Checked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || _vm == null) return;
        if (sender is RadioButton rb)
            _vm.ThemeMode = rb.Content?.ToString() ?? "System";
    }
}
