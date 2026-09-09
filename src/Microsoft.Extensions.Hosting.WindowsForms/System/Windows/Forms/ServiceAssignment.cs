// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.ComponentModel;
using System.Windows.Forms;

namespace Microsoft.Extensions.WinForms;

/// <summary>
///  Walks an activated object graph and assigns the owning scope's service provider to every
///  object that implements <see cref="IServiceProviderAssignable"/>.
/// </summary>
/// <remarks>
///  <para>
///   The walk visits the object itself, its child controls recursively, and the components of any
///   <see cref="IContainer"/> it encounters. This covers everything <c>InitializeComponent</c>
///   creates, because those children already exist when the owner's constructor returns.
///  </para>
///  <para>
///   The walk deliberately does not subscribe to <see cref="Control.ControlAdded"/>. Objects
///   created after activation are assigned explicitly through
///   <see cref="IUIActivator.AssignServices(object)"/> or <see cref="UIScope.AssignServices(object)"/>,
///   which keeps the default path free of event subscriptions.
///  </para>
/// </remarks>
internal static class ServiceAssignment
{
    internal static int Assign(IServiceProvider services, object target)
    {
        HashSet<object> visited = new(ReferenceEqualityComparer.Instance);
        int assigned = 0;
        Visit(services, target, visited, ref assigned);

        return assigned;
    }

    private static void Visit(
        IServiceProvider services,
        object? target,
        HashSet<object> visited,
        ref int assigned)
    {
        if (target is null || !visited.Add(target))
        {
            return;
        }

        // Design-time objects never receive application services, and the walk does not descend
        // into them.
        if (target is IComponent { Site.DesignMode: true })
        {
            return;
        }

        if (target is IServiceProviderAssignable assignable)
        {
            assignable.AssignServiceProvider(services);
            assigned++;
            UIScopeDiagnostics.OnServiceAssigned();
        }

        if (target is Control control)
        {
            foreach (Control child in control.Controls)
            {
                Visit(services, child, visited, ref assigned);
            }
        }

        if (target is IContainer container)
        {
            foreach (IComponent component in container.Components)
            {
                Visit(services, component, visited, ref assigned);
            }
        }
    }
}
