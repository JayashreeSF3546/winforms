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
        hostBuilder.Services.AddHostedService<HeartbeatService>();
        IHost host = hostBuilder.Build();

        using WinFormsApplication application = WinFormsApplication.CreateBuilder()
            .UseHost(host)
            .UseStartupForm<SampleForm>()
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

internal sealed class HeartbeatService(LifecycleRecorder recorder) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        recorder.Record("background-service-started");
        using PeriodicTimer timer = new(TimeSpan.FromSeconds(1));

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                recorder.Record("background-service-heartbeat");
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            recorder.Record("background-service-cancellation-observed");
        }
    }
}

internal sealed class SampleForm : Form
{
    public SampleForm()
    {
        Text = "BackgroundService";
        ClientSize = new Size(460, 120);
        Controls.Add(new Label
        {
            AutoSize = true,
            Location = new Point(16, 20),
            Text = "Watch lifecycle.log for worker heartbeats."
        });
    }
}
