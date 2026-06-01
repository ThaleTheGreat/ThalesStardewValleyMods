using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;
using StardewValley.Menus;
using StardewModdingAPI;
using StardewModdingAPI.Events;

namespace ThaleTheGreat.ModPatcher;

internal sealed partial class ModEntry : Mod
{
    private static readonly object Lock = new();

    private static readonly Dictionary<string, RegisteredPatch> PatchesByTargetFullPath = new(StringComparer.OrdinalIgnoreCase);

    private static IMonitor? StaticMonitor;
    private static bool StaticDebugLogging;

    private static string? CachedMenuBoxSignature;

    private string GeneratedAssetRoot = "";

    private ModConfig Config = new();

    private Harmony? Harmony;

    public override void Entry(IModHelper helper)
    {
        StaticMonitor = this.Monitor;

        this.Config = helper.ReadConfig<ModConfig>();
        StaticDebugLogging = this.Config.DebugLogging;
        this.GeneratedAssetRoot = Path.Combine(helper.DirectoryPath, "generated");

        this.LoadChanges();
        this.ApplyHarmonyPatches();

        helper.Events.GameLoop.GameLaunched += this.OnGameLaunched;
        helper.Events.GameLoop.UpdateTicked += this.OnUpdateTicked;
        helper.Events.GameLoop.Saving += this.OnSaving;
        helper.Events.Player.Warped += this.OnInteractionWarped;
        helper.Events.Display.RenderedWorld += this.OnInteractionRenderedWorld;
        helper.Events.GameLoop.ReturnedToTitle += (_, _) =>
        {
            this.ClearBridgeProxies();
            this.UsedInteractionKeysToday.Clear();
            this.PendingInteractions.Clear();
        };
        helper.Events.GameLoop.DayStarted += (_, _) =>
        {
            this.UsedInteractionKeysToday.Clear();
            this.PendingInteractions.Clear();
        };
        helper.Events.Input.ButtonPressed += this.OnInteractionButtonPressed;
    }

    private void LoadChanges()
    {
        foreach (IContentPack pack in this.Helper.ContentPacks.GetOwned())
        {
            PatchContentFile? content;
            try
            {
                content = pack.ReadJsonFile<PatchContentFile>("patches.json");
            }
            catch (Exception ex)
            {
                this.Monitor.Log($"Failed reading patches.json from {pack.Manifest.UniqueID}: {ex.Message}", LogLevel.Error);
                continue;
            }

            if (content?.Changes == null || content.Changes.Count == 0)
            {
                this.LogDebug($"Pack {pack.Manifest.UniqueID} has no changes.");
                continue;
            }

            foreach (AssetPatchChange change in content.Changes)
                this.TryRegisterChange(pack, change);
        }

        this.Monitor.Log($"Loaded {PatchesByTargetFullPath.Count} asset patch(es).", LogLevel.Info);
    }

