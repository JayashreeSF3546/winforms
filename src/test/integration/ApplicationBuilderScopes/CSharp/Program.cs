// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.WinForms;

namespace ApplicationBuilderScopes.CSharp;

/// <summary>
///  Runs the repository-only scoped activation prototype with observable service disposal.
/// </summary>
internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        bool smoke = false;
        bool cancel = false;
        bool componentManager = false;
        string theme = "classic";
        SystemColorMode color = SystemColorMode.Classic;
        foreach (string argument in args)
        {
            switch (argument)
            {
                case "--smoke": smoke = true; break;
                case "--cancel": cancel = true; break;
                case "--component-manager": componentManager = true; break;
                case "--classic": color = SystemColorMode.Classic; theme = "classic"; break;
                case "--light": color = SystemColorMode.Classic; theme = "light"; break;
                case "--dark": color = SystemColorMode.Dark; theme = "dark"; break;
                default: throw new ArgumentException($"Unknown option: {argument}");
            }
        }

        if (cancel && !smoke)
        {
            throw new ArgumentException("--cancel requires --smoke; interactive windows have a Cancel application button.");
        }

        AppContext.SetSwitch("Switch.System.Windows.Forms.EnableMsoComponentManager", componentManager);
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        if (theme != "classic")
        {
            Application.EnableVisualStyles();
        }

        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetColorMode(color);
        Console.WriteLine($"Environment: {RuntimeInformation.ProcessArchitecture}; terminal={SystemInformation.TerminalServerSession}");
        Console.WriteLine($"Color: {theme}; component-manager={componentManager}");
        List<ScopeSession> sessions = [];
        using CancellationTokenSource cancellation = new();
        SampleContext? context = null;
        using WinFormsApplication application = WinFormsApplication.CreateBuilder()
            .UseHost(new HostBuilder().ConfigureServices((_, services) => services.AddScoped(_ =>
            {
                ScopeSession session = new();
                sessions.Add(session);
                return session;
            })))
            .UseScopedApplicationContext((services, scopes) =>
                context = new SampleContext(scopes, services.GetRequiredService<ScopeSession>(), cancellation, smoke, cancel))
            .Build();
        try
        {
            application.Run(cancellation.Token);
        }
        catch (OperationCanceledException exception) when (
            cancellation.IsCancellationRequested && exception.CancellationToken == cancellation.Token)
        {
            Console.WriteLine("Run canceled after UI cleanup");
        }

        if (context?.Failure is { } failure)
        {
            ExceptionDispatchInfo.Throw(failure);
        }

        if (sessions.Count == 0 || sessions.Any(session => session.DisposeCount != 1))
        {
            throw new InvalidOperationException("Every created session must be disposed exactly once.");
        }

        Console.WriteLine($"PASS: {sessions.Count} sessions disposed exactly once");
    }
}

/// <summary>
///  Keeps navigation and explicit lifetime ownership at the composition boundary, not on controls.
/// </summary>
internal sealed class SampleContext : ApplicationContext
{
    private readonly WinFormsUiScope _scopes;
    private readonly ScopeSession _applicationSession;
    private readonly CancellationTokenSource _cancellation;
    private readonly bool _cancelSmoke;
    private WinFormsFormScope _current;
    private int _page = 1;

    internal SampleContext(WinFormsUiScope scopes, ScopeSession applicationSession,
        CancellationTokenSource cancellation, bool smoke, bool cancelSmoke)
    {
        _scopes = scopes;
        _applicationSession = applicationSession;
        _cancellation = cancellation;
        _cancelSmoke = cancelSmoke;
        _current = CreateWindow("Page 1", owner: null);
        MainForm = _current.Form;
        if (smoke)
        {
            MainForm.Shown += (_, _) => MainForm.BeginInvoke(RunSmoke);
        }
    }

    internal Exception? Failure { get; private set; }

    private WinFormsFormScope CreateWindow(string title, WinFormsFormScope? owner)
    {
        WinFormsFormScope lifetime = _scopes.CreateForm(
            services => new ScopeForm(services.GetRequiredService<ScopeSession>(), title), owner);
        var form = (ScopeForm)lifetime.Form;
        lifetime.Assign(form.SessionView, static (target, services) => target.InitializeServices(services.GetRequiredService<ScopeSession>()));
        lifetime.Assign(form.SessionWorker, static (target, services) => target.InitializeServices(services.GetRequiredService<ScopeSession>()));
        form.VerifyAssignment();
        if (ReferenceEquals(form.Session, _applicationSession))
        {
            throw new InvalidOperationException("A form must not borrow the application scope.");
        }

        form.ConfigureNavigation(
            () => CreateWindow("Modeless", lifetime).Show(),
            () => ShowModal(lifetime, automatic: false),
            owner is null ? Navigate : null,
            _cancellation.Cancel);
        return lifetime;
    }

    private void ShowModal(WinFormsFormScope owner, bool automatic)
    {
        using WinFormsFormScope dialog = CreateWindow("Modal", owner);
        if (automatic)
        {
            dialog.Form.Shown += (_, _) => dialog.Form.BeginInvoke(() => dialog.Form.DialogResult = DialogResult.OK);
        }

        DialogResult result = dialog.ShowDialog();
        Console.WriteLine($"Modal result: {result}");
        ((ScopeForm)owner.Form).Session!.VerifyAlive();
        _applicationSession.VerifyAlive();
    }

    private void Navigate()
    {
        WinFormsFormScope previous = _current;
        WinFormsFormScope next = CreateWindow($"Page {++_page}", owner: null);
        _current = next;
        MainForm = next.Form;
        next.Show();
        previous.Form.Close();
        Console.WriteLine("Navigation completed");
    }

    private void RunSmoke()
    {
        try
        {
            using WinFormsFormScope modeless = CreateWindow("Modeless", _current);
            modeless.Show();
            var child = (ScopeForm)modeless.Form;
            if (ReferenceEquals(child.Session, ((ScopeForm)_current.Form).Session))
            {
                throw new InvalidOperationException("Independent forms must have distinct scoped services.");
            }

            modeless.Form.Close();
            if (child.Session!.DisposeCount != 1)
            {
                throw new InvalidOperationException("Closing a modeless window must release its scope.");
            }

            Console.WriteLine("Modeless scope completed");
            ShowModal(_current, automatic: true);
            Navigate();
            _current.Form.BeginInvoke(() =>
            {
                if (_cancelSmoke)
                {
                    _cancellation.Cancel();
                }
                else
                {
                    _current.Form.Close();
                }
            });
        }
        catch (Exception exception)
        {
            Failure = exception;
            ExitThread();
        }
    }
}

/// <summary>
///  Supplies a scoped identity and deterministic disposal diagnostics without external resources.
/// </summary>
internal sealed class ScopeSession : IDisposable
{
    internal Guid Id { get; } = Guid.NewGuid();

    internal int DisposeCount { get; private set; }

    internal ScopeSession() => Console.WriteLine($"Session created: {Id}");

    internal void VerifyAlive()
        => ObjectDisposedException.ThrowIf(DisposeCount != 0, this);

    public void Dispose()
    {
        DisposeCount++;
        Console.WriteLine($"Session disposed: {Id}");
    }
}
