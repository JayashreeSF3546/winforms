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
///  Composes a presentation layer over a separate, UI-free scoped business service project.
/// </summary>
public static class EnterpriseSample
{
    public static int Run(string[] args)
    {
        IHostBuilder hostBuilder = new HostBuilder()
            .ConfigureHostConfiguration(config => config.AddInMemoryCollection(
                [new("hostBuilder:reloadConfigOnChange", "false")]))
            .ConfigureWinFormsConfiguration(args)
            .UseDefaultServiceProvider(options =>
            {
                options.ValidateScopes = true;
                options.ValidateOnBuild = true;
            })
            .ConfigureLogging(logging => logging.AddDebug())
            .ConfigureServices((context, services) =>
            {
                services.AddOptions<EnterpriseOptions>()
                    .Bind(context.Configuration.GetSection("Enterprise"))
                    .Validate(options => options.Department is "Operations" or "Finance",
                        "Enterprise:Department must be Operations or Finance.")
                    .ValidateOnStart();
                services.AddSingleton<RepositoryMetrics>();
                services.AddScoped<WorkOrderRepository>();
                services.AddScoped<WorkOrderService>();
                services.AddSingleton<WorkOrderQuery>();
                services.AddHostedService<HostTrace>();
            });
        SampleProbe? probe = null;
        RepositoryMetrics? metrics = null;
        WinFormsApplication? application = null;
        using (application = WinFormsApplication.CreateBuilder()
            .UseHost(hostBuilder)
            .UseApplicationContext(() =>
            {
                IServiceProvider services = application!.Host!.Services;
                probe = new SampleProbe(services.GetRequiredService<IConfiguration>());
                SampleProbe.Require(probe.Scenario is "" or "smoke", "Unknown enterprise probe.");
                metrics = services.GetRequiredService<RepositoryMetrics>();
                string department = services.GetRequiredService<IOptions<EnterpriseOptions>>().Value.Department;
                EnterpriseForm form = new(services.GetRequiredService<WorkOrderQuery>(), department);
                probe.OnShown(form, () =>
                {
                    for (int operation = 0; operation < 2; operation++)
                    {
                        form.ClickLoad();
                        int expected = department == "Finance" ? 1 : 2;
                        SampleProbe.Require(form.OrderCount == expected, "The business filter returned incorrect orders.");
                        SampleProbe.Require(metrics.Created == operation + 1 && metrics.Disposed == operation + 1,
                            "An operation scope outlived its materialized result.");
                    }

                    Console.WriteLine($"SCOPES created={metrics.Created} disposed={metrics.Disposed}; department={department}; orders={form.OrderCount}");
                    form.Close();

                    return Task.CompletedTask;
                });

                return new ApplicationContext(form);
            })
            .Build())
        {
            application.Run();
        }

        SampleProbe.Require(metrics is not null && metrics.Created == metrics.Disposed, "A repository scope leaked.");
        probe!.Complete("enterprise: detached results; operation scopes disposed");

        return 0;
    }
}

/// <summary>
///  Displays detached orders without resolving a scoped repository from the root provider.
/// </summary>
internal sealed class EnterpriseForm : Form
{
    private readonly Button _load = new() { Text = "Load open orders", Dock = DockStyle.Top, Height = 40 };
    private readonly ListBox _orders = new() { Dock = DockStyle.Fill };
    private readonly WorkOrderQuery _query;

    internal EnterpriseForm(WorkOrderQuery query, string department)
    {
        _query = query;
        Text = $"Enterprise - {department}";
        ClientSize = new Size(480, 240);
        Controls.Add(_orders);
        Controls.Add(_load);
        _load.Click += (_, _) => LoadOrders();
    }

    internal int OrderCount => _orders.Items.Count;
    internal void ClickLoad() => _load.PerformClick();

    private void LoadOrders()
    {
        try
        {
            WorkOrder[] orders = _query.Load();
            _orders.Items.Clear();
            foreach (WorkOrder order in orders)
            {
                _orders.Items.Add($"{order.Id}: {order.Description}");
            }
        }
        catch (Exception exception) when (exception is not (OutOfMemoryException or StackOverflowException or AccessViolationException))
        {
            Console.Error.WriteLine($"ORDER QUERY FAILED: {exception.GetType().Name}");
            _orders.Items.Clear();
            _orders.Items.Add("Unable to load orders; see diagnostics.");
        }
    }
}
