// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Drawing;
using System.Net;
using System.Windows.Forms;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.WinForms;

namespace ApplicationBuilderSample.Backend.CSharp;

/// <summary>
///  Defines structured log messages for backend polling.
/// </summary>
internal static class BackendLog
{
    private static readonly Action<ILogger, int, Exception?> s_checkSucceeded =
        LoggerMessage.Define<int>(
            LogLevel.Information,
            new EventId(1, "HealthCheckSucceeded"),
            "Backend health check succeeded with HTTP status {StatusCode}.");
    private static readonly Action<ILogger, string, int?, Exception?> s_checkFailed =
        LoggerMessage.Define<string, int?>(
            LogLevel.Warning,
            new EventId(2, "HealthCheckFailed"),
            "Backend health check failed with request error {RequestError} and status {StatusCode}; polling will retry.");
    private static readonly Action<ILogger, Exception?> s_pollingStopped =
        LoggerMessage.Define(
            LogLevel.Information,
            new EventId(3, "PollingStopped"),
            "Backend polling observed application shutdown.");
    private static readonly Action<ILogger, Exception?> s_windowStarted =
        LoggerMessage.Define(
            LogLevel.Information,
            new EventId(4, "WindowStarted"),
            "Backend integration sample window started.");

    /// <summary>
    ///  Logs a successful backend health check.
    /// </summary>
    internal static void CheckSucceeded(ILogger logger, int statusCode)
        => s_checkSucceeded(logger, statusCode, null);

    /// <summary>
    ///  Logs request failure details without logging the request URI or response data.
    /// </summary>
    internal static void CheckFailed(ILogger logger, HttpRequestException exception)
        => s_checkFailed(
            logger,
            exception.HttpRequestError.ToString(),
            exception.StatusCode is HttpStatusCode statusCode ? (int)statusCode : null,
            null);

    /// <summary>
    ///  Logs graceful cancellation of backend polling.
    /// </summary>
    internal static void PollingStopped(ILogger logger)
        => s_pollingStopped(logger, null);

    /// <summary>
    ///  Logs startup of the sample's UI.
    /// </summary>
    internal static void WindowStarted(ILogger logger)
        => s_windowStarted(logger, null);
}

/// <summary>
///  Composes a configured backend client and a cancellable polling worker.
/// </summary>
internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        WinFormsApplicationBuilder builder = WinFormsApplication.CreateBuilder(args);
        builder.Services
            .AddOptions<BackendOptions>()
            .Bind(builder.Configuration.GetSection("Backend"))
            .Validate(
                static options =>
                    Uri.TryCreate(options.BaseAddress, UriKind.Absolute, out Uri? address)
                    && address.UserInfo.Length == 0
                    && (address.Scheme == Uri.UriSchemeHttp || address.Scheme == Uri.UriSchemeHttps),
                "Backend:BaseAddress must be an absolute HTTP or HTTPS URL without user information.")
            .Validate(
                static options => options.PollIntervalSeconds is >= 5 and <= 300,
                "Backend:PollIntervalSeconds must be between 5 and 300.")
            .ValidateOnStart();

        builder.Services.AddSingleton(static services =>
        {
            BackendOptions options = services.GetRequiredService<IOptions<BackendOptions>>().Value;
            return new HttpClient
            {
                BaseAddress = new Uri(options.BaseAddress, UriKind.Absolute)
            };
        });
        builder.Services.AddSingleton<BackendHealthClient>();
        builder.Services.AddHostedService<BackendPollingWorker>();
        builder.Services.AddTransient<MainForm>();
        builder.UseStartupForm<MainForm>(
            static services => services.GetRequiredService<MainForm>());

        using WinFormsApplication application = builder.Build();
        application.Run();
    }
}

/// <summary>
///  Contains the backend address and polling cadence.
/// </summary>
internal sealed class BackendOptions
{
    /// <summary>
    ///  Gets or sets the backend base address.
    /// </summary>
    public string BaseAddress { get; set; } = string.Empty;

    /// <summary>
    ///  Gets or sets the delay between health requests in seconds.
    /// </summary>
    public int PollIntervalSeconds { get; set; }
}

/// <summary>
///  Performs the backend health request for the service layer.
/// </summary>
internal sealed class BackendHealthClient(HttpClient httpClient)
{
    /// <summary>
    ///  Requests the backend's health endpoint and returns its HTTP status.
    /// </summary>
    internal async Task<HttpStatusCode> CheckHealthAsync(CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await httpClient
            .GetAsync(
                "health",
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        return response.StatusCode;
    }
}

/// <summary>
///  Polls the backend on a worker thread and stops with the application host.
/// </summary>
internal sealed class BackendPollingWorker(
    BackendHealthClient backendClient,
    IOptions<BackendOptions> options,
    ILogger<BackendPollingWorker> logger) : BackgroundService
{
    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using PeriodicTimer timer = new(TimeSpan.FromSeconds(options.Value.PollIntervalSeconds));

        try
        {
            do
            {
                try
                {
                    HttpStatusCode statusCode = await backendClient
                        .CheckHealthAsync(stoppingToken)
                        .ConfigureAwait(false);
                    BackendLog.CheckSucceeded(logger, (int)statusCode);
                }
                catch (HttpRequestException exception)
                {
                    BackendLog.CheckFailed(logger, exception);
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            BackendLog.PollingStopped(logger);
        }
    }
}

/// <summary>
///  Displays backend polling configuration without exposing request data.
/// </summary>
internal sealed class MainForm : Form
{
    /// <summary>
    ///  Initializes the window with validated polling options.
    /// </summary>
    public MainForm(IOptions<BackendOptions> options, ILogger<MainForm> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        Text = "WinForms Application Builder - Backend Integration";
        ClientSize = new Size(600, 180);

        Label description = new()
        {
            AutoSize = true,
            Location = new Point(16, 20),
            Text = $"Backend health is checked every {options.Value.PollIntervalSeconds} seconds."
        };

        Label diagnostics = new()
        {
            AutoSize = true,
            Location = new Point(16, 52),
            Text = "Check the configured host logging provider for request status and retry diagnostics."
        };

        Button closeButton = new()
        {
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
            Location = new Point(490, 120),
            Text = "Close"
        };
        closeButton.Click += (_, _) => Close();

        Controls.AddRange([description, diagnostics, closeButton]);
        Shown += (_, _) => BackendLog.WindowStarted(logger);
    }
}
