// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Windows.Forms;

namespace ApplicationBuilderScopes.CSharp;

/// <summary>
///  Preserves service-free Designer construction alongside explicit runtime constructor injection.
/// </summary>
public partial class ScopeForm : Form
{
    public ScopeForm() => InitializeComponent();

    internal ScopeForm(ScopeSession session, string title) : this()
    {
        Session = session;
        Text = $"{title} - C# - {session.Id}";
        Disposed += (_, _) =>
        {
            session.VerifyAlive();
            Console.WriteLine($"Form UI disposed: {session.Id}");
        };
    }

    internal ScopeSession? Session { get; }
    internal SessionControl SessionView => sessionView!;
    internal SessionComponent SessionWorker => sessionWorker!;

    internal void VerifyAssignment()
    {
        if (Session is null || !ReferenceEquals(Session, SessionView.Session)
            || !ReferenceEquals(Session, SessionWorker.Session))
        {
            throw new InvalidOperationException("Designer-created children must borrow their form's typed service.");
        }

        Console.WriteLine($"Typed assignment verified: {Session.Id}");
    }

    internal void ConfigureNavigation(Action modeless, Action modal, Action? next, Action cancel)
    {
        modelessButton!.Click += (_, _) => modeless();
        modalButton!.Click += (_, _) => modal();
        nextButton!.Enabled = next is not null;
        nextButton.Click += (_, _) => next!();
        cancelButton!.Click += (_, _) => cancel();
        closeButton!.Click += (_, _) => Close();
    }
}
