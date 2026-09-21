using System;
using System.Diagnostics;
using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Unlimotion.Services;
using L10n = Unlimotion.ViewModel.Localization.Localization;

namespace Unlimotion.Views;

public partial class SettingsRecoveryView : UserControl
{
    private string? _configPath;
    internal Action<string> OpenFolder { get; set; } = folder =>
        Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });

    public SettingsRecoveryView()
    {
        InitializeComponent();
        TitleText.Text = L10n.Get("SettingsRecoveryTitle");
        OpenFolderButton.Content = L10n.Get("SettingsRecoveryOpenFolder");
        CloseButton.Content = L10n.Get("SettingsRecoveryClose");
    }

    internal SettingsRecoveryView(SettingsFileRecoveryResult result) : this()
    {
        _configPath = result.ConfigPath;
        PathText.Text = result.ConfigPath;
        MessageText.Text = L10n.Get(result.Error switch
        {
            SettingsRecoveryError.InvalidSettings => "SettingsRecoveryInvalid",
            SettingsRecoveryError.AccessDenied => "SettingsRecoveryAccessDenied",
            _ => "SettingsRecoveryIoFailure"
        });
    }

    private void OpenFolderClicked(object? sender, RoutedEventArgs e)
    {
        try
        {
            var folder = Path.GetDirectoryName(_configPath);
            if (string.IsNullOrWhiteSpace(folder))
            {
                throw new IOException("The configuration directory is unavailable.");
            }

            OpenFolder(folder);
            FolderErrorText.IsVisible = false;
        }
        catch (Exception)
        {
            // Do not surface raw exceptions: they can contain settings values or shell arguments.
            FolderErrorText.Text = L10n.Get("SettingsRecoveryOpenFolderFailed");
            FolderErrorText.IsVisible = true;
        }
    }

    private void CloseClicked(object? sender, RoutedEventArgs e) => (TopLevel.GetTopLevel(this) as Window)?.Close();
}
