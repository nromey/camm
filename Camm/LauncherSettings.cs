namespace Camm;

// User-editable settings for a CAMM-built launcher. Stored as a simple
// key=value ini in %LocalAppData%\<CammConfig.LocalAppDataFolderName>\
// so a screen-reader user can edit it with Notepad / their editor of
// choice without right-click tray menus or custom dialogs. Hand-parsed
// (no INI library) because the schema is tiny and AOT-friendly parsing
// matters more than feature parity with ini.h.
//
// Why %LocalAppData% and not next to the exe:
//   1. Program Files write requires admin elevation. We want users to
//      be able to change update channel (via --config / Apps & Features
//      Modify button / dialog at install) without UAC every time they
//      tweak a setting.
//   2. Per-user settings make sense if multiple Windows accounts share
//      an install — each can pick their own channel.
//   3. Matches Logger's location, so a single per-mod directory under
//      LocalAppData holds all per-user state (settings + log + markers).
public sealed class LauncherSettings
{
    public const string FileName = "launcher.ini";

    public UpdateChannel UpdateChannel { get; set; } = UpdateChannel.Stable;

    public static string DefaultPath
    {
        get
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                CammConfig.LocalAppDataFolderName);
            try { Directory.CreateDirectory(dir); } catch { /* read-only profile is fine */ }
            return Path.Combine(dir, FileName);
        }
    }

    // Load from disk. Missing file is not an error — we return defaults
    // and write a commented template back so the user can discover the
    // available keys.
    public static LauncherSettings LoadOrCreate(string path)
    {
        var settings = new LauncherSettings();
        if (!File.Exists(path))
        {
            try { WriteTemplate(path); } catch { /* read-only profile is fine */ }
            return settings;
        }

        try
        {
            foreach (var rawLine in File.ReadAllLines(path))
            {
                var line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith('#') || line.StartsWith(';'))
                {
                    continue;
                }
                var eq = line.IndexOf('=');
                if (eq <= 0) continue;
                var key = line.Substring(0, eq).Trim();
                var value = line.Substring(eq + 1).Trim();

                if (string.Equals(key, "UpdateChannel", StringComparison.OrdinalIgnoreCase))
                {
                    settings.UpdateChannel =
                        string.Equals(value, "latest", StringComparison.OrdinalIgnoreCase) ? UpdateChannel.Latest :
                        string.Equals(value, "off", StringComparison.OrdinalIgnoreCase) ? UpdateChannel.Off :
                        UpdateChannel.Stable;
                }
            }
        }
        catch
        {
            // Corrupt or unreadable file → defaults. Don't crash the
            // launcher over user config.
        }
        return settings;
    }

    // Persist current settings to disk. Used by the channel-picker
    // flow when the user changes their preference. Writes the full
    // commented template so the file stays self-documenting.
    public void Save(string path)
    {
        var channelValue = UpdateChannel switch
        {
            UpdateChannel.Latest => "latest",
            UpdateChannel.Off => "off",
            _ => "stable",
        };
        var template =
            "# " + CammConfig.DisplayName + " launcher settings\n" +
            "#\n" +
            "# UpdateChannel: which release track to follow.\n" +
            "#   stable  — only tagged stable releases (default, safer)\n" +
            "#   latest  — includes pre-release builds (newer, may be rougher)\n" +
            "#   off     — never check for updates (not recommended — you\n" +
            "#             will miss bug fixes and new screen support)\n" +
            "#\n" +
            "# This file lives in %LocalAppData%\\" + CammConfig.LocalAppDataFolderName + "\\ so you can\n" +
            "# edit it without admin elevation. The launcher also exposes\n" +
            "# a settings dialog via:\n" +
            "#   - Welcome dialog at first install\n" +
            "#   - Apps & Features \"Modify\" button (Settings > Apps)\n" +
            "#   - Command line: " + CammConfig.LauncherExeName + " --config\n" +
            "#\n" +
            "UpdateChannel=" + channelValue + "\n";
        File.WriteAllText(path, template);
    }

    private static void WriteTemplate(string path)
    {
        new LauncherSettings().Save(path);
    }
}

public enum UpdateChannel
{
    Stable,
    Latest,
    Off,
}
