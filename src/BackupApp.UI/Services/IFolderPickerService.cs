namespace BackupApp.UI.Services;

public interface IFolderPickerService
{
    string? PickFolder(string? initialDirectory = null);
}
