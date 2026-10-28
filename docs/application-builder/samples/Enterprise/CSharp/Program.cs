// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Drawing;
using System.Windows.Forms;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.WinForms;

namespace ApplicationBuilderSample.Enterprise.CSharp;

/// <summary>
///  Defines structured log messages for the enterprise sample.
/// </summary>
internal static class EnterpriseLog
{
    private static readonly Action<ILogger, string, Exception?> s_orderCreated =
        LoggerMessage.Define<string>(
            LogLevel.Information,
            new EventId(1, "OrderCreated"),
            "Created sample order {OrderId}.");
    private static readonly Action<ILogger, Exception?> s_hostStarted =
        LoggerMessage.Define(
            LogLevel.Information,
            new EventId(2, "HostStarted"),
            "Enterprise sample host started.");
    private static readonly Action<ILogger, Exception?> s_hostStopping =
        LoggerMessage.Define(
            LogLevel.Information,
            new EventId(3, "HostStopping"),
            "Enterprise sample host is stopping.");
    private static readonly Action<ILogger, string, Exception?> s_orderDisplayed =
        LoggerMessage.Define<string>(
            LogLevel.Information,
            new EventId(4, "OrderDisplayed"),
            "Displayed order {OrderId} in the main window.");

    /// <summary>
    ///  Logs order creation in the service layer.
    /// </summary>
    internal static void OrderCreated(ILogger logger, string orderId)
        => s_orderCreated(logger, orderId, null);

    /// <summary>
    ///  Logs host startup.
    /// </summary>
    internal static void HostStarted(ILogger logger)
        => s_hostStarted(logger, null);

    /// <summary>
    ///  Logs host shutdown.
    /// </summary>
    internal static void HostStopping(ILogger logger)
        => s_hostStopping(logger, null);

    /// <summary>
    ///  Logs display of an order in the UI.
    /// </summary>
    internal static void OrderDisplayed(ILogger logger, string orderId)
        => s_orderDisplayed(logger, orderId, null);
}

/// <summary>
///  Composes the enterprise sample's configuration, services, and UI lifetime.
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
            .AddOptions<EnterpriseOptions>()
            .Bind(builder.Configuration.GetSection("Enterprise"))
            .Validate(
                static options => !string.IsNullOrWhiteSpace(options.OrderPrefix),
                "Enterprise:OrderPrefix must not be empty.")
            .ValidateOnStart();

        builder.Services.AddScoped<IOrderService, OrderService>();
        builder.Services.AddHostedService<ApplicationLifetimeDiagnostics>();
        builder.Services.AddTransient<MainForm>();
        builder.UseStartupForm<MainForm>(
            static services => services.GetRequiredService<MainForm>());

        using WinFormsApplication application = builder.Build();
        application.Run();
    }
}

/// <summary>
///  Contains configuration for the enterprise order service.
/// </summary>
internal sealed class EnterpriseOptions
{
    /// <summary>
    ///  Gets or sets the prefix used in generated order identifiers.
    /// </summary>
    public string OrderPrefix { get; set; } = string.Empty;
}

/// <summary>
///  Defines the boundary used by the UI to create an order.
/// </summary>
internal interface IOrderService
{
    /// <summary>
    ///  Creates a sample order and returns its identifier.
    /// </summary>
    string CreateOrder();
}

/// <summary>
///  Implements the sample order workflow using validated host options.
/// </summary>
internal sealed class OrderService(
    IOptions<EnterpriseOptions> options,
    ILogger<OrderService> logger) : IOrderService
{
    private int _nextOrderNumber;

    /// <inheritdoc/>
    public string CreateOrder()
    {
        string orderId = $"{options.Value.OrderPrefix}-{Interlocked.Increment(ref _nextOrderNumber):D4}";
        EnterpriseLog.OrderCreated(logger, orderId);

        return orderId;
    }
}

/// <summary>
///  Writes host start and stop events to the shared logging pipeline.
/// </summary>
internal sealed class ApplicationLifetimeDiagnostics(ILogger<ApplicationLifetimeDiagnostics> logger)
    : IHostedService
{
    /// <inheritdoc/>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        EnterpriseLog.HostStarted(logger);

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        EnterpriseLog.HostStopping(logger);

        return Task.CompletedTask;
    }
}

/// <summary>
///  Displays the service-layer operation and current application state.
/// </summary>
internal sealed class MainForm : Form
{
    private readonly IOrderService _orderService;
    private readonly ILogger<MainForm> _logger;
    private readonly Label _statusLabel = new();

    /// <summary>
    ///  Initializes the window using its activation-scope services.
    /// </summary>
    public MainForm(IOrderService orderService, ILogger<MainForm> logger)
    {
        ArgumentNullException.ThrowIfNull(orderService);
        ArgumentNullException.ThrowIfNull(logger);

        _orderService = orderService;
        _logger = logger;

        Text = "WinForms Application Builder - Enterprise";
        ClientSize = new Size(560, 200);

        Label description = new()
        {
            AutoSize = true,
            Location = new Point(16, 20),
            Text = "The Form calls a scoped service; the host owns configuration and shutdown."
        };

        Button createOrderButton = new()
        {
            Location = new Point(16, 64),
            Text = "Create sample order",
            Width = 160
        };
        createOrderButton.Click += CreateOrder;

        _statusLabel.AutoSize = true;
        _statusLabel.Location = new Point(16, 112);
        _statusLabel.Text = "No order has been created.";

        Controls.AddRange([description, createOrderButton, _statusLabel]);
    }

    private void CreateOrder(object? sender, EventArgs e)
    {
        string orderId = _orderService.CreateOrder();
        _statusLabel.Text = $"Created {orderId}.";
        EnterpriseLog.OrderDisplayed(_logger, orderId);
    }
}
