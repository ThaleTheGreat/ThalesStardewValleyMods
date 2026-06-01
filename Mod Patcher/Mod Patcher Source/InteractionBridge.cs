using System.Text.Json;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Tools;

namespace ThaleTheGreat.ModPatcher;

internal sealed partial class ModEntry
{
    private readonly List<RegisteredInteractionPatch> RegisteredInteractionPatches = new();
    private readonly Dictionary<string, Dictionary<string, InteractionConfigValue>> InteractionConfigs = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> UsedInteractionKeysToday = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<PendingInteraction> PendingInteractions = new();
    private readonly Dictionary<string, Texture2D> InteractionTextures = new(StringComparer.OrdinalIgnoreCase);

    private void TryRegisterInteractionChange(IContentPack pack, AssetPatchChange change)
    {
        if (change.When is null)
        {
            this.Monitor.Log($"Ignored PatchInteraction change from {pack.Manifest.UniqueID}: When is required.", LogLevel.Warn);
            return;
        }

        if (change.Effects.Count == 0)
        {
            this.Monitor.Log($"Ignored PatchInteraction change from {pack.Manifest.UniqueID}: Effects is required.", LogLevel.Warn);
            return;
        }

        RegisteredInteractionPatch patch = new()
        {
            PackId = pack.Manifest.UniqueID,
            Name = string.IsNullOrWhiteSpace(change.Name) ? pack.Manifest.Name : change.Name.Trim(),
            Pack = pack,
            When = change.When,
            Limit = change.Limit ?? new InteractionLimit(),
            Effects = change.Effects,
            Config = change.Config
        };

        if (!this.InteractionConfigs.TryGetValue(patch.PackId, out Dictionary<string, InteractionConfigValue>? configValues))
        {
            configValues = this.LoadInteractionConfig(pack, patch.Config);
            this.InteractionConfigs[patch.PackId] = configValues;
        }

        this.RegisteredInteractionPatches.Add(patch);
        this.LogDebug($"Registered PatchInteraction patch '{patch.Name}' from {pack.Manifest.UniqueID}.");
    }

    private Dictionary<string, InteractionConfigValue> LoadInteractionConfig(IContentPack pack, List<InteractionConfigOption> options)
    {
        Dictionary<string, InteractionConfigValue> result = new(StringComparer.OrdinalIgnoreCase);
        foreach (InteractionConfigOption option in options.Where(option => !string.IsNullOrWhiteSpace(option.Key)))
        {
            string key = option.Key.Trim();
            result[key] = new InteractionConfigValue
            {
                Bool = option.Default,
                Number = Math.Clamp(option.DefaultNumber, option.Min, Math.Max(option.Min, option.Max))
            };
        }

        string path = Path.Combine(pack.DirectoryPath, "config.json");
        if (!File.Exists(path))
            return result;

        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return result;

            foreach (JsonProperty property in document.RootElement.EnumerateObject())
            {
                if (!result.TryGetValue(property.Name, out InteractionConfigValue? value))
                    continue;

                if (property.Value.ValueKind is JsonValueKind.True or JsonValueKind.False)
                    value.Bool = property.Value.GetBoolean();
                else if (property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetInt32(out int number))
                    value.Number = number;
            }
        }
        catch (Exception ex)
        {
            this.Monitor.Log($"Failed reading config.json for {pack.Manifest.UniqueID}: {ex.Message}", LogLevel.Warn);
        }

