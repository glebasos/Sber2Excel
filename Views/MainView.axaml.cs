using Avalonia.Controls;
using Sber2Excel.ViewModels;

namespace Sber2Excel.Views;

public partial class MainView : UserControl
{
    public MainView()
    {
        InitializeComponent();
        DataContext = new MainWindowViewModel();

        AttachedToVisualTree += (_, _) =>
        {
            if (DataContext is MainWindowViewModel vm && TopLevel.GetTopLevel(this) is { } tl)
                vm.AttachTopLevel(tl);
        };
    }
}
