// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Windows.Forms;
using Microsoft.Extensions.Logging;

namespace Microsoft.Extensions.WinForms;

/// <summary>
///  Forwards opt-in WinForms and CLR exception notifications to the host logger.
/// </summary>
internal sealed class WinFormsExceptionLogging(ILogger<WinFormsApplication> logger) : IDisposable
{
    private static readonly Action<ILogger, string, bool, Exception?> s_logUnhandledException =
        LoggerMessage.Define<string, bool>(
            LogLevel.Error,
            new EventId(1, "UnhandledException"),
            "Unhandled exception from {ExceptionSource}; process terminating: {IsTerminating}.");
    private static readonly Action<ILogger, string, bool, Exception?> s_logTerminatingException =
        LoggerMessage.Define<string, bool>(
            LogLevel.Critical,
            new EventId(2, "TerminatingException"),
            "Unhandled exception from {ExceptionSource}; process terminating: {IsTerminating}.");
    private static readonly Action<ILogger, string, string, bool, Exception?> s_logNonException =
        LoggerMessage.Define<string, string, bool>(
            LogLevel.Critical,
            new EventId(3, "UnhandledNonException"),
            "Unhandled non-Exception object from {ExceptionSource}; type: {ExceptionType}; " +
            "process terminating: {IsTerminating}.");

    private int _subscribed;

    /// <summary>
    ///  Subscribes to WinForms and CLR exception notifications.
    /// </summary>
    internal void Start()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _subscribed) == -1, this);

        if (Interlocked.CompareExchange(ref _subscribed, 1, 0) != 0)
        {
            throw new InvalidOperationException("Exception logging has already been started.");
        }

        Application.ThreadException += OnThreadException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    /// <summary>
    ///  Logs a WinForms UI-thread exception.
    /// </summary>
    /// <param name="sender">The event sender.</param>
    /// <param name="e">The exception event arguments.</param>
    internal void OnThreadException(object? sender, ThreadExceptionEventArgs e)
        => TryLog(
            e.Exception,
            source: "WinForms.ThreadException",
            isTerminating: false);

    /// <summary>
    ///  Logs an unhandled CLR exception without changing termination behavior.
    /// </summary>
    /// <param name="sender">The event sender.</param>
    /// <param name="e">The unhandled exception event arguments.</param>
    internal void OnUnhandledException(object? sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            TryLog(exception, source: "AppDomain.UnhandledException", e.IsTerminating);
        }
        else
        {
            TryLogNonException(
                source: "AppDomain.UnhandledException",
                exceptionType: e.ExceptionObject?.GetType().FullName ?? "unknown",
                isTerminating: e.IsTerminating);
        }
    }

    /// <summary>
    ///  Logs an unobserved task exception without marking it observed.
    /// </summary>
    /// <param name="sender">The event sender.</param>
    /// <param name="e">The unobserved task exception event arguments.</param>
    internal void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        => TryLog(
            e.Exception,
            source: "TaskScheduler.UnobservedTaskException",
            isTerminating: false);

    /// <summary>
    ///  Logs an exception that escaped the application runtime.
    /// </summary>
    /// <param name="exception">The exception to log.</param>
    internal void LogApplicationFailure(Exception exception)
        => TryLog(exception, source: "WinFormsApplication.Run", isTerminating: false);

    /// <inheritdoc/>
    public void Dispose()
    {
        int previousState = Interlocked.Exchange(ref _subscribed, -1);

        if (previousState == 1)
        {
            Application.ThreadException -= OnThreadException;
            AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;
            TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;
        }
    }

    private void TryLog(Exception exception, string source, bool isTerminating)
    {
        try
        {
            Action<ILogger, string, bool, Exception?> logException = isTerminating
                ? s_logTerminatingException
                : s_logUnhandledException;
            logException(logger, source, isTerminating, exception);
        }
        catch (Exception loggingFailure)
        {
            Trace.TraceError("Logging {0} failed: {1}", source, loggingFailure);
        }
    }

    private void TryLogNonException(string source, string exceptionType, bool isTerminating)
    {
        try
        {
            s_logNonException(logger, source, exceptionType, isTerminating, null);
        }
        catch (Exception loggingFailure)
        {
            Trace.TraceError("Logging {0} failed: {1}", source, loggingFailure);
        }
    }
}
