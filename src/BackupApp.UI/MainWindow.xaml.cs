using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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

    private void OnListViewItemMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListViewItem item && item.DataContext is FileItemViewModel fileItem)
        {
            if (ViewModel.OpenSelectedFileCommand.CanExecute(fileItem))
            {
                ViewModel.OpenSelectedFileCommand.Execute(fileItem);
            }
        }
    }
}
