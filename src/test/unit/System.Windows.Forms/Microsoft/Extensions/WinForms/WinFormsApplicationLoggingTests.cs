// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Debug;
using Microsoft.Extensions.WinForms;

namespace System.Windows.Forms.Tests;

/// <summary>
///  Verifies optional builder logging uses the selected host's services, filters, scopes, and ownership.
/// </summary>
public class WinFormsApplicationLoggingTests
{
    [Fact]
    public void Logging_EmptyAccess_IsStableAndPreservesHostlessBuild()
    {
        WinFormsApplicationBuilder builder = WinFormsApplication.CreateBuilder()
            .UseApplicationContext(() => new ApplicationContext());
        ILoggingBuilder logging = builder.Logging;

        builder.Logging.Should().BeSameAs(logging);
        logging.Services.Should().BeSameAs(builder.Services);
        builder.Services.Should().BeEmpty();
        using WinFormsApplication application = builder.Build();
        application.Host.Should().BeNull();
        logging.Services.IsReadOnly.Should().BeTrue();
        Assert.Throws<InvalidOperationException>(() => builder.Logging);
    }

    [Fact]
    public void Logging_RegistrationRequiresHost_AndFreezesRetainedBuilder()
    {
        WinFormsApplicationBuilder builder = WinFormsApplication.CreateBuilder()
            .UseApplicationContext(() => new ApplicationContext());
        ILoggingBuilder logging = builder.Logging;
        logging.UseWinFormsLogging().Should().BeSameAs(logging);
        Assert.Throws<InvalidOperationException>(() => builder.Build());
        logging.Services.IsReadOnly.Should().BeFalse();
        builder.UseHost(new HostBuilder());
        using WinFormsApplication application = builder.Build();

        Assert.Throws<InvalidOperationException>(() => logging.SetMinimumLevel(LogLevel.Trace));
        Assert.Throws<InvalidOperationException>(() => builder.ConfigureLogging(_ => { }));
    }

    [Fact]
    public void Logging_NullAndWrongThreadAccess_RejectWithoutRegistrations()
    {
        WinFormsApplicationBuilder missing = null!;
        Assert.Throws<ArgumentNullException>("builder", () => missing.Logging);
        Assert.Throws<ArgumentNullException>("builder", () => missing.ConfigureLogging(_ => { }));
        Assert.Throws<ArgumentNullException>("builder", () => WinFormsLoggingExtensions.UseWinFormsLogging(null!));
        WinFormsApplicationBuilder builder = WinFormsApplication.CreateBuilder()
            .UseApplicationContext(() => new ApplicationContext());
        Assert.Throws<ArgumentNullException>("configure", () => builder.ConfigureLogging(null!));
        RunOnOtherThread(() =>
        {
            Assert.Throws<InvalidOperationException>(() => builder.Logging);
            Assert.Throws<InvalidOperationException>(() => builder.ConfigureLogging(_ => { }));
        });
        builder.Services.Should().BeEmpty();
        using WinFormsApplication application = builder.Build();
        application.Host.Should().BeNull();
    }

    [Fact]
    public void ConfigureLogging_UsesSameBuilderAndPropagatesCallbackFailureWithoutConsumingApplication()
    {
        WinFormsApplicationBuilder builder = WinFormsApplication.CreateBuilder()
            .UseHost(new HostBuilder())
            .UseApplicationContext(() => new ApplicationContext());
        ILoggingBuilder logging = builder.Logging;
        InvalidOperationException expected = new("Logging configuration failed.");
        builder.ConfigureLogging(actual => actual.Should().BeSameAs(logging)).Should().BeSameAs(builder);

        Assert.Throws<InvalidOperationException>(() => builder.ConfigureLogging(actual =>
        {
            actual.SetMinimumLevel(LogLevel.Warning);
            throw expected;
        })).Should().BeSameAs(expected);

        builder.ConfigureLogging(actual => actual.UseWinFormsLogging());
        using WinFormsApplication application = builder.Build();
    }

