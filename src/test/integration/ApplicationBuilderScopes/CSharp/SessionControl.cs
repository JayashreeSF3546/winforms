// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Windows.Forms;

namespace ApplicationBuilderScopes.CSharp;

/// <summary>
///  Borrows a typed service without resolving services or creating a control-owned scope.
/// </summary>
public partial class SessionControl : UserControl
{
    public SessionControl() => InitializeComponent();

    internal ScopeSession? Session { get; private set; }

    internal void InitializeServices(ScopeSession session)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        ArgumentNullException.ThrowIfNull(session);
        if (Session is not null)
        {
            throw new InvalidOperationException("Services have already been assigned.");
        }

        Session = session;
        identityLabel!.Text = $"Form/UserControl/component share session: {session.Id}";
        Disposed += (_, _) =>
        {
            session.VerifyAlive();
            Console.WriteLine($"UserControl disposed: {session.Id}");
        };
    }
}
