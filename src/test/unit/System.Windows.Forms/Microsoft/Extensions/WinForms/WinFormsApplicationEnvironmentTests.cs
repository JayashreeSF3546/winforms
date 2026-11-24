// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.WinForms;

namespace System.Windows.Forms.Tests;

/// <summary>
///  Verifies explicit environment overrides reach the host before application configuration is selected.
/// </summary>
[Collection("Sequential")]
public class WinFormsApplicationEnvironmentTests
{
    [Fact]
    public void Environment_EmptyAccess_IsStableAndPreservesHostlessBuild()
    {
        WinFormsApplicationBuilder builder = CreateBuilder();
        WinFormsApplicationEnvironmentOptions options = builder.Environment;

        builder.Environment.Should().BeSameAs(options);
        options.EnvironmentName.Should().BeNull();
        options.ApplicationName.Should().BeNull();
        options.ContentRootPath.Should().BeNull();
        builder.Services.Should().BeEmpty();
        using WinFormsApplication application = builder.Build();
        application.Host.Should().BeNull();
        AssertUnavailable(builder, options);
    }

    [Fact]
    public void Environment_OverridesRequireHost_AndCanRetryBeforeConsumption()
    {
        WinFormsApplicationBuilder builder = CreateBuilder();
        builder.Environment.EnvironmentName = Environments.Development;
        Assert.Throws<InvalidOperationException>(() => builder.Build());
        builder.Environment.EnvironmentName.Should().Be(Environments.Development);
        builder.UseHost(new HostBuilder());
        using WinFormsApplication application = builder.Build();

        application.Host!.Services.GetRequiredService<IHostEnvironment>().IsDevelopment().Should().BeTrue();
    }

    [Fact]
    public void Environment_ResetAllOverridesToNull_PreservesHostlessBuild()
    {
        WinFormsApplicationBuilder builder = CreateBuilder();
        WinFormsApplicationEnvironmentOptions options = builder.Environment;
        options.EnvironmentName = Environments.Development;
        options.ApplicationName = "Desktop";
        options.ContentRootPath = AppContext.BaseDirectory;
        options.EnvironmentName = null;
        options.ApplicationName = null;
        options.ContentRootPath = null;

        options.EnvironmentName.Should().BeNull();
        options.ApplicationName.Should().BeNull();
        options.ContentRootPath.Should().BeNull();
        using WinFormsApplication application = builder.Build();
        application.Host.Should().BeNull();
    }

