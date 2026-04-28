using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SoMan.ViewModels;

namespace SoMan;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void NavItem_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBoxItem item && item.Tag is string page && DataContext is MainViewModel vm)
        {
            vm.NavigateCommand.Execute(page);
        }
    }
}