        return result;
    }

    private void SaveInteractionConfig(IContentPack pack, Dictionary<string, InteractionConfigValue> values)
    {
        try
        {
            Dictionary<string, object> plain = new(StringComparer.OrdinalIgnoreCase);
            RegisteredInteractionPatch? patch = this.RegisteredInteractionPatches.FirstOrDefault(entry => string.Equals(entry.PackId, pack.Manifest.UniqueID, StringComparison.OrdinalIgnoreCase));
            foreach ((string key, InteractionConfigValue value) in values)
            {
                InteractionConfigOption? option = patch?.Config.FirstOrDefault(entry => string.Equals(entry.Key, key, StringComparison.OrdinalIgnoreCase));
                if (option is not null && string.Equals(option.Type, "Number", StringComparison.OrdinalIgnoreCase))
                    plain[key] = value.Number;
                else
                    plain[key] = value.Bool;
            }

            string path = Path.Combine(pack.DirectoryPath, "config.json");
            string json = JsonSerializer.Serialize(plain, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
        }
        catch (Exception ex)
        {
            this.Monitor.Log($"Failed writing config.json for {pack.Manifest.UniqueID}: {ex.Message}", LogLevel.Warn);
        }
    }

    private void RegisterInteractionGmcm(IGenericModConfigMenuApi gmcm)
    {
        foreach (RegisteredInteractionPatch patch in this.RegisteredInteractionPatches.Where(patch => patch.Config.Count > 0))
        {
            Dictionary<string, InteractionConfigValue> values = this.InteractionConfigs[patch.PackId];

            gmcm.Register(
                patch.Pack.Manifest,
                reset: () =>
                {
                    foreach (InteractionConfigOption option in patch.Config.Where(option => !string.IsNullOrWhiteSpace(option.Key)))
                    {
                        values[option.Key] = new InteractionConfigValue
                        {
                            Bool = option.Default,
                            Number = Math.Clamp(option.DefaultNumber, option.Min, Math.Max(option.Min, option.Max))
                        };
                    }
                },
                save: () => this.SaveInteractionConfig(patch.Pack, values)
            );

            foreach (InteractionConfigOption option in patch.Config.Where(option => !string.IsNullOrWhiteSpace(option.Key)))
            {
                string key = option.Key.Trim();
                if (string.Equals(option.Type, "Number", StringComparison.OrdinalIgnoreCase))
                {
                    gmcm.AddNumberOption(
                        patch.Pack.Manifest,
                        getValue: () => this.GetInteractionInt(patch, key),
                        setValue: value => values[key].Number = Math.Clamp(value, option.Min, Math.Max(option.Min, option.Max)),
                        name: () => this.TranslateInteractionText(patch, option.Name),
                        tooltip: () => this.TranslateInteractionText(patch, option.Tooltip),
                        min: option.Min,
                        max: Math.Max(option.Min, option.Max),
                        interval: Math.Max(1, option.Interval),
                        fieldId: key
                    );
                    continue;
                }

                gmcm.AddBoolOption(
                    patch.Pack.Manifest,
                    getValue: () => this.GetInteractionBool(patch, key),
                    setValue: value => values[key].Bool = value,
                    name: () => this.TranslateInteractionText(patch, option.Name),
                    tooltip: () => this.TranslateInteractionText(patch, option.Tooltip),
                    fieldId: key
                );
            }
        }
    }

    private void OnInteractionButtonPressed(object? sender, ButtonPressedEventArgs e)
    {
        if (!Context.IsWorldReady || this.RegisteredInteractionPatches.Count == 0)
            return;

        foreach (RegisteredInteractionPatch patch in this.RegisteredInteractionPatches)
        {
            if (!this.MatchesInteractionEvent(patch.When, "ButtonPressed") || !this.MatchesInput(patch.When, e) || !this.MatchesInteractionState(patch))
                continue;

            if (!this.MatchesHeldTool(patch.When))
                continue;

            NPC? target = this.FindInteractionNpcTarget(patch.When, e.Cursor.GrabTile);
            if (target is null)
                continue;

            if (this.IsInteractionUsedToday(patch))
            {
                this.ApplyInteractionEffects(patch, target, patch.Effects.Where(effect => string.Equals(effect.Type, "AlreadyUsed", StringComparison.OrdinalIgnoreCase)));
                return;
            }

            if (this.IsInteractionOncePerDayEnabled(patch))
                this.MarkInteractionUsedToday(patch, "");

            Game1.player.BeginUsingTool();
            this.ApplyInteractionEffects(patch, target, patch.Effects.Where(effect => !string.Equals(effect.Type, "AlreadyUsed", StringComparison.OrdinalIgnoreCase)));
            return;
        }
    }

    private void OnInteractionWarped(object? sender, WarpedEventArgs e)
    {
        if (!Context.IsWorldReady || !ReferenceEquals(e.Player, Game1.player))
            return;

        foreach (RegisteredInteractionPatch patch in this.RegisteredInteractionPatches)
        {
            if (!this.MatchesInteractionEvent(patch.When, "Warped") || !this.MatchesInteractionState(patch, e.NewLocation))
                continue;

            if (this.IsInteractionUsedToday(patch))
                continue;

            int delay = Math.Max(0, patch.When.DelayTicks);
            this.PendingInteractions.Add(new PendingInteraction { Patch = patch, Ticks = delay });
        }
    }

    private void ProcessPendingInteractions()
    {
        if (!Context.IsWorldReady || this.PendingInteractions.Count == 0)
            return;

        if (Game1.activeClickableMenu != null || Game1.fadeToBlackAlpha > 0.01f)
            return;

        for (int i = this.PendingInteractions.Count - 1; i >= 0; i--)
        {
            PendingInteraction pending = this.PendingInteractions[i];
            if (!this.MatchesInteractionState(pending.Patch))
            {
                this.PendingInteractions.RemoveAt(i);
                continue;
            }

            if (pending.Ticks > 0)
            {
                pending.Ticks--;
                continue;
            }

            this.PendingInteractions.RemoveAt(i);
            this.ApplyInteractionEffects(pending.Patch, null, pending.Patch.Effects.Where(effect => !string.Equals(effect.Type, "AlreadyUsed", StringComparison.OrdinalIgnoreCase)));
        }
    }

    private void OnInteractionRenderedWorld(object? sender, RenderedWorldEventArgs e)
    {
        if (!Context.IsWorldReady || this.RegisteredInteractionPatches.Count == 0)
            return;

        foreach (RegisteredInteractionPatch patch in this.RegisteredInteractionPatches)
        {
            if (!this.MatchesInteractionEvent(patch.When, "RenderedWorld") || !this.MatchesInteractionState(patch))
                continue;

            foreach (InteractionEffect effect in patch.Effects.Where(effect => string.Equals(effect.Type, "DrawSprite", StringComparison.OrdinalIgnoreCase)))
                this.DrawInteractionSprite(patch, effect, e.SpriteBatch);
        }
    }

    private bool MatchesInput(InteractionWhen when, ButtonPressedEventArgs e)
    {
        return string.Equals(when.Input, "UseToolButton", StringComparison.OrdinalIgnoreCase) && e.Button.IsUseToolButton();
    }

    private bool MatchesInteractionEvent(InteractionWhen when, string eventName)
    {
        return string.Equals(string.IsNullOrWhiteSpace(when.Event) ? "ButtonPressed" : when.Event, eventName, StringComparison.OrdinalIgnoreCase);
    }

    private bool MatchesInteractionState(RegisteredInteractionPatch patch, GameLocation? location = null)
    {
        GameLocation? targetLocation = location ?? Game1.currentLocation;
        InteractionWhen when = patch.When;

        if (!string.IsNullOrWhiteSpace(when.Location))
        {
            if (targetLocation is null)
                return false;

            if (!string.Equals(targetLocation.Name, when.Location, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(targetLocation.NameOrUniqueName, when.Location, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        if (!string.IsNullOrWhiteSpace(when.TimeAtOrAfter) && Game1.timeOfDay < this.ResolveInteractionInt(patch, when.TimeAtOrAfter))
            return false;

        if (when.DayOfWeekConfig.Count > 0)
        {
            string day = GetCurrentStardewDayName();
            if (when.DayOfWeekConfig.TryGetValue(day, out string? configKey) && !this.GetInteractionBool(patch, configKey))
                return false;
        }

        return true;
    }

    private bool MatchesHeldTool(InteractionWhen when)
    {
        if (string.IsNullOrWhiteSpace(when.HeldToolType))
            return true;

        Tool? tool = Game1.player.CurrentTool;
        if (tool is null)
            return false;

        Type? wanted = AccessTools.TypeByName(when.HeldToolType);
        if (wanted is not null)
            return wanted.IsInstanceOfType(tool);

        return tool.GetType().FullName?.Equals(when.HeldToolType, StringComparison.OrdinalIgnoreCase) == true
            || tool.GetType().Name.Equals(when.HeldToolType, StringComparison.OrdinalIgnoreCase);
    }

    private NPC? FindInteractionNpcTarget(InteractionWhen when, Vector2 grabTile)
    {
        if (!string.Equals(when.Target.Type, "NPC", StringComparison.OrdinalIgnoreCase))
            return null;

        foreach (NPC character in Game1.currentLocation.characters)
        {
            if (when.Target.Names.Count > 0 && !when.Target.Names.Any(name => character.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                continue;

            if (Vector2.Distance(grabTile, character.Tile) > when.Target.MaxTileDistance)
                continue;

            return character;
        }

        return null;
    }

    private void ApplyInteractionEffects(RegisteredInteractionPatch patch, NPC? target, IEnumerable<InteractionEffect> effects)
    {
        foreach (InteractionEffect effect in effects)
        {
            if (string.Equals(effect.Type, "Branch", StringComparison.OrdinalIgnoreCase))
            {
                bool value = this.GetInteractionBool(patch, effect.Config);
                this.ApplyInteractionEffects(patch, target, value ? effect.WhenTrue : effect.WhenFalse);
                continue;
            }

            if (string.Equals(effect.Type, "ChangeFriendship", StringComparison.OrdinalIgnoreCase) && target is not null)
            {
                Game1.player.changeFriendship(effect.Amount, target);
                continue;
            }

            if (string.Equals(effect.Type, "ShowDialogue", StringComparison.OrdinalIgnoreCase) || string.Equals(effect.Type, "AlreadyUsed", StringComparison.OrdinalIgnoreCase))
            {
                Game1.drawObjectDialogue(this.ResolveInteractionText(patch, effect.Text));
                continue;
            }

            if (string.Equals(effect.Type, "AskMoneyQuestion", StringComparison.OrdinalIgnoreCase))
            {
                this.AskInteractionMoneyQuestion(patch, effect);
                continue;
            }

            if (string.Equals(effect.Type, "RememberForDay", StringComparison.OrdinalIgnoreCase))
            {
                this.MarkInteractionUsedToday(patch, effect.Key);
                continue;
            }

            if (string.Equals(effect.Type, "Warp", StringComparison.OrdinalIgnoreCase))
            {
                string location = string.IsNullOrWhiteSpace(effect.Location) ? Game1.player.currentLocation?.NameOrUniqueName ?? "Town" : effect.Location;
                Game1.warpFarmer(location, this.ResolveInteractionInt(patch, effect.TileX), this.ResolveInteractionInt(patch, effect.TileY), effect.Facing);
                continue;
            }
        }
    }

    private void AskInteractionMoneyQuestion(RegisteredInteractionPatch patch, InteractionEffect effect)
    {
        int amount = Math.Max(0, this.ResolveInteractionAmount(patch, effect));
        if (amount <= 0)
        {
            if (this.IsInteractionOncePerDayEnabled(patch))
                this.MarkInteractionUsedToday(patch, "");
            return;
        }

        Game1.currentLocation.createQuestionDialogue(
            this.ResolveInteractionText(patch, effect.Text),
            new[]
            {
                new Response(string.IsNullOrWhiteSpace(effect.PayKey) ? "Pay" : effect.PayKey, this.ResolveInteractionText(patch, effect.PayText)),
                new Response(string.IsNullOrWhiteSpace(effect.LeaveKey) ? "Leave" : effect.LeaveKey, this.ResolveInteractionText(patch, effect.LeaveText))
            },
            (Farmer who, string answer) =>
            {
                if (!string.Equals(answer, string.IsNullOrWhiteSpace(effect.PayKey) ? "Pay" : effect.PayKey, StringComparison.OrdinalIgnoreCase))
                {
                    this.ApplyInteractionEffects(patch, null, effect.OnDeclined);
                    return;
                }

                if (Game1.player.Money < amount)
                {
                    this.ApplyInteractionEffects(patch, null, effect.OnCannotPay);
                    return;
                }

                Game1.player.Money -= amount;
                this.ApplyInteractionEffects(patch, null, effect.OnPaid);
            }
        );
    }

    private void DrawInteractionSprite(RegisteredInteractionPatch patch, InteractionEffect effect, SpriteBatch spriteBatch)
    {
        string textureName = this.ResolveInteractionText(patch, effect.Texture);
        if (string.IsNullOrWhiteSpace(textureName))
            return;

        if (!this.InteractionTextures.TryGetValue(textureName, out Texture2D? texture))
        {
            try
            {
                texture = Game1.content.Load<Texture2D>(textureName);
                this.InteractionTextures[textureName] = texture;
            }
            catch (Exception ex)
            {
                this.LogDebug($"Could not load interaction texture '{textureName}': {ex.Message}");
                return;
            }
        }

        int tileX = this.ResolveInteractionInt(patch, effect.TileX);
        int tileY = this.ResolveInteractionInt(patch, effect.TileY);
        Rectangle source = new(effect.SourceX, effect.SourceY, effect.SourceWidth, effect.SourceHeight);
        Vector2 worldPosition = new(tileX * Game1.tileSize, (tileY - 1) * Game1.tileSize);
        Vector2 screenPosition = Game1.GlobalToLocal(Game1.viewport, worldPosition);
        float layerDepth = (tileY * Game1.tileSize + 64) / 10000f;

        spriteBatch.Draw(
            texture,
            screenPosition,
            source,
            Color.White,
            0f,
            Vector2.Zero,
            effect.PixelZoom ? Game1.pixelZoom : 1f,
            SpriteEffects.None,
            layerDepth
        );
    }

    private bool IsInteractionUsedToday(RegisteredInteractionPatch patch)
    {
        if (!this.IsInteractionOncePerDayEnabled(patch))
            return false;

        return this.UsedInteractionKeysToday.Contains(this.GetInteractionDayKey(patch, ""));
    }


    private bool IsInteractionOncePerDayEnabled(RegisteredInteractionPatch patch)
    {
        if (!patch.Limit.OncePerDay)
            return false;

        return string.IsNullOrWhiteSpace(patch.Limit.Config) || this.GetInteractionBool(patch, patch.Limit.Config);
    }

    private void MarkInteractionUsedToday(RegisteredInteractionPatch patch, string key)
    {
        string fullKey = this.GetInteractionDayKey(patch, key);
        this.UsedInteractionKeysToday.Add(fullKey);
    }

    private string GetInteractionDayKey(RegisteredInteractionPatch patch, string key)
    {
        string raw = !string.IsNullOrWhiteSpace(key) ? key : !string.IsNullOrWhiteSpace(patch.Limit.Key) ? patch.Limit.Key : patch.Name;
        return $"{patch.PackId}/{raw}";
    }

    private bool GetInteractionBool(RegisteredInteractionPatch patch, string key)
    {
        return this.InteractionConfigs.TryGetValue(patch.PackId, out Dictionary<string, InteractionConfigValue>? values)
            && values.TryGetValue(key, out InteractionConfigValue? value)
            && value.Bool;
    }

    private int GetInteractionInt(RegisteredInteractionPatch patch, string key)
    {
        return this.InteractionConfigs.TryGetValue(patch.PackId, out Dictionary<string, InteractionConfigValue>? values)
            && values.TryGetValue(key, out InteractionConfigValue? value)
            ? value.Number
            : 0;
    }

    private int ResolveInteractionAmount(RegisteredInteractionPatch patch, InteractionEffect effect)
    {
        if (!string.IsNullOrWhiteSpace(effect.AmountFromConfig))
            return this.GetInteractionInt(patch, effect.AmountFromConfig);

        return effect.Amount;
    }

    private int ResolveInteractionInt(RegisteredInteractionPatch patch, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return 0;

        const string configPrefix = "{{config:";
        if (value.StartsWith(configPrefix, StringComparison.OrdinalIgnoreCase) && value.EndsWith("}}", StringComparison.Ordinal))
            return this.GetInteractionInt(patch, value[configPrefix.Length..^2]);

        return int.TryParse(value, out int number) ? number : 0;
    }

    private string ResolveInteractionText(RegisteredInteractionPatch patch, string value)
    {
        string text = this.TranslateInteractionText(patch, value);
        foreach (InteractionConfigOption option in patch.Config.Where(option => !string.IsNullOrWhiteSpace(option.Key)))
        {
            string token = "{{config:" + option.Key + "}}";
            if (!text.Contains(token, StringComparison.OrdinalIgnoreCase))
                continue;

            string replacement = string.Equals(option.Type, "Number", StringComparison.OrdinalIgnoreCase)
                ? this.GetInteractionInt(patch, option.Key).ToString()
                : this.GetInteractionBool(patch, option.Key).ToString();
            text = text.Replace(token, replacement, StringComparison.OrdinalIgnoreCase);
        }

        const string timePrefix = "{{time:";
        int start = text.IndexOf(timePrefix, StringComparison.OrdinalIgnoreCase);
        while (start >= 0)
        {
            int end = text.IndexOf("}}", start, StringComparison.Ordinal);
            if (end < 0)
                break;

            string key = text[(start + timePrefix.Length)..end];
            string replacement = FormatStardewTime(this.GetInteractionInt(patch, key));
            text = text[..start] + replacement + text[(end + 2)..];
            start = text.IndexOf(timePrefix, StringComparison.OrdinalIgnoreCase);
        }

        return text;
    }

    private string TranslateInteractionText(RegisteredInteractionPatch patch, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        const string prefix = "{{i18n:";
        if (value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && value.EndsWith("}}", StringComparison.Ordinal))
        {
            string key = value[prefix.Length..^2];
            return patch.Pack.Translation.Get(key).ToString();
        }

        return value;
    }

    private static string GetCurrentStardewDayName()
    {
        return (Math.Abs(Game1.dayOfMonth - 1) % 7) switch
        {
            0 => "Monday",
            1 => "Tuesday",
            2 => "Wednesday",
            3 => "Thursday",
            4 => "Friday",
            5 => "Saturday",
            6 => "Sunday",
            _ => "Monday"
        };
    }

    private static string FormatStardewTime(int time)
    {
        int hour = time / 100;
        int minute = time % 100;
        string suffix = hour >= 12 ? "pm" : "am";
        int displayHour = hour % 12;
        if (displayHour == 0)
            displayHour = 12;

        return $"{displayHour}:{minute:00}{suffix}";
    }
}
