// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.ExceptionServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.WinForms;

namespace System.Windows.Forms.Tests;

/// <summary>
///  Tests opt-in WinForms and CLR exception logging.
/// </summary>
public class WinFormsExceptionLoggingTests
{
    [WinFormsFact]
    public void ExceptionEvents_AreLoggedWithoutSuppressingClrTaskPolicy()
    {
        List<LogRecord> records = [];
        using TestLoggerProvider provider = new(records);
        using ILoggerFactory loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(provider));
        using WinFormsExceptionLogging logging = new(loggerFactory.CreateLogger<WinFormsApplication>());
        logging.Start();
        InvalidOperationException exception = new("UI failure.");

        Application.OnThreadException(exception);
        logging.OnUnhandledException(
            sender: null,
            new UnhandledExceptionEventArgs(exception, isTerminating: true));
        UnobservedTaskExceptionEventArgs unobserved = new(new AggregateException(exception));
        logging.OnUnobservedTaskException(sender: null, unobserved);

        Assert.Equal(3, records.Count);
        Assert.Contains(records, record =>
            record.Message.Contains("WinForms.ThreadException", StringComparison.Ordinal));
        Assert.Contains(records, record =>
            record.Message.Contains("AppDomain.UnhandledException", StringComparison.Ordinal)
            && record.Level == LogLevel.Critical);
        Assert.Contains(records, record =>
            record.Message.Contains("TaskScheduler.UnobservedTaskException", StringComparison.Ordinal));
        Assert.False(unobserved.Observed);
    }

    [WinFormsFact]
    public void EnableExceptionLogging_LogsRuntimeFailuresAndDisposesProviderAfterShutdown()
    {
        RunOnStaThread(() =>
        {
            List<LogRecord> records = [];
            TestLoggerProvider? provider = null;
            WinFormsApplicationBuilder builder = WinFormsApplication.CreateBuilder();
            builder.Services.AddSingleton<ILoggerProvider>(_ => provider = new(records));
            using Form form = new();
            form.Shown += (_, _) => form.Close();
            using WinFormsApplication application = builder
                .EnableExceptionLogging()
                .UseStartupForm(form)
                .Build();

            application.Run();

            Assert.NotNull(provider);
            Assert.True(provider.IsDisposed);
        });
    }

    [WinFormsFact]
    public void EnableExceptionLogging_LogsStartupFailureBeforeRethrowing()
    {
        RunOnStaThread(() =>
        {
            List<LogRecord> records = [];
            TestLoggerProvider? provider = null;
            WinFormsApplicationBuilder builder = WinFormsApplication.CreateBuilder();
            builder.Services.AddSingleton<ILoggerProvider>(_ => provider = new(records));
            using WinFormsApplication application = builder
                .EnableExceptionLogging()
                .UseStartupForm<Form>(_ => throw new InvalidOperationException("Startup failure."))
                .Build();

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(application.Run);

            Assert.Equal("Startup failure.", exception.Message);
            Assert.Contains(records, record =>
                record.Message.Contains("WinFormsApplication.Run", StringComparison.Ordinal)
                && record.Exception is InvalidOperationException);
            Assert.NotNull(provider);
            Assert.True(provider.IsDisposed);
        });
    }

    private static void RunOnStaThread(Action action)
    {
        Exception? failure = null;
        Thread thread = new(() =>
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    /// <summary>
    ///  Captures logger output and observes provider disposal.
    /// </summary>
    private sealed class TestLoggerProvider(List<LogRecord> records) : ILoggerProvider
    {
        /// <summary>
        ///  Gets whether the provider has been disposed.
        /// </summary>
        internal bool IsDisposed { get; private set; }

        /// <inheritdoc/>
        public ILogger CreateLogger(string categoryName)
            => new TestLogger(records, categoryName);

        /// <inheritdoc/>
        public void Dispose()
            => IsDisposed = true;
    }

    /// <summary>
    ///  Records messages emitted through an <see cref="ILogger"/>.
    /// </summary>
    private sealed class TestLogger(List<LogRecord> records, string categoryName) : ILogger
    {
        /// <inheritdoc/>
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
            => null;

        /// <inheritdoc/>
        public bool IsEnabled(LogLevel logLevel)
            => true;

        /// <inheritdoc/>
        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            records.Add(new LogRecord(categoryName, logLevel, formatter(state, exception), exception));
        }
    }

    /// <summary>
    ///  Contains a structured logger output record.
    /// </summary>
    private sealed record LogRecord(string Category, LogLevel Level, string Message, Exception? Exception);
}
