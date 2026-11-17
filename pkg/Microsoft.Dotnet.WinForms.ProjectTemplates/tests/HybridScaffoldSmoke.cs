using Company.WinFormsHybridApp1;
using Microsoft.AspNetCore.Components.WebView.WindowsForms;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.WinForms;

/// <summary>
///  Verifies the scaffold's activation barrier and native-only construction, not browser integration.
/// </summary>
internal static class HybridScaffoldSmoke
{
    [STAThread]
    internal static int Main()
    {
        try
        {
            ExpectUnavailable(() =>
            {
                using WinFormsApplication application = Program.CreateApplication([]);
            });
            ExpectUnavailable(() =>
            {
                using HybridForm runtimeForm = HybridForm.CreateRuntime(
                    new UnavailableServices(), new ApplicationOptions(), NullLogger<HybridForm>.Instance);
            });

            ApplicationConfiguration.Initialize();
            using HybridForm form = new();
            form.CreateControl();
            VerifyNativeOnly(form);
            Console.WriteLine("PASS Hybrid activation gate and native-only Form construction.");

            return 0;
        }
        catch (Exception exception) when (exception is not (OutOfMemoryException or StackOverflowException or AccessViolationException))
        {
            Console.Error.WriteLine(exception);

            return 1;
        }
    }

    private static void ExpectUnavailable(Action action)
    {
        try
        {
            action();
        }
        catch (NotSupportedException)
        {
            return;
        }

        throw new InvalidOperationException("The gated Hybrid scaffold unexpectedly allowed runtime activation.");
    }

    private static void VerifyNativeOnly(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            if (child is BlazorWebView)
            {
                throw new InvalidOperationException("Designer-safe construction must not create a WebView.");
            }

            VerifyNativeOnly(child);
        }
    }

    /// <summary>
    ///  Fails if runtime activation reaches service resolution instead of the readiness barrier.
    /// </summary>
    private sealed class UnavailableServices : IServiceProvider
    {
        public object? GetService(Type serviceType)
            => throw new InvalidOperationException("The Hybrid activation gate must precede service resolution.");
    }
}
