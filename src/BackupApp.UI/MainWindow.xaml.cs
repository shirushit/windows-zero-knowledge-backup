using System.Drawing;
using System.Windows;
using System.Windows.Forms;
using ListViewItem = System.Windows.Controls.ListViewItem;
using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using ToolTipIcon = System.Windows.Forms.ToolTipIcon;
using BackupApp.UI.ViewModels;

namespace BackupApp.UI;

public partial class MainWindow : Window, IDisposable
{
    private NotifyIcon? _notifyIcon;
    private bool _isRealExit;

    public MainViewModel ViewModel { get; }

    public MainWindow()
    {
        App.Log("MainWindow constructor start");
        try
        {
            InitializeComponent();
            App.Log("MainWindow InitializeComponent done");
            ViewModel = new MainViewModel();
            App.Log("MainWindow new MainViewModel done");
            DataContext = ViewModel;

            Loaded += async (_, _) =>
            {
                App.Log("MainWindow Loaded start");
                InitializeTrayIcon();
                await ViewModel.InitializeAsync().ConfigureAwait(true);
                App.Log("MainWindow ViewModel.InitializeAsync done");
            };

            StateChanged += OnWindowStateChanged;
            Closing += OnWindowClosing;
            Closed += (_, _) => App.Log("MainWindow Closed event");
            App.Log("MainWindow constructor complete");
        }
        catch (Exception ex)
        {
            App.Log("MainWindow constructor EXCEPTION: " + ex);
            throw;
        }
    }

    private void InitializeTrayIcon()
    {
        var contextMenu = new ContextMenuStrip();

        var openItem = new ToolStripMenuItem("פתח ממשק");
        openItem.Font = new Font(openItem.Font, System.Drawing.FontStyle.Bold);
        openItem.Click += (_, _) => RestoreWindowFromTray();

        var backupItem = new ToolStripMenuItem("הפעל גיבוי מיידי");
        backupItem.Click += async (_, _) =>
        {
            if (ViewModel.TriggerBackupCommand.CanExecute(null))
            {
                if (ViewModel.TriggerBackupCommand is AsyncRelayCommand asyncCmd)
                {
                    await asyncCmd.ExecuteAsync(null).ConfigureAwait(false);
                }
                else
                {
                    ViewModel.TriggerBackupCommand.Execute(null);
                }
            }
        };

        var separator = new ToolStripSeparator();

        var exitItem = new ToolStripMenuItem("יציאה");
        exitItem.Click += (_, _) =>
        {
            _isRealExit = true;
            _notifyIcon?.Dispose();
            _notifyIcon = null;
            Close();
            System.Windows.Application.Current.Shutdown();
        };

        contextMenu.Items.Add(openItem);
        contextMenu.Items.Add(backupItem);
        contextMenu.Items.Add(separator);
        contextMenu.Items.Add(exitItem);

        _notifyIcon = new NotifyIcon
        {
            Icon = SystemIcons.Shield,
            Text = "גיבוי מאובטח ואפס-ידע - פעיל ברקע",
            ContextMenuStrip = contextMenu,
            Visible = true
        };

        _notifyIcon.DoubleClick += (_, _) => RestoreWindowFromTray();
    }

    private void RestoreWindowFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void OnWindowStateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
        {
            Hide();
            _notifyIcon?.ShowBalloonTip(
                2000,
                "גיבוי מאובטח פעיל ברקע",
                "האפליקציה ממוזערת למגש המערכת וממשיכה להגן על הנתונים שלך.",
                ToolTipIcon.Info
            );
        }
    }

    private void OnWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!_isRealExit)
        {
            e.Cancel = true;
            Hide();
            _notifyIcon?.ShowBalloonTip(
                2000,
                "גיבוי מאובטח פעיל ברקע",
                "האפליקציה ממשיכה לפעול ברקע ליד השעון. לחץ פעמיים על האייקון לפתיחה מחדש.",
                ToolTipIcon.Info
            );
        }
        else
        {
            _notifyIcon?.Dispose();
            _notifyIcon = null;
            ViewModel.Dispose();
        }
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

    public void Dispose()
    {
        _notifyIcon?.Dispose();
        _notifyIcon = null;
        ViewModel.Dispose();
        GC.SuppressFinalize(this);
    }
}
