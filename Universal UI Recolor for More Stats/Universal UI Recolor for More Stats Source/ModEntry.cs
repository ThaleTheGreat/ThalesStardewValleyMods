using System.Reflection;
using System.Text.Json;
using HarmonyLib;
using StardewModdingAPI;

namespace ThaleTheGreat.UniversalUIMoreStats;

internal sealed class ModEntry : Mod
{
    private const string TargetModId = "Xan.MoreStats";
    private const string TargetPath = "Assets/InfoTab.png";

    private static readonly object Lock = new();
    private static string? TargetFullPath;
    private static string? SourcePath;
    private static IMonitor? StaticMonitor;

    private Harmony? harmony;

    public override void Entry(IModHelper helper)
    {
        StaticMonitor = this.Monitor;
        SourcePath = Path.GetFullPath(Path.Combine(helper.DirectoryPath, "assets", "InfoTab.png"));
        TargetFullPath = this.ResolveTargetFullPath();

        this.ApplyFileLookupPatch();
    }

    private string? ResolveTargetFullPath()
    {
        string? targetModDirectory = this.FindInstalledModDirectory(TargetModId);
        if (string.IsNullOrWhiteSpace(targetModDirectory))
        {
            this.Monitor.Log("More Stats was not found. Texture replacement was not registered.", LogLevel.Warn);
            return null;
        }

        return Path.GetFullPath(Path.Combine(targetModDirectory, TargetPath.Replace('/', Path.DirectorySeparatorChar)));
    }

    private void ApplyFileLookupPatch()
    {
        this.harmony = new Harmony(this.ModManifest.UniqueID);

        MethodInfo? postfix = AccessTools.Method(typeof(ModEntry), nameof(FileLookupGetFilePostfix));
        if (postfix is null)
        {
            this.Monitor.Log("Could not find file lookup postfix. More Stats texture replacement will not run.", LogLevel.Error);
            return;
        }

        int patched = 0;
        foreach (string typeName in new[]
        {
            "StardewModdingAPI.Toolkit.Utilities.PathLookups.CaseInsensitiveFileLookup",
            "StardewModdingAPI.Toolkit.Utilities.PathLookups.MinimalFileLookup"
        })
        {
            Type? lookupType = AccessTools.TypeByName(typeName);
            if (lookupType is null)
                continue;

            MethodInfo? getFile = AccessTools.Method(lookupType, "GetFile", new[] { typeof(string) });
            if (getFile is null || getFile.ReturnType != typeof(FileInfo))
                continue;

            this.harmony.Patch(getFile, postfix: new HarmonyMethod(postfix));
            patched++;
        }

        if (patched == 0)
            this.Monitor.Log("No SMAPI file lookup methods were patched. More Stats texture replacement will not run.", LogLevel.Error);
    }

    private static void FileLookupGetFilePostfix(string relativePath, ref FileInfo __result)
    {
        try
        {
            if (__result is null)
                return;

            string? target;
            string? source;
            lock (Lock)
            {
                target = TargetFullPath;
                source = SourcePath;
            }

            if (string.IsNullOrWhiteSpace(target) || string.IsNullOrWhiteSpace(source) || !File.Exists(source))
                return;

            string fullPath = Path.GetFullPath(__result.FullName);
            if (!string.Equals(fullPath, target, StringComparison.OrdinalIgnoreCase))
                return;

            __result = new FileInfo(source);
        }
        catch (Exception ex)
        {
            StaticMonitor?.Log($"More Stats texture replacement failed during file lookup: {ex.Message}", LogLevel.Trace);
        }
    }

    private string? FindInstalledModDirectory(string uniqueId)
    {
        IModInfo? mod = this.Helper.ModRegistry.Get(uniqueId);
        string? directory = mod is not null ? TryGetDirectoryPathFromModInfo(mod) : null;

        if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
            return directory;

        return this.FindInstalledModDirectoryByManifestScan(uniqueId);
    }

    private static string? TryGetDirectoryPathFromModInfo(IModInfo mod)
    {
        foreach (string propertyName in new[] { "DirectoryPath", "Directory", "ModDirectory" })
        {
            PropertyInfo? property = mod.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            object? value = property?.GetValue(mod);

            if (value is string path && Directory.Exists(path))
                return path;

            string? fullName = value?.GetType().GetProperty("FullName", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(value) as string;
            if (!string.IsNullOrWhiteSpace(fullName) && Directory.Exists(fullName))
                return fullName;
        }

        return null;
    }

    private string? FindInstalledModDirectoryByManifestScan(string uniqueId)
    {
        string modsRoot = Directory.GetParent(this.Helper.DirectoryPath)?.FullName ?? this.Helper.DirectoryPath;

        JsonDocumentOptions options = new()
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip
        };

        foreach (string manifestPath in Directory.EnumerateFiles(modsRoot, "manifest.json", SearchOption.AllDirectories))
        {
            try
            {
                using FileStream stream = File.OpenRead(manifestPath);
                using JsonDocument document = JsonDocument.Parse(stream, options);

                if (!document.RootElement.TryGetProperty("UniqueID", out JsonElement idElement))
                    continue;

                string? id = idElement.GetString();
                if (!string.Equals(id, uniqueId, StringComparison.OrdinalIgnoreCase))
                    continue;

                return Path.GetDirectoryName(manifestPath);
            }
            catch
            {
            }
        }

        return null;
    }
}
