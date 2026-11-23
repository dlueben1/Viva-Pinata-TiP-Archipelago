using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace VivaAP.Launcher;

public partial class MainWindow : Window
{
    private static readonly string LauncherDir = AppContext.BaseDirectory;
    private static readonly string IniPath = Path.Combine(LauncherDir, "launcher.ini");

    private AptipPatch? _selectedPatch;

    public MainWindow()
    {
        InitializeComponent();

        // Restore the previously selected retip.exe, if it still exists.
        var savedGamePath = ReadIniSetting("GamePath");
        if (!string.IsNullOrWhiteSpace(savedGamePath) && File.Exists(savedGamePath))
        {
            GamePathTextBox.Text = savedGamePath;
        }

        UpdateStartButton();
    }

    #region File Selection & Validation

    private void BrowsePatch_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select Archipelago Patch",
            Filter = "TiP Archipelago Patch (*.aptip)|*.aptip",
            CheckFileExists = true,
        };

        if (dialog.ShowDialog() != true)
            return;

        _selectedPatch = null;
        PatchPathTextBox.Text = "";
        PatchDetailsText.Text = "No valid patch selected";

        try
        {
            string json = File.ReadAllText(dialog.FileName);

            var patch = JsonSerializer.Deserialize<AptipPatch>(
                json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
            );

            if (
                patch is null
                || patch.Version is null
                || string.IsNullOrWhiteSpace(patch.Seed)
                || string.IsNullOrWhiteSpace(patch.Slot)
            )
            {
                throw new InvalidDataException("The patch must contain version, seed, and slot.");
            }

            _selectedPatch = patch;
            PatchPathTextBox.Text = dialog.FileName;
            PatchDetailsText.Text = $"Seed: {patch.Seed} | Slot: {patch.Slot}";
        }
        catch (Exception ex)
            when (ex
                    is IOException
                        or UnauthorizedAccessException
                        or JsonException
                        or InvalidDataException
            )
        {
            MessageBox.Show(
                $"Unable to load patch:\n{ex.Message}",
                "Invalid Patch",
                MessageBoxButton.OK,
                MessageBoxImage.Warning
            );
        }

        UpdateStartButton();
    }

    private void BrowseGame_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select Game Location",
            Filter = "TiP ReTiP Executable (retip.exe)|retip.exe",
            CheckFileExists = true,
        };

        if (dialog.ShowDialog() != true)
            return;

        GamePathTextBox.Text = dialog.FileName;

        // Persist every valid selection so the launcher remembers it next time.
        WriteIniSetting("GamePath", dialog.FileName);

        UpdateStartButton();
    }

    #endregion

    private void Inputs_Changed(object sender, TextChangedEventArgs e)
    {
        UpdateStartButton();
    }

    private void UpdateStartButton()
    {
        // TextChanged may fire during InitializeComponent().
        if (StartButton is null || ServerTextBox is null || PortTextBox is null)
        {
            return;
        }

        bool validServer = IsValidServer(ServerTextBox.Text);

        bool validPort = int.TryParse(PortTextBox.Text, out int port) && port is >= 1 and <= 65535;

        bool validPatch = _selectedPatch is not null && File.Exists(PatchPathTextBox.Text);

        bool validGame = File.Exists(GamePathTextBox.Text);

        StartButton.IsEnabled = validServer && validPort && validPatch && validGame;
    }

    private static bool IsValidServer(string server)
    {
        server = server.Trim();

        if (string.IsNullOrWhiteSpace(server))
            return false;

        // Allow both hostnames and full URLs.
        string address = server.Contains("://") ? server : $"ws://{server}";

        return Uri.TryCreate(address, UriKind.Absolute, out var uri)
            && uri.Scheme is "ws" or "wss" or "http" or "https"
            && uri.HostNameType != UriHostNameType.Unknown;
    }

    private void StartButton_Click(object sender, RoutedEventArgs e)
    {
        AptipPatch patch = _selectedPatch!;

        // Per-session user-data root: <launcher dir>/sessions/<seed>_<slot>.
        string saveRoot = Path.Combine(
            LauncherDir,
            "sessions",
            $"{SanitizeFileName(patch.Seed!)}_{SanitizeFileName(patch.Slot!)}"
        );

        try
        {
            // The game must never launch before its data directory exists.
            Directory.CreateDirectory(saveRoot);

            string gameExe = GamePathTextBox.Text;
            string gameDirectory = Path.GetDirectoryName(gameExe) ?? LauncherDir;

            var startInfo = new ProcessStartInfo
            {
                FileName = gameExe,
                WorkingDirectory = gameDirectory,
                UseShellExecute = false,
            };

            startInfo.Environment["AP_LAUNCHED"] = "1";
            startInfo.Environment["REX_USER_DATA_ROOT"] = saveRoot;
            startInfo.Environment["AP_SERVER"] = ServerTextBox.Text.Trim();
            startInfo.Environment["AP_PASSWORD"] = PasswordInput.Password;
            startInfo.Environment["AP_PORT"] = PortTextBox.Text.Trim();
            startInfo.Environment["AP_SLOT"] = patch.Slot;
            startInfo.Environment["AP_SEED"] = patch.Seed;

            Process.Start(startInfo);
        }
        catch (Exception ex)
            when (ex
                    is IOException
                        or UnauthorizedAccessException
                        or System.ComponentModel.Win32Exception
            )
        {
            MessageBox.Show(
                $"Unable to launch the game:\n{ex.Message}",
                "Launch Failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );
            return;
        }

        // The launcher is no longer needed once the game is running.
        Application.Current.Shutdown();
    }

    private static string SanitizeFileName(string name)
    {
        foreach (char c in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(c, '_');
        }
        return name.Trim();
    }

    #region Local Settings

    private static string? ReadIniSetting(string key)
    {
        try
        {
            if (!File.Exists(IniPath))
                return null;

            foreach (string line in File.ReadAllLines(IniPath))
            {
                int eq = line.IndexOf('=');
                if (eq <= 0)
                    continue;

                string k = line[..eq].Trim();
                if (k.Equals(key, StringComparison.OrdinalIgnoreCase))
                    return line[(eq + 1)..].Trim();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Ignore unreadable config; fall back to defaults.
        }

        return null;
    }

    private static void WriteIniSetting(string key, string value)
    {
        try
        {
            var lines = new List<string>();
            if (File.Exists(IniPath))
            {
                lines.AddRange(
                    File.ReadAllLines(IniPath)
                        .Where(l =>
                            !l.Split('=', 2)[0]
                                .Trim()
                                .Equals(key, StringComparison.OrdinalIgnoreCase)
                        )
                );
            }

            lines.Add($"{key}={value}");
            File.WriteAllLines(IniPath, lines);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Non-fatal: the launcher still works, just won't remember the path.
        }
    }

    #endregion
}

// DTO for .aptip files.
public sealed record AptipPatch(int? Version, string? Seed, string? Slot);
