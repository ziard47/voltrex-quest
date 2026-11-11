using System;
using System.IO;

namespace voltrex_quest.Services;

/// <summary>
/// Resolves local application storage and bundled data paths for both packaged and unpackaged runs.
/// </summary>
public static class AppDataPaths
{
    public static string UserDataDirectory
    {
        get
        {
            var overrideDir = Environment.GetEnvironmentVariable("VOLTREX_QUEST_DATA_DIR");
            var dir = !string.IsNullOrWhiteSpace(overrideDir)
                ? overrideDir
                : Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "VoltrexQuest");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static string LibraryFilePath => Path.Combine(UserDataDirectory, "library.json");

    public static string SettingsFilePath => Path.Combine(UserDataDirectory, "settings.json");

    public static string ResolveCatalogFilePath()
    {
        var baseCandidate = Path.Combine(AppContext.BaseDirectory, "Data", "gamelist.json");
        if (File.Exists(baseCandidate))
        {
            return baseCandidate;
        }

        var currentCandidate = Path.Combine(Environment.CurrentDirectory, "Data", "gamelist.json");
        if (File.Exists(currentCandidate))
        {
            return currentCandidate;
        }

        return baseCandidate;
    }
}

