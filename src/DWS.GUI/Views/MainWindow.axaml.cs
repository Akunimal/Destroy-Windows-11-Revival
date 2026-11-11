using Avalonia.Controls;
using Avalonia.Interactivity;
using DWS.GUI.ViewModels;

namespace DWS.GUI.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        Program.Log("MainWindow constructor started.");
        InitializeComponent();
        Program.Log("MainWindow constructor completed.");
    }

    private void OnRevertCategoryClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Content: string category } && DataContext is MainViewModel vm)
        {
            vm.RevertByCategoryCommand.Execute(category);
        }
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        Program.Log($"MainWindow.OnDataContextChanged: DataContext={DataContext?.GetType().FullName ?? "NULL"}");
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        Program.Log($"MainWindow.OnOpened: DataContext={DataContext?.GetType().FullName ?? "NULL"}");
        if (DataContext is MainViewModel vm)
        {
            Program.Log($"OnOpened: VM AvailableRules={vm.AvailableRules.Count}, FilteredRules={vm.FilteredRules.Count}, StatusText='{vm.StatusText}', Categories={vm.Categories.Count}");
        }

        var lb = this.FindControl<ListBox>("RulesListBox");
        if (lb != null)
        {
            Program.Log($"RulesListBox found! ItemCount={lb.ItemCount}, Bounds={lb.Bounds}, IsVisible={lb.IsVisible}, ItemsSourceType={lb.ItemsSource?.GetType().FullName ?? "NULL"}");
            if (lb.ItemsSource is System.Collections.IEnumerable enumerable)
            {
                int count = 0;
                foreach (var item in enumerable) { count++; }
                Program.Log($"RulesListBox ItemsSource enumerated count: {count}");
            }
        }
        else
        {
            Program.Log("RulesListBox NOT found via FindControl!");
        }
    }
}
