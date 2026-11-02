// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;

namespace Microsoft.Extensions.WinForms;

/// <summary>
///  Runs explicitly requested standard health diagnostics away from the caller's UI thread.
/// </summary>
/// <remarks>
///  <para>
///   The application owns readiness, offline operation, report presentation, and logging privacy policies.
///   This service installs no timer, publisher, endpoint, dispatcher, or automatic shutdown policy.
///   Outstanding operations must be awaited before their dependency owner is disposed.
///  </para>
/// </remarks>
public sealed class WinFormsHealthDiagnostics
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly CancellationToken _applicationStopping;

    /// <summary>
    ///  Initializes diagnostics using the application's service scopes and host stopping signal.
    /// </summary>
    /// <param name="scopeFactory">The application's service scope factory.</param>
    /// <param name="lifetime">The application's host lifetime.</param>
    public WinFormsHealthDiagnostics(IServiceScopeFactory scopeFactory, IHostApplicationLifetime lifetime)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(lifetime);
        _scopeFactory = scopeFactory;
        _applicationStopping = lifetime.ApplicationStopping;
    }

    /// <summary>
    ///  Resolves and executes the standard health service in a background operation and awaits scope disposal.
    /// </summary>
    /// <param name="predicate">An optional registration selector; null selects every registered check.</param>
    /// <param name="cancellationToken">Cancellation for this operation, not for application shutdown.</param>
    /// <returns>The standard health report, without changing its statuses, descriptions, exceptions, or data.</returns>
    /// <remarks>
    ///  <para>
    ///   Service resolution, selection, checks, and asynchronous scope disposal run away from the UI thread.
    ///   Caller cancellation or committed host stopping cancels the operation, not the shared host.
    ///   Non-cooperative checks or disposers are not abandoned; completion waits for them.
    ///   Registration timeouts retain the standard health service's cooperative timeout semantics.
    ///  </para>
    ///  <para>
    ///   Predicates and service factories must be UI-free. Infrastructure failures propagate to the caller;
    ///   dependency failures retain the standard service's reporting behavior. The standard health service
    ///   may log sensitive report contents; configure its logging category before executing checks.
    ///  </para>
    /// </remarks>
    [CLSCompliant(false)]
    public Task<HealthReport> CheckHealthAsync(
        Func<HealthCheckRegistration, bool>? predicate = null,
        CancellationToken cancellationToken = default)
        => Task.Run(async () =>
        {
            using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, _applicationStopping);
            CancellationToken token = cancellation.Token;
            token.ThrowIfCancellationRequested();
            AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
            HealthReport report;
            await using (scope.ConfigureAwait(false))
            {
                var service = scope.ServiceProvider.GetRequiredService<HealthCheckService>();
                report = await service.CheckHealthAsync(predicate, token).ConfigureAwait(false);
            }

            token.ThrowIfCancellationRequested();

            return report;
        }, cancellationToken);
}
