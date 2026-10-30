// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.ExceptionServices;
using System.Windows.Forms;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Extensions.WinForms;

/// <summary>
///  Owns the explicit UI lifetime tree, independently of the host's flat DI scopes.
/// </summary>
/// <remarks>
///  <para>
///   Composition and synchronous cleanup run on the owning STA. The host provider is borrowed.
///   Async-only services are unsupported; container disposal failures are surfaced, not retried.
///  </para>
/// </remarks>
internal sealed class WinFormsUiScope : IDisposable
{
    private readonly Thread _ownerThread = Thread.CurrentThread;
    private readonly IServiceProvider _services;
    private readonly IServiceScope _applicationScope;
    private readonly Func<bool>? _isStopping;
    private readonly List<WinFormsFormScope> _forms = [];
    private int _activations;
    private bool _stopping;
    private bool _disposeRequested;
    private bool _disposed;

    internal WinFormsUiScope(IServiceProvider services, Func<bool>? isStopping = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        WinFormsServiceActivation.VerifyRuntime();
        _services = services;
        _isStopping = isStopping;
        _applicationScope = services.CreateScope();
    }

    internal ApplicationContext CreateContext(
        Func<IServiceProvider, WinFormsUiScope, ApplicationContext> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        VerifyCanActivate();
        ApplicationContext? context = null;
        bool accepted = false;
        List<Exception> failures = [];
        _activations++;
        try
        {
            context = factory(_applicationScope.ServiceProvider, this)
                ?? throw new InvalidOperationException(SR.WinFormsApplication_NullContext);
            if (context.MainForm is { } mainForm)
            {
                if (!_forms.Any(lifetime => ReferenceEquals(lifetime.Form, mainForm)))
                {
                    throw new InvalidOperationException(SR.WinFormsApplication_UiWrongOwner);
                }

                WinFormsApplicationOptions.ValidateStartupForm(mainForm);
            }

            accepted = true;
            VerifyCanActivate();
        }
        catch (Exception exception) when (!exception.IsCriticalException())
        {
            failures.Add(exception);
            if (accepted)
            {
                Capture(failures, context!.Dispose);
            }
        }
        finally
        {
            _activations--;
            Capture(failures, TryComplete);
        }

        ThrowFailures(failures);
        return context!;
    }

    internal WinFormsFormScope CreateForm(
        Func<IServiceProvider, Form> factory,
        WinFormsFormScope? owner = null)
    {
        ArgumentNullException.ThrowIfNull(factory);
        VerifyCanActivate();
        owner?.VerifyOwner(this);
        IServiceScope? scope = null;
        Form? form = null;
        WinFormsFormScope? lifetime = null;
        List<Exception> failures = [];
        _activations++;
        try
        {
            scope = _services.CreateScope();
            form = WinFormsServiceActivation.Create(scope.ServiceProvider, services =>
            {
                Form created = factory(services);
                if (created?.RuntimeDispose is not null)
                {
                    throw new InvalidOperationException(SR.WinFormsApplication_UiAlreadyOwned);
                }

                return created!;
            });
            VerifyCanActivate();
            owner?.VerifyOwner(this);
            if (form.Visible || !form.TopLevel)
            {
                throw new InvalidOperationException(SR.WinFormsApplication_UiActivationState);
            }

            lifetime = new(this, scope, form, owner);
            _forms.Add(lifetime);
            scope = null;
            form = null;
        }
        catch (Exception exception) when (!exception.IsCriticalException())
        {
            failures.Add(exception);
            if (form is not null)
            {
                Capture(failures, form.Dispose);
            }

            if (scope is not null)
            {
                Capture(failures, scope.Dispose);
            }
        }
        finally
        {
            _activations--;
            Capture(failures, TryComplete);
        }

        ThrowFailures(failures);
        return lifetime!;
    }

    internal void VerifyAccess()
    {
        if (Thread.CurrentThread != _ownerThread)
        {
            throw new InvalidOperationException(SR.WinFormsApplication_WrongThread);
        }
    }

    internal void VerifyCanActivate()
    {
        VerifyAccess();
        WinFormsServiceActivation.VerifyRuntime();
        ObjectDisposedException.ThrowIf(_disposeRequested || _disposed, this);
        if (_stopping || _isStopping?.Invoke() == true)
        {
            throw new InvalidOperationException(SR.WinFormsApplication_UiStopping);
        }
    }

    internal void BeginStopping()
    {
        VerifyAccess();
        _stopping = true;
    }

    public void Dispose()
    {
        VerifyAccess();
        if (_disposed || _disposeRequested)
        {
            return;
        }

        _stopping = true;
        _disposeRequested = true;
        List<Exception> failures = [];
        foreach (WinFormsFormScope form in _forms.ToArray().Reverse())
        {
            Capture(failures, form.Dispose);
        }

        Capture(failures, TryComplete);
        ThrowFailures(failures);
    }

    internal void FormCompleted(WinFormsFormScope form)
        => _forms.Remove(form);

    internal void TryComplete()
    {
        if (!_disposeRequested || _disposed || _forms.Count != 0 || _activations != 0)
        {
            return;
        }

        _disposed = true;
        _applicationScope.Dispose();
    }

    internal static void Capture(List<Exception> failures, Action cleanup)
    {
        try
        {
            cleanup();
        }
        catch (Exception exception) when (!exception.IsCriticalException())
        {
            failures.Add(exception);
        }
    }

    internal static void ThrowFailures(List<Exception> failures)
    {
        if (failures.Count == 1)
        {
            ExceptionDispatchInfo.Throw(failures[0]);
        }

        if (failures.Count > 1)
        {
            throw new AggregateException(failures);
        }
    }
}
