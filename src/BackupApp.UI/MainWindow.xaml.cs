using System.Windows;
using BackupApp.UI.ViewModels;

namespace BackupApp.UI;

public partial class MainWindow : Window
{
    public MainViewModel ViewModel { get; }

    public MainWindow()
    {
        InitializeComponent();
        ViewModel = new MainViewModel();
        DataContext = ViewModel;
        Loaded += async (_, _) => await ViewModel.InitializeAsync().ConfigureAwait(true);
    }
}