    [Fact]
    public void Logging_PreservesHostSelectedLoggerFactory()
    {
        using ILoggerFactory factory = LoggerFactory.Create(_ => { });
        WinFormsApplicationBuilder builder = WinFormsApplication.CreateBuilder()
            .UseHost(new HostBuilder().ConfigureServices(services => services.AddSingleton(factory)))
            .UseApplicationContext(() => new ApplicationContext());
        builder.Logging.UseWinFormsLogging();
        using WinFormsApplication application = builder.Build();

        application.Host!.Services.GetRequiredService<ILoggerFactory>().Should().BeSameAs(factory);
        application.Dispose();
        factory.CreateLogger("CallerOwned").Should().NotBeNull();
    }

    [Fact]
    public void UseWinFormsLogging_RetainsExistingProvidersAndFilters_WithoutDuplicatingDebugProvider()
    {
        CapturingProvider provider = new();
        WinFormsApplicationBuilder builder = WinFormsApplication.CreateBuilder()
            .UseHost(new HostBuilder().ConfigureServices(services =>
                services.AddSingleton<ILoggerProvider>(_ => provider)))
            .UseApplicationContext(() => new ApplicationContext());
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.Logging.UseWinFormsLogging();
        builder.Logging.UseWinFormsLogging();
        using WinFormsApplication application = builder.Build();
        IServiceProvider services = application.Host!.Services;
        services.GetServices<ILoggerProvider>().OfType<DebugLoggerProvider>().Should().ContainSingle();
        var logger = services.GetRequiredService<ILogger<WinFormsApplicationLoggingTests>>();

        Write(logger, LogLevel.Information);
        Write(logger, LogLevel.Warning);

        provider.Entries.Where(entry => entry.Category == typeof(WinFormsApplicationLoggingTests).FullName)
            .Select(entry => entry.Level).Should().Equal(LogLevel.Warning);
        application.Dispose();
        provider.DisposeCount.Should().Be(1);
    }

    [Fact]
    public void Logging_ConfigurationReloadCategoryFiltersAndScopes_UseHostLoggerPipeline()
    {
        using ConfigurationManager configuration = new();
        configuration.AddInMemoryCollection([new("Logging:LogLevel:Default", "Warning")]);
        CapturingProvider provider = new();
        WinFormsApplicationBuilder builder = WinFormsApplication.CreateBuilder()
            .UseHost(new HostBuilder())
            .UseApplicationContext(() => new ApplicationContext());
        builder.Logging.AddConfiguration(configuration.GetSection("Logging"));
        builder.Logging.AddFilter<CapturingProvider>("Hidden", LogLevel.None);
        builder.Logging.Services.AddSingleton<ILoggerProvider>(_ => provider);
        using WinFormsApplication application = builder.Build();
        var factory = application.Host!.Services.GetRequiredService<ILoggerFactory>();
        ILogger visible = factory.CreateLogger("Visible");
        ILogger hidden = factory.CreateLogger("Hidden");
        Write(visible, LogLevel.Information);
        Write(hidden, LogLevel.Critical);
        provider.Entries.Should().BeEmpty();
        configuration["Logging:LogLevel:Default"] = "Trace";
        ((IConfigurationRoot)configuration).Reload();
        Dictionary<string, object?> scope = new() { ["Session"] = 17 };
        InvalidOperationException original = new("Explicit application log.");

        using (visible.BeginScope(scope))
        {
            Write(visible, LogLevel.Information, original);
            Write(hidden, LogLevel.Critical);
        }

        Entry entry = provider.Entries.Should().ContainSingle().Which;
        entry.Category.Should().Be("Visible");
        entry.EventId.Id.Should().Be(42);
        entry.Exception.Should().BeSameAs(original);
        entry.State.Should().Contain(new KeyValuePair<string, object?>("Value", 23));
        entry.Scopes.Should().ContainSingle().Which.Should().BeSameAs(scope);
    }

