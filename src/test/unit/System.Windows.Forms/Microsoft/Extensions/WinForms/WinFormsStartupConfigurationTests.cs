// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.ComponentModel;
using System.Drawing;
using System.Runtime.ExceptionServices;
using System.Windows.Forms.VisualStyles;
using Microsoft.DotNet.RemoteExecutor;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.WinForms;

namespace System.Windows.Forms.Tests;

/// <summary>
///  Verifies fluent startup configuration without leaking process defaults into other tests.
/// </summary>
public class WinFormsStartupConfigurationTests
{
    [Fact]
    public void Configuration_InvalidSelections_DoNotMutateOrConsumeBuilder()
    {
        WinFormsApplicationBuilder builder = WinFormsApplication.CreateBuilder();
        Assert.Throws<InvalidEnumArgumentException>(() => builder.UseHighDpiMode((HighDpiMode)(-1)));
        Assert.Throws<InvalidEnumArgumentException>(() => builder.UseColorMode((SystemColorMode)(-1)));
        Assert.Throws<InvalidEnumArgumentException>(() => builder.UseFormRevealMode((FormRevealMode)(-2)));
        Assert.Throws<InvalidEnumArgumentException>(() => builder.UseVisualStylesMode((VisualStylesMode)(-2)));
        Assert.Throws<ArgumentException>(() => builder.UseVisualStylesMode(VisualStylesMode.Inherit));
        Assert.Throws<InvalidEnumArgumentException>(() => builder.UseVisualStyleState((VisualStyleState)(-1)));
        Assert.Throws<InvalidEnumArgumentException>(() => builder.UseUnhandledExceptionMode((UnhandledExceptionMode)(-1)));
        Assert.Throws<ArgumentNullException>(() => builder.UseDefaultFont(null!));

        using WinFormsApplication application = builder.UseApplicationContext(() => new ApplicationContext()).Build();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    public void Configuration_RequiresOwnerAndUnbuiltBuilder(int selection)
    {
        WinFormsApplicationBuilder builder = WinFormsApplication.CreateBuilder();
        Exception? failure = null;
        Thread other = new(() =>
        {
            failure = Record.Exception(() => SelectConfiguration(builder, selection));
        });
        other.Start();
        Assert.True(other.Join(TimeSpan.FromSeconds(30)));
        Assert.IsType<InvalidOperationException>(failure);

        using WinFormsApplication application = builder.UseApplicationContext(() => new ApplicationContext()).Build();
        Assert.Throws<InvalidOperationException>(() => SelectConfiguration(builder, selection));
    }

    [Fact]
    public void Build_AppliesRecordedDefaultsBeforeHostBuildAndDeferredUi()
    {
        using RemoteInvokeHandle child = RemoteExecutor.Invoke(() => RunOnSta(() =>
        {
            bool originalTextRendering = Control.UseCompatibleTextRenderingDefault;
            bool originalStyles = Application.UseVisualStyles;
            SystemColorMode originalColor = Application.ColorMode;
            FormRevealMode originalReveal = Application.DefaultFormRevealMode;
            Font font = new("Arial", 13F);
            bool hostConfigured = false;
            bool activated = false;

            WinFormsApplicationBuilder builder = WinFormsApplication.CreateBuilder();
            Assert.Same(builder, builder.UseTextRendering(!originalTextRendering));
            Assert.Same(builder, builder.UseTextRendering(originalTextRendering));
            Assert.Same(builder, builder.UseTextRendering(!originalTextRendering));
            builder.UseColorMode(SystemColorMode.System);
            Assert.Same(builder, builder.UseColorMode(SystemColorMode.Classic));
            Assert.Throws<InvalidEnumArgumentException>(() => builder.UseColorMode((SystemColorMode)(-1)));
            builder.UseFormRevealMode(FormRevealMode.Inherit);
            Assert.Same(builder, builder.UseFormRevealMode(FormRevealMode.Deferred));
            Assert.Same(builder, builder.UseVisualStylesMode(VisualStylesMode.Net11));
            Assert.Same(builder, builder.UseLegacyVisualStyles());
            Assert.Same(builder, builder.UseDefaultFont(font));
            Assert.Same(builder, builder.UseVisualStyleState(VisualStyleState.ClientAndNonClientAreasEnabled));
            Assert.Same(builder, builder.UseUnhandledExceptionMode(UnhandledExceptionMode.ThrowException));

            Assert.Equal(originalTextRendering, Control.UseCompatibleTextRenderingDefault);
            Assert.Equal(originalStyles, Application.UseVisualStyles);
            Assert.Equal(originalColor, Application.ColorMode);
            Assert.Equal(originalReveal, Application.DefaultFormRevealMode);
            Assert.False(NativeWindow.AnyHandleCreated);

            builder.UseHost(new HostBuilder().ConfigureHostConfiguration(_ =>
            {
                hostConfigured = true;
                Assert.False(activated);
                Assert.False(NativeWindow.AnyHandleCreated);
                Assert.Equal(!originalTextRendering, Control.UseCompatibleTextRenderingDefault);
                Assert.True(Application.UseVisualStyles);
                Assert.Equal(SystemColorMode.Classic, Application.ColorMode);
                Assert.Equal(FormRevealMode.Deferred, Application.DefaultFormRevealMode);
                Assert.Equal(VisualStylesMode.Net11, Application.DefaultVisualStylesMode);
                Assert.Equal(VisualStyleState.ClientAndNonClientAreasEnabled, Application.VisualStyleState);
                Assert.Equal(font.FontFamily.Name, Application.DefaultFont.FontFamily.Name);
                Assert.True(NativeWindow.WndProcShouldBeDebuggable);
            }));
            builder.UseApplicationContext(() =>
            {
                activated = true;
                Assert.Equal(!originalTextRendering, Control.UseCompatibleTextRenderingDefault);
                return new ApplicationContext();
            });

            using (WinFormsApplication application = builder.Build())
            {
                Assert.True(hostConfigured);
                Assert.False(activated);
                application.ActivateStartup();
                Assert.True(activated);
            }

            Assert.Equal(FormRevealMode.Deferred, Application.DefaultFormRevealMode);
            Assert.Equal(font.FontFamily.Name, Application.DefaultFont.FontFamily.Name);
            Assert.True(font.Height > 0);
        }));
    }

    [Fact]
    public void Build_DpiAndFont_AreAppliedBeforeFormConstruction()
    {
        using RemoteInvokeHandle child = RemoteExecutor.Invoke(() => RunOnSta(() =>
        {
            Font font = new("Arial", 12F);
            WinFormsApplicationBuilder builder = WinFormsApplication.CreateBuilder()
                .UseDefaultFont(font)
                .UseHighDpiMode(HighDpiMode.SystemAware)
                .UseHighDpiMode(HighDpiMode.PerMonitorV2)
                .UseStartupForm<ConfiguredForm>();

            using WinFormsApplication application = builder.Build();
            Assert.Equal(HighDpiMode.PerMonitorV2, Application.HighDpiMode);
            ApplicationContext context = application.ActivateStartup();
            Assert.IsType<ConfiguredForm>(context.MainForm);
        }));
    }

    [Fact]
    public void Build_ConfiguredExistingForm_IsRejectedWithoutTransferringOwnership()
    {
        using RemoteInvokeHandle child = RemoteExecutor.Invoke(() => RunOnSta(() =>
        {
            bool autoInstall = WindowsFormsSynchronizationContext.AutoInstall;
            WindowsFormsSynchronizationContext.AutoInstall = false;
            try
            {
                using Form form = new();
                WinFormsApplicationBuilder builder = WinFormsApplication.CreateBuilder()
                    .UseTextRendering(false)
                    .UseStartupForm(form);

                Assert.Throws<InvalidOperationException>(() => builder.Build());
                Assert.False(form.IsDisposed);
                Assert.False(form.IsHandleCreated);
            }
            finally
            {
                WindowsFormsSynchronizationContext.AutoInstall = autoInstall;
            }
        }));
    }

    [Fact]
    public void Build_AfterHandleCreation_DoesNotBuildHostOrChangeDefaults()
    {
        using RemoteInvokeHandle child = RemoteExecutor.Invoke(() => RunOnSta(() =>
        {
            using Control control = new();
            _ = control.Handle;
            bool original = Control.UseCompatibleTextRenderingDefault;
            bool hostConfigured = false;
            WinFormsApplicationBuilder builder = WinFormsApplication.CreateBuilder()
                .UseTextRendering(!original)
                .UseApplicationContext(() => new ApplicationContext())
                .UseHost(new HostBuilder().ConfigureHostConfiguration(_ => hostConfigured = true));

            Assert.Throws<InvalidOperationException>(() => builder.Build());
            Assert.Equal(original, Control.UseCompatibleTextRenderingDefault);
            Assert.False(hostConfigured);
        }));
    }

    [Fact]
    public void Build_ConflictingStyles_ConsumesBuilderWithoutBuildingHost()
    {
        using RemoteInvokeHandle child = RemoteExecutor.Invoke(() => RunOnSta(() =>
        {
            Application.SetDefaultVisualStylesMode(VisualStylesMode.Classic);
            bool hostConfigured = false;
            WinFormsApplicationBuilder builder = WinFormsApplication.CreateBuilder()
                .UseVisualStylesMode(VisualStylesMode.Net11)
                .UseApplicationContext(() => new ApplicationContext())
                .UseHost(new HostBuilder().ConfigureHostConfiguration(_ => hostConfigured = true));

            Assert.Throws<InvalidOperationException>(() => builder.Build());
            Assert.Throws<InvalidOperationException>(() => builder.Build());
            Assert.Throws<InvalidOperationException>(() => builder.UseTextRendering(false));
            Assert.False(hostConfigured);
        }));
    }

    [Fact]
    public void Build_DpiAlreadyEstablished_FailsExplicitlyAndConsumesBuilder()
    {
        using RemoteInvokeHandle child = RemoteExecutor.Invoke(() => RunOnSta(() =>
        {
            // Establish process awareness first, as a manifest or prior initializer might do.
            _ = Application.SetHighDpiMode(HighDpiMode.SystemAware);
            WinFormsApplicationBuilder builder = WinFormsApplication.CreateBuilder();
            Assert.Same(builder, builder.UseHighDpiMode(HighDpiMode.PerMonitorV2));
            builder.UseApplicationContext(() => new ApplicationContext());

            InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() => builder.Build());
            Assert.Equal(SR.WinFormsApplication_HighDpiConfigurationFailed, failure.Message);
            Assert.Throws<InvalidOperationException>(() => builder.UseTextRendering(false));
        }));
    }

