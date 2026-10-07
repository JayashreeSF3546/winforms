// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.ExceptionServices;
using System.Windows.Forms;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Extensions.WinForms;

/// <summary>
///  Associates an activated form with the service scope for its lifetime.
/// </summary>
internal sealed class WinFormsFormScope : IDisposable
{
    private readonly IServiceProvider _services;
    private Action<WinFormsFormScope>? _disposedCallback;
    private Form? _form;
    private IServiceScope? _scope;

    /// <summary>
    ///  Initializes a new instance of the <see cref="WinFormsFormScope"/> class.
    /// </summary>
    /// <param name="scope">The service scope, if the application has a host.</param>
    /// <param name="services">The service provider supplied to the form factory.</param>
    /// <param name="disposedCallback">The callback used to release application tracking.</param>
    internal WinFormsFormScope(
        IServiceScope? scope,
        IServiceProvider services,
        Action<WinFormsFormScope> disposedCallback)
    {
        _scope = scope;
        _services = services;
        _disposedCallback = disposedCallback;
    }

    /// <summary>
    ///  Gets the services available to this form activation.
    /// </summary>
    internal IServiceProvider Services => _services;

    /// <summary>
    ///  Associates this scope with its activated form.
    /// </summary>
    /// <param name="form">The activated form.</param>
    internal void Attach(Form form)
    {
        ArgumentNullException.ThrowIfNull(form);
        ObjectDisposedException.ThrowIf(_scope is null && _disposedCallback is null, this);
        ObjectDisposedException.ThrowIf(form.IsDisposed, form);

        if (_form is not null)
        {
            throw new InvalidOperationException("A form scope can be attached to only one form.");
        }

        _form = form;
        form.Disposed += OnFormDisposed;
    }

    /// <summary>
    ///  Disposes the form before disposing its activation scope.
    /// </summary>
    internal void DisposeForm()
    {
        Exception? failure = null;
        Form? form = Volatile.Read(ref _form);

        if (form is not null && !form.IsDisposed)
        {
            try
            {
                form.Dispose();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        }

        try
        {
            Dispose();
        }
        catch (Exception exception)
        {
            failure = CombineFailures(failure, exception);
        }

        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    /// <summary>
    ///  Disposes the activation scope and releases application tracking.
    /// </summary>
    public void Dispose()
    {
        Form? form = Interlocked.Exchange(ref _form, null);
        form?.Disposed -= OnFormDisposed;

        IServiceScope? scope = Interlocked.Exchange(ref _scope, null);
        Action<WinFormsFormScope>? disposedCallback = Interlocked.Exchange(ref _disposedCallback, null);
        Exception? failure = null;

        try
        {
            scope?.Dispose();
        }
        catch (Exception exception)
        {
            failure = exception;
        }

        try
        {
            disposedCallback?.Invoke(this);
        }
        catch (Exception exception)
        {
            failure = CombineFailures(failure, exception);
        }

        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    private void OnFormDisposed(object? sender, EventArgs e)
        => Dispose();

    private static Exception CombineFailures(Exception? first, Exception second)
        => first is null ? second : new AggregateException(first, second);
}
