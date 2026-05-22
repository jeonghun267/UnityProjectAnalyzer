using System.Windows;
using System.Windows.Controls;
using UnityProjectAnalyzer.ViewModels;

namespace UnityProjectAnalyzer.Views.Pages;

public partial class SettingsPage : UserControl
{
    public SettingsPage()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            if (DataContext is MainViewModel vm && !string.IsNullOrEmpty(vm.ApiKey))
                ApiKeyBox.Password = vm.ApiKey;
        };
    }

    private void ApiKeyBox_Save(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            vm.ApiKey = ApiKeyBox.Password;
            vm.SaveApiKeyCommand.Execute(null);
        }
    }
}
