using Microsoft.Win32;

namespace BackupApp.UI.Services;

public sealed class WindowsFolderPickerService : IFolderPickerService
{
    public string? PickFolder(string? initialDirectory = null)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "בחר תיקייה לגיבוי",
            Multiselect = false
        };

        if (!string.IsNullOrWhiteSpace(initialDirectory) && System.IO.Directory.Exists(initialDirectory))
        {
            dialog.InitialDirectory = initialDirectory;
        }

        var result = dialog.ShowDialog();
        return result == true ? dialog.FolderName : null;
    }
}
