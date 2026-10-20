// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;

namespace Microsoft.Extensions.WinForms;

/// <summary>
///  Adds user settings as an optional, read-only configuration provider.
/// </summary>
public static class UserSettingsConfigurationExtensions
{
    /// <summary>
    ///  Adds settings below the <c>UserSettings</c> configuration section.
    ///  When keys are specified, only those keys and their descendants are
    ///  projected.
    /// </summary>
    /// <param name="configurationBuilder">The configuration builder.</param>
    /// <param name="settingsService">The settings service to observe.</param>
    /// <param name="settingKeys">
    ///  Optional case-insensitive paths within the settings object to project.
    ///  When omitted, the complete settings object is projected.
    /// </param>
    /// <returns>The configuration builder.</returns>
    /// <exception cref="ArgumentNullException">
    ///  <paramref name="configurationBuilder"/> or <paramref name="settingsService"/> is null.
    /// </exception>
    /// <exception cref="ArgumentException">
    ///  A specified settings key is null or empty.
    /// </exception>
    public static IConfigurationBuilder AddUserSettings(
        this IConfigurationBuilder configurationBuilder,
        IUserSettingsService settingsService,
        params string[] settingKeys)
    {
        ArgumentNullException.ThrowIfNull(configurationBuilder);
        ArgumentNullException.ThrowIfNull(settingsService);
        ArgumentNullException.ThrowIfNull(settingKeys);

        if (settingKeys.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("Settings keys must not be empty.", nameof(settingKeys));
        }

        configurationBuilder.Add(new UserSettingsConfigurationSource(settingsService, [.. settingKeys]));

        return configurationBuilder;
    }

    /// <summary>
    ///  Creates a provider that projects the settings snapshot and tracks
    ///  committed changes.
    /// </summary>
    private sealed class UserSettingsConfigurationSource(
        IUserSettingsService settingsService,
        string[] settingKeys) : IConfigurationSource
    {
        /// <inheritdoc/>
        public IConfigurationProvider Build(IConfigurationBuilder builder)
            => new UserSettingsConfigurationProvider(settingsService, settingKeys);
    }

    /// <summary>
    ///  Supplies the read-only configuration projection.
    /// </summary>
    private sealed class UserSettingsConfigurationProvider(
        IUserSettingsService settingsService,
        string[] settingKeys) : ConfigurationProvider, IDisposable
    {
        private readonly HashSet<string> _settingKeys = new(settingKeys, StringComparer.OrdinalIgnoreCase);
        private int _subscribed;

        /// <inheritdoc/>
        public override void Load()
        {
            if (Interlocked.Exchange(ref _subscribed, 1) == 0)
            {
                settingsService.Changed += OnSettingsChanged;
            }

            bool loaded = false;

            try
            {
                JsonObject settings = settingsService.LoadAsync(new JsonObject()).GetAwaiter().GetResult();
                SetData(settings);
                loaded = true;
            }
            finally
            {
                if (!loaded)
                {
                    Dispose();
                }
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _subscribed, 0) != 0)
            {
                settingsService.Changed -= OnSettingsChanged;
            }
        }

        private void OnSettingsChanged(object? sender, UserSettingsChangedEventArgs e)
        {
            JsonObject settings = JsonNode.Parse(e.Settings.GetRawText())?.AsObject()
                ?? throw new InvalidDataException("The changed user settings snapshot is not a JSON object.");
            SetData(settings);
            OnReload();
        }

        private void SetData(JsonObject settings)
        {
            Dictionary<string, string?> data = new(StringComparer.OrdinalIgnoreCase);
            AddValues(settings, "UserSettings", data);
            Data = data;
        }

        private void AddValues(JsonNode? node, string path, Dictionary<string, string?> data)
        {
            switch (node)
            {
                case JsonObject objectNode:
                    foreach ((string key, JsonNode? childNode) in objectNode)
                    {
                        string childPath = $"{path}:{key}";

                        if (IsSelected(keyPath: childPath["UserSettings:".Length..]))
                        {
                            AddValues(childNode, childPath, data);
                        }
                    }

                    break;
                case JsonArray arrayNode:
                    for (int index = 0; index < arrayNode.Count; index++)
                    {
                        AddValues(arrayNode[index], $"{path}:{index}", data);
                    }

                    break;
                case JsonValue valueNode:
                    string? scalarValue = valueNode.TryGetValue(out string? stringValue)
                        ? stringValue
                        : valueNode.ToJsonString();
                    data[path] = scalarValue;
                    break;
                case null:
                    data[path] = null;
                    break;
            }
        }

        private bool IsSelected(string keyPath)
            => _settingKeys.Count == 0
                || _settingKeys.Any(key =>
                    keyPath.Equals(key, StringComparison.OrdinalIgnoreCase)
                    || keyPath.StartsWith($"{key}:", StringComparison.OrdinalIgnoreCase)
                    || key.StartsWith($"{keyPath}:", StringComparison.OrdinalIgnoreCase));
    }
}
