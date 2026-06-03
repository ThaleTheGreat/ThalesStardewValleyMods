using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;

namespace ThaleTheGreat.UniversalUIMoreStats;

internal sealed class ModEntry : Mod
{
    private static readonly object Lock = new();
    private static readonly Dictionary<string, RegisteredPatch> PatchesByTargetFullPath = new(StringComparer.OrdinalIgnoreCase);
    private static IMonitor? StaticMonitor;
    private static string? CachedMenuBoxSignature;
    private string GeneratedAssetRoot = "";

    public override void Entry(IModHelper helper)
    {
        StaticMonitor = this.Monitor;
        this.GeneratedAssetRoot = Path.Combine(helper.DirectoryPath, "generated");
        this.RegisterPatches();
        this.ApplyHarmonyPatches();
    }

    private void RegisterPatches()
    {
        this.RegisterFilePatch("Xan.MoreStats", "Assets/InfoTab.png", "assets/InfoTab.png", "Universal UI Recolor for More Stats");
    }

    private void RegisterFilePatch(string targetMod, string targetPath, string sourceRelativePath, string logName)
    {
        string? targetDirectory = this.FindInstalledModDirectory(targetMod);
        if (targetDirectory is null)
            return;

        string targetFullPath = Path.GetFullPath(Path.Combine(targetDirectory, NormalizeForPlatform(targetPath)));
        if (!IsInsideDirectory(targetDirectory, targetFullPath))
            return;

        string sourcePath = Path.GetFullPath(Path.Combine(this.Helper.DirectoryPath, NormalizeForPlatform(sourceRelativePath)));
        lock (Lock)
        {
            PatchesByTargetFullPath[targetFullPath] = new RegisteredPatch(logName, targetMod, targetPath, sourcePath, "", 0, 0);
        }
    }

    private void RegisterGeneratedMenuBoxPatch(string targetMod, string targetPath, int outputWidth, int outputHeight, string logName)
    {
        string? targetDirectory = this.FindInstalledModDirectory(targetMod);
        if (targetDirectory is null)
            return;

        string targetFullPath = Path.GetFullPath(Path.Combine(targetDirectory, NormalizeForPlatform(targetPath)));
        if (!IsInsideDirectory(targetDirectory, targetFullPath))
            return;

        string sourcePath = Path.Combine(this.GeneratedAssetRoot, SanitizeFileName(targetMod), BuildGeneratedFileName(this.ModManifest.UniqueID, targetMod, targetPath, "MenuBox", outputWidth, outputHeight));
        lock (Lock)
        {
            PatchesByTargetFullPath[targetFullPath] = new RegisteredPatch(logName, targetMod, targetPath, sourcePath, "MenuBox", outputWidth, outputHeight);
        }
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
        JsonDocumentOptions options = new() { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip };
        foreach (string manifestPath in Directory.EnumerateFiles(modsRoot, "manifest.json", SearchOption.AllDirectories))
        {
            try
            {
                using FileStream stream = File.OpenRead(manifestPath);
                using JsonDocument document = JsonDocument.Parse(stream, options);
                if (document.RootElement.TryGetProperty("UniqueID", out JsonElement idElement) && string.Equals(idElement.GetString(), uniqueId, StringComparison.OrdinalIgnoreCase))
                    return Path.GetDirectoryName(manifestPath);
            }
            catch { }
        }
        return null;
    }

    private void ApplyHarmonyPatches()
    {
        Harmony harmony = new(this.ModManifest.UniqueID);
        MethodInfo? postfix = AccessTools.Method(typeof(ModEntry), nameof(FileLookupGetFilePostfix));
        foreach (string typeName in new[] { "StardewModdingAPI.Toolkit.Utilities.PathLookups.CaseInsensitiveFileLookup", "StardewModdingAPI.Toolkit.Utilities.PathLookups.MinimalFileLookup" })
        {
            Type? lookupType = AccessTools.TypeByName(typeName);
            MethodInfo? getFile = lookupType is not null ? AccessTools.Method(lookupType, "GetFile", new[] { typeof(string) }) : null;
            if (getFile is not null && getFile.ReturnType == typeof(FileInfo))
                harmony.Patch(getFile, postfix: new HarmonyMethod(postfix));
        }
    }

    private static void FileLookupGetFilePostfix(string relativePath, ref FileInfo __result)
    {
        try
        {
            if (__result is null)
                return;
            string fullPath = Path.GetFullPath(__result.FullName);
            RegisteredPatch? patch;
            lock (Lock)
            {
                if (!PatchesByTargetFullPath.TryGetValue(fullPath, out patch))
                    return;
            }
        }
        catch { }
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
            IClickableMenu.drawTextureBox(spriteBatch, Game1.menuTexture, new Rectangle(0, 256, 60, 60), 0, 0, width, height, Color.White, 1f, false);
            spriteBatch.End();
        }
        finally
        {
            graphicsDevice.SetRenderTargets(previousTargets);
        }
        using FileStream stream = File.Create(outputPath);
        renderTarget.SaveAsPng(stream, width, height);
    }

    private static string BuildGeneratedFileName(string modId, string targetMod, string targetPath, string sourceKind, int width, int height)
    {
        return $"{SanitizeFileName(targetMod)}_{BuildStableHash($"{modId}|{targetMod}|{targetPath}|{sourceKind}|{width}|{height}")}.png";
    }

    private static string BuildGeneratedFileName(RegisteredPatch patch)
    {
        return $"{SanitizeFileName(patch.TargetMod)}_{BuildStableHash($"{patch.TargetMod}|{patch.TargetPath}|{patch.FromVanillaUi}|{patch.OutputWidth}|{patch.OutputHeight}|{GetMenuBoxSignature()}")}.png";
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
            CachedMenuBoxSignature = Convert.ToHexString(SHA256.HashData(bytes), 0, 8);
        }
        catch { CachedMenuBoxSignature = "MenuBox"; }
        return CachedMenuBoxSignature;
    }

    private static string BuildStableHash(string text)
    {
        byte[] data = Encoding.UTF8.GetBytes(text.ToUpperInvariant());
        return Convert.ToHexString(SHA256.HashData(data), 0, 8);
    }

    private static string SanitizeFileName(string value)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        return new string(value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
    }

    private static string NormalizeForPlatform(string path) => path.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);

    private static bool IsInsideDirectory(string directory, string path)
    {
        string root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string fullPath = Path.GetFullPath(path);
        return fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class RegisteredPatch
    {
        public RegisteredPatch(string logName, string targetMod, string targetPath, string sourcePath, string fromVanillaUi, int outputWidth, int outputHeight)
        {
            this.LogName = logName;
            this.TargetMod = targetMod;
            this.TargetPath = targetPath;
            this.SourcePath = sourcePath;
            this.FromVanillaUi = fromVanillaUi;
            this.OutputWidth = outputWidth;
            this.OutputHeight = outputHeight;
        }
        public string LogName { get; }
        public string TargetMod { get; }
        public string TargetPath { get; }
        public string SourcePath { get; set; }
        public string FromVanillaUi { get; }
        public int OutputWidth { get; }
        public int OutputHeight { get; }
        public bool IsGenerated => !string.IsNullOrWhiteSpace(this.FromVanillaUi);
    }
}
