// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using System.Windows.Forms;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.WinForms;

namespace ApplicationBuilderSample.DiScopes;

/// <summary>
///  Configures and runs the DI and scope sample.
/// </summary>
internal static class Program
{
    /// <summary>
    ///  Starts the Generic Host and the WinForms application.
    /// </summary>
    /// <param name="args">Command-line arguments.</param>
    [STAThread]
    private static void Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        HostApplicationBuilder hostBuilder = Host.CreateApplicationBuilder(args);
        hostBuilder.Services.AddScoped<SessionService>();
        hostBuilder.Services.AddHostedService<ShutdownDiagnosticsService>();
        IHost host = hostBuilder.Build();

        WinFormsApplication? application = null;
        WinFormsApplicationBuilder applicationBuilder = WinFormsApplication.CreateBuilder()
            .UseHost(host)
            .UseStartupForm(services =>
            {
                WinFormsApplication currentApplication = application
                    ?? throw new InvalidOperationException("The application has not been built.");

                return ActivatorUtilities.CreateInstance<MainForm>(services, currentApplication);
            });

        using WinFormsApplication builtApplication = applicationBuilder.Build();
        application = builtApplication;
        builtApplication.Run();
    }
}

/// <summary>
///  Represents one Form activation's scoped service.
/// </summary>
internal sealed class SessionService : IDisposable
{
    private int _disposed;

    /// <summary>
    ///  Initializes a new session service and records its identity.
    /// </summary>
    public SessionService()
    {
        Id = Guid.NewGuid();
        Debug.WriteLine($"Session service {Id:N} created.");
    }

    /// <summary>
    ///  Gets the unique identity for this activation.
    /// </summary>
    public Guid Id { get; }

    /// <summary>
    ///  Gets whether this scoped service has been disposed.
    /// </summary>
    public bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    /// <summary>
    ///  Records scope disposal once.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            Debug.WriteLine($"Session service {Id:N} disposed.");
        }
    }
}

/// <summary>
///  Logs host cancellation while the WinForms application shuts down.
/// </summary>
internal sealed class ShutdownDiagnosticsService : BackgroundService
{
    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using PeriodicTimer timer = new(TimeSpan.FromSeconds(1));

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                Debug.WriteLine($"Background service heartbeat at {DateTimeOffset.Now}.");
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            Debug.WriteLine("Background service observed host shutdown.");
        }
    }
}
