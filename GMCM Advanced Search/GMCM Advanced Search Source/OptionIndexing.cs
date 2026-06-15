using StardewModdingAPI;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace GMCMAdvancedSearch;

internal sealed class GmcmOptionRecord
{
    public IManifest Mod { get; init; } = null!;
    public string ModName { get; init; } = "";
    public string UniqueId { get; init; } = "";
    public string Section { get; init; } = "";
    public string OptionName { get; init; } = "";
    public string Tooltip { get; init; } = "";
    public string FieldId { get; init; } = "";
    public string OptionType { get; init; } = "";
    public bool IsFallbackConfigKey { get; init; }

    public string PrimaryText => !string.IsNullOrWhiteSpace(OptionName) ? OptionName : FieldId;

    public string SecondaryText
    {
        get
        {
            string[] pieces = new[] { Section, Tooltip, FieldId, OptionType }
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            return string.Join("  •  ", pieces);
        }
    }

    public bool Matches(string[] terms, string author, string modName, string uniqueId)
    {
        string haystack = StripOwnerMetadata(string.Join("\n", OptionName, Tooltip, FieldId), author, modName, uniqueId);
        return terms.All(term => haystack.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0);
    }

    private static string StripOwnerMetadata(string text, params string[] values)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "";

        foreach (string value in values)
        {
            if (string.IsNullOrWhiteSpace(value))
                continue;

            text = text.Replace(value, "", StringComparison.OrdinalIgnoreCase);
        }

        return text;
    }
}

internal static class GMCMRegistryScanner
{
    private const int MaxRegistryReflectionDepth = 5;

    public static List<IManifest> GetRegisteredModsOrFallback(IModHelper helper, object gmcmApiObj, IMonitor monitor, IManifest selfManifest, bool includeContentPacks, bool debugLogging)
    {
        List<IManifest> reflected = TryGetRegisteredModsViaReflection(helper, gmcmApiObj, monitor, includeContentPacks, debugLogging);
        if (reflected.Count > 0)
            return CleanSort(reflected, selfManifest);

        if (debugLogging)
            monitor.Log("Couldn't locate GMCM registry via reflection; falling back to all loaded mods.", LogLevel.Debug);
        return CleanSort(helper.ModRegistry.GetAll()
            .Select(m => m.Manifest)
            .Where(m => m != null)
            .Where(m => includeContentPacks || helper.ModRegistry.Get(m.UniqueID)?.IsContentPack != true), selfManifest);
    }

    private static List<IManifest> TryGetRegisteredModsViaReflection(IModHelper helper, object gmcmApiObj, IMonitor monitor, bool includeContentPacks, bool debugLogging)
    {
        try
        {
            List<IManifest> best = new();
            HashSet<object> visited = new(ReferenceEqualityComparer.Instance);
            Queue<(object Obj, int Depth)> queue = new();
            Enqueue(gmcmApiObj, 0);

            while (queue.Count > 0)
            {
                var (obj, depth) = queue.Dequeue();
                if (TryExtractManyManifests(helper, obj, out IEnumerable<IManifest> manifests, includeContentPacks))
                {
                    List<IManifest> list = manifests.DistinctBy(m => m.UniqueID).ToList();
                    if (list.Count > best.Count)
                        best = list;
                }

                foreach (MemberInfo member in GetReadableMembers(obj.GetType()))
                {
                    if (!TryGetMemberValue(member, obj, out object? value))
                        continue;
                    if (value != null && !IsLeaf(value))
                        Enqueue(value, depth + 1);
                }
            }

            return best;

            void Enqueue(object? obj, int depth)
            {
                if (obj == null || depth > MaxRegistryReflectionDepth || obj is string || !visited.Add(obj))
                    return;
                queue.Enqueue((obj, depth));
            }
        }
        catch (Exception ex)
        {
            if (debugLogging)
                monitor.Log($"GMCM registry reflection failed: {ex}", LogLevel.Debug);
            return new List<IManifest>();
        }
    }

