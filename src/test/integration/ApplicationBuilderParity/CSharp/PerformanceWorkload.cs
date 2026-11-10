// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Windows.Forms;
using Microsoft.Extensions.WinForms;

namespace ApplicationBuilderParity.CSharp;

/// <summary>
///  Composes measured shared lifetimes directly in C#, without a second host or service provider.
/// </summary>
internal static class PerformanceWorkload
{
    internal static void Run(string scenario, string[] args)
    {
        Probe.Require(args[0] == "shared", "The VB worker supplies the native legacy baseline.");
        if (scenario == "measure-ipc")
        {
            BenchmarkMeasurements.RunIpc(args, RunCycle);
        }
        else
        {
            BenchmarkMeasurements.Run(args, RunCycle);
        }
    }

    private static void RunCycle(BenchmarkCycle cycle)
    {
        WinFormsSingleInstance? primary = null;
        if (cycle.Ipc)
        {
            WinFormsSingleInstanceSelection selection = WinFormsSingleInstance.SelectAsync(
                WinFormsSingleInstanceIdentity.Create(cycle.Identity, "14981"), cycle.Tokens,
                false, TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
            cycle.DeliveryResult = selection.Result.ToString();
            primary = selection.Primary;
            if (primary is null)
            {
                return;
            }
        }

        using (primary)
        {
            MeasuredNetworkSource network = new();
            WinFormsApplicationBuilder builder = WinFormsApplication.CreateBuilder()
                .UseApplicationContext(() => new ApplicationContext(cycle.CreateForm()))
                .ConfigureApplicationFeatures(features =>
                {
                    if (cycle.Splash)
                    {
                        features.SplashScreenFactory = () => new WinFormsSplashScreen();
                        features.MinimumSplashDisplayTime = TimeSpan.Zero;
                    }

                    if (cycle.Network)
                    {
                        features.NetworkSource = network;
                        features.NetworkAvailabilityChanged = _ => { };
                    }
                });
            if (primary is not null)
            {
                builder.UseSingleInstance(primary, (_, request) =>
                {
                    cycle.Activated(request.CommandLine);
                    request.BringToForeground = false;
                });
            }

            using (WinFormsApplication application = builder.Build())
            {
                cycle.ApplicationReference.Target = application;
                application.Run();
            }

            cycle.SubscriptionsAfterRun = network.Subscriptions;
        }
    }
}
