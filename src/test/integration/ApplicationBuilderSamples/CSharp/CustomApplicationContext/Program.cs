// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Drawing;
using System.Windows.Forms;

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
        IHost host = hostBuilder.Build();
        SampleApplicationContext context = new(recorder);

        using WinFormsApplication application = WinFormsApplication.CreateBuilder()
            .UseHost(host)
            .UseApplicationContext(context)
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

internal sealed class SampleApplicationContext : ApplicationContext
{
    public SampleApplicationContext(LifecycleRecorder recorder)
        : base(CreateMainForm(recorder))
    {
        recorder.Record("custom-context-created");
        ThreadExit += (_, _) => recorder.Record("custom-context-thread-exit");
    }

    private static Form CreateMainForm(LifecycleRecorder recorder)
    {
        recorder.Record("context-main-form-created");

        Form form = new()
        {
            Text = "Custom ApplicationContext",
            ClientSize = new Size(460, 120)
        };

        form.Controls.Add(new Label
        {
            AutoSize = true,
            Location = new Point(16, 20),
            Text = "Close this form to exit the custom context."
        });

        return form;
    }
}
