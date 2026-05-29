using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using StardewModdingAPI;
using StardewModdingAPI.Events;

namespace ThaleTheGreat.AssetPatcher;

public sealed class ModEntry : Mod
{
    private const string ContentFileName = "content.json";
    private const string BackupsFileName = "backups.json";

    private ModConfig Config = new();
    private readonly JsonSerializerOptions JsonOptions = new()
    {
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public override void Entry(IModHelper helper)
    {
        Config = helper.ReadConfig<ModConfig>();
        helper.Events.GameLoop.GameLaunched += OnGameLaunched;
        helper.ConsoleCommands.Add("ap_restore", "Restore one backed-up file. Usage: ap_restore <TargetMod> <TargetPath>", RestoreCommand);
        helper.ConsoleCommands.Add("ap_restore_all", "Restore every backup recorded by Asset Patcher.", RestoreAllCommand);

        ApplyAllContentPacks();
    }

    private void OnGameLaunched(object? sender, GameLaunchedEventArgs e)
    {
        RegisterGmcm();
    }

    private void RegisterGmcm()
    {
        IGenericModConfigMenuApi? gmcm = Helper.ModRegistry.GetApi<IGenericModConfigMenuApi>("spacechase0.GenericModConfigMenu");
        if (gmcm is null)
            return;

        gmcm.Register(ModManifest, ResetConfig, SaveConfig);

        gmcm.AddBoolOption(ModManifest, () => Config.EnableAssetPatching, value => Config.EnableAssetPatching = value, () => "Enable Asset Patching", () => "Allow Asset Patcher packs to copy files into target mod folders.");
        gmcm.AddBoolOption(ModManifest, () => Config.CreateBackups, value => Config.CreateBackups = value, () => "Create Backups", () => "Create an .original backup before replacing a target file. For example, InfoTab.png backs up to InfoTab.original.png.");
        gmcm.AddBoolOption(ModManifest, () => Config.ReapplyWhenTargetChanged, value => Config.ReapplyWhenTargetChanged = value, () => "Reapply When Target Changes", () => "If a target mod update restores or changes the file, copy the replacement again.");
        gmcm.AddBoolOption(ModManifest, () => Config.DebugLogging, value => Config.DebugLogging = value, () => "Debug Logging", () => "Log detailed asset patching checks.");

        gmcm.AddPageLink(ModManifest, "restore-backups", () => "Restore Backups", () => "Open a user-friendly restore menu for backed-up files.");
        gmcm.AddPage(ModManifest, "restore-backups", () => "Restore Backups");
        gmcm.AddSectionTitle(ModManifest, () => "Restore Backups", () => "Restore files from backups created by Asset Patcher.");
        gmcm.AddParagraph(ModManifest, GetRestoreStatusText);

        BackupEntry[] backups = GetBackupEntries().ToArray();
        string[] allowedValues = backups.Select(entry => entry.Id).ToArray();
        if (allowedValues.Length > 0 && string.IsNullOrWhiteSpace(Config.SelectedBackup))
            Config.SelectedBackup = allowedValues[0];

        gmcm.AddTextOption(
            ModManifest,
            () => Config.SelectedBackup,
            value => Config.SelectedBackup = value,
            () => "Backup to Restore",
            () => "Choose the backed-up file to restore.",
            allowedValues.Length > 0 ? allowedValues : new[] { string.Empty },
            FormatBackupId);

        gmcm.AddBoolOption(
            ModManifest,
            () => false,
            value =>
            {
                if (value)
                    RestoreSelectedBackupFromMenu();
            },
            () => "Restore Selected Backup",
            () => "Restore the selected backup over the currently installed file.");

        gmcm.AddBoolOption(
            ModManifest,
            () => false,
            value =>
            {
                if (value)
                    RestoreAllBackupsFromMenu();
            },
            () => "Restore All Backups",
            () => "Restore every backup recorded by Asset Patcher.");
    }

    private void ResetConfig()
    {
        Config = new ModConfig();
    }

    private void SaveConfig()
    {
        Helper.WriteConfig(Config);
    }

    private void ApplyAllContentPacks()
    {
        if (!Config.EnableAssetPatching)
        {
            DebugLog("Asset patching is disabled.");
            return;
        }

        int packCount = 0;
        int ruleCount = 0;

        foreach (IContentPack contentPack in Helper.ContentPacks.GetOwned())
        {
            packCount++;
            AssetPatcherContentFile? content = ReadContentFile(contentPack);
            if (content is null)
                continue;

            foreach (AssetPatchRule rule in GetRules(content))
            {
                ruleCount++;
                ApplyRule(contentPack, rule);
            }
        }

        DebugLog($"Checked {ruleCount} asset patch rule(s) from {packCount} content pack(s).");
    }

    private AssetPatcherContentFile? ReadContentFile(IContentPack contentPack)
    {
        if (!contentPack.HasFile(ContentFileName))
        {
            Monitor.Log($"{contentPack.Manifest.Name} has no {ContentFileName}; skipped.", LogLevel.Warn);
            return null;
        }

        try
        {
            AssetPatcherContentFile? content = contentPack.ReadJsonFile<AssetPatcherContentFile>(ContentFileName);
            if (content is null)
                Monitor.Log($"{contentPack.Manifest.Name} has an empty or unreadable {ContentFileName}; skipped.", LogLevel.Warn);
            return content;
        }
        catch (Exception ex)
        {
            Monitor.Log($"Failed reading {contentPack.Manifest.Name}/{ContentFileName}: {ex.Message}", LogLevel.Error);
            return null;
        }
    }

    private static IEnumerable<AssetPatchRule> GetRules(AssetPatcherContentFile content)
    {
        if (content.Replacements.Count > 0)
            return content.Replacements;

        return content.Changes ?? Enumerable.Empty<AssetPatchRule>();
    }

    private void ApplyRule(IContentPack contentPack, AssetPatchRule rule)
    {
        string logName = string.IsNullOrWhiteSpace(rule.LogName) ? $"{rule.TargetMod}/{rule.TargetPath}" : rule.LogName!;

        if (!IsReplaceAction(rule.Action))
        {
            Monitor.Log($"{contentPack.Manifest.Name}: unsupported action '{rule.Action}' for {logName}; skipped.", LogLevel.Warn);
            return;
        }

        if (!ConditionsPass(rule.When))
        {
            DebugLog($"{contentPack.Manifest.Name}: conditions failed for {logName}; skipped.");
            return;
        }

        if (string.IsNullOrWhiteSpace(rule.TargetMod) || string.IsNullOrWhiteSpace(rule.TargetPath) || string.IsNullOrWhiteSpace(rule.FromFile))
        {
            Monitor.Log($"{contentPack.Manifest.Name}: {logName} is missing TargetMod, TargetPath, or FromFile; skipped.", LogLevel.Warn);
            return;
        }

        if (!contentPack.HasFile(rule.FromFile))
        {
            Monitor.Log($"{contentPack.Manifest.Name}: replacement file '{rule.FromFile}' does not exist; skipped.", LogLevel.Error);
            return;
        }

        string? targetModPath = FindModDirectory(rule.TargetMod);
        if (targetModPath is null)
        {
            Monitor.Log($"{contentPack.Manifest.Name}: target mod '{rule.TargetMod}' was not found; skipped {logName}.", LogLevel.Warn);
            return;
        }

        string? targetPath = GetSafePath(targetModPath, rule.TargetPath);
        if (targetPath is null)
        {
            Monitor.Log($"{contentPack.Manifest.Name}: unsafe target path '{rule.TargetPath}' for {logName}; skipped.", LogLevel.Error);
            return;
        }

        string sourcePath = Path.Combine(contentPack.DirectoryPath, NormalizeLocalPath(rule.FromFile));
        string backupSuffix = string.IsNullOrWhiteSpace(rule.BackupSuffix) ? ".original" : rule.BackupSuffix;
        string backupPath = GetBackupPath(targetPath, backupSuffix);
        bool createBackup = rule.CreateBackup ?? Config.CreateBackups;
        bool reapply = rule.ReapplyWhenTargetChanged ?? Config.ReapplyWhenTargetChanged;

        try
        {
            if (!File.Exists(targetPath))
            {
                Monitor.Log($"{contentPack.Manifest.Name}: target file does not exist: {rule.TargetMod}/{rule.TargetPath}", LogLevel.Warn);
                return;
            }

            if (!File.Exists(sourcePath))
            {
                Monitor.Log($"{contentPack.Manifest.Name}: replacement file is missing on disk: {sourcePath}", LogLevel.Error);
                return;
            }

            bool targetMatchesReplacement = FilesMatch(sourcePath, targetPath);
            if (targetMatchesReplacement)
            {
                DebugLog($"{contentPack.Manifest.Name}: {logName} is already applied.");
                return;
            }

            if (!reapply && File.Exists(backupPath))
            {
                DebugLog($"{contentPack.Manifest.Name}: backup already exists and reapply is disabled for {logName}; skipped.");
                return;
            }

            if (createBackup && !File.Exists(backupPath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
                File.Copy(targetPath, backupPath, overwrite: false);
                DebugLog($"{contentPack.Manifest.Name}: created backup {backupPath}.");
            }

            if (createBackup && File.Exists(backupPath))
                RecordBackup(rule.TargetMod, rule.TargetPath, targetModPath, backupPath, backupSuffix);

            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            File.Copy(sourcePath, targetPath, overwrite: true);
            Monitor.Log($"Applied {contentPack.Manifest.Name}: {logName}.", LogLevel.Info);
        }
        catch (Exception ex)
        {
            Monitor.Log($"Failed applying {contentPack.Manifest.Name}: {logName}: {ex.Message}", LogLevel.Error);
        }
    }

    private bool ConditionsPass(AssetPatchConditions? conditions)
    {
        if (conditions is null)
            return true;

        if (conditions.HasMod is not null)
        {
            foreach (string modId in conditions.HasMod)
            {
                if (!Helper.ModRegistry.IsLoaded(modId))
                    return false;
            }
        }

        if (conditions.NotHasMod is not null)
        {
            foreach (string modId in conditions.NotHasMod)
            {
                if (Helper.ModRegistry.IsLoaded(modId))
                    return false;
            }
        }

        return true;
    }

    private static string? ReadUniqueIdFromManifest(string manifestPath)
    {
        try
        {
            using FileStream stream = File.OpenRead(manifestPath);
            using JsonDocument document = JsonDocument.Parse(stream, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });

            if (!document.RootElement.TryGetProperty("UniqueID", out JsonElement uniqueIdElement))
                return null;

            return uniqueIdElement.GetString();
        }
        catch
        {
            return null;
        }
    }

    private string? FindModDirectory(string uniqueId)
    {
        DirectoryInfo? modsRoot = Directory.GetParent(Helper.DirectoryPath);
        if (modsRoot is null || !modsRoot.Exists)
            return null;

        foreach (string manifestPath in Directory.EnumerateFiles(modsRoot.FullName, "manifest.json", SearchOption.AllDirectories))
        {
            try
            {
                using FileStream stream = File.OpenRead(manifestPath);
                using JsonDocument document = JsonDocument.Parse(stream, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });

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

    private static bool IsReplaceAction(string? action)
    {
        return string.Equals(action, "ReplaceFile", StringComparison.OrdinalIgnoreCase)
            || string.Equals(action, "LoadFile", StringComparison.OrdinalIgnoreCase)
            || string.Equals(action, "FileReplace", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeLocalPath(string path)
    {
        return path.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
    }

    private static string? GetSafePath(string root, string relativePath)
    {
        if (Path.IsPathRooted(relativePath))
            return null;

        string normalized = NormalizeLocalPath(relativePath);
        string fullRoot = Path.GetFullPath(root);
        string fullPath = Path.GetFullPath(Path.Combine(fullRoot, normalized));

        string rootWithSeparator = fullRoot.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
            ? fullRoot
            : fullRoot + Path.DirectorySeparatorChar;

        if (!fullPath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase) && !string.Equals(fullPath, fullRoot, StringComparison.OrdinalIgnoreCase))
            return null;

        return fullPath;
    }

    private static bool FilesMatch(string firstPath, string secondPath)
    {
        FileInfo first = new(firstPath);
        FileInfo second = new(secondPath);
        if (!first.Exists || !second.Exists || first.Length != second.Length)
            return false;

        using SHA256 sha = SHA256.Create();
        using FileStream firstStream = File.OpenRead(firstPath);
        using FileStream secondStream = File.OpenRead(secondPath);
        byte[] firstHash = sha.ComputeHash(firstStream);
        byte[] secondHash = sha.ComputeHash(secondStream);
        return firstHash.SequenceEqual(secondHash);
    }

    private string GetRestoreStatusText()
    {
        int count = GetBackupEntries().Count();
        return count == 0
            ? "No Asset Patcher backups were found. Backups will appear here after addon packs replace files."
            : $"{count} backup file(s) found. Choose one backup below and enable Restore Selected Backup, or enable Restore All Backups to restore every recorded backup.";
    }

    private IEnumerable<BackupEntry> GetBackupEntries()
    {
        foreach (BackupRecord record in ReadBackupRegistry().Backups)
        {
            if (string.IsNullOrWhiteSpace(record.TargetMod) || string.IsNullOrWhiteSpace(record.TargetPath) || string.IsNullOrWhiteSpace(record.BackupPath))
                continue;

            string? targetModPath = FindModDirectory(record.TargetMod);
            if (targetModPath is null)
                continue;

            string? targetPath = GetSafePath(targetModPath, record.TargetPath);
            string? backupPath = GetSafePath(targetModPath, record.BackupPath);
            if (targetPath is null || backupPath is null || !File.Exists(backupPath))
                continue;

            string relativeBackupPath = Path.GetRelativePath(targetModPath, backupPath).Replace(Path.DirectorySeparatorChar, '/');
            yield return new BackupEntry($"{record.TargetMod}|{record.TargetPath}", record.TargetMod, record.TargetPath, relativeBackupPath, backupPath, targetPath);
        }
    }

    private string FormatBackupId(string backupId)
    {
        if (string.IsNullOrWhiteSpace(backupId))
            return "No backups found";

        BackupEntry? entry = GetBackupEntries().FirstOrDefault(candidate => string.Equals(candidate.Id, backupId, StringComparison.OrdinalIgnoreCase));
        return entry is null
            ? backupId
            : $"{entry.TargetMod}: {entry.TargetPath}";
    }

    private void RestoreSelectedBackupFromMenu()
    {
        if (string.IsNullOrWhiteSpace(Config.SelectedBackup))
        {
            Monitor.Log("No backup is selected.", LogLevel.Warn);
            return;
        }

        BackupEntry? entry = GetBackupEntries().FirstOrDefault(candidate => string.Equals(candidate.Id, Config.SelectedBackup, StringComparison.OrdinalIgnoreCase));
        if (entry is null)
        {
            Monitor.Log("The selected backup could not be found. Reopen the GMCM menu to refresh the backup list.", LogLevel.Warn);
            return;
        }

        if (RestoreBackup(entry.BackupPath, entry.TargetFullPath))
            SelectFirstAvailableBackup();
    }

    private void RestoreAllBackupsFromMenu()
    {
        int restored = 0;
        foreach (BackupEntry entry in GetBackupEntries().ToArray())
        {
            if (RestoreBackup(entry.BackupPath, entry.TargetFullPath, logSuccess: false))
                restored++;
        }

        SelectFirstAvailableBackup();
        Monitor.Log($"Restored {restored} backup file(s).", LogLevel.Info);
    }

    private void RestoreCommand(string command, string[] args)
    {
        if (args.Length < 2)
        {
            Monitor.Log("Usage: ap_restore <TargetMod> <TargetPath>", LogLevel.Info);
            return;
        }

        string targetMod = args[0];
        string targetPathArg = string.Join(" ", args.Skip(1));
        string? targetModPath = FindModDirectory(targetMod);
        if (targetModPath is null)
        {
            Monitor.Log($"Target mod '{targetMod}' was not found.", LogLevel.Warn);
            return;
        }

        string? targetPath = GetSafePath(targetModPath, targetPathArg);
        if (targetPath is null)
        {
            Monitor.Log($"Unsafe target path '{targetPathArg}'.", LogLevel.Error);
            return;
        }

        string backupPath = GetBackupPath(targetPath, ".original");
        RestoreBackup(backupPath, targetPath);
    }

    private void RestoreAllCommand(string command, string[] args)
    {
        int restored = 0;
        foreach (BackupEntry entry in GetBackupEntries().ToArray())
        {
            if (RestoreBackup(entry.BackupPath, entry.TargetFullPath, logSuccess: false))
                restored++;
        }

        SelectFirstAvailableBackup();
        Monitor.Log($"Restored {restored} backup file(s).", LogLevel.Info);
    }

    private BackupRegistry ReadBackupRegistry()
    {
        string path = Path.Combine(Helper.DirectoryPath, BackupsFileName);
        if (!File.Exists(path))
            return new BackupRegistry();

        try
        {
            using FileStream stream = File.OpenRead(path);
            return JsonSerializer.Deserialize<BackupRegistry>(stream, JsonOptions) ?? new BackupRegistry();
        }
        catch (Exception ex)
        {
            Monitor.Log($"Failed reading {BackupsFileName}: {ex.Message}", LogLevel.Warn);
            return new BackupRegistry();
        }
    }

    private void WriteBackupRegistry(BackupRegistry registry)
    {
        string path = Path.Combine(Helper.DirectoryPath, BackupsFileName);
        try
        {
            using FileStream stream = File.Create(path);
            JsonSerializer.Serialize(stream, registry, JsonOptions);
        }
        catch (Exception ex)
        {
            Monitor.Log($"Failed writing {BackupsFileName}: {ex.Message}", LogLevel.Warn);
        }
    }

    private void RecordBackup(string targetMod, string targetPath, string targetModDirectory, string backupPath, string backupSuffix)
    {
        string normalizedTargetPath = targetPath.Replace('\\', '/');
        string relativeBackupPath = Path.GetRelativePath(targetModDirectory, backupPath).Replace(Path.DirectorySeparatorChar, '/');
        BackupRegistry registry = ReadBackupRegistry();

        BackupRecord? existing = registry.Backups.FirstOrDefault(entry =>
            string.Equals(entry.TargetMod, targetMod, StringComparison.OrdinalIgnoreCase)
            && string.Equals(entry.TargetPath, normalizedTargetPath, StringComparison.OrdinalIgnoreCase));

        if (existing is null)
        {
            registry.Backups.Add(new BackupRecord
            {
                TargetMod = targetMod,
                TargetPath = normalizedTargetPath,
                BackupPath = relativeBackupPath,
                BackupSuffix = backupSuffix
            });
        }
        else
        {
            existing.BackupPath = relativeBackupPath;
            existing.BackupSuffix = backupSuffix;
        }

        WriteBackupRegistry(registry);
    }

    private static string GetBackupPath(string targetPath, string backupSuffix)
    {
        string directory = Path.GetDirectoryName(targetPath) ?? string.Empty;
        string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(targetPath);
        string extension = Path.GetExtension(targetPath);

        if (string.IsNullOrEmpty(extension))
            return targetPath + backupSuffix;

        return Path.Combine(directory, fileNameWithoutExtension + backupSuffix + extension);
    }

    private bool RestoreBackup(string backupPath, string targetPath, bool logSuccess = true)
    {
        try
        {
            if (!File.Exists(backupPath))
            {
                Monitor.Log($"Backup does not exist: {backupPath}", LogLevel.Warn);
                RemoveBackupRecord(backupPath, targetPath);
                return false;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            File.Copy(backupPath, targetPath, overwrite: true);
            File.Delete(backupPath);
            RemoveBackupRecord(backupPath, targetPath);

            if (logSuccess)
                Monitor.Log($"Restored backup to {targetPath} and removed {backupPath}.", LogLevel.Info);
            return true;
        }
        catch (Exception ex)
        {
            Monitor.Log($"Failed restoring {backupPath}: {ex.Message}", LogLevel.Error);
            return false;
        }
    }


    private void RemoveBackupRecord(string backupPath, string targetPath)
    {
        string normalizedBackupPath = Path.GetFullPath(backupPath);
        string normalizedTargetPath = Path.GetFullPath(targetPath);
        BackupRegistry registry = ReadBackupRegistry();
        int oldCount = registry.Backups.Count;

        registry.Backups.RemoveAll(record => BackupRecordMatches(record, normalizedBackupPath, normalizedTargetPath));

        if (registry.Backups.Count != oldCount)
            WriteBackupRegistry(registry);
    }

    private bool BackupRecordMatches(BackupRecord record, string backupPath, string targetPath)
    {
        if (string.IsNullOrWhiteSpace(record.TargetMod) || string.IsNullOrWhiteSpace(record.TargetPath) || string.IsNullOrWhiteSpace(record.BackupPath))
            return false;

        string? targetModPath = FindModDirectory(record.TargetMod);
        if (targetModPath is null)
            return false;

        string? recordedTargetPath = GetSafePath(targetModPath, record.TargetPath);
        string? recordedBackupPath = GetSafePath(targetModPath, record.BackupPath);
        if (recordedTargetPath is null || recordedBackupPath is null)
            return false;

        return string.Equals(Path.GetFullPath(recordedTargetPath), targetPath, StringComparison.OrdinalIgnoreCase)
            && string.Equals(Path.GetFullPath(recordedBackupPath), backupPath, StringComparison.OrdinalIgnoreCase);
    }

    private void SelectFirstAvailableBackup()
    {
        Config.SelectedBackup = GetBackupEntries().FirstOrDefault()?.Id ?? string.Empty;
        Helper.WriteConfig(Config);
    }

    private sealed record BackupEntry(
        string Id,
        string TargetMod,
        string TargetPath,
        string BackupPathRelative,
        string BackupPath,
        string TargetFullPath);

    private void DebugLog(string message)
    {
        if (Config.DebugLogging)
            Monitor.Log(message, LogLevel.Debug);
    }
}