    private static bool TryExtractManyManifests(IModHelper helper, object obj, out IEnumerable<IManifest> manifests, bool includeContentPacks)
    {
        List<IManifest> found = new();

        if (obj is IDictionary dictionary)
        {
            foreach (DictionaryEntry entry in dictionary)
            {
                AddIfFound(helper, entry.Key, found, includeContentPacks);
                AddIfFound(helper, entry.Value, found, includeContentPacks);
                if (entry.Key is string key)
                    AddUid(helper, key, found, includeContentPacks);
                if (entry.Value is string value)
                    AddUid(helper, value, found, includeContentPacks);
            }
        }

        if (obj is IEnumerable enumerable && obj is not string)
        {
            foreach (object? item in enumerable)
                AddIfFound(helper, item, found, includeContentPacks);
        }

        AddIfFound(helper, obj, found, includeContentPacks);
        List<IManifest> list = found.Where(m => m != null).DistinctBy(m => m.UniqueID).ToList();
        manifests = list;
        return list.Count > 0;
    }

    private static void AddIfFound(IModHelper helper, object? obj, List<IManifest> results, bool includeContentPacks)
    {
        if (TryExtractManifest(helper, obj, out IManifest? manifest, includeContentPacks) && manifest != null)
            results.Add(manifest);
    }

    private static void AddUid(IModHelper helper, string uid, List<IManifest> results, bool includeContentPacks)
    {
        IModInfo? info = helper.ModRegistry.Get(uid);
        if (info?.Manifest == null || (!includeContentPacks && info.IsContentPack))
            return;
        results.Add(info.Manifest);
    }

    public static bool TryExtractManifest(IModHelper helper, object? obj, out IManifest? manifest, bool includeContentPacks)
    {
        manifest = null;
        if (obj == null)
            return false;

        if (obj is IManifest direct)
            return Accept(helper, direct, includeContentPacks, out manifest);

        if (obj is IModInfo modInfo && modInfo.Manifest != null)
            return Accept(helper, modInfo.Manifest, includeContentPacks, out manifest);

        Type type = obj.GetType();
        foreach (string memberName in new[] { "Manifest", "ModManifest" })
        {
            MemberInfo? member = GetMember(type, memberName);
            if (member != null && TryGetMemberValue(member, obj, out object? value) && value is IManifest m)
                return Accept(helper, m, includeContentPacks, out manifest);
        }

        foreach (string memberName in new[] { "UniqueID", "UniqueId", "ModID", "ModId", "Id", "ID" })
        {
            MemberInfo? member = GetMember(type, memberName);
            if (member == null || !TryGetMemberValue(member, obj, out object? value) || value is not string uid || string.IsNullOrWhiteSpace(uid))
                continue;
            IModInfo? info = helper.ModRegistry.Get(uid);
            if (info?.Manifest != null)
                return Accept(helper, info.Manifest, includeContentPacks, out manifest);
        }

        return false;
    }

    private static bool Accept(IModHelper helper, IManifest candidate, bool includeContentPacks, out IManifest? manifest)
    {
        manifest = null;
        IModInfo? info = helper.ModRegistry.Get(candidate.UniqueID);
        if (!includeContentPacks && info?.IsContentPack == true)
            return false;
        manifest = candidate;
        return true;
    }