    [Fact]
    public void Environment_UnsetAndResetValues_InheritTheSuppliedHost()
    {
        WinFormsApplicationBuilder builder = CreateBuilder().UseHost(new HostBuilder()
            .UseEnvironment(Environments.Staging)
            .UseContentRoot(AppContext.BaseDirectory)
            .ConfigureHostConfiguration(configuration => configuration.AddInMemoryCollection(
                [new(HostDefaults.ApplicationKey, "SuppliedApplication")])));
        WinFormsApplicationEnvironmentOptions options = builder.Environment;
        options.EnvironmentName = Environments.Development;
        options.EnvironmentName = null;
        options.ApplicationName = "SelectedApplication";
        using WinFormsApplication application = builder.Build();
        var environment = application.Host!.Services.GetRequiredService<IHostEnvironment>();

        environment.IsStaging().Should().BeTrue();
        environment.ApplicationName.Should().Be("SelectedApplication");
        Path.TrimEndingDirectorySeparator(environment.ContentRootPath)
            .Should().Be(Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Environment_InvalidValues_RejectWithoutReplacingValidOverrides(string value)
    {
        WinFormsApplicationBuilder builder = CreateBuilder().UseHost(new HostBuilder());
        WinFormsApplicationEnvironmentOptions options = builder.Environment;
        options.EnvironmentName = "Selected";
        options.ApplicationName = "Desktop";
        options.ContentRootPath = AppContext.BaseDirectory;

        Assert.Throws<ArgumentException>("value", () => options.EnvironmentName = value);
        Assert.Throws<ArgumentException>("value", () => options.ApplicationName = value);
        Assert.Throws<ArgumentException>("value", () => options.ContentRootPath = value);

        options.EnvironmentName.Should().Be("Selected");
        options.ApplicationName.Should().Be("Desktop");
        options.ContentRootPath.Should().Be(AppContext.BaseDirectory);
        using WinFormsApplication application = builder.Build();
    }

    [WinFormsFact]
    public async Task Environment_NullAndWrongThreadAccess_RejectWithoutChangingTheRecipe()
    {
        WinFormsApplicationBuilder missing = null!;
        Assert.Throws<ArgumentNullException>("builder", () => missing.Environment);
        Assert.Throws<ArgumentNullException>("builder", () => missing.ConfigureEnvironment(_ => { }));
        WinFormsApplicationBuilder builder = CreateBuilder();
        Assert.Throws<ArgumentNullException>("configuration", () => builder.UseHostConfiguration(null!));
        Assert.Throws<ArgumentNullException>("configure", () => builder.ConfigureEnvironment(null!));
        WinFormsApplicationEnvironmentOptions options = builder.Environment;

        await Task.Run(() => AssertUnavailable(builder, options), TestContext.Current.CancellationToken);

        options.EnvironmentName.Should().BeNull();
        options.ApplicationName.Should().BeNull();
        options.ContentRootPath.Should().BeNull();
        using WinFormsApplication application = builder.Build();
        application.Host.Should().BeNull();
    }

    [Fact]
    public void ConfigureEnvironment_UsesSameOptions_AndPreservesEditsOnCallbackFailure()
    {
        WinFormsApplicationBuilder builder = CreateBuilder().UseHost(new HostBuilder());
        WinFormsApplicationEnvironmentOptions options = builder.Environment;
        InvalidOperationException expected = new("Environment callback failed.");
        builder.ConfigureEnvironment(actual => actual.Should().BeSameAs(options)).Should().BeSameAs(builder);

        Assert.Throws<InvalidOperationException>(() => builder.ConfigureEnvironment(actual =>
        {
            actual.EnvironmentName = "First";
            actual.ApplicationName = "Desktop";
            throw expected;
        })).Should().BeSameAs(expected);
        builder.ConfigureEnvironment(actual => actual.EnvironmentName = "Last");
        using WinFormsApplication application = builder.Build();
        var environment = application.Host!.Services.GetRequiredService<IHostEnvironment>();
        environment.EnvironmentName.Should().Be("Last");
        environment.ApplicationName.Should().Be("Desktop");
        AssertUnavailable(builder, options);
    }

    [Fact]
    public void Environment_OverridesAllHostSourcesBeforeEnvironmentSpecificJson_WithoutReselectingFromApplicationKeys()
    {
        DirectoryInfo directory = Directory.CreateTempSubdirectory("WinFormsEnvironment-");
        try
        {
            string inheritedRoot = directory.CreateSubdirectory("inherited").FullName;
            string selectedRoot = directory.CreateSubdirectory("selected").FullName;
            File.WriteAllText(Path.Combine(inheritedRoot, "appsettings.json"), "{ malformed");
            File.WriteAllText(Path.Combine(selectedRoot, "appsettings.Development.json"),
                """{"Feature":"environment"}""");
            File.WriteAllText(Path.Combine(selectedRoot, "WinFormsEnvironmentApp.settings.Development.json"),
                """{"Feature":"application-environment"}""");
            IHostEnvironment? observed = null;
            IHostBuilder hostBuilder = new HostBuilder()
                .ConfigureWinFormsConfiguration(
                    ["--environment=Production", "--applicationName=UnselectedApplication",
                     $"--contentRoot={inheritedRoot}", "--hostBuilder:reloadConfigOnChange=false"])
                .ConfigureAppConfiguration((context, _) => observed = context.HostingEnvironment);
            WinFormsApplicationBuilder builder = CreateBuilder().UseHost(hostBuilder);
            builder.Environment.EnvironmentName = Environments.Development;
            builder.Environment.ApplicationName = "WinFormsEnvironmentApp";
            builder.Environment.ContentRootPath = selectedRoot;
            hostBuilder.UseEnvironment(Environments.Production).UseContentRoot(inheritedRoot);
            builder.Configuration.AddInMemoryCollection([new(HostDefaults.EnvironmentKey, "ApplicationOnly")]);

            using WinFormsApplication application = builder.Build();
            var environment = application.Host!.Services.GetRequiredService<IHostEnvironment>();
            var configuration = application.Host.Services.GetRequiredService<IConfiguration>();

            environment.Should().BeSameAs(observed);
            environment.IsDevelopment().Should().BeTrue();
            environment.ApplicationName.Should().Be("WinFormsEnvironmentApp");
            Path.TrimEndingDirectorySeparator(environment.ContentRootPath).Should().Be(selectedRoot);
            environment.ContentRootFileProvider.GetFileInfo("appsettings.Development.json").Exists.Should().BeTrue();
            configuration["Feature"].Should().Be("application-environment");
            configuration[HostDefaults.EnvironmentKey].Should().Be("ApplicationOnly");
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void Environment_FailedHostBuild_ConsumesRetainedOptionsWithoutCreatingTheRequestedRoot()
    {
        DirectoryInfo directory = Directory.CreateTempSubdirectory("WinFormsEnvironment-");
        try
        {
            string missingRoot = Path.Combine(directory.FullName, "not-created");
            WinFormsApplicationBuilder builder = CreateBuilder().UseHost(new HostBuilder());
            WinFormsApplicationEnvironmentOptions options = builder.Environment;
            options.ContentRootPath = missingRoot;
            Directory.Exists(missingRoot).Should().BeFalse();

            Assert.Throws<DirectoryNotFoundException>(() => builder.Build());

            Directory.Exists(missingRoot).Should().BeFalse();
            Assert.Throws<InvalidOperationException>(() => builder.Build());
            AssertUnavailable(builder, options);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void UseHostConfiguration_SnapshotsExternalRecipe_AndRejectsReplacingItWithEnvironmentOptions()
    {
        ConfigurationBuilder recipe = new();
        recipe.AddInMemoryCollection([new(HostDefaults.EnvironmentKey, Environments.Staging)]);
        IHostBuilder hostBuilder = new HostBuilder().ConfigureHostConfiguration(_ =>
            recipe.AddInMemoryCollection([new(HostDefaults.EnvironmentKey, "LateRecipeEdit")]));
        WinFormsApplicationBuilder builder = CreateBuilder().UseHost(hostBuilder);
        builder.UseHostConfiguration(recipe).Should().BeSameAs(builder);
        Assert.Throws<InvalidOperationException>(() => builder.UseHostConfiguration(new ConfigurationBuilder()));
        Assert.Throws<InvalidOperationException>(() => builder.Environment);
        using WinFormsApplication application = builder.Build();

        application.Host!.Services.GetRequiredService<IHostEnvironment>().IsStaging().Should().BeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UseHostConfiguration_NonemptyRecipeRequiresHost_AndCanRetry(bool propertyOnly)
    {
        ConfigurationBuilder recipe = new();
        if (propertyOnly)
        {
            recipe.Properties["Marker"] = "value";
        }
        else
        {
            recipe.AddInMemoryCollection([new(HostDefaults.EnvironmentKey, Environments.Staging)]);
        }

        WinFormsApplicationBuilder builder = CreateBuilder().UseHostConfiguration(recipe);
        Assert.Throws<InvalidOperationException>(() => builder.Build());
        builder.UseHost(new HostBuilder());
        using WinFormsApplication application = builder.Build();
        application.Host!.Services.GetRequiredService<IHostEnvironment>().EnvironmentName
            .Should().Be(propertyOnly ? Environments.Production : Environments.Staging);
    }

    private static WinFormsApplicationBuilder CreateBuilder()
        => WinFormsApplication.CreateBuilder().UseApplicationContext(() => new ApplicationContext());

    private static void AssertUnavailable(
        WinFormsApplicationBuilder builder, WinFormsApplicationEnvironmentOptions options)
    {
        Assert.Throws<InvalidOperationException>(() => builder.Environment);
        Assert.Throws<InvalidOperationException>(() => builder.ConfigureEnvironment(_ => { }));
        Assert.Throws<InvalidOperationException>(() => builder.UseHostConfiguration(new ConfigurationBuilder()));
        Assert.Throws<InvalidOperationException>(() => options.EnvironmentName);
        Assert.Throws<InvalidOperationException>(() => options.ApplicationName);
        Assert.Throws<InvalidOperationException>(() => options.ContentRootPath);
        Assert.Throws<InvalidOperationException>(() => options.EnvironmentName = null);
        Assert.Throws<InvalidOperationException>(() => options.ApplicationName = null);
        Assert.Throws<InvalidOperationException>(() => options.ContentRootPath = null);
    }
}
