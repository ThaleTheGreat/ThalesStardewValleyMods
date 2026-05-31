using System.Reflection;
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

    [ThreadStatic]
    private static RegisteredPatch? PendingRuntimeVanillaUiPatch;

    private ModConfig Config = new();

    private Harmony? Harmony;

    public override void Entry(IModHelper helper)
    {
        StaticMonitor = this.Monitor;

        this.Config = helper.ReadConfig<ModConfig>();
        StaticDebugLogging = this.Config.DebugLogging;
        this.LoadChanges();
        this.ApplyHarmonyPatches();

        helper.Events.GameLoop.GameLaunched += this.OnGameLaunched;
        helper.Events.GameLoop.UpdateTicked += this.OnUpdateTicked;
        helper.Events.GameLoop.Saving += this.OnSaving;
        helper.Events.GameLoop.ReturnedToTitle += (_, _) => this.ClearBridgeProxies();
    }

    private void LoadChanges()
    {
        foreach (IContentPack pack in this.Helper.ContentPacks.GetOwned())
        {
            PatchContentFile? content;
            try
            {
                content = pack.ReadJsonFile<PatchContentFile>("content.json")
                    ?? pack.ReadJsonFile<PatchContentFile>("patches.json");
            }
            catch (Exception ex)
            {
                this.Monitor.Log($"Failed reading content.json from {pack.Manifest.UniqueID}: {ex.Message}", LogLevel.Error);
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
        if (string.Equals(change.Action, "BridgeMods", StringComparison.OrdinalIgnoreCase))
        {
            this.TryRegisterBridgeChange(pack, change);
            return;
        }

        if (!string.Equals(change.Action, "PatchMod", StringComparison.OrdinalIgnoreCase))
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
            sourcePath = string.Empty;
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
            this.LogDebug($"Registered runtime vanilla UI patch '{patch.LogName}': {targetFullPath} -> {patch.FromVanillaUi} {patch.OutputWidth}x{patch.OutputHeight}");
        else
            this.LogDebug($"Registered patch '{patch.LogName}': {targetFullPath} -> {sourcePath}");
    }

    private static bool IsSupportedVanillaUi(string value)
    {
        return string.Equals(value.Trim(), "MenuBox", StringComparison.OrdinalIgnoreCase);
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

        MethodInfo? texturePrefix = AccessTools.Method(typeof(ModEntry), nameof(Texture2DFromStreamPrefix));
        MethodInfo? fromStream = AccessTools.Method(typeof(Texture2D), nameof(Texture2D.FromStream), new[] { typeof(GraphicsDevice), typeof(Stream) });
        if (texturePrefix == null || fromStream == null)
        {
            this.Monitor.Log("Could not find Texture2D.FromStream patch hook. Runtime vanilla UI sources will not run.", LogLevel.Warn);
        }
        else
        {
            this.Harmony.Patch(fromStream, prefix: new HarmonyMethod(texturePrefix));
            patched++;
        }

        if (patched == 0)
            this.Monitor.Log("No SMAPI file lookup or texture load methods were patched. Asset patches will not run.", LogLevel.Error);
        else
            this.Monitor.Log($"Harmony patch applied to {patched} SMAPI/mod texture lookup method(s).", LogLevel.Info);
    }

    private void OnGameLaunched(object? sender, GameLaunchedEventArgs e)
    {
        this.RegisterGmcm();
        this.ApplyBridgeModPatches();
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
            {
                PendingRuntimeVanillaUiPatch = patch;

                if (StaticDebugLogging)
                    StaticMonitor?.Log($"Prepared runtime vanilla UI patch '{patch.LogName}' for {relativePath}.", LogLevel.Debug);

                return;
            }

            if (!File.Exists(patch.SourcePath))
                return;

            if (StaticDebugLogging)
                StaticMonitor?.Log($"Patched asset '{patch.LogName}': {relativePath} -> {patch.SourcePath}", LogLevel.Debug);

            __result = new FileInfo(patch.SourcePath);
        }
        catch
        {
            // Never break SMAPI's file lookup if our patch check fails.
        }
    }

    private static bool Texture2DFromStreamPrefix(GraphicsDevice graphicsDevice, Stream stream, ref Texture2D __result)
    {
        RegisteredPatch? pendingPatch = PendingRuntimeVanillaUiPatch;

        try
        {
            RegisteredPatch? patch = null;

            if (stream is FileStream fileStream)
            {
                string fullPath = Path.GetFullPath(fileStream.Name);
                lock (Lock)
                {
                    PatchesByTargetFullPath.TryGetValue(fullPath, out patch);
                }
            }
            else if (pendingPatch is not null)
            {
                patch = pendingPatch;
            }

            if (patch == null || !patch.IsGenerated)
                return true;

            if (string.Equals(patch.FromVanillaUi, "MenuBox", StringComparison.OrdinalIgnoreCase))
            {
                __result = CreateMenuBoxTexture(graphicsDevice, patch.OutputWidth, patch.OutputHeight);

                if (StaticDebugLogging)
                    StaticMonitor?.Log($"Patched runtime vanilla UI texture '{patch.LogName}': {patch.FromVanillaUi} {patch.OutputWidth}x{patch.OutputHeight}", LogLevel.Debug);

                return false;
            }
        }
        catch (Exception ex)
        {
            if (StaticDebugLogging)
                StaticMonitor?.Log($"Failed creating runtime vanilla UI texture: {ex.Message}", LogLevel.Debug);
        }
        finally
        {
            PendingRuntimeVanillaUiPatch = null;
        }

        return true;
    }

    private static Texture2D CreateMenuBoxTexture(GraphicsDevice graphicsDevice, int width, int height)
    {
        RenderTargetBinding[] previousTargets = graphicsDevice.GetRenderTargets();
        RenderTarget2D renderTarget = new(graphicsDevice, width, height, false, SurfaceFormat.Color, DepthFormat.None);
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

        return renderTarget;
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
