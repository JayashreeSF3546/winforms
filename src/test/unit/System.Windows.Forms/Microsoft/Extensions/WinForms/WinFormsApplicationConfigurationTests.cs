// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.WinForms;

namespace System.Windows.Forms.Tests;

/// <summary>
///  Verifies staged provider recipes compose with the selected host without creating a separate root.
/// </summary>
public class WinFormsApplicationConfigurationTests
{
    [Fact]
    public void Configuration_EmptyAccess_IsStableAndPreservesHostlessBuild()
    {
        WinFormsApplicationBuilder builder = CreateBuilder();
        IConfigurationBuilder configuration = builder.Configuration;

        builder.Configuration.Should().BeSameAs(configuration);
        configuration.Sources.Should().BeEmpty();
        configuration.Properties.Should().BeEmpty();
        builder.Services.Should().BeEmpty();
        using WinFormsApplication application = builder.Build();
        application.Host.Should().BeNull();
        Assert.Throws<InvalidOperationException>(() => builder.Configuration);
        Assert.Throws<InvalidOperationException>(() => builder.ConfigureConfiguration(_ => { }));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Configuration_NonemptyRecipeRequiresHost_AndCanRetryBeforeConsumption(bool propertyOnly)
    {
        WinFormsApplicationBuilder builder = CreateBuilder();
        IConfigurationBuilder configuration = builder.Configuration;
        if (propertyOnly)
        {
            configuration.Properties["Marker"] = "value";
        }
        else
        {
            configuration.AddInMemoryCollection([new("Feature:Value", "10")]);
        }

        Assert.Throws<InvalidOperationException>(() => builder.Build());
        builder.UseHost(new HostBuilder());
        using WinFormsApplication application = builder.Build();
        var effective = application.Host!.Services.GetRequiredService<IConfiguration>();
        effective["Feature:Value"].Should().Be(propertyOnly ? null : "10");
    }

    [WinFormsFact]
    public async Task Configuration_NullConsumedAndWrongThreadAccess_RejectWithoutLoadingProviders()
    {
        WinFormsApplicationBuilder missing = null!;
        Assert.Throws<ArgumentNullException>("builder", () => missing.Configuration);
        Assert.Throws<ArgumentNullException>("builder", () => missing.ConfigureConfiguration(_ => { }));
        WinFormsApplicationBuilder builder = CreateBuilder();
        Assert.Throws<ArgumentNullException>("configuration", () => builder.UseConfiguration(null!));
        Assert.Throws<ArgumentNullException>("configure", () => builder.ConfigureConfiguration(null!));
        await Task.Run(() =>
        {
            Assert.Throws<InvalidOperationException>(() => builder.Configuration);
            Assert.Throws<InvalidOperationException>(() => builder.UseConfiguration(new ConfigurationBuilder()));
            Assert.Throws<InvalidOperationException>(() => builder.ConfigureConfiguration(_ => { }));
        }, TestContext.Current.CancellationToken);

        using WinFormsApplication application = builder.Build();
        application.Host.Should().BeNull();
        Assert.Throws<InvalidOperationException>(() => builder.UseConfiguration(new ConfigurationBuilder()));
    }

    [Fact]
    public void ConfigureConfiguration_UsesSameRecipe_AndPreservesEditsOnCallbackFailure()
    {
        WinFormsApplicationBuilder builder = CreateBuilder().UseHost(new HostBuilder());
        IConfigurationBuilder configuration = builder.Configuration;
        InvalidOperationException expected = new("Configuration callback failed.");
        builder.ConfigureConfiguration(actual => actual.Should().BeSameAs(configuration)).Should().BeSameAs(builder);

        Assert.Throws<InvalidOperationException>(() => builder.ConfigureConfiguration(actual =>
        {
            actual.AddInMemoryCollection([new("Feature:Value", "10")]);
            throw expected;
        })).Should().BeSameAs(expected);
        using WinFormsApplication application = builder.Build();
        application.Host!.Services.GetRequiredService<IConfiguration>()["Feature:Value"].Should().Be("10");
    }

    [Fact]
    public void UseConfiguration_RejectsMultipleSelections_WithoutReplacingExistingRecipe()
    {
        ConfigurationBuilder configuration = new();
        configuration.AddInMemoryCollection([new("Feature:Value", "10")]);
        WinFormsApplicationBuilder builder = CreateBuilder().UseHost(new HostBuilder());
        builder.UseConfiguration(configuration).Should().BeSameAs(builder);

        Assert.Throws<InvalidOperationException>(() => builder.UseConfiguration(new ConfigurationBuilder()));
        Assert.Throws<InvalidOperationException>(() => builder.Configuration);
        using WinFormsApplication application = builder.Build();
        application.Host!.Services.GetRequiredService<IConfiguration>()["Feature:Value"].Should().Be("10");
    }

    [Fact]
    public void Configuration_StagesSourcesAndProperties_AfterHostCallbacksBeforeServices()
    {
        TrackingSource source = new();
        ConfigurationBuilder configuration = new();
        configuration.Properties["Marker"] = "staged";
        configuration.Add(source);
        bool activated = false;
        IConfiguration? serviceConfiguration = null;
        IHostBuilder hostBuilder = new HostBuilder().ConfigureAppConfiguration((_, target) =>
        {
            target.Properties["Marker"] = "host";
            target.AddInMemoryCollection([new("Feature:Value", "host")]);
            configuration.AddInMemoryCollection([new("Feature:Late", "not in snapshot")]);
        }).ConfigureServices((context, services) =>
        {
            source.BuildCount.Should().Be(1);
            context.Configuration["Feature:Value"].Should().Be("10");
            serviceConfiguration = context.Configuration;
            services.AddOptions<WinFormsConfigurationTests.ConfigurationOptions>()
                .Bind(context.Configuration.GetSection("Feature"));
        });
        WinFormsApplicationBuilder builder = WinFormsApplication.CreateBuilder()
            .UseApplicationContext(() =>
            {
                activated = true;
                return new ApplicationContext();
            })
            .UseHost(hostBuilder)
            .UseConfiguration(configuration);

        source.BuildCount.Should().Be(0);
        using WinFormsApplication application = builder.Build();
        var effective = application.Host!.Services.GetRequiredService<IConfiguration>();
        effective.Should().BeSameAs(serviceConfiguration);
        effective["Feature:Value"].Should().Be("10");
        effective["Feature:Late"].Should().BeNull();
        application.Host.Services.GetRequiredService<IOptions<WinFormsConfigurationTests.ConfigurationOptions>>()
            .Value.Value.Should().Be(10);
        source.Marker.Should().Be("staged");
        activated.Should().BeFalse();
        configuration.AddInMemoryCollection([new("Feature:Value", "later recipe")]);
        effective["Feature:Value"].Should().Be("10");
        application.Dispose();
        source.Provider!.Disposed.Should().BeTrue();
    }

    [Fact]
    public void Configuration_ClearSources_RemovesOnlyStagedProviders()
    {
        TrackingSource removed = new();
        WinFormsApplicationBuilder builder = CreateBuilder().UseHost(new HostBuilder()
            .ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
                [new("Feature:Value", "host")])));
        builder.Configuration.Add(removed);
        builder.Configuration.Sources.Clear();
        using WinFormsApplication application = builder.Build();

