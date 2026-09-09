// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.WinForms;

namespace System.Windows.Forms.Tests;

/// <summary>
///  Resource-stability coverage for repeated scoped activation.
/// </summary>
/// <remarks>
///  <para>
///   The assertions here are deterministic: scope counts, scoped-service disposal counts, and
///   managed retention. Process-level GDI/USER measurements are reported as output rather than
///   asserted, because a shared test process cannot isolate them. The
///   <c>ScopeStress</c> sample produces the full measurement table.
///  </para>
/// </remarks>
[Collection(WinFormsUIScopeCollection.Name)]
public class WinFormsUIScopeStressTests(ITestOutputHelper output)
{
    private const int ActivationIterations = 300;
    private const int WindowIterations = 25;

    private static ServiceProvider CreateServices(Action<IServiceCollection>? configure = null)
    {
        ServiceCollection services = new();
        services.AddWindowsFormsActivation();
        configure?.Invoke(services);

        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true
        });
    }

    [WinFormsFact]
    public void RepeatedActivation_ReleasesEveryScope()
    {
        using ServiceProvider provider = CreateServices();
        IUIActivator activator = provider.GetApplicationActivator();

        int scopesBefore = UIScopeDiagnostics.ActiveScopeCount;
        long activationsBefore = UIScopeDiagnostics.TotalActivations;
        using Process process = Process.GetCurrentProcess();
        int handlesBefore = process.HandleCount;

        for (int i = 0; i < ActivationIterations; i++)
        {
            using UIActivation<StressForm> activation = activator.CreateForm<StressForm>();
            using UIActivation<StressControl> nested = activation.Scope.Services
                .GetRequiredService<IUIActivator>()
                .CreateControl<StressControl>();

            activation.Instance.Controls.Add(nested.Instance);
        }

        process.Refresh();
        int handlesAfter = process.HandleCount;

        output.WriteLine(
            $"Activation cycles: {ActivationIterations}; process handles before/after: "
            + $"{handlesBefore}/{handlesAfter}.");

        Assert.Equal(scopesBefore, UIScopeDiagnostics.ActiveScopeCount);
        Assert.Equal(activationsBefore + (ActivationIterations * 2), UIScopeDiagnostics.TotalActivations);
        Assert.Empty(activator.Scope.Children);
    }

    [WinFormsFact]
    public void RepeatedActivation_DisposesScopedServicesEveryCycle()
    {
        DisposeCountingService.Reset();
        using ServiceProvider provider = CreateServices(
            services => services.AddScoped<DisposeCountingService>());
        IUIActivator activator = provider.GetApplicationActivator();

        for (int i = 0; i < ActivationIterations; i++)
        {
            using UIActivation<StressForm> activation = activator.CreateForm<StressForm>();
            activation.Scope.Services.GetRequiredService<DisposeCountingService>();
        }

        Assert.Equal(ActivationIterations, DisposeCountingService.Created);
        Assert.Equal(ActivationIterations, DisposeCountingService.Disposed);
    }

    [WinFormsFact]
    public void RepeatedShowAndClose_ReleasesScopesAndWindows()
    {
        using ServiceProvider provider = CreateServices();
        IUIActivator activator = provider.GetApplicationActivator();

        int scopesBefore = UIScopeDiagnostics.ActiveScopeCount;
        int openFormsBefore = Application.OpenForms.Count;

        for (int i = 0; i < WindowIterations; i++)
        {
            UIActivation<StressForm> activation = activator.Show<StressForm>();
            Assert.True(activation.Instance.IsHandleCreated);

            activation.Instance.Close();

            Assert.True(activation.IsDisposed);
        }

        Assert.Equal(scopesBefore, UIScopeDiagnostics.ActiveScopeCount);
        Assert.Equal(openFormsBefore, Application.OpenForms.Count);
        Assert.Empty(activator.Scope.Children);
    }

    [WinFormsFact]
    public void DisposedActivation_DoesNotRetainTheActivatedInstance()
    {
        using ServiceProvider provider = CreateServices();
        IUIActivator activator = provider.GetApplicationActivator();

        WeakReference reference = ActivateAndDispose(activator);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.False(reference.IsAlive);
        Assert.Empty(activator.Scope.Children);
    }

    [WinFormsFact]
    public void RepeatedNavigation_ReleasesEveryViewScope()
    {
        using ServiceProvider provider = CreateServices();
        IUIActivator activator = provider.GetApplicationActivator();
        using Panel region = new();
        using UINavigator navigator = new(activator, region);

        int scopesBefore = UIScopeDiagnostics.ActiveScopeCount;

        for (int i = 0; i < ActivationIterations; i++)
        {
            navigator.Navigate<StressControl>();

            Assert.Single(region.Controls);
        }

        navigator.Clear();

        Assert.Equal(scopesBefore, UIScopeDiagnostics.ActiveScopeCount);
        Assert.Empty(region.Controls);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference ActivateAndDispose(IUIActivator activator)
    {
        UIActivation<StressForm> activation = activator.CreateForm<StressForm>();
        WeakReference reference = new(activation.Instance);
        activation.Dispose();

        return reference;
    }

    /// <summary>
    ///  A scoped service that counts creation and disposal across activation cycles.
    /// </summary>
    private sealed class DisposeCountingService : IDisposable
    {
        public DisposeCountingService() => Created++;

        public static int Created { get; private set; }

        public static int Disposed { get; private set; }

        public static void Reset()
        {
            Created = 0;
            Disposed = 0;
        }

        public void Dispose() => Disposed++;
    }

    /// <summary>
    ///  A form used for repeated activation cycles.
    /// </summary>
    private sealed class StressForm : Form, IServiceProviderAssignable
    {
        public IServiceProvider? Services { get; private set; }

        public void AssignServiceProvider(IServiceProvider serviceProvider)
            => Services = serviceProvider;
    }

    /// <summary>
    ///  A control used for repeated activation and navigation cycles.
    /// </summary>
    private sealed class StressControl : UserControl, IServiceProviderAssignable
    {
        public IServiceProvider? Services { get; private set; }

        public void AssignServiceProvider(IServiceProvider serviceProvider)
            => Services = serviceProvider;
    }
}
