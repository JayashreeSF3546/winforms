// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.ComponentModel;
using System.Diagnostics;
using System.Windows.Forms;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.WinForms;

namespace ApplicationBuilderSample.DiScopes;

/// <summary>
///  The startup Form demonstrates root activation and Designer-created descendants.
/// </summary>
internal sealed partial class MainForm : Form
{
    private readonly SessionService? _session;
    private readonly WinFormsApplication? _application;

    /// <summary>
    ///  Initializes the Form for Designer construction.
    /// </summary>
    public MainForm()
    {
        InitializeComponent();
    }

    /// <summary>
    ///  Initializes the Form with its activation-scoped service and navigator.
    /// </summary>
    /// <param name="session">The startup Form's scoped service.</param>
    /// <param name="application">The application activation coordinator.</param>
    public MainForm(SessionService session, WinFormsApplication application)
        : this()
    {
        _session = session;
        _application = application;
        _sessionSummary!.Session = session;
        _sessionDiagnostics!.Session = session;
        _statusLabel!.Text = $"Startup Form scope: {session.Id:N}";
    }

    private void OnOpenModelessClick(object? sender, EventArgs e)
        => OpenDetails();

    private void OnReviewClick(object? sender, EventArgs e)
    {
        SessionService session = GetSession();
        WinFormsApplication application = GetApplication();
        DialogResult result = application.ShowDialog(
            services => ActivatorUtilities.CreateInstance<ReviewDialog>(services, session.Id),
            this);

        if (result == DialogResult.OK)
        {
            OpenDetails();
        }
        else
        {
            Debug.WriteLine("Navigation canceled in the modal dialog.");
        }
    }

    private void OnCloseClick(object? sender, EventArgs e)
        => Close();

    private void OpenDetails()
    {
        SessionService session = GetSession();
        WinFormsApplication application = GetApplication();
        DetailsForm detailsForm = application.CreateForm(
            services => ActivatorUtilities.CreateInstance<DetailsForm>(services, session.Id));
        detailsForm.Show(this);
    }

    private SessionService GetSession()
        => _session
            ?? throw new InvalidOperationException("Runtime services are not assigned to this Form.");

    private WinFormsApplication GetApplication()
        => _application
            ?? throw new InvalidOperationException("The Form is not running in the sample application.");
}

/// <summary>
///  A modeless Form activated with an independent service scope.
/// </summary>
internal sealed partial class DetailsForm : Form
{
    /// <summary>
    ///  Initializes the Form for Designer construction.
    /// </summary>
    public DetailsForm()
    {
        InitializeComponent();
    }

    /// <summary>
    ///  Initializes the Form with its own scope and the caller's session identity.
    /// </summary>
    /// <param name="session">The modeless Form's scoped service.</param>
    /// <param name="parentSessionId">The session that navigated to this Form.</param>
    public DetailsForm(SessionService session, Guid parentSessionId)
        : this()
    {
        _sessionLabel!.Text = $"Modeless Form scope: {session.Id:N}";
        _parentSessionLabel!.Text = $"Opened from scope: {parentSessionId:N}";
    }
}

/// <summary>
///  A modal Form that accepts or cancels navigation.
/// </summary>
internal sealed partial class ReviewDialog : Form
{
    /// <summary>
    ///  Initializes the dialog for Designer construction.
    /// </summary>
    public ReviewDialog()
    {
        InitializeComponent();
    }

    /// <summary>
    ///  Initializes the dialog with its own scope and the caller's session identity.
    /// </summary>
    /// <param name="session">The dialog's scoped service.</param>
    /// <param name="parentSessionId">The session that opened the dialog.</param>
    public ReviewDialog(SessionService session, Guid parentSessionId)
        : this()
    {
        _scopeLabel!.Text = $"Modal dialog scope: {session.Id:N}";
        _parentSessionLabel!.Text = $"Opened from scope: {parentSessionId:N}";
    }
}

/// <summary>
///  A Designer-created UserControl that receives its Form's scoped service explicitly.
/// </summary>
internal sealed partial class SessionSummaryControl : UserControl
{
    /// <summary>
    ///  Initializes the UserControl for Designer construction.
    /// </summary>
    public SessionSummaryControl()
    {
        InitializeComponent();
    }

    /// <summary>
    ///  Gets or sets the service explicitly assigned by the owning Form.
    /// </summary>
    internal SessionService? Session
    {
        get;
        set
        {
            field = value;
            UpdateSummary();
        }
    }

    private void UpdateSummary()
    {
        _summaryLabel?.Text = Session is null
            ? "Designer-created UserControl (no runtime scope assigned)"
            : $"UserControl uses Form scope: {Session.Id:N}";
    }
}

/// <summary>
///  A Designer-created component that reports disposal while its Form scope is alive.
/// </summary>
internal sealed class SessionDiagnosticsComponent : Component
{
    /// <summary>
    ///  Initializes a component for Designer construction.
    /// </summary>
    public SessionDiagnosticsComponent()
    {
    }

    /// <summary>
    ///  Initializes and adds the component to the Designer's component container.
    /// </summary>
    /// <param name="container">The owning Form's component container.</param>
    public SessionDiagnosticsComponent(IContainer container)
    {
        ArgumentNullException.ThrowIfNull(container);
        container.Add(this);
    }

    /// <summary>
    ///  Gets or sets the scoped service explicitly assigned by the owning Form.
    /// </summary>
    internal SessionService? Session { get; set; }

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            string state = Session is null
                ? "unassigned"
                : Session.IsDisposed
                    ? "already disposed"
                    : "still alive";
            Debug.WriteLine($"Designer-created component disposed; Form scope is {state}.");
        }

        base.Dispose(disposing);
    }
}
