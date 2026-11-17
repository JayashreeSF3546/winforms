// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Drawing;
using System.Windows.Forms;
using ApplicationBuilderAdoption.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.WinForms;

namespace ApplicationBuilderAdoption;

/// <summary>
///  Composes optional backend polling without giving background services a reference to a Control.
/// </summary>
public static class BackendSample
{
    public static int Run(string[] args)
    {
        IHostBuilder hostBuilder = new HostBuilder()
            .ConfigureHostConfiguration(config => config.AddInMemoryCollection(
                [new("hostBuilder:reloadConfigOnChange", "false")]))
            .ConfigureWinFormsConfiguration(args)
            .ConfigureLogging(logging => logging.AddDebug())
            .ConfigureServices((context, services) =>
            {
                services.AddOptions<BackendOptions>()
                    .Bind(context.Configuration.GetSection("Backend"))
                    .Validate(options => options.IntervalMilliseconds is >= 100 and <= 60000,
                        "Backend:IntervalMilliseconds must be between 100 and 60000.")
                    .Validate(options => options.OfflineDelayMilliseconds is >= 0 and <= 30000,
                        "Backend:OfflineDelayMilliseconds must be between 0 and 30000.")
                    .Validate(options => options.HasSafeEndpoint(),
                        "An online endpoint must be HTTPS (or loopback HTTP), without credentials, query or fragment.")
                    .ValidateOnStart();
                services.AddSingleton<BackendState>();
                services.AddSingleton<HttpClient>(_ => new HttpClient { Timeout = TimeSpan.FromSeconds(10) });
                if (context.Configuration.GetValue("Backend:Offline", true))
                {
                    services.AddSingleton<IBackendSource, OfflineBackendSource>();
                }
                else
                {
                    services.AddSingleton<IBackendSource, HttpBackendSource>();
                }

                services.AddHostedService<HostTrace>();
                services.AddHostedService<BackendWorker>();
                services.Configure<HostOptions>(options => options.ShutdownTimeout = Timeout.InfiniteTimeSpan);
            });
        SampleProbe? probe = null;
        BackendState? state = null;
        WinFormsApplication? application = null;
        using (application = WinFormsApplication.CreateBuilder()
            .UseHost(hostBuilder)
            .UseApplicationContext(() =>
            {
                IServiceProvider services = application!.Host!.Services;
                probe = new SampleProbe(services.GetRequiredService<IConfiguration>());
                SampleProbe.Require(probe.Scenario is "" or "smoke" or "close-pending" or "host-stop" or "stop-startup" or "unavailable",
                    "Unknown backend probe.");
                state = services.GetRequiredService<BackendState>();
                var lifetime = services.GetRequiredService<IHostApplicationLifetime>();
                BackendForm form = new(state, lifetime);
                Console.WriteLine($"BACKEND mode={(services.GetRequiredService<IOptions<BackendOptions>>().Value.Offline ? "offline" : "http")}");
                probe.OnShown(form, async () =>
                {
                    await state.ReadEntered.Task;
                    if (probe.Scenario is "smoke" or "unavailable")
                    {
                        await state.ReadCompleted.Task;
                        form.RefreshStatus();
                        SampleProbe.Require(probe.Scenario == "smoke" ? state.Succeeded : state.Failed,
                            "The backend did not publish the expected result.");
                        SampleProbe.Require(form.StatusText == state.Status, "The UI did not read the worker's snapshot.");
                        form.ClickButton();
                        SampleProbe.Require(form.ClickCount == 1, "The UI stopped responding during backend work.");
                        Console.WriteLine($"SNAPSHOT {state.Status}; UI clicks={form.ClickCount}");
                    }
                    else
                    {
                        SampleProbe.Require(!state.ReadCompleted.Task.IsCompleted, "The cancellation probe needs a pending read.");
                        Console.WriteLine("READ PENDING");
                    }

                    if (probe.Scenario == "host-stop")
                    {
                        lifetime.StopApplication();
                    }
                    else
                    {
                        form.Close();
                    }
                });

                if (probe.Scenario == "stop-startup")
                {
                    lifetime.StopApplication();
                }

                return new ApplicationContext(form);
            })
            .Build())
        {
            application.Run();
        }

        SampleProbe.Require(state is { Stopped: true, Disposed: true, Failure: null },
            "Backend work failed or outlived its provider.");
        probe!.Complete("backend: worker joined before provider disposal");

        return 0;
    }
}

/// <summary>
///  Reads snapshots on the UI thread and owns its polling timer independently of the hosted worker.
/// </summary>
internal sealed class BackendForm : Form
{
    private readonly BackendState _state;
    private readonly Label _status = new() { Dock = DockStyle.Top, Height = 50 };
    private readonly Button _click = new() { Text = "Click while backend is busy", Dock = DockStyle.Top, Height = 40 };
    private readonly Button _stop = new() { Text = "Stop host", Dock = DockStyle.Top, Height = 40 };
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 100 };

    internal BackendForm(BackendState state, IHostApplicationLifetime lifetime)
    {
        _state = state;
        Text = "Optional backend - offline by default";
        ClientSize = new Size(480, 180);
        Controls.AddRange([_stop, _click, _status]);
        _click.Click += (_, _) => _click.Text = $"UI clicks: {++ClickCount}";
        _stop.Click += (_, _) => lifetime.StopApplication();
        _timer.Tick += (_, _) => RefreshStatus();
        RefreshStatus();
        _timer.Start();
    }

    internal string StatusText => _status.Text;
    internal int ClickCount { get; private set; }
    internal void ClickButton() => _click.PerformClick();
    internal void RefreshStatus() => _status.Text = _state.Status;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Dispose();
        }

        base.Dispose(disposing);
    }
}
