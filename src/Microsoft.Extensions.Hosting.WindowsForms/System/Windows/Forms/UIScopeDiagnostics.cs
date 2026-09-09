// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Extensions.WinForms;

/// <summary>
///  Process-wide counters that make UI scope and activation behavior observable to tests, samples,
///  and resource-stability runs.
/// </summary>
/// <remarks>
///  <para>
///   These counters hold no object references and expose no services. They are diagnostics only and
///   are not a service locator.
///  </para>
/// </remarks>
public static class UIScopeDiagnostics
{
    private static int s_activeScopeCount;
    private static long s_totalScopesCreated;
    private static long s_totalActivations;
    private static long s_totalActivationFailures;
    private static long s_totalServiceAssignments;

    /// <summary>
    ///  Gets the number of <see cref="UIScope"/> instances that have been created and not disposed.
    /// </summary>
    public static int ActiveScopeCount => Volatile.Read(ref s_activeScopeCount);

    /// <summary>
    ///  Gets the total number of <see cref="UIScope"/> instances that have been created.
    /// </summary>
    public static long TotalScopesCreated => Interlocked.Read(ref s_totalScopesCreated);

    /// <summary>
    ///  Gets the total number of successful activations.
    /// </summary>
    public static long TotalActivations => Interlocked.Read(ref s_totalActivations);

    /// <summary>
    ///  Gets the total number of activations that failed and were rolled back.
    /// </summary>
    public static long TotalActivationFailures => Interlocked.Read(ref s_totalActivationFailures);

    /// <summary>
    ///  Gets the total number of objects that have received a service provider through
    ///  <see cref="IServiceProviderAssignable"/>.
    /// </summary>
    public static long TotalServiceAssignments => Interlocked.Read(ref s_totalServiceAssignments);

    /// <summary>
    ///  Resets every counter. Intended for repeatable measurements.
    /// </summary>
    public static void Reset()
    {
        Volatile.Write(ref s_activeScopeCount, 0);
        Interlocked.Exchange(ref s_totalScopesCreated, 0);
        Interlocked.Exchange(ref s_totalActivations, 0);
        Interlocked.Exchange(ref s_totalActivationFailures, 0);
        Interlocked.Exchange(ref s_totalServiceAssignments, 0);
    }

    internal static void OnScopeCreated()
    {
        Interlocked.Increment(ref s_activeScopeCount);
        Interlocked.Increment(ref s_totalScopesCreated);
    }

    internal static void OnScopeDisposed() => Interlocked.Decrement(ref s_activeScopeCount);

    internal static void OnActivation() => Interlocked.Increment(ref s_totalActivations);

    internal static void OnActivationFailure() => Interlocked.Increment(ref s_totalActivationFailures);

    internal static void OnServiceAssigned() => Interlocked.Increment(ref s_totalServiceAssignments);
}
