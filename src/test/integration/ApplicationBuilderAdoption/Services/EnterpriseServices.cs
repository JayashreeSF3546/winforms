// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ApplicationBuilderAdoption.Services;

/// <summary>
///  Selects one of the offline sample departments.
/// </summary>
public sealed class EnterpriseOptions
{
    public string Department { get; set; } = "Operations";
}

/// <summary>
///  Records repository ownership independently of a Form or service scope.
/// </summary>
public sealed class RepositoryMetrics
{
    private int _created;
    private int _disposed;

    public int Created => Volatile.Read(ref _created);
    public int Disposed => Volatile.Read(ref _disposed);
    internal void Open() => Interlocked.Increment(ref _created);
    internal void Close() => Interlocked.Increment(ref _disposed);
}

/// <summary>
///  Represents a detached business result, not a live scoped dependency.
/// </summary>
public sealed record WorkOrder(int Id, string Department, string Description, bool Closed);

/// <summary>
///  Owns a per-operation repository, standing in for a database session without requiring a database.
/// </summary>
public sealed class WorkOrderRepository : IDisposable
{
    private readonly RepositoryMetrics _metrics;
    private bool _disposed;

    public WorkOrderRepository(RepositoryMetrics metrics)
    {
        _metrics = metrics;
        _metrics.Open();
    }

    public WorkOrder[] Read()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        return
        [
            new(1, "Operations", "Inspect packing station", Closed: false),
            new(2, "Operations", "Restock labels", Closed: false),
            new(3, "Operations", "Replace scanner", Closed: true),
            new(4, "Finance", "Review supplier invoice", Closed: false)
        ];
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _metrics.Close();
        }
    }
}

/// <summary>
///  Applies the business filter inside the scoped service layer.
/// </summary>
public sealed class WorkOrderService(WorkOrderRepository repository)
{
    public WorkOrder[] GetOpenOrders(string department)
        => repository.Read().Where(order => !order.Closed && order.Department == department).ToArray();
}

/// <summary>
///  Creates a short-lived operation scope and returns only materialized data to presentation.
/// </summary>
public sealed partial class WorkOrderQuery(
    IServiceScopeFactory scopes, IOptions<EnterpriseOptions> options, ILogger<WorkOrderQuery> logger)
{
    public WorkOrder[] Load()
    {
        using IServiceScope scope = scopes.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<WorkOrderService>();
        WorkOrder[] orders = service.GetOpenOrders(options.Value.Department);
        Loaded(logger, orders.Length, options.Value.Department);

        return orders;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Loaded {Count} open orders for {Department}.")]
    private static partial void Loaded(ILogger logger, int count, string department);
}
