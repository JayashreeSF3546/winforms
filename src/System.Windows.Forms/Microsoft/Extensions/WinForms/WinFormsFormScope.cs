// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.ComponentModel;
using System.Windows.Forms;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Extensions.WinForms;

/// <summary>
///  Owns one opt-in form and service scope, with explicit child lifetime relationships.
/// </summary>
/// <remarks>
///  <para>
///   Service-dependent derived form cleanup must precede base.Dispose. Controls and components
///   borrow this scope. Modal invocation retains both its own and disposed owners' scopes until
///   the invocation unwinds, so no child scope can outlive its logical parent scope.
///  </para>
/// </remarks>
internal sealed class WinFormsFormScope : IDisposable
{
    private static readonly int s_propAssignmentOwner = PropertyStore.CreateKey();
    private readonly object _assignmentOwner = new();
    private readonly WinFormsUiScope _application;
    private readonly IServiceScope _scope;
    private readonly WinFormsFormScope? _owner;
    private readonly List<WinFormsFormScope> _children = [];
    private bool _disposing;
    private bool _uiDisposed;
    private bool _modalActive;
    private bool _completed;

    internal WinFormsFormScope(
        WinFormsUiScope application,
        IServiceScope scope,
        Form form,
        WinFormsFormScope? owner)
    {
        _application = application;
        _scope = scope;
        _owner = owner;
        Form = form;
        _owner?._children.Add(this);
        form.RuntimeDispose = DisposeFromForm;
    }

    internal Form Form { get; }

    internal void Assign<TComponent>(
        TComponent component,
        Action<TComponent, IServiceProvider> assignServices)
        where TComponent : class, IComponent
    {
        VerifyActive();
        ArgumentNullException.ThrowIfNull(component);
        ArgumentNullException.ThrowIfNull(assignServices);
        WinFormsServiceActivation.Assign(component, _scope.ServiceProvider, (target, services) =>
        {
            if (target is Control control)
            {
                if ((control.FindForm() is { } parent && parent != Form)
                    || (control.Properties.TryGetValue(s_propAssignmentOwner, out object? assignedOwner)
                        && !ReferenceEquals(assignedOwner, _assignmentOwner)))
                {
                    throw new InvalidOperationException(SR.WinFormsApplication_UiWrongOwner);
                }

                control.Properties.AddOrRemoveValue(s_propAssignmentOwner, _assignmentOwner);
            }

            assignServices(target, services);
        });
        VerifyActive();
    }

    internal void Show()
    {
        VerifyCanShow();
        List<Exception> failures = [];
        try
        {
            if (_owner is null)
            {
                Form.Show();
            }
            else
            {
                Form.Show(_owner.Form);
            }
        }
        catch (Exception exception) when (!exception.IsCriticalException())
        {
            failures.Add(exception);
            WinFormsUiScope.Capture(failures, Dispose);
            WinFormsUiScope.ThrowFailures(failures);
        }
    }

    internal DialogResult ShowDialog()
    {
        VerifyCanShow();
        List<Exception> failures = [];
        DialogResult result = DialogResult.None;
        _modalActive = true;
        try
        {
            result = _owner is null ? Form.ShowDialog() : Form.ShowDialog(_owner.Form);
        }
        catch (Exception exception) when (!exception.IsCriticalException())
        {
            failures.Add(exception);
        }
        finally
        {
            _modalActive = false;
            WinFormsUiScope.Capture(failures, Dispose);
        }

        WinFormsUiScope.ThrowFailures(failures);
        return result;
    }

    internal void VerifyOwner(WinFormsUiScope application)
    {
        VerifyActive();
        if (!ReferenceEquals(application, _application))
        {
            throw new InvalidOperationException(SR.WinFormsApplication_UiWrongOwner);
        }
    }

    private void VerifyActive()
    {
        _application.VerifyCanActivate();
        ObjectDisposedException.ThrowIf(_completed || _uiDisposed || _disposing || Form.IsDisposed, this);
    }

    private void VerifyCanShow()
    {
        VerifyActive();
        if (_modalActive)
        {
            throw new InvalidOperationException(SR.WinFormsApplication_UiActivationState);
        }

        _owner?.VerifyActive();
    }

    private void DisposeFromForm(Action disposeForm)
    {
        _application.VerifyAccess();
        if (_disposing)
        {
            return;
        }

        _disposing = true;
        Form.RuntimeDispose = null;
        List<Exception> failures = [];
        try
        {
            foreach (WinFormsFormScope child in _children.ToArray().Reverse())
            {
                WinFormsUiScope.Capture(failures, child.Dispose);
            }

            WinFormsUiScope.Capture(failures, disposeForm);
            _uiDisposed = true;
            WinFormsUiScope.Capture(failures, TryComplete);
        }
        finally
        {
            _disposing = false;
        }

        WinFormsUiScope.ThrowFailures(failures);
    }

    public void Dispose()
    {
        _application.VerifyAccess();
        if (_completed || _disposing)
        {
            return;
        }

        List<Exception> failures = [];
        if (!_uiDisposed)
        {
            WinFormsUiScope.Capture(failures, Form.Dispose);
        }

        if (!_uiDisposed)
        {
            // A derived override may throw before reaching the Form hook; still release its scope.
            WinFormsUiScope.Capture(failures, () => DisposeFromForm(static () => { }));
        }
        else
        {
            WinFormsUiScope.Capture(failures, TryComplete);
        }

        WinFormsUiScope.ThrowFailures(failures);
    }

    private void TryComplete()
    {
        if (!_uiDisposed || _modalActive || _children.Count != 0 || _completed)
        {
            return;
        }

        _completed = true;
        List<Exception> failures = [];
        WinFormsUiScope.Capture(failures, _scope.Dispose);
        _owner?._children.Remove(this);
        _application.FormCompleted(this);
        if (_owner is not null)
        {
            WinFormsUiScope.Capture(failures, _owner.TryComplete);
        }

        WinFormsUiScope.Capture(failures, _application.TryComplete);
        WinFormsUiScope.ThrowFailures(failures);
    }
}