    private void TryRegisterChange(IContentPack pack, AssetPatchChange change)
    {
        if (IsPatchRuntimeAction(change.Action))
        {
            this.TryRegisterRuntimeChange(pack, change);
            return;
        }

        if (IsPatchInteractionAction(change.Action))
        {
            this.TryRegisterInteractionChange(pack, change);
            return;
        }

        if (!IsPatchModAction(change.Action))
        {
            this.Monitor.Log($"Ignored change from {pack.Manifest.UniqueID}: unsupported Action '{change.Action}'.", LogLevel.Warn);
            return;
        }

        if (string.IsNullOrWhiteSpace(change.TargetMod))
        {
            this.Monitor.Log($"Ignored change from {pack.Manifest.UniqueID}: TargetMod is required.", LogLevel.Warn);
            return;
        }

        if (string.IsNullOrWhiteSpace(change.TargetPath))
        {
            this.Monitor.Log($"Ignored change from {pack.Manifest.UniqueID}: TargetPath is required.", LogLevel.Warn);
            return;
        }

        bool hasFileSource = !string.IsNullOrWhiteSpace(change.FromFile);
        bool hasVanillaUiSource = !string.IsNullOrWhiteSpace(change.FromVanillaUi);

        if (hasFileSource == hasVanillaUiSource)
        {
            this.Monitor.Log($"Ignored change from {pack.Manifest.UniqueID}: exactly one source is required: FromFile or FromVanillaUi.", LogLevel.Warn);
            return;
        }

        if (hasVanillaUiSource && !IsSupportedVanillaUi(change.FromVanillaUi))
        {
            this.Monitor.Log($"Ignored change from {pack.Manifest.UniqueID}: unsupported FromVanillaUi '{change.FromVanillaUi}'.", LogLevel.Warn);
            return;
        }

        if (!IsSafeRelativePath(change.TargetPath))
        {
            this.Monitor.Log($"Ignored change from {pack.Manifest.UniqueID}: TargetPath '{change.TargetPath}' is unsafe.", LogLevel.Warn);
            return;
        }

        string sourcePath;
        string fromVanillaUi = change.FromVanillaUi.Trim();
        int outputWidth = Math.Clamp(change.OutputWidth, 1, 4096);
        int outputHeight = Math.Clamp(change.OutputHeight, 1, 4096);

        if (hasFileSource)
        {
            if (!IsSafeRelativePath(change.FromFile))
            {
                this.Monitor.Log($"Ignored change from {pack.Manifest.UniqueID}: FromFile '{change.FromFile}' is unsafe.", LogLevel.Warn);
                return;
            }

            sourcePath = Path.GetFullPath(Path.Combine(pack.DirectoryPath, NormalizeForPlatform(change.FromFile)));
            if (!File.Exists(sourcePath))
            {
                this.Monitor.Log($"Ignored change from {pack.Manifest.UniqueID}: source file '{change.FromFile}' was not found.", LogLevel.Warn);
                return;
            }
        }
        else
        {
            string generatedFileName = BuildGeneratedFileName(pack.Manifest.UniqueID, change.TargetMod, change.TargetPath, fromVanillaUi, outputWidth, outputHeight);
            sourcePath = Path.Combine(this.GeneratedAssetRoot, SanitizeFileName(pack.Manifest.UniqueID), generatedFileName);
        }

        string targetModId = change.TargetMod.Trim();
        string? targetModDirectory = this.FindInstalledModDirectory(targetModId);
        if (targetModDirectory == null)
        {
            this.Monitor.Log($"Ignored change from {pack.Manifest.UniqueID}: target mod '{targetModId}' is not installed.", LogLevel.Warn);
            return;
        }

        string targetPath = NormalizeAssetPath(change.TargetPath);
        string targetFullPath = Path.GetFullPath(Path.Combine(targetModDirectory, NormalizeForPlatform(targetPath)));

        if (!IsInsideDirectory(targetModDirectory, targetFullPath))
        {
            this.Monitor.Log($"Ignored change from {pack.Manifest.UniqueID}: TargetPath '{change.TargetPath}' escapes target mod folder.", LogLevel.Warn);
            return;
        }

        RegisteredPatch patch = new()
        {
            PackId = pack.Manifest.UniqueID,
            LogName = string.IsNullOrWhiteSpace(change.LogName) ? $"{targetModId}/{targetPath}" : change.LogName.Trim(),
            TargetMod = targetModId,
            TargetPath = targetPath,
            SourcePath = sourcePath,
            TargetFullPath = targetFullPath,
            FromVanillaUi = fromVanillaUi,
            OutputWidth = outputWidth,
            OutputHeight = outputHeight
        };

        lock (Lock)
            PatchesByTargetFullPath[targetFullPath] = patch;

        if (patch.IsGenerated)
            this.LogDebug($"Registered generated patch '{patch.LogName}': {targetFullPath} -> {patch.FromVanillaUi} {patch.OutputWidth}x{patch.OutputHeight}");
        else
            this.LogDebug($"Registered patch '{patch.LogName}': {targetFullPath} -> {sourcePath}");
    }

