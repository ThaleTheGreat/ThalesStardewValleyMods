using System;
using System.IO;
using System.Text.Json;
using StardewModdingAPI;

namespace ThaleTheGreat.EarthyMoreStats;

public sealed class ModEntry : Mod
{
    private const string TargetModId = "Xan.MoreStats";

    public override void Entry(IModHelper helper)
    {
        try
        {
            ApplyReplacement(helper);
        }
        catch (Exception ex)
        {
            Monitor.Log($"Failed to replace More Stats info tab texture: {ex}", LogLevel.Error);
        }
    }

    private void ApplyReplacement(IModHelper helper)
    {
        if (!helper.ModRegistry.IsLoaded(TargetModId))
        {
            Monitor.Log("More Stats is not installed, so no texture was replaced.", LogLevel.Warn);
            return;
        }

        string sourcePath = Path.Combine(helper.DirectoryPath, "assets", "InfoTab.png");
        string? targetModPath = FindModDirectory(helper.DirectoryPath, TargetModId);

        if (targetModPath is null)
        {
            Monitor.Log("More Stats is loaded, but its mod folder could not be found.", LogLevel.Warn);
            return;
        }

        string targetPath = Path.Combine(targetModPath, "Assets", "InfoTab.png");
        string backupPath = targetPath + ".original";

        if (!File.Exists(sourcePath))
        {
            Monitor.Log($"Replacement texture is missing: {sourcePath}", LogLevel.Error);
            return;
        }

        if (!File.Exists(targetPath))
        {
            Monitor.Log("More Stats texture was not found at Assets/InfoTab.png.", LogLevel.Warn);
            return;
        }

        if (!File.Exists(backupPath))
            File.Copy(targetPath, backupPath, overwrite: false);

        if (FilesMatch(sourcePath, targetPath))
        {
            Monitor.Log("More Stats info tab texture is already replaced.", LogLevel.Trace);
            return;
        }

        File.Copy(sourcePath, targetPath, overwrite: true);
        Monitor.Log("Replaced More Stats info tab texture. Restart the game if More Stats already loaded the old texture.", LogLevel.Info);
    }

    private static string? FindModDirectory(string currentModPath, string uniqueId)
    {
        DirectoryInfo? modsRoot = Directory.GetParent(currentModPath);
        if (modsRoot is null || !modsRoot.Exists)
            return null;

        foreach (string manifestPath in Directory.EnumerateFiles(modsRoot.FullName, "manifest.json", SearchOption.AllDirectories))
        {
            try
            {
                using FileStream stream = File.OpenRead(manifestPath);
                using JsonDocument document = JsonDocument.Parse(stream);

                if (!document.RootElement.TryGetProperty("UniqueID", out JsonElement uniqueIdElement))
                    continue;

                string? foundUniqueId = uniqueIdElement.GetString();
                if (string.Equals(foundUniqueId, uniqueId, StringComparison.OrdinalIgnoreCase))
                    return Path.GetDirectoryName(manifestPath);
            }
            catch
            {
            }
        }

        return null;
    }

    private static bool FilesMatch(string firstPath, string secondPath)
    {
        byte[] first = File.ReadAllBytes(firstPath);
        byte[] second = File.ReadAllBytes(secondPath);

        if (first.Length != second.Length)
            return false;

        for (int index = 0; index < first.Length; index++)
        {
            if (first[index] != second[index])
                return false;
        }

        return true;
    }
}
