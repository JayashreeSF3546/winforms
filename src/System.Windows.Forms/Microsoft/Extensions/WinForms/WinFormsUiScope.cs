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
///  <para>
///   The application creates and owns this coordinator when scoped startup is selected. Do not dispose
///   it independently during a run. Create fresh forms through factories rather than resolving
///   container-owned UI. Forms must complete service-dependent cleanup before calling base.Dispose.
///  </para>
/// </remarks>
public sealed class WinFormsUiScope : IDisposable
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

    /// <summary>
    ///  Creates and tracks an unshown form with a distinct service scope.
    /// </summary>
    /// <param name="factory">Creates a fresh, owning-thread form using its scoped services.</param>
    /// <param name="owner">An active form scope from this coordinator, or null for an independent form.</param>
    /// <returns>The lifetime owner of the form and its scoped services.</returns>
    /// <remarks>
    ///  <para>
    ///   The factory must not show the form, return a container-owned form, or create it on another thread.
    ///   Factory and cleanup failures propagate. Each form's scope is independent of its owner's DI scope;
    ///   lifetime ownership determines cleanup ordering, not scoped-service sharing.
    ///  </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="factory"/> is null.</exception>
    /// <exception cref="ObjectDisposedException">The coordinator, owner, or returned form is disposed.</exception>
    /// <exception cref="InvalidOperationException">
    ///  Activation is attempted on the wrong thread, at design time, or during shutdown;
    ///  the owner belongs to another coordinator; or the returned form cannot transfer ownership.
    /// </exception>
    public WinFormsFormScope CreateForm(
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

    /// <summary>
    ///  Disposes tracked forms and then application-scoped services on the owning thread.
    /// </summary>
    /// <remarks>
    ///  <para>
    ///   The application normally calls this during teardown. Active modal invocations retain their
    ///   service scopes until they unwind. Cleanup failures propagate after independent forms are processed.
    ///  </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">The calling thread does not own this coordinator.</exception>
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