    [Fact]
    public void Build_ConfiguredMta_IsRejectedBeforeMutation()
    {
        using RemoteInvokeHandle child = RemoteExecutor.Invoke(() => RunOnThread(() =>
        {
            bool original = Control.UseCompatibleTextRenderingDefault;
            WinFormsApplicationBuilder builder = WinFormsApplication.CreateBuilder()
                .UseTextRendering(!original)
                .UseApplicationContext(() => new ApplicationContext());
            Assert.Throws<InvalidOperationException>(() => builder.Build());
            Assert.Equal(original, Control.UseCompatibleTextRenderingDefault);
        }, ApartmentState.MTA));
    }

    private static WinFormsApplicationBuilder SelectConfiguration(WinFormsApplicationBuilder builder, int selection)
        => selection switch
        {
            0 => builder.UseHighDpiMode(HighDpiMode.PerMonitorV2),
            1 => builder.UseColorMode(SystemColorMode.Classic),
            2 => builder.UseTextRendering(false),
            3 => builder.UseLegacyVisualStyles(),
            4 => builder.UseFormRevealMode(FormRevealMode.Deferred),
            5 => builder.UseVisualStylesMode(VisualStylesMode.Net11),
            6 => builder.UseDefaultFont(SystemFonts.DefaultFont),
            7 => builder.UseUnhandledExceptionMode(UnhandledExceptionMode.ThrowException),
            8 => builder.UseVisualStyleState(VisualStyleState.ClientAndNonClientAreasEnabled),
            _ => throw new ArgumentOutOfRangeException(nameof(selection))
        };

    private static void RunOnSta(Action action)
        => RunOnThread(action, ApartmentState.STA);

    private static void RunOnThread(Action action, ApartmentState apartment)
    {
        Exception? failure = null;
        Thread thread = new(() =>
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(apartment);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)));
        if (failure is not null)
        {
            ExceptionDispatchInfo.Throw(failure);
        }
    }

    /// <summary>
    ///  Observes defaults during the deferred startup constructor.
    /// </summary>
    private sealed class ConfiguredForm : Form
    {
        public ConfiguredForm()
        {
            Assert.Equal(HighDpiMode.PerMonitorV2, Application.HighDpiMode);
            Assert.Equal("Arial", Font.FontFamily.Name);
        }
    }
}
