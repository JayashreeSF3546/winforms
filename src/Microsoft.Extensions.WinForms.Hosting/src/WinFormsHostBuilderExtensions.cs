// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Reflection;
using Microsoft.Extensions.Configuration;

namespace Microsoft.Extensions.Hosting;

/// <summary>
///  Provides opt-in desktop configuration defaults for an unbuilt Generic Host.
/// </summary>
public static class WinFormsHostBuilderExtensions
{
    /// <summary>
    ///  Adds desktop host configuration and standard application configuration providers.
    /// </summary>
    /// <param name="builder">The unbuilt host builder to configure.</param>
    /// <param name="args">Optional command-line arguments for host and application configuration.</param>
    /// <returns>The supplied host builder.</returns>
    /// <remarks>
    ///  <para>
    ///   Call once on a new host builder, before application customizations, instead of ConfigureDefaults.
    ///   The default content root is AppContext.BaseDirectory. DOTNET_ environment variables, command-line
    ///   host settings, and subsequent explicit host configuration can override it, in that order.
    ///  </para>
    ///  <para>
    ///   Application configuration loads optional JSON files, Development User Secrets, environment variables,
    ///   and command-line arguments, in that order. Additional application providers retain their registration
    ///   order. Set hostBuilder:reloadConfigOnChange to false to disable JSON change watchers.
    ///  </para>
    ///  <para>
    ///   This method does not build or start the host, register logging providers, change the service provider
    ///   factory, or subscribe to exception events. Register options and eager validation with the standard
    ///   Microsoft.Extensions.Options APIs, then pass this builder to WinFormsApplicationBuilder.UseHost.
    ///  </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is null.</exception>
    public static IHostBuilder ConfigureWinFormsConfiguration(this IHostBuilder builder, string[]? args = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder
            .UseContentRoot(AppContext.BaseDirectory)
            .ConfigureHostConfiguration(configuration =>
            {
                configuration.AddEnvironmentVariables(prefix: "DOTNET_");
                if (args is { Length: > 0 })
                {
                    configuration.AddCommandLine(args);
                }
            })
            .ConfigureAppConfiguration((context, configuration) =>
            {
                IHostEnvironment environment = context.HostingEnvironment;
                bool reloadOnChange = bool.Parse(context.Configuration["hostBuilder:reloadConfigOnChange"] ?? bool.TrueString);
                configuration
                    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: reloadOnChange)
                    .AddJsonFile($"appsettings.{environment.EnvironmentName}.json", optional: true, reloadOnChange: reloadOnChange);

                if (environment.ApplicationName is { Length: > 0 } applicationName)
                {
                    string fileName = applicationName
                        .Replace(Path.DirectorySeparatorChar, '_')
                        .Replace(Path.AltDirectorySeparatorChar, '_');
                    configuration
                        .AddJsonFile($"{fileName}.settings.json", optional: true, reloadOnChange: reloadOnChange)
                        .AddJsonFile($"{fileName}.settings.{environment.EnvironmentName}.json", optional: true, reloadOnChange: reloadOnChange);

                    if (environment.IsDevelopment())
                    {
                        AddOptionalUserSecrets(configuration, applicationName, reloadOnChange);
                    }
                }

                configuration.AddEnvironmentVariables();
                if (args is { Length: > 0 })
                {
                    configuration.AddCommandLine(args);
                }
            });
    }

    private static void AddOptionalUserSecrets(IConfigurationBuilder configuration, string applicationName, bool reloadOnChange)
    {
        Assembly applicationAssembly;
        try
        {
            applicationAssembly = Assembly.Load(new AssemblyName(applicationName));
        }
        catch (FileNotFoundException)
        {
            // Match Generic Host: an absent application assembly has no optional User Secrets source.
            return;
        }

        configuration.AddUserSecrets(applicationAssembly, optional: true, reloadOnChange: reloadOnChange);
    }
}