    private static List<IManifest> CleanSort(IEnumerable<IManifest> manifests, IManifest selfManifest)
    {
        return manifests
            .Where(m => m != null)
            .Where(m => !string.Equals(m.UniqueID, selfManifest.UniqueID, StringComparison.OrdinalIgnoreCase))
            .DistinctBy(m => m.UniqueID)
            .OrderBy(m => m.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(m => m.UniqueID, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    internal static IEnumerable<MemberInfo> GetReadableMembers(Type type)
    {
        const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        foreach (FieldInfo field in type.GetFields(Flags))
            yield return field;
        foreach (PropertyInfo property in type.GetProperties(Flags))
        {
            if (property.CanRead && property.GetIndexParameters().Length == 0)
                yield return property;
        }
    }

    internal static bool TryGetMemberValue(MemberInfo member, object obj, out object? value)
    {
        value = null;
        try
        {
            switch (member)
            {
                case FieldInfo field:
                    value = field.GetValue(obj);
                    return true;
                case PropertyInfo property when property.CanRead && property.GetIndexParameters().Length == 0:
                    value = property.GetValue(obj);
                    return true;
            }
        }
        catch
        {
        }
        return false;
    }

    private static MemberInfo? GetMember(Type type, string name)
    {
        const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase;
        return (MemberInfo?)type.GetProperty(name, Flags) ?? type.GetField(name, Flags);
    }

    internal static bool IsLeaf(object value)
    {
        Type type = value.GetType();
        return value is string || type.IsPrimitive || value is decimal || value is DateTime || value is Delegate || type.IsEnum;
    }

    private sealed class ReferenceEqualityComparer : IEqualityComparer<object>
    {
        public static readonly ReferenceEqualityComparer Instance = new();
        public new bool Equals(object? x, object? y) => ReferenceEquals(x, y);
        public int GetHashCode(object obj) => RuntimeHelpers.GetHashCode(obj);
    }
}

internal static class OptionIndexBuilder
{
    private const int MaxReflectionDepth = 5;
    private const int MaxReflectionObjects = 3000;
    public static List<GmcmOptionRecord> Build(IModHelper helper, object gmcmApiObj, IMonitor monitor, IManifest selfManifest, ModConfig config)
    {
        List<IManifest> registeredMods = GMCMRegistryScanner.GetRegisteredModsOrFallback(helper, gmcmApiObj, monitor, selfManifest, config.IncludeContentPacks, config.DebugLogging);
        Dictionary<string, IManifest> manifestLookup = registeredMods
            .Where(m => !string.Equals(m.UniqueID, selfManifest.UniqueID, StringComparison.OrdinalIgnoreCase))
            .DistinctBy(m => m.UniqueID)
            .ToDictionary(m => m.UniqueID, m => m, StringComparer.OrdinalIgnoreCase);

        List<GmcmOptionRecord> records = new();
        records.AddRange(TryReadGmcmOptionsViaReflection(helper, gmcmApiObj, monitor, manifestLookup, config));

        if (config.IncludeConfigFileFallback)
            records.AddRange(ConfigFileOptionScanner.Scan(helper, registeredMods, config.IncludeConfigValues, selfManifest, monitor, config.DebugLogging));

        return records
            .Where(IsUseful)
            .DistinctBy(GetDedupKey, StringComparer.OrdinalIgnoreCase)
            .OrderBy(r => r.ModName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(r => r.IsFallbackConfigKey ? 1 : 0)
            .ThenBy(r => r.Section, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(r => r.PrimaryText, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static IEnumerable<GmcmOptionRecord> TryReadGmcmOptionsViaReflection(IModHelper helper, object gmcmApiObj, IMonitor monitor, Dictionary<string, IManifest> manifestLookup, ModConfig config)
    {
        HashSet<object> visited = new(ReferenceEqualityComparer.Instance);
        Queue<ScanState> queue = new();
        queue.Enqueue(new ScanState(gmcmApiObj, 0, null, ""));
        int scannedObjects = 0;

        while (queue.Count > 0)
        {
            ScanState state = queue.Dequeue();
            if (state.Depth > MaxReflectionDepth || state.Obj is string || !visited.Add(state.Obj))
                continue;
            if (++scannedObjects > MaxReflectionObjects)
            {
                if (config.DebugLogging)
                    monitor.Log($"Stopped GMCM option reflection after {MaxReflectionObjects} objects to avoid slow menu opening.", LogLevel.Debug);
                yield break;
            }

            object obj = state.Obj;
            IManifest? manifest = FindManifest(helper, obj, manifestLookup, config.IncludeContentPacks) ?? state.Manifest;
            string section = state.Section;

            if (manifest != null)
            {
                GmcmOptionRecord? record = TryCreateOptionRecord(obj, manifest, section);
                if (record != null)
                {
                    if (LooksLikeSection(record))
                        section = record.PrimaryText;
                    yield return record;
                }
            }

            foreach (MemberInfo member in GMCMRegistryScanner.GetReadableMembers(obj.GetType()))
            {
                if (!GMCMRegistryScanner.TryGetMemberValue(member, obj, out object? value) || value == null)
                    continue;

                IManifest? childManifest = FindManifest(helper, value, manifestLookup, config.IncludeContentPacks) ?? manifest;
                string childSection = section;

                if (IsTextMember(member.Name))
                {
                    string text = CoerceString(value);
                    if (!string.IsNullOrWhiteSpace(text) && LooksLikeSectionMember(member.Name))
                        childSection = text;
                }

                if (value is IDictionary dictionary)
                {
                    foreach (DictionaryEntry entry in dictionary)
                    {
                        IManifest? entryManifest = FindManifest(helper, entry.Key, manifestLookup, config.IncludeContentPacks)
                            ?? FindManifest(helper, entry.Value, manifestLookup, config.IncludeContentPacks)
                            ?? childManifest;

                        Enqueue(entry.Key, state.Depth + 1, entryManifest, childSection);
                        Enqueue(entry.Value, state.Depth + 1, entryManifest, childSection);
                    }
                    continue;
                }

                if (value is IEnumerable enumerable && value is not string)
                {
                    foreach (object? item in enumerable)
                        Enqueue(item, state.Depth + 1, childManifest, childSection);
                    continue;
                }

                Enqueue(value, state.Depth + 1, childManifest, childSection);
            }
        }

        void Enqueue(object? value, int depth, IManifest? manifest, string section)
        {
            if (value == null || depth > MaxReflectionDepth || GMCMRegistryScanner.IsLeaf(value))
                return;
            queue.Enqueue(new ScanState(value, depth, manifest, section));
        }
    }

    private static GmcmOptionRecord? TryCreateOptionRecord(object obj, IManifest manifest, string inheritedSection)
    {
        Type type = obj.GetType();
        Dictionary<string, string> strings = ReadInterestingStrings(obj);

        string optionName = Pick(strings, "Name", "DisplayName", "Label", "Text", "Title", "Header");
        string tooltip = Pick(strings, "Tooltip", "Description", "Desc", "HoverText", "HelpText");
        string fieldId = Pick(strings, "FieldId", "FieldID", "fieldId", "Id", "ID", "Key", "SettingKey");
        string section = Pick(strings, "Section", "SectionTitle", "Page", "PageId", "PageID", "Category", "Group");
        if (string.IsNullOrWhiteSpace(section))
            section = inheritedSection;

        string typeName = type.Name;
        bool looksOptionLike = LooksOptionLike(type, strings, optionName, tooltip, fieldId);
        if (!looksOptionLike)
            return null;

        if (string.IsNullOrWhiteSpace(optionName) && string.IsNullOrWhiteSpace(tooltip) && string.IsNullOrWhiteSpace(fieldId))
            return null;

        return new GmcmOptionRecord
        {
            Mod = manifest,
            ModName = manifest.Name ?? manifest.UniqueID,
            UniqueId = manifest.UniqueID,
            Section = Clean(section),
            OptionName = Clean(optionName),
            Tooltip = Clean(tooltip),
            FieldId = Clean(fieldId),
            OptionType = Clean(GuessOptionType(typeName))
        };
    }

    private static Dictionary<string, string> ReadInterestingStrings(object obj)
    {
        Dictionary<string, string> result = new(StringComparer.OrdinalIgnoreCase);
        foreach (MemberInfo member in GMCMRegistryScanner.GetReadableMembers(obj.GetType()))
        {
            if (!IsInterestingMemberName(member.Name))
                continue;
            if (!GMCMRegistryScanner.TryGetMemberValue(member, obj, out object? value) || value == null)
                continue;
            string text = CoerceString(value);
            if (!string.IsNullOrWhiteSpace(text) && text.Length <= 500)
                result[member.Name] = text;
        }
        return result;
    }

    private static string CoerceString(object value)
    {
        try
        {
            if (value is string text)
                return text;

            if (value is Func<string> stringFunc)
                return stringFunc() ?? "";

            Type type = value.GetType();
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Func<>) && type.GetGenericArguments()[0] == typeof(string))
                return value is Delegate del ? del.DynamicInvoke() as string ?? "" : "";
        }
        catch
        {
            return "";
        }

        return "";
    }

    private static IManifest? FindManifest(IModHelper helper, object? value, Dictionary<string, IManifest> manifestLookup, bool includeContentPacks)
    {
        if (value == null)
            return null;

        if (GMCMRegistryScanner.TryExtractManifest(helper, value, out IManifest? manifest, includeContentPacks) && manifest != null && manifestLookup.ContainsKey(manifest.UniqueID))
            return manifest;

        if (value is string uid && manifestLookup.TryGetValue(uid, out IManifest? byUid))
            return byUid;

        return null;
    }

    private static bool LooksOptionLike(Type type, Dictionary<string, string> strings, string optionName, string tooltip, string fieldId)
    {
        string typeName = type.Name;
        if (typeName.Contains("Option", StringComparison.OrdinalIgnoreCase)
            || typeName.Contains("Field", StringComparison.OrdinalIgnoreCase)
            || typeName.Contains("Complex", StringComparison.OrdinalIgnoreCase))
            return true;

        if (!string.IsNullOrWhiteSpace(fieldId) && (!string.IsNullOrWhiteSpace(optionName) || !string.IsNullOrWhiteSpace(tooltip)))
            return true;

        return strings.Keys.Any(k => LooksLikeOptionMember(k)) && (!string.IsNullOrWhiteSpace(optionName) || !string.IsNullOrWhiteSpace(tooltip));
    }

    private static bool LooksLikeSection(GmcmOptionRecord record)
    {
        return record.OptionType.Contains("Section", StringComparison.OrdinalIgnoreCase)
            || record.OptionType.Contains("Paragraph", StringComparison.OrdinalIgnoreCase)
            || record.Section.Equals("Section", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsUseful(GmcmOptionRecord record)
    {
        if (string.IsNullOrWhiteSpace(record.UniqueId))
            return false;
        if (record.OptionType.Equals("Mod", StringComparison.OrdinalIgnoreCase)
            || record.OptionType.Contains("Page", StringComparison.OrdinalIgnoreCase)
            || record.OptionType.Contains("Section", StringComparison.OrdinalIgnoreCase)
            || record.OptionType.Contains("Paragraph", StringComparison.OrdinalIgnoreCase))
            return false;

        if (string.IsNullOrWhiteSpace(record.OptionName)
            && string.IsNullOrWhiteSpace(record.Tooltip)
            && string.IsNullOrWhiteSpace(record.FieldId))
            return false;

        return true;
    }

    private static string GetDedupKey(GmcmOptionRecord r)
    {
        return string.Join("|", r.UniqueId, r.Section, r.OptionName, r.Tooltip, r.FieldId, r.OptionType, r.IsFallbackConfigKey);
    }

    private static string Pick(Dictionary<string, string> strings, params string[] names)
    {
        foreach (string name in names)
        {
            if (strings.TryGetValue(name, out string? value) && !string.IsNullOrWhiteSpace(value))
                return value;
        }

        foreach (string name in names)
        {
            KeyValuePair<string, string> fuzzy = strings.FirstOrDefault(p => p.Key.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0);
            if (!string.IsNullOrWhiteSpace(fuzzy.Value))
                return fuzzy.Value;
        }

        return "";
    }

    private static string GuessOptionType(string typeName)
    {
        if (typeName.EndsWith("Option", StringComparison.OrdinalIgnoreCase))
            return typeName[..^6];
        return typeName;
    }

    private static bool IsInterestingMemberName(string name)
    {
        return IsTextMember(name) || LooksLikeOptionMember(name);
    }

    private static bool IsTextMember(string name)
    {
        return name.Contains("Name", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Text", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Title", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Label", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Tooltip", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Description", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Desc", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Help", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeOptionMember(string name)
    {
        return name.Contains("Field", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Option", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Setting", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Section", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Page", StringComparison.OrdinalIgnoreCase)
            || name.Equals("Id", StringComparison.OrdinalIgnoreCase)
            || name.Equals("ID", StringComparison.OrdinalIgnoreCase)
            || name.Equals("Key", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeSectionMember(string name)
    {
        return name.Contains("Section", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Header", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Title", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Page", StringComparison.OrdinalIgnoreCase);
    }

    private static string Clean(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";
        value = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        while (value.Contains("  ", StringComparison.Ordinal))
            value = value.Replace("  ", " ");
        return value.Length <= 240 ? value : value[..240] + "…";
    }

    private sealed record ScanState(object Obj, int Depth, IManifest? Manifest, string Section);

    private sealed class ReferenceEqualityComparer : IEqualityComparer<object>
    {
        public static readonly ReferenceEqualityComparer Instance = new();
        public new bool Equals(object? x, object? y) => ReferenceEquals(x, y);
        public int GetHashCode(object obj) => RuntimeHelpers.GetHashCode(obj);
    }
}

internal static class ConfigFileOptionScanner
{
    public static IEnumerable<GmcmOptionRecord> Scan(IModHelper helper, IEnumerable<IManifest> manifests, bool includeValues, IManifest selfManifest, IMonitor monitor, bool debugLogging)
    {
        List<GmcmOptionRecord> results = new();

        foreach (IManifest manifest in manifests)
        {
            if (string.Equals(manifest.UniqueID, selfManifest.UniqueID, StringComparison.OrdinalIgnoreCase))
                continue;

            IModInfo? info = helper.ModRegistry.Get(manifest.UniqueID);
            string? directory = TryGetDirectoryPath(info, manifest);
            if (string.IsNullOrWhiteSpace(directory))
                continue;

            string path = Path.Combine(directory, "config.json");
            if (!File.Exists(path))
                continue;

            JsonDocument? document = null;
            try
            {
                document = JsonDocument.Parse(File.ReadAllText(path));
                foreach (GmcmOptionRecord record in WalkElement(document.RootElement, manifest, "", includeValues))
                    results.Add(record);
            }
            catch (Exception ex)
            {
                if (debugLogging)
                    monitor.Log($"Could not scan config.json for {manifest.UniqueID}: {ex.Message}", LogLevel.Trace);
            }
            finally
            {
                document?.Dispose();
            }
        }

        return results;
    }

    private static string? TryGetDirectoryPath(IModInfo? info, IManifest manifest)
    {
        object?[] candidates = { info, manifest };
        string[] names = { "DirectoryPath", "Directory", "ModPath", "Path" };

        foreach (object? candidate in candidates)
        {
            if (candidate == null)
                continue;

            Type type = candidate.GetType();
            foreach (string name in names)
            {
                PropertyInfo? property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (property != null && property.PropertyType == typeof(string) && property.CanRead)
                {
                    try
                    {
                        string? value = property.GetValue(candidate) as string;
                        if (!string.IsNullOrWhiteSpace(value) && Directory.Exists(value))
                            return value;
                    }
                    catch
                    {
                    }
                }

                FieldInfo? field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field != null && field.FieldType == typeof(string))
                {
                    try
                    {
                        string? value = field.GetValue(candidate) as string;
                        if (!string.IsNullOrWhiteSpace(value) && Directory.Exists(value))
                            return value;
                    }
                    catch
                    {
                    }
                }
            }
        }

        return null;
    }

    private static IEnumerable<GmcmOptionRecord> WalkElement(JsonElement element, IManifest manifest, string path, bool includeValues)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    string childPath = string.IsNullOrWhiteSpace(path) ? property.Name : $"{path}.{property.Name}";
                    if (property.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                    {
                        foreach (GmcmOptionRecord nested in WalkElement(property.Value, manifest, childPath, includeValues))
                            yield return nested;
                    }
                    else
                    {
                        string valueText = includeValues ? GetValueText(property.Value) : "";
                        yield return new GmcmOptionRecord
                        {
                            Mod = manifest,
                            ModName = manifest.Name ?? manifest.UniqueID,
                            UniqueId = manifest.UniqueID,
                            Section = "config.json",
                            OptionName = property.Name,
                            Tooltip = valueText,
                            FieldId = childPath,
                            OptionType = property.Value.ValueKind.ToString(),
                            IsFallbackConfigKey = true
                        };
                    }
                }
                break;

            case JsonValueKind.Array:
                int index = 0;
                foreach (JsonElement child in element.EnumerateArray())
                {
                    foreach (GmcmOptionRecord nested in WalkElement(child, manifest, $"{path}[{index}]", includeValues))
                        yield return nested;
                    index++;
                }
                break;
        }
    }

    private static string GetValueText(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString() ?? "",
            JsonValueKind.Number => element.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null => "null",
            _ => ""
        };
    }
}
