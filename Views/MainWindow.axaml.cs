using Avalonia.Controls;
using Sber2Excel.ViewModels;

namespace Sber2Excel.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainWindowViewModel(this);
    }
}
