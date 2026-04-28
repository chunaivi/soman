using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SoMan.ViewModels;

namespace SoMan.Views;

public partial class TemplateEditorView : UserControl
{
    public TemplateEditorView()
    {
        InitializeComponent();
    }

    private void Backdrop_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is TemplateEditorViewModel vm)
        {
            vm.IsDialogOpen = false;
        }
    }

    private void SaveTemplate_Click(object sender, RoutedEventArgs e)
    {
        System.Diagnostics.Debug.WriteLine("[CLICK] Save button CLICKED!");
        System.Diagnostics.Debug.WriteLine($"[CLICK] DataContext type: {DataContext?.GetType().Name}");
        
        if (DataContext is TemplateEditorViewModel vm)
        {
            System.Diagnostics.Debug.WriteLine($"[CLICK] VM found. FormTemplateName='{vm.FormTemplateName}'");
            System.Diagnostics.Debug.WriteLine($"[CLICK] SaveTemplateCommand CanExecute={vm.SaveTemplateCommand.CanExecute(null)}");
            
            // Force execute as fallback
            if (vm.SaveTemplateCommand.CanExecute(null))
            {
                System.Diagnostics.Debug.WriteLine("[CLICK] Executing command manually...");
                vm.SaveTemplateCommand.Execute(null);
            }
        }
        else
        {
            System.Diagnostics.Debug.WriteLine($"[CLICK] DataContext is NOT TemplateEditorViewModel: {DataContext}");
            MessageBox.Show("DataContext is not TemplateEditorViewModel!", "Debug");
        }
    }
}
