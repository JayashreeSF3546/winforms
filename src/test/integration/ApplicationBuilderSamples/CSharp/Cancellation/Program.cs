// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Drawing;
using System.Windows.Forms;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.WinForms;

namespace ApplicationBuilderSamples;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        LifecycleRecorder recorder = new();
        recorder.Record("main-entered");

        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        HostApplicationBuilder hostBuilder = Host.CreateApplicationBuilder(args);
        hostBuilder.Services.AddSingleton(recorder);
        hostBuilder.Services.AddHostedService<CancellableService>();
        IHost host = hostBuilder.Build();
        CancellationForm form = new(recorder, host);

        using WinFormsApplication application = WinFormsApplication.CreateBuilder()
            .UseHost(host)
            .UseStartupForm(form)
            .Build();

        application.Lifetime.ApplicationStarted +=
            (_, _) => recorder.Record("application-started");
        application.Lifetime.ApplicationStopping +=
            (_, _) => recorder.Record("application-stopping");
        application.Lifetime.ApplicationStopped +=
            (_, _) => recorder.Record("application-stopped");

        recorder.Record("run-entering");
        application.Run();
        recorder.Record("run-returned");
    }
}

internal sealed class CancellableService(LifecycleRecorder recorder) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        recorder.Record("cancellable-service-started");
        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            recorder.Record("cancellation-token-observed");
        }
    }
}

internal sealed class CancellationForm : Form
{
    public CancellationForm(LifecycleRecorder recorder, IHost host)
    {
        Text = "Cancellation";
        ClientSize = new Size(460, 150);

        Controls.Add(new Label
        {
            AutoSize = true,
            Location = new Point(16, 18),
            Text = "Request host stop to cancel the background worker."
        });

        Button cancelButton = new()
        {
            AutoSize = true,
            Location = new Point(16, 58),
            Text = "Cancel worker and stop"
        };

        cancelButton.Click += async (_, _) =>
        {
            cancelButton.Enabled = false;
            recorder.Record("stop-requested-from-form");
            await host.StopAsync();
        };

        Controls.Add(cancelButton);
    }
}
