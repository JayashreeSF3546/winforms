// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Windows.Forms;

namespace Microsoft.Extensions.WinForms;

/// <summary>
///  Creates Windows Forms objects inside a child <see cref="UIScope"/> of the scope that owns the
///  activator.
/// </summary>
/// <remarks>
///  <para>
///   The activator is registered as a scoped service. Resolve it from
///   <see cref="UIScope.Services"/> - or from the service provider an object received through
///   <see cref="IServiceProviderAssignable"/> - so that the objects it creates are owned by the
///   right scope. Use <c>IServiceProvider.GetApplicationActivator()</c> for the application scope.
///  </para>
///  <para>
///   Activation precedence is: a type registered in the container is resolved from the container
///   and disposed by the container; every other type is constructed with
///   <c>ActivatorUtilities.CreateInstance</c> and disposed by the UI scope.
///  </para>
/// </remarks>
public interface IUIActivator
{
    /// <summary>
    ///  Gets the scope that owns this activator and parents everything it creates.
    /// </summary>
    UIScope Scope { get; }

    /// <summary>
    ///  Creates a form in a new child scope.
    /// </summary>
    /// <typeparam name="TForm">The form type.</typeparam>
    /// <param name="scopeKind">The kind recorded on the new scope.</param>
    /// <param name="scopeMode">Whether the new scope creates its own service scope.</param>
    /// <param name="cancellationToken">Cancels the activation.</param>
    /// <returns>A handle that owns the form and its scope.</returns>
    UIActivation<TForm> CreateForm<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TForm>(
        UIScopeKind scopeKind = UIScopeKind.Form,
        UIServiceScopeMode scopeMode = UIServiceScopeMode.Isolated,
        CancellationToken cancellationToken = default)
        where TForm : Form;

    /// <summary>
    ///  Creates a control in a new child scope.
    /// </summary>
    /// <typeparam name="TControl">The control type.</typeparam>
    /// <param name="scopeKind">The kind recorded on the new scope.</param>
    /// <param name="scopeMode">Whether the new scope creates its own service scope.</param>
    /// <param name="cancellationToken">Cancels the activation.</param>
    /// <returns>A handle that owns the control and its scope.</returns>
    UIActivation<TControl> CreateControl<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TControl>(
        UIScopeKind scopeKind = UIScopeKind.Control,
        UIServiceScopeMode scopeMode = UIServiceScopeMode.Isolated,
        CancellationToken cancellationToken = default)
        where TControl : Control;

    /// <summary>
    ///  Creates a component in a new child scope.
    /// </summary>
    /// <typeparam name="TComponent">The component type.</typeparam>
    /// <param name="scopeKind">The kind recorded on the new scope.</param>
    /// <param name="scopeMode">Whether the new scope creates its own service scope.</param>
    /// <param name="cancellationToken">Cancels the activation.</param>
    /// <returns>A handle that owns the component and its scope.</returns>
    UIActivation<TComponent> CreateComponent<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TComponent>(
        UIScopeKind scopeKind = UIScopeKind.Component,
        UIServiceScopeMode scopeMode = UIServiceScopeMode.Isolated,
        CancellationToken cancellationToken = default)
        where TComponent : class, IComponent;

    /// <summary>
    ///  Creates an object with a factory in a new child scope.
    /// </summary>
    /// <typeparam name="T">The activated type.</typeparam>
    /// <param name="factory">Creates the instance from the new scope's service provider.</param>
    /// <param name="scopeKind">The kind recorded on the new scope.</param>
    /// <param name="scopeMode">Whether the new scope creates its own service scope.</param>
    /// <param name="name">An optional diagnostic scope name.</param>
    /// <param name="cancellationToken">Cancels the activation.</param>
    /// <returns>A handle that owns the instance and its scope.</returns>
    UIActivation<T> Create<T>(
        Func<IServiceProvider, T> factory,
        UIScopeKind scopeKind,
        UIServiceScopeMode scopeMode = UIServiceScopeMode.Isolated,
        string? name = null,
        CancellationToken cancellationToken = default)
        where T : class;

    /// <summary>
    ///  Creates and shows a modeless form. The scope is disposed when the form is disposed.
    /// </summary>
    /// <typeparam name="TForm">The form type.</typeparam>
    /// <param name="cancellationToken">Cancels the activation.</param>
    /// <returns>A handle that owns the form and its scope.</returns>
    UIActivation<TForm> Show<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TForm>(
        CancellationToken cancellationToken = default)
        where TForm : Form;

    /// <summary>
    ///  Creates and shows a modal dialog, and disposes its scope when the dialog closes.
    /// </summary>
    /// <typeparam name="TForm">The dialog type.</typeparam>
    /// <param name="owner">The dialog owner, or <see langword="null"/>.</param>
    /// <param name="initialize">
    ///  An optional callback invoked after activation and before the dialog is shown.
    /// </param>
    /// <param name="cancellationToken">Cancels the activation.</param>
    /// <returns>The dialog result.</returns>
    /// <remarks>
    ///  <para>
    ///   Use <see cref="CreateForm{TForm}(UIScopeKind, UIServiceScopeMode, CancellationToken)"/>
    ///   instead when values must be read from the dialog after it closes.
    ///  </para>
    /// </remarks>
    DialogResult ShowDialog<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TForm>(
        IWin32Window? owner = null,
        Action<TForm>? initialize = null,
        CancellationToken cancellationToken = default)
        where TForm : Form;

    /// <summary>
    ///  Assigns this scope's service provider to an object that was created outside an activation,
    ///  and to its reachable Designer-created children.
    /// </summary>
    /// <param name="target">The object to assign services to.</param>
    /// <returns>The number of objects that received the service provider.</returns>
    int AssignServices(object target);
}
