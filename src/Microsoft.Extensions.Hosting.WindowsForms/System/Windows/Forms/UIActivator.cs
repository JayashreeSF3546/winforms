// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Windows.Forms;

namespace Microsoft.Extensions.WinForms;

/// <summary>
///  The scoped implementation of <see cref="IUIActivator"/>.
/// </summary>
internal sealed partial class UIActivator : IUIActivator
{
    private readonly ILogger _logger;

    /// <summary>
    ///  Initializes a new instance of the <see cref="UIActivator"/> class.
    /// </summary>
    /// <param name="services">The scope's service provider.</param>
    /// <param name="scopeHolder">The holder that binds the scope to its service provider.</param>
    /// <remarks>
    ///  <para>
    ///   The constructor is public because the dependency-injection container only considers public
    ///   constructors, even for internal implementation types.
    ///  </para>
    /// </remarks>
    public UIActivator(IServiceProvider services, UIScopeHolder scopeHolder)
    {
        Scope = scopeHolder.Scope
            ?? throw new InvalidOperationException(
                "The current dependency-injection scope is not a Windows Forms UI scope. Resolve "
                + $"{nameof(IUIActivator)} from {nameof(UIScope)}.{nameof(UIScope.Services)}, or use "
                + "IServiceProvider.GetApplicationActivator().");

        _logger = services.GetService<ILoggerFactory>()?.CreateLogger(typeof(UIActivator).FullName!)
            ?? NullLogger.Instance;
    }

    public UIScope Scope { get; }

    public UIActivation<TForm> CreateForm<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TForm>(
        UIScopeKind scopeKind = UIScopeKind.Form,
        UIServiceScopeMode scopeMode = UIServiceScopeMode.Isolated,
        CancellationToken cancellationToken = default)
        where TForm : Form
        => Activate(scopeKind, scopeMode, typeof(TForm).Name, ResolveOrConstruct<TForm>, cancellationToken);

    public UIActivation<TControl> CreateControl<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TControl>(
        UIScopeKind scopeKind = UIScopeKind.Control,
        UIServiceScopeMode scopeMode = UIServiceScopeMode.Isolated,
        CancellationToken cancellationToken = default)
        where TControl : Control
        => Activate(scopeKind, scopeMode, typeof(TControl).Name, ResolveOrConstruct<TControl>, cancellationToken);

    public UIActivation<TComponent> CreateComponent<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TComponent>(
        UIScopeKind scopeKind = UIScopeKind.Component,
        UIServiceScopeMode scopeMode = UIServiceScopeMode.Isolated,
        CancellationToken cancellationToken = default)
        where TComponent : class, IComponent
        => Activate(scopeKind, scopeMode, typeof(TComponent).Name, ResolveOrConstruct<TComponent>, cancellationToken);

    public UIActivation<T> Create<T>(
        Func<IServiceProvider, T> factory,
        UIScopeKind scopeKind,
        UIServiceScopeMode scopeMode = UIServiceScopeMode.Isolated,
        string? name = null,
        CancellationToken cancellationToken = default)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(factory);

        return Activate(
            scopeKind,
            scopeMode,
            name ?? typeof(T).Name,
            services => (factory(services), OwnedByScope: true),
            cancellationToken);
    }

    public UIActivation<TForm> Show<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TForm>(
        CancellationToken cancellationToken = default)
        where TForm : Form
    {
        UIActivation<TForm> activation = CreateForm<TForm>(
            UIScopeKind.Form,
            UIServiceScopeMode.Isolated,
            cancellationToken);

        try
        {
            // A modeless form disposes itself when it is closed, so the scope follows the form.
            activation.Instance.Disposed += (_, _) => activation.Dispose();
            activation.Instance.Show();

            return activation;
        }
        catch
        {
            activation.Dispose();
            throw;
        }
    }

    public DialogResult ShowDialog<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TForm>(
        IWin32Window? owner = null,
        Action<TForm>? initialize = null,
        CancellationToken cancellationToken = default)
        where TForm : Form
    {
        UIActivation<TForm> activation = CreateForm<TForm>(
            UIScopeKind.Dialog,
            UIServiceScopeMode.Isolated,
            cancellationToken);

        try
        {
            initialize?.Invoke(activation.Instance);
            cancellationToken.ThrowIfCancellationRequested();

            return activation.Instance.ShowDialog(owner);
        }
        finally
        {
            activation.Dispose();
        }
    }

    public int AssignServices(object target)
    {
        ArgumentNullException.ThrowIfNull(target);
        DesignTimeGuard.ThrowIfDesignTime();

        return Scope.AssignServices(target);
    }

    private static (T Instance, bool OwnedByScope) ResolveOrConstruct<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>(
        IServiceProvider services)
        where T : class
    {
        // A registered type keeps its registered lifetime, and the container owns its disposal.
        IServiceProviderIsService? isService = services.GetService<IServiceProviderIsService>();

        return isService?.IsService(typeof(T)) == true
            ? (services.GetRequiredService<T>(), false)
            : (ActivatorUtilities.CreateInstance<T>(services), true);
    }

    private UIActivation<T> Activate<T>(
        UIScopeKind scopeKind,
        UIServiceScopeMode scopeMode,
        string name,
        Func<IServiceProvider, (T Instance, bool OwnedByScope)> factory,
        CancellationToken cancellationToken)
        where T : class
    {
        DesignTimeGuard.ThrowIfDesignTime();
        cancellationToken.ThrowIfCancellationRequested();

        UIScope scope = Scope.CreateChildScope(scopeKind, scopeMode, name);

        try
        {
            (T instance, bool ownedByScope) = factory(scope.Services);

            if (instance is null)
            {
                throw new InvalidOperationException(
                    $"The activation factory for '{typeof(T).FullName}' returned null.");
            }

            // The instance is attached before anything else can fail, so rollback disposes it.
            scope.AttachOwner(instance, ownedByScope);
            cancellationToken.ThrowIfCancellationRequested();
            scope.AssignServices(instance);

            UIScopeDiagnostics.OnActivation();
            LogActivated(typeof(T).FullName, scope.Kind, scope.Name);

            return new UIActivation<T>(instance, scope);
        }
        catch (Exception exception)
        {
            UIScopeDiagnostics.OnActivationFailure();
            LogActivationFailed(typeof(T).FullName, scope.Name, exception);
            RollBack(scope);

            throw;
        }
    }

    private void RollBack(UIScope scope)
    {
        try
        {
            scope.Dispose();
        }
        catch (Exception exception)
        {
            // A failure while rolling back must not replace the original activation failure.
            LogRollbackFailed(scope.Name, exception);
        }
    }

    [LoggerMessage(
        EventId = 100,
        Level = LogLevel.Debug,
        Message = "Activated '{TypeName}' in a {ScopeKind} UI scope named '{ScopeName}'.")]
    private partial void LogActivated(string? typeName, UIScopeKind scopeKind, string scopeName);

    [LoggerMessage(
        EventId = 101,
        Level = LogLevel.Error,
        Message = "Activation of '{TypeName}' failed and the '{ScopeName}' UI scope was rolled back.")]
    private partial void LogActivationFailed(string? typeName, string scopeName, Exception exception);

    [LoggerMessage(
        EventId = 102,
        Level = LogLevel.Warning,
        Message = "Rolling back the '{ScopeName}' UI scope failed.")]
    private partial void LogRollbackFailed(string scopeName, Exception exception);
}
