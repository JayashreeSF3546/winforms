// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.Configuration.UserSecrets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace System.Windows.Forms.Tests;

/// <summary>
///  Verifies opt-in desktop configuration and standard options without starting a UI or a second host lifecycle.
/// </summary>
[Collection("Sequential")]
public class WinFormsConfigurationTests
{
    private const string ConfigurationKey = "WinForms14957:Value";

    [Fact]
    public void ConfigureWinFormsConfiguration_ValidatesBuilderAndDefersConfiguration()
    {
        Assert.Throws<ArgumentNullException>(() => WinFormsHostBuilderExtensions.ConfigureWinFormsConfiguration(null!));
        IHostBuilder builder = new HostBuilder();
        bool configured = false;
        builder.ConfigureAppConfiguration(_ => configured = true);

        builder.ConfigureWinFormsConfiguration().Should().BeSameAs(builder);

        configured.Should().BeFalse();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Configuration_ContentRoot_UsesDesktopDefaultAndHonorsHostOverrides(int source)
    {
        using ConfigurationScope scope = new();
        Directory.SetCurrentDirectory(scope.Root);
        string environmentRoot = Directory.CreateDirectory(Path.Combine(scope.Root, "environment")).FullName;
        string commandLineRoot = Directory.CreateDirectory(Path.Combine(scope.Root, "command-line")).FullName;
        string explicitRoot = Directory.CreateDirectory(Path.Combine(scope.Root, "explicit")).FullName;
        if (source >= 1)
        {
            scope.SetVariable("DOTNET_CONTENTROOT", environmentRoot);
        }

        string[]? args = source >= 2 ? ["--contentRoot", commandLineRoot] : null;
        IHostBuilder builder = new HostBuilder().ConfigureWinFormsConfiguration(args);
        if (source == 3)
        {
            builder.UseContentRoot(explicitRoot);
        }

        using IHost host = builder.Build();
        var environment = host.Services.GetRequiredService<IHostEnvironment>();
        string expected = source switch
        {
            1 => environmentRoot,
            2 => commandLineRoot,
            3 => explicitRoot,
            _ => AppContext.BaseDirectory
        };
        Path.TrimEndingDirectorySeparator(environment.ContentRootPath)
            .Should().Be(Path.TrimEndingDirectorySeparator(expected));
        environment.EnvironmentName.Should().Be(Environments.Production);
    }

    [Theory]
    [InlineData(0, "host")]
    [InlineData(1, "base-json")]
    [InlineData(2, "environment-json")]
    [InlineData(3, "application-json")]
    [InlineData(4, "application-environment-json")]
    [InlineData(5, "user-secrets")]
    [InlineData(6, "environment-variable")]
    [InlineData(7, "command-line")]
    [InlineData(8, "additional-provider")]
    public void Configuration_DefaultPrecedence_PreservesEveryProviderLayer(int layer, string expected)
    {
        using ConfigurationScope scope = new();
        scope.SetVariable("DOTNET_ENVIRONMENT", Environments.Development);
        string[]? args = layer >= 7 ? [$"--{ConfigurationKey}=command-line"] : null;
        IHostBuilder builder = scope.CreateBuilder(args)
            .ConfigureHostConfiguration(configuration =>
                configuration.AddInMemoryCollection([new(ConfigurationKey, "host")]));
        if (layer >= 1)
        {
            scope.WriteJson("appsettings.json", "base-json");
        }

        if (layer >= 2)
        {
            scope.WriteJson("appsettings.Development.json", "environment-json");
        }

        if (layer >= 3)
        {
            scope.WriteJson($"{ConfigurationScope.ApplicationName}.settings.json", "application-json");
        }

        if (layer >= 4)
        {
            scope.WriteJson($"{ConfigurationScope.ApplicationName}.settings.Development.json", "application-environment-json");
        }

        if (layer >= 5)
        {
            scope.WriteSecrets("user-secrets");
        }

        if (layer >= 6)
        {
            scope.SetVariable("WinForms14957__Value", "environment-variable");
        }

        if (layer >= 8)
        {
            builder.ConfigureAppConfiguration(configuration =>
                configuration.AddInMemoryCollection([new(ConfigurationKey, "additional-provider")]));
        }

        using IHost host = builder.Build();
        var configuration = (IConfigurationRoot)host.Services.GetRequiredService<IConfiguration>();
        configuration[ConfigurationKey].Should().Be(expected);
        configuration.Providers.OfType<JsonConfigurationProvider>()
            .Select(provider => Path.GetFileName(provider.Source.Path))
            .Should().Equal(
                "appsettings.json",
                "appsettings.Development.json",
                $"{ConfigurationScope.ApplicationName}.settings.json",
                $"{ConfigurationScope.ApplicationName}.settings.Development.json",
                "secrets.json");
    }

    [Theory]
    [InlineData(null, null, "Production")]
    [InlineData("Development", null, "Development")]
    [InlineData("Development", "Staging", "Staging")]
    public void Configuration_Environment_IsSelectedBeforeEnvironmentSpecificJson(
        string? environmentVariable,
        string? commandLineEnvironment,
        string expected)
    {
        using ConfigurationScope scope = new();
        scope.SetVariable("ASPNETCORE_ENVIRONMENT", "NotDesktop");
        scope.SetVariable("DOTNET_ENVIRONMENT", environmentVariable);
        scope.WriteJson($"appsettings.{expected}.json", expected);
        string[]? args = commandLineEnvironment is null ? null : ["--environment", commandLineEnvironment];
        using IHost host = scope.CreateBuilder(args).Build();

        host.Services.GetRequiredService<IHostEnvironment>().EnvironmentName.Should().Be(expected);
        host.Services.GetRequiredService<IConfiguration>()[ConfigurationKey].Should().Be(expected);
    }

    [Theory]
    [InlineData("Production", false)]
    [InlineData("Staging", false)]
    [InlineData("Development", true)]
    public void Configuration_DefaultUserSecrets_AreDevelopmentOnly(string environment, bool included)
    {
        using ConfigurationScope scope = new();
        scope.WriteSecrets("test-value");
        using IHost host = scope.CreateBuilder().UseEnvironment(environment).Build();

        host.Services.GetRequiredService<IConfiguration>()[ConfigurationKey]
            .Should().Be(included ? "test-value" : null);
    }

    [Theory]
    [InlineData("appsettings.json")]
    [InlineData("appsettings.Production.json")]
    public void Configuration_MalformedJson_PreservesFileDiagnostics(string fileName)
    {
        using ConfigurationScope scope = new();
        string path = Path.Combine(scope.Root, fileName);
        File.WriteAllText(path, "{ malformed");

        InvalidDataException failure = Assert.Throws<InvalidDataException>(() => scope.CreateBuilder().Build());

        failure.Message.Should().Contain(path);
        failure.InnerException.Should().BeOfType<FormatException>();
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("true", true)]
    [InlineData("false", false)]
    public void Configuration_ReloadFlag_ConfiguresAllJsonProviders(string? setting, bool expected)
    {
        using ConfigurationScope scope = new();
        scope.SetVariable("DOTNET_HOSTBUILDER__RELOADCONFIGONCHANGE", setting);
        using IHost host = scope.CreateBuilder().Build();
        var configuration = (IConfigurationRoot)host.Services.GetRequiredService<IConfiguration>();

        configuration.Providers.OfType<JsonConfigurationProvider>()
            .Should().HaveCount(4)
            .And.OnlyContain(provider => provider.Source.ReloadOnChange == expected);
    }

    [Fact]
    public void Options_Reload_UpdatesMonitorAndNewScopesButNotCachedOptionsOrExistingSnapshots()
    {
        using ConfigurationScope scope = new();
        scope.WriteJson("appsettings.json", "10");
        using IHost host = scope.CreateBuilder()
            .ConfigureServices((context, services) =>
            {
                services.AddOptions<ConfigurationOptions>()
                    .Bind(context.Configuration.GetSection("WinForms14957"));
                services.AddOptions<ConfigurationOptions>("named")
                    .Bind(context.Configuration.GetSection("WinForms14957"));
            })
            .Build();
        var cached = host.Services.GetRequiredService<IOptions<ConfigurationOptions>>();
        var monitor = host.Services.GetRequiredService<IOptionsMonitor<ConfigurationOptions>>();
        using IServiceScope existingScope = host.Services.CreateScope();
        var snapshot = existingScope.ServiceProvider.GetRequiredService<IOptionsSnapshot<ConfigurationOptions>>();
        cached.Value.Value.Should().Be(10);
        snapshot.Value.Value.Should().Be(10);
        monitor.CurrentValue.Value.Should().Be(10);
        monitor.Get("named").Value.Should().Be(10);
        List<int> changes = [];
        using IDisposable? subscription = monitor.OnChange((options, _) => changes.Add(options.Value));

        scope.WriteJson("appsettings.json", "20");
        var configuration = (IConfigurationRoot)host.Services.GetRequiredService<IConfiguration>();
        configuration.Reload();

        changes.Should().NotBeEmpty().And.OnlyContain(value => value == 20);
        monitor.CurrentValue.Value.Should().Be(20);
        monitor.Get("named").Value.Should().Be(20);
        cached.Value.Value.Should().Be(10);
        snapshot.Value.Value.Should().Be(10);
        using IServiceScope newScope = host.Services.CreateScope();
        newScope.ServiceProvider.GetRequiredService<IOptionsSnapshot<ConfigurationOptions>>().Value.Value.Should().Be(20);
    }

    [Fact]
    public void Options_InvalidReload_DoesNotPublishInvalidOptionsOrPromiseLastKnownGood()
    {
        using ConfigurationScope scope = new();
        scope.WriteJson("appsettings.json", "10");
        using IHost host = scope.CreateBuilder()
            .ConfigureServices((context, services) => services.AddOptions<ConfigurationOptions>()
                .Bind(context.Configuration.GetSection("WinForms14957"))
                .Validate(options => options.Value > 0, "'WinForms14957:Value' must be greater than zero."))
            .Build();
        var monitor = host.Services.GetRequiredService<IOptionsMonitor<ConfigurationOptions>>();
        ConfigurationOptions accepted = monitor.CurrentValue;
        List<int> published = [];
        using IDisposable? subscription = monitor.OnChange((options, _) => published.Add(options.Value));
        scope.WriteJson("appsettings.json", "0");
        var configuration = (IConfigurationRoot)host.Services.GetRequiredService<IConfiguration>();

        Exception? failure = Record.Exception(configuration.Reload);

        failure.Should().NotBeNull();
        published.Should().BeEmpty();
        accepted.Value.Should().Be(10);
        Assert.Throws<OptionsValidationException>(() => monitor.CurrentValue)
            .Failures.Should().Contain("'WinForms14957:Value' must be greater than zero.");
    }

    /// <summary>
    ///  Supplies a bindable options model for configuration and reload scenarios.
    /// </summary>
    public sealed class ConfigurationOptions
    {
        public int Value { get; set; }
    }

    /// <summary>
    ///  Owns isolated deployment files and restores process configuration after each sequential test.
    /// </summary>
    private sealed class ConfigurationScope : IDisposable
    {
        private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("WinFormsConfiguration-");
        private readonly Dictionary<string, string?> _environment = [];
        private readonly string _workingDirectory = Directory.GetCurrentDirectory();

        internal static string ApplicationName
            => typeof(WinFormsConfigurationTests).Assembly.GetName().Name!;

        internal string Root => _directory.FullName;

        internal ConfigurationScope()
        {
            SetVariable("APPDATA", Path.Combine(Root, "profile"));
            SetVariable("DOTNET_ENVIRONMENT", null);
            SetVariable("DOTNET_CONTENTROOT", null);
            SetVariable("DOTNET_APPLICATIONNAME", null);
            SetVariable("DOTNET_HOSTBUILDER__RELOADCONFIGONCHANGE", "false");
            SetVariable("WinForms14957__Value", null);
        }

        internal IHostBuilder CreateBuilder(string[]? args = null)
            => new HostBuilder()
                .ConfigureWinFormsConfiguration(args)
                .UseContentRoot(Root)
                .ConfigureHostConfiguration(configuration =>
                    configuration.AddInMemoryCollection([new(HostDefaults.ApplicationKey, ApplicationName)]));

        internal void WriteJson(string name, string value)
            => File.WriteAllText(Path.Combine(Root, name), Serialize(value));

        internal void WriteSecrets(string value)
        {
            string path = PathHelper.GetSecretsPathFromSecretsId("WinForms-Configuration-Tests-14957");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, Serialize(value));
        }

        internal void SetVariable(string name, string? value)
        {
            _environment.TryAdd(name, Environment.GetEnvironmentVariable(name));
            Environment.SetEnvironmentVariable(name, value);
        }

        private static string Serialize(string value)
            => JsonSerializer.Serialize(new { WinForms14957 = new { Value = value } });

        public void Dispose()
        {
            Directory.SetCurrentDirectory(_workingDirectory);
            foreach ((string name, string? value) in _environment)
            {
                Environment.SetEnvironmentVariable(name, value);
            }

            _directory.Delete(recursive: true);
        }
    }
}