        application.Host!.Services.GetRequiredService<IConfiguration>()["Feature:Value"].Should().Be("host");
        removed.BuildCount.Should().Be(0);
    }

    [Fact]
    public void Configuration_HostRelativeJsonAndReload_UpdateTheHostOptionsMonitor()
    {
        DirectoryInfo directory = Directory.CreateTempSubdirectory("WinFormsBuilderConfiguration-");
        try
        {
            string path = Path.Combine(directory.FullName, "feature.json");
            File.WriteAllText(path, """{"Feature":{"Value":10}}""");
            WinFormsApplicationBuilder builder = CreateBuilder().UseHost(new HostBuilder()
                .UseContentRoot(directory.FullName)
                .ConfigureServices((context, services) => services
                    .AddOptions<WinFormsConfigurationTests.ConfigurationOptions>()
                    .Bind(context.Configuration.GetSection("Feature"))));
            builder.Configuration.AddJsonFile("feature.json", optional: false, reloadOnChange: false);
            using WinFormsApplication application = builder.Build();
            var monitor = application.Host!.Services
                .GetRequiredService<IOptionsMonitor<WinFormsConfigurationTests.ConfigurationOptions>>();
            monitor.CurrentValue.Value.Should().Be(10);
            File.WriteAllText(path, """{"Feature":{"Value":20}}""");

            var effective = (IConfigurationRoot)application.Host.Services.GetRequiredService<IConfiguration>();
            effective.Reload();

            monitor.CurrentValue.Value.Should().Be(20);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Configuration_InvalidRequiredJson_PropagatesAndConsumesBuilder(bool malformed)
    {
        DirectoryInfo directory = Directory.CreateTempSubdirectory("WinFormsBuilderConfiguration-");
        try
        {
            string path = Path.Combine(directory.FullName, "feature.json");
            if (malformed)
            {
                File.WriteAllText(path, "{ malformed");
            }

            WinFormsApplicationBuilder builder = CreateBuilder().UseHost(new HostBuilder()
                .UseContentRoot(directory.FullName));
            builder.ConfigureConfiguration(configuration =>
                configuration.AddJsonFile("feature.json", optional: false, reloadOnChange: false));

            Exception failure = malformed
                ? Assert.Throws<InvalidDataException>(() => builder.Build())
                : Assert.Throws<FileNotFoundException>(() => builder.Build());

            failure.Message.Should().Contain("feature.json");
            Assert.Throws<InvalidOperationException>(() => builder.Build());
            Assert.Throws<InvalidOperationException>(() => builder.Configuration);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private static WinFormsApplicationBuilder CreateBuilder()
        => WinFormsApplication.CreateBuilder().UseApplicationContext(() => new ApplicationContext());

    /// <summary>
    ///  Observes deferred provider construction, forwarded metadata, and effective host ownership.
    /// </summary>
    private sealed class TrackingSource : IConfigurationSource
    {
        internal int BuildCount { get; private set; }
        internal object? Marker { get; private set; }
        internal TrackingProvider? Provider { get; private set; }

        public IConfigurationProvider Build(IConfigurationBuilder builder)
        {
            BuildCount++;
            builder.Properties.TryGetValue("Marker", out object? marker);
            Marker = marker;
            Provider = new TrackingProvider();

            return Provider;
        }
    }

    /// <summary>
    ///  Supplies a value without external resources and records host-managed disposal.
    /// </summary>
    private sealed class TrackingProvider : ConfigurationProvider, IDisposable
    {
        internal bool Disposed { get; private set; }

        public override void Load()
            => Data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase) { ["Feature:Value"] = "10" };

        public void Dispose() => Disposed = true;
    }
}