    private static bool IsPatchModAction(string action)
    {
        return string.Equals(action, "PatchMod", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPatchRuntimeAction(string action)
    {
        return string.Equals(action, "PatchRuntime", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPatchInteractionAction(string action)
    {
        return string.Equals(action, "PatchInteraction", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSupportedVanillaUi(string value)
    {
        return string.Equals(value.Trim(), "MenuBox", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildGeneratedFileName(string packId, string targetMod, string targetPath, string sourceKind, int width, int height)
    {
        string text = $"{packId}|{targetMod}|{targetPath}|{sourceKind}|{width}|{height}";
        return $"{SanitizeFileName(targetMod)}_{BuildStableHash(text)}.png";
    }

    private static string BuildGeneratedFileName(RegisteredPatch patch)
    {
        string sourceSignature = GetGeneratedSourceSignature(patch);
        string text = $"{patch.PackId}|{patch.TargetMod}|{patch.TargetPath}|{patch.FromVanillaUi}|{patch.OutputWidth}|{patch.OutputHeight}|{sourceSignature}";
        return $"{SanitizeFileName(patch.TargetMod)}_{BuildStableHash(text)}.png";
    }

    private static string BuildStableHash(string text)
    {
        byte[] data = Encoding.UTF8.GetBytes(text.ToUpperInvariant());
        byte[] hash = SHA256.HashData(data);
        return Convert.ToHexString(hash, 0, 8);
    }

    private static string SanitizeFileName(string value)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        return new string(value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
    }

    private string? FindInstalledModDirectory(string uniqueId)
    {
        IModInfo? mod = this.Helper.ModRegistry.Get(uniqueId);
        string? directory = mod != null ? TryGetDirectoryPathFromModInfo(mod) : null;

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
                // Ignore malformed manifests during fallback lookup.
            }
        }

        return null;
    }

    private void ApplyHarmonyPatches()
    {
        this.Harmony = new Harmony(this.ModManifest.UniqueID);

        MethodInfo? postfix = AccessTools.Method(typeof(ModEntry), nameof(FileLookupGetFilePostfix));
        if (postfix == null)
        {
            this.Monitor.Log("Could not find Mod Patcher file lookup postfix. Asset patches will not run.", LogLevel.Error);
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
            if (lookupType == null)
            {
                this.LogDebug($"Could not find {typeName}; skipping.");
                continue;
            }

            MethodInfo? getFile = AccessTools.Method(lookupType, "GetFile", new[] { typeof(string) });
            if (getFile == null || getFile.ReturnType != typeof(FileInfo))
            {
                this.Monitor.Log($"Could not find {typeName}.GetFile(string); skipping.", LogLevel.Warn);
                continue;
            }

            this.Harmony.Patch(getFile, postfix: new HarmonyMethod(postfix));
            patched++;
        }

        if (patched == 0)
            this.Monitor.Log("No SMAPI file lookup methods were patched. Asset patches will not run.", LogLevel.Error);
        else
            this.Monitor.Log($"Harmony patch applied to {patched} SMAPI file lookup method(s).", LogLevel.Info);
    }

    private void OnGameLaunched(object? sender, GameLaunchedEventArgs e)
    {
        this.RegisterGmcm();
        this.ApplyRuntimePatches();
    }

    private void RegisterGmcm()
    {
        IGenericModConfigMenuApi? gmcm = this.Helper.ModRegistry.GetApi<IGenericModConfigMenuApi>("spacechase0.GenericModConfigMenu");
        if (gmcm == null)
            return;

        gmcm.Register(
            this.ModManifest,
            reset: () =>
            {
                this.Config = new ModConfig();
                StaticDebugLogging = this.Config.DebugLogging;
            },
            save: () =>
            {
                StaticDebugLogging = this.Config.DebugLogging;
                this.Helper.WriteConfig(this.Config);
            }
        );

        this.RegisterInteractionGmcm(gmcm);

        gmcm.AddBoolOption(
            this.ModManifest,
            getValue: () => this.Config.DebugLogging,
            setValue: value =>
            {
                this.Config.DebugLogging = value;
                StaticDebugLogging = value;
            },
            name: () => "Debug Logging",
            tooltip: () => "Show detailed Mod Patcher diagnostics."
        );
    }

    private void LogDebug(string message)
    {
        if (this.Config.DebugLogging)
            this.Monitor.Log(message, LogLevel.Debug);
    }

    private static void FileLookupGetFilePostfix(string relativePath, ref FileInfo __result)
    {
        try
        {
            if (__result == null)
                return;

            string fullPath = Path.GetFullPath(__result.FullName);

            RegisteredPatch? patch;
            lock (Lock)
            {
                if (!PatchesByTargetFullPath.TryGetValue(fullPath, out patch))
                    return;
            }

            if (patch.IsGenerated)
                UpdateGeneratedPatchSourcePath(patch);

            if (!File.Exists(patch.SourcePath))
            {
                if (!patch.IsGenerated || !TryGeneratePatchSource(patch))
                    return;
            }

            if (StaticDebugLogging)
                StaticMonitor?.Log($"Patched asset '{patch.LogName}': {relativePath} -> {patch.SourcePath}", LogLevel.Debug);

            __result = new FileInfo(patch.SourcePath);
        }
        catch
        {
            // Never break SMAPI's file lookup if our patch check fails.
        }
    }

    private static void UpdateGeneratedPatchSourcePath(RegisteredPatch patch)
    {
        string? generatedRoot = Path.GetDirectoryName(patch.SourcePath);
        if (string.IsNullOrWhiteSpace(generatedRoot))
            return;

        patch.SourcePath = Path.Combine(generatedRoot, BuildGeneratedFileName(patch));
    }

    private static string GetGeneratedSourceSignature(RegisteredPatch patch)
    {
        if (string.Equals(patch.FromVanillaUi, "MenuBox", StringComparison.OrdinalIgnoreCase))
            return GetMenuBoxSignature();

        return "unknown";
    }

    private static string GetMenuBoxSignature()
    {
        if (!string.IsNullOrWhiteSpace(CachedMenuBoxSignature))
            return CachedMenuBoxSignature;

        try
        {
            Texture2D menuTexture = Game1.menuTexture;
            Rectangle sourceRect = new(0, 256, 60, 60);
            Color[] pixels = new Color[sourceRect.Width * sourceRect.Height];
            menuTexture.GetData(0, sourceRect, pixels, 0, pixels.Length);

            byte[] bytes = new byte[pixels.Length * 4];
            for (int i = 0; i < pixels.Length; i++)
            {
                int offset = i * 4;
                bytes[offset] = pixels[i].R;
                bytes[offset + 1] = pixels[i].G;
                bytes[offset + 2] = pixels[i].B;
                bytes[offset + 3] = pixels[i].A;
            }

            byte[] hash = SHA256.HashData(bytes);
            CachedMenuBoxSignature = Convert.ToHexString(hash, 0, 8);
        }
        catch
        {
            CachedMenuBoxSignature = "MenuBox";
        }

        return CachedMenuBoxSignature;
    }

    private static bool TryGeneratePatchSource(RegisteredPatch patch)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(patch.SourcePath)!);

            if (string.Equals(patch.FromVanillaUi, "MenuBox", StringComparison.OrdinalIgnoreCase))
            {
                GenerateMenuBoxTexture(patch.SourcePath, patch.OutputWidth, patch.OutputHeight);
                return File.Exists(patch.SourcePath);
            }
        }
        catch (Exception ex)
        {
            if (StaticDebugLogging)
                StaticMonitor?.Log($"Failed generating patch asset '{patch.LogName}': {ex.Message}", LogLevel.Debug);
        }

        return false;
    }

    private static void GenerateMenuBoxTexture(string outputPath, int width, int height)
    {
        GraphicsDevice graphicsDevice = Game1.graphics.GraphicsDevice;
        RenderTargetBinding[] previousTargets = graphicsDevice.GetRenderTargets();

        using RenderTarget2D renderTarget = new(graphicsDevice, width, height, false, SurfaceFormat.Color, DepthFormat.None);
        using SpriteBatch spriteBatch = new(graphicsDevice);

        try
        {
            graphicsDevice.SetRenderTarget(renderTarget);
            graphicsDevice.Clear(Color.Transparent);

            spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.None, RasterizerState.CullNone);
            IClickableMenu.drawTextureBox(
                spriteBatch,
                Game1.menuTexture,
                new Rectangle(0, 256, 60, 60),
                0,
                0,
                width,
                height,
                Color.White,
                1f,
                false
            );
            spriteBatch.End();
        }
        finally
        {
            graphicsDevice.SetRenderTargets(previousTargets);
        }

        using FileStream stream = File.Create(outputPath);
        renderTarget.SaveAsPng(stream, width, height);
    }

    private static string NormalizeAssetPath(string path)
    {
        return path.Replace('\\', '/').Trim().TrimStart('/');
    }

    private static string NormalizeForPlatform(string path)
    {
        return path.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
    }

    private static bool IsSafeRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        if (Path.IsPathRooted(path))
            return false;

        string normalized = path.Replace('\\', '/');
        return !normalized.Split('/', StringSplitOptions.RemoveEmptyEntries).Any(segment => segment == "..");
    }

    private static bool IsInsideDirectory(string directory, string path)
    {
        string root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string child = Path.GetFullPath(path);
        return child.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }
}
