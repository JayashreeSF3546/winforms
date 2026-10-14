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
        hostBuilder.Services.AddHostedService<GracefulShutdownService>();
        IHost host = hostBuilder.Build();
        SampleForm form = new(recorder, host);

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

internal sealed class GracefulShutdownService(LifecycleRecorder recorder) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        recorder.Record("graceful-service-started");
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        recorder.Record("graceful-cleanup-started");
        await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
        recorder.Record("graceful-cleanup-completed");
    }
}

internal sealed class SampleForm : Form
{
    public SampleForm(LifecycleRecorder recorder, IHost host)
    {
        Text = "Graceful shutdown";
        ClientSize = new Size(500, 190);

        Label status = new()
        {
            AutoSize = true,
            Location = new Point(16, 18),
            Text = "UI message-loop ticks: 0"
        };

        Controls.Add(status);
        Controls.Add(new Label
        {
            AutoSize = true,
            Location = new Point(16, 44),
            Text = "Request shutdown, then move this window while cleanup runs."
        });

        Button stopButton = new()
        {
            AutoSize = true,
            Location = new Point(16, 82),
            Text = "Request graceful shutdown"
        };

        stopButton.Click += async (_, _) =>
        {
            stopButton.Enabled = false;
            recorder.Record("graceful-shutdown-requested");
            await host.StopAsync();
        };

        int tickCount = 0;
        System.Windows.Forms.Timer uiTimer = new() { Interval = 100 };
        uiTimer.Tick += (_, _) => status.Text = $"UI message-loop ticks: {++tickCount}";
        FormClosed += (_, _) => uiTimer.Dispose();
        uiTimer.Start();

        Controls.Add(stopButton);
    }
}
