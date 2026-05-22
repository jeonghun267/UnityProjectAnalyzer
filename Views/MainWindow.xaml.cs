using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using UnityProjectAnalyzer.ViewModels;

namespace UnityProjectAnalyzer.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Loaded += MainWindow_Loaded;
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            vm.ChatScrollRequested += (_, _) =>
            {
                Dispatcher.BeginInvoke(new System.Action(() => ChatScroll.ScrollToEnd()));
            };
        }
    }

    /// <summary>
    /// 채팅 입력 Enter 키 (Shift+Enter는 줄바꿈)
    /// </summary>
    private void ChatInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter
            && (Keyboard.Modifiers & ModifierKeys.Shift) != ModifierKeys.Shift
            && DataContext is MainViewModel vm)
        {
            e.Handled = true;
            vm.SendChatCommand.Execute(null);
        }
    }
}
