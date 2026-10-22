using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;

namespace AdventureLauncher.Core;

public static class GameLanguages
{
    public const string English = "English";
    public const string Russian = "Russian";
    internal const string PreferenceName = "language_h1730749655";
    private const string PreferenceKey = @"Software\IronGate\Valheim";

    public static string Normalize(string? language) => (language ?? "").Trim().ToLowerInvariant() switch
    {
        "" or "english" => English,
        "russian" => Russian,
        _ => throw new InvalidDataException("Choose English or Russian as the game language.")
    };

    // The installed Steam game's Localization.SetStartupLanguage reads PlatformPrefs("language").
    // Its Steam platform uses the Unity PlayerPrefs fallback, a null-terminated UTF8 REG_BINARY.
    // Only this preference is changed, immediately before launching a closed game.
    public static void Apply(string? language, string dataDirectory)
    {
        var selected = Normalize(language);
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Game language selection requires Windows.");
        using var key = Registry.CurrentUser.CreateSubKey(PreferenceKey, writable: true);
        ApplyToKey(key, selected, Path.Combine(dataDirectory, "game-language-backup.json"));
    }

    [SupportedOSPlatform("windows")]
    internal static void ApplyToKey(RegistryKey key, string? language, string backupPath)
    {
        var selected = Normalize(language);
        var bytes = Encoding.UTF8.GetBytes(selected + "\0");
        var exists = key.GetValueNames().Contains(PreferenceName, StringComparer.OrdinalIgnoreCase);
        if (exists && key.GetValue(PreferenceName) is byte[] current && current.SequenceEqual(bytes)) return;
        // Keep the original preference once; no other registry values are exported.
        if (!File.Exists(backupPath))
        {
            var snapshot = new
            {
                registryPath = key.Name, valueName = PreferenceName, existed = exists,
                kind = exists ? key.GetValueKind(PreferenceName).ToString() : null,
                data = exists ? key.GetValue(PreferenceName) : null
            };
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(backupPath))!);
            var temporary = backupPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    JsonSerializer.Serialize(output, snapshot, PackJson.Options);
                    output.Flush(true);
                }
                File.Move(temporary, backupPath, overwrite: false);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }
        key.SetValue(PreferenceName, bytes, RegistryValueKind.Binary);
        key.Flush();
    }
}
