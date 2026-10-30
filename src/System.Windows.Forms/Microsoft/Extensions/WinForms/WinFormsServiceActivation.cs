// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.ComponentModel;
using System.Windows.Forms;

namespace Microsoft.Extensions.WinForms;

/// <summary>
///  Prototypes explicit service composition without exposing a provider on UI objects.
/// </summary>
/// <remarks>
///  <para>
///   The provider is borrowed. This helper neither creates scopes nor owns resolved dependencies.
///   Factories must return fresh, caller-thread-owned components, not container-owned UI instances.
///  </para>
///  <para>
///   An existing handle can be checked for thread ownership. A handleless component's creation thread
///   is a caller obligation, not something the helper can establish.
///  </para>
/// </remarks>
internal static class WinFormsServiceActivation
{
    internal static TComponent Create<TComponent>(
        IServiceProvider services,
        Func<IServiceProvider, TComponent> factory,
        Action<TComponent, IServiceProvider>? assignServices = null)
        where TComponent : class, IComponent
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(factory);
        VerifyRuntime();

        TComponent component = factory(services)
            ?? throw new InvalidOperationException(SR.WinFormsApplication_NullComponent);

        // Validate ownership before attempting cleanup; a foreign-thread or designer object is not ours.
        VerifyComponent(component);
        try
        {
            assignServices?.Invoke(component, services);
            VerifyComponent(component);

            return component;
        }
        catch (Exception activationFailure) when (!activationFailure.IsCriticalException())
        {
            try
            {
                component.Dispose();
            }
            catch (Exception cleanupFailure) when (!cleanupFailure.IsCriticalException())
            {
                throw new AggregateException(activationFailure, cleanupFailure);
            }

            throw;
        }
    }

    internal static void Assign<TComponent>(
        TComponent component,
        IServiceProvider services,
        Action<TComponent, IServiceProvider> assignServices)
        where TComponent : class, IComponent
    {
        ArgumentNullException.ThrowIfNull(component);
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(assignServices);
        VerifyRuntime();
        VerifyComponent(component);
        assignServices(component, services);
        VerifyComponent(component);
    }

    internal static void VerifyRuntime()
    {
        if (LicenseManager.UsageMode == LicenseUsageMode.Designtime)
        {
            throw new InvalidOperationException(SR.WinFormsApplication_DesignTimeServices);
        }

        if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
        {
            throw new InvalidOperationException(SR.ThreadMustBeSTA);
        }
    }

    private static void VerifyComponent(IComponent component)
    {
        if (component.Site?.DesignMode == true
            || component is Control { IsAncestorSiteInDesignMode: true })
        {
            throw new InvalidOperationException(SR.WinFormsApplication_DesignTimeServices);
        }

        if (component is Control control)
        {
            ObjectDisposedException.ThrowIf(control.IsDisposed, control);
            if (control.IsHandleCreated && control.CreateThreadId != PInvokeCore.GetCurrentThreadId())
            {
                throw new InvalidOperationException(SR.WinFormsApplication_WrongThread);
            }
        }
    }
}
