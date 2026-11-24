// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Microsoft.Extensions.WinForms;

/// <summary>
///  Stages explicit host identity overrides without constructing a hosting environment.
/// </summary>
/// <remarks>
///  <para>
///   Null values inherit the selected host's settings. Non-null values override its host configuration
///   during application Build, before application providers are selected. These are requested values,
///   not the effective IHostEnvironment. Resolve that environment through host services after Build.
///  </para>
///  <para>
///   Access on the application builder's owning thread before Build. Retained options are unavailable
///   once the builder is consumed. The selected host creates and owns its content-root file provider.
///  </para>
/// </remarks>
public sealed class WinFormsApplicationEnvironmentOptions
{
    private readonly WinFormsApplicationBuilder _builder;
    private readonly ConfigurationBuilder _configuration = new();
    private readonly Dictionary<string, string?> _values = new(StringComparer.OrdinalIgnoreCase);

    internal WinFormsApplicationEnvironmentOptions(WinFormsApplicationBuilder builder)
    {
        _builder = builder;
        builder.UseHostConfiguration(_configuration);
    }

    /// <summary>
    ///  Gets or sets the requested environment name, or null to inherit the selected host's value.
    /// </summary>
    /// <exception cref="ArgumentException">The assigned value is empty or whitespace.</exception>
    /// <exception cref="InvalidOperationException">The application builder is consumed or belongs to another thread.</exception>
    public string? EnvironmentName
    {
        get => GetValue(HostDefaults.EnvironmentKey);
        set => SetValue(HostDefaults.EnvironmentKey, value);
    }

    /// <summary>
    ///  Gets or sets the requested application name, or null to inherit the selected host's value.
    /// </summary>
    /// <exception cref="ArgumentException">The assigned value is empty or whitespace.</exception>
    /// <exception cref="InvalidOperationException">The application builder is consumed or belongs to another thread.</exception>
    public string? ApplicationName
    {
        get => GetValue(HostDefaults.ApplicationKey);
        set => SetValue(HostDefaults.ApplicationKey, value);
    }

    /// <summary>
    ///  Gets or sets the requested content-root path, or null to inherit the selected host's value.
    /// </summary>
    /// <remarks>
    ///  <para>
    ///   Relative paths retain Generic Host resolution semantics. No directory is created, changed, or
    ///   validated by this setter; invalid or unavailable roots follow the selected host's Build behavior.
    ///  </para>
    /// </remarks>
    /// <exception cref="ArgumentException">The assigned value is empty or whitespace.</exception>
    /// <exception cref="InvalidOperationException">The application builder is consumed or belongs to another thread.</exception>
    public string? ContentRootPath
    {
        get => GetValue(HostDefaults.ContentRootKey);
        set => SetValue(HostDefaults.ContentRootKey, value);
    }

    private string? GetValue(string key)
    {
        _ = _builder.Services;

        return _values.GetValueOrDefault(key);
    }

    private void SetValue(string key, string? value)
    {
        _ = _builder.Services;
        if (value is null)
        {
            _values.Remove(key);
        }
        else
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
            _values[key] = value;
        }

        _configuration.Sources.Clear();
        if (_values.Count > 0)
        {
            _configuration.AddInMemoryCollection(_values.ToArray());
        }
    }
}
