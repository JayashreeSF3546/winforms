// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Extensions.Logging;

/// <summary>
///  Selects opt-in standard logging destinations for Windows Forms applications.
/// </summary>
public static class WinFormsLoggingExtensions
{
    /// <summary>
    ///  Adds the standard Debug logging provider without clearing existing providers or changing filters.
    /// </summary>
    /// <param name="builder">The application's logging builder.</param>
    /// <returns>The supplied logging builder.</returns>
    /// <remarks>
    ///  <para>
    ///   Debug output requires an attached debugger. Repeated calls do not duplicate the Debug provider.
    ///   Configure other providers, filtering, structured scopes, and configuration with standard
    ///   Microsoft.Extensions.Logging APIs. No file, network, console, telemetry, or exception subscriptions
    ///   are added by this helper. File logging and asynchronous flushing require an application-selected provider.
    ///  </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is null.</exception>
    public static ILoggingBuilder UseWinFormsLogging(this ILoggingBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.AddDebug();
    }
}
