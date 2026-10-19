using System;
using System.IO;
using System.Text.Json;
using NodeVision.Core;

namespace NodeVision.App.Integration;

/// <summary>
/// The choices worth remembering between launches: which camera and capture mode were used, and
/// which project file was open. Persisted as JSON under the user's local application data.
/// </summary>
public sealed record AppSettings(
    string? CameraDisplayName,
    int? CameraDeviceIndex,
    CameraMode? CameraMode,
    string? ProjectPath);

public static class AppSettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NodeVision",
        "settings.json");

    /// <summary>
    /// Returns the saved settings, or null when none exist or the file cannot be read. Never throws:
    /// bad settings must not stop the app from launching.
    /// </summary>
    public static AppSettings? Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return null;

            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Options);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Console.WriteLine($"[Settings] Could not read settings: {ex.Message}");
            return null;
        }
    }

    public static void Save(AppSettings settings)
    {
        try
        {
            var directory = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            File.WriteAllText(FilePath, JsonSerializer.Serialize(settings, Options));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.WriteLine($"[Settings] Could not save settings: {ex.Message}");
        }
    }
}