    [Fact]
    public void Logging_ClearProvidersRemovesBuilderProviders_NotSuppliedHostRegistrations()
    {
        CapturingProvider hostProvider = new();
        CapturingProvider builderProvider = new();
        WinFormsApplicationBuilder builder = WinFormsApplication.CreateBuilder()
            .UseHost(new HostBuilder().ConfigureServices(services =>
                services.AddSingleton<ILoggerProvider>(_ => hostProvider)))
            .UseApplicationContext(() => new ApplicationContext());
        builder.Logging.Services.AddSingleton<ILoggerProvider>(_ => builderProvider);
        builder.Logging.UseWinFormsLogging();
        builder.Logging.ClearProviders();
        using WinFormsApplication application = builder.Build();
        var logger = application.Host!.Services.GetRequiredService<ILogger<WinFormsApplicationLoggingTests>>();

        Write(logger, LogLevel.Information);

        hostProvider.Entries.Should().ContainSingle();
        builderProvider.Entries.Should().BeEmpty();
        application.Host.Services.GetServices<ILoggerProvider>().Should().ContainSingle().Which.Should().BeSameAs(hostProvider);
        application.Dispose();
        hostProvider.DisposeCount.Should().Be(1);
        builderProvider.DisposeCount.Should().Be(0);
    }

    [Fact]
    public void Logging_ProviderFailurePropagates_AndCallerOwnedProviderRemainsCallerOwned()
    {
        using CapturingProvider provider = new();
        WinFormsApplicationBuilder builder = WinFormsApplication.CreateBuilder()
            .UseHost(new HostBuilder())
            .UseApplicationContext(() => new ApplicationContext());
        builder.Logging.AddProvider(provider);
        using WinFormsApplication application = builder.Build();
        var logger = application.Host!.Services.GetRequiredService<ILogger<WinFormsApplicationLoggingTests>>();
        IOException expected = new("Provider failed.");
        provider.Failure = expected;

        Assert.Throws<AggregateException>(() => Write(logger, LogLevel.Information))
            .Flatten().InnerExceptions.Should().Contain(expected);

        provider.Failure = null;
        application.Dispose();
        provider.DisposeCount.Should().Be(0);
    }

    private static void Write(ILogger logger, LogLevel level, Exception? exception = null)
    {
        KeyValuePair<string, object?>[] state = [new("Value", 23)];
        logger.Log(level, new EventId(42, "Work"), state, exception, static (_, _) => "Work");
    }

    private static void RunOnOtherThread(Action action)
    {
        ExceptionDispatchInfo? failure = null;
        Thread thread = new(() =>
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                failure = ExceptionDispatchInfo.Capture(exception);
            }
        }) { IsBackground = true };
        thread.Start();
        thread.Join(TimeSpan.FromSeconds(30)).Should().BeTrue();
        failure?.Throw();
    }

    /// <summary>
    ///  Captures structured messages and external scopes without selecting a logging destination.
    /// </summary>
    private sealed class CapturingProvider : ILoggerProvider, ISupportExternalScope
    {
        private IExternalScopeProvider _scopes = new LoggerExternalScopeProvider();
        internal ConcurrentQueue<Entry> Entries { get; } = new();
        internal int DisposeCount { get; private set; }
        internal Exception? Failure { get; set; }

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(this, categoryName);

        public void SetScopeProvider(IExternalScopeProvider scopeProvider) => _scopes = scopeProvider;

        public void Dispose() => DisposeCount++;

        /// <summary>
        ///  Preserves category, event, exception, structured values, and the host's scope chain.
        /// </summary>
        private sealed class CapturingLogger(CapturingProvider provider, string category) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
                Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (provider.Failure is { } failure)
                {
                    ExceptionDispatchInfo.Throw(failure);
                }

                List<object?> scopes = [];
                provider._scopes.ForEachScope(static (scope, values) => values.Add(scope), scopes);
                var values = state as IEnumerable<KeyValuePair<string, object?>>;
                provider.Entries.Enqueue(new(category, logLevel, eventId, exception, values?.ToArray() ?? [], [.. scopes]));
            }
        }
    }

    /// <summary>
    ///  Records the application-visible data delivered through the configured host logging pipeline.
    /// </summary>
    private sealed record Entry(
        string Category, LogLevel Level, EventId EventId, Exception? Exception,
        KeyValuePair<string, object?>[] State, object?[] Scopes);
}
