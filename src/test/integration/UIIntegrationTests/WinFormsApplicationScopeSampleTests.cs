// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.ComponentModel;
using System.ComponentModel.Design;
using System.ComponentModel.Design.Serialization;
using System.Globalization;
using System.Resources;
using System.Windows.Forms.IntegrationTests.Common;

namespace System.Windows.Forms.UITests;

/// <summary>
///  Exercises both repository-only scope samples in isolated native UI processes.
/// </summary>
[Collection(nameof(WinFormsApplicationScopeSampleTests))]
[CollectionDefinition(nameof(WinFormsApplicationScopeSampleTests), DisableParallelization = true)]
public class WinFormsApplicationScopeSampleTests(ITestOutputHelper output)
{
    [WinFormsTheory]
    [InlineData("CSharp")]
    [InlineData("VisualBasic")]
    public void Sample_DesignerConstructionAndSerializationNeedNoRuntimeServices(string language)
    {
        Type formType = language == "CSharp"
            ? typeof(global::ApplicationBuilderScopes.CSharp.ScopeForm)
            : typeof(global::ApplicationBuilderScopes.VisualBasic.ScopeForm);
        using DesignSurface surface = new(formType);
        Assert.True(surface.IsLoaded);
        Assert.Empty(surface.LoadErrors);
        var host = (IDesignerHost)surface.GetService(typeof(IDesignerHost))!;
        Form original = Assert.IsAssignableFrom<Form>(host.RootComponent);
        Assert.Null(TypeDescriptor.GetProperties(original)["Session"]);
        Type controlType = language == "CSharp"
            ? typeof(global::ApplicationBuilderScopes.CSharp.SessionControl)
            : typeof(global::ApplicationBuilderScopes.VisualBasic.SessionControl);
        Type componentType = language == "CSharp"
            ? typeof(global::ApplicationBuilderScopes.CSharp.SessionComponent)
            : typeof(global::ApplicationBuilderScopes.VisualBasic.SessionComponent);
        UserControl control = Assert.IsAssignableFrom<UserControl>(host.CreateComponent(controlType, "addedControl"));
        IComponent component = host.CreateComponent(componentType, "addedComponent");
        original.Controls.Add(control);
        Assert.True(control.Site!.DesignMode);
        Assert.True(component.Site!.DesignMode);
        Assert.Null(TypeDescriptor.GetProperties(control)["Session"]);
        Assert.Null(TypeDescriptor.GetProperties(component)["Session"]);

        using ServiceContainer serializationServices = new();
        serializationServices.AddService(typeof(ITypeResolutionService), new AssemblyNamesTypeResolutionService(
        [
            formType.Assembly.GetName(),
            typeof(Form).Assembly.GetName(),
            typeof(Drawing.Size).Assembly.GetName(),
            typeof(Drawing.Font).Assembly.GetName(),
            typeof(Component).Assembly.GetName(),
            typeof(CodeDomSerializer).Assembly.GetName(),
            typeof(object).Assembly.GetName()
        ]));
        using SampleResourceService resources = new();
        serializationServices.AddService(typeof(IResourceService), resources);
        using Container restoredComponents = new();
        UserControl restored = Assert.IsAssignableFrom<UserControl>(RoundTrip(control));
        Assert.Equal(controlType, restored.GetType());
        Assert.Equal(control.ClientSize, restored.ClientSize);
        Assert.Null(TypeDescriptor.GetProperties(restored)["Session"]);
        IComponent restoredComponent = RoundTrip(component);
        Assert.Equal(componentType, restoredComponent.GetType());
        Assert.Null(TypeDescriptor.GetProperties(restoredComponent)["Session"]);

        IComponent RoundTrip(IComponent value)
        {
            DesignerSerializationManager manager = new(serializationServices);
            object? code;
            using (manager.CreateSession())
            {
                var serializer = Assert.IsAssignableFrom<CodeDomSerializer>(
                    manager.GetSerializer(value.GetType(), typeof(CodeDomSerializer)));
                code = serializer.Serialize(manager, value);
                Assert.NotNull(code);
                Assert.Empty(manager.Errors);
            }

            manager.Container = restoredComponents;
            using (manager.CreateSession())
            {
                var serializer = Assert.IsAssignableFrom<CodeDomSerializer>(
                    manager.GetSerializer(value.GetType(), typeof(CodeDomSerializer)));
                IComponent result = Assert.IsAssignableFrom<IComponent>(serializer.Deserialize(manager, code));
                Assert.Empty(manager.Errors);
                return result;
            }
        }
    }

    public static TheoryData<string, string, bool, bool> Scenarios
    {
        get
        {
            TheoryData<string, string, bool, bool> data = [];
            foreach (string language in new[] { "CSharp", "VisualBasic" })
            {
                foreach (string theme in new[] { "classic", "light", "dark" })
                {
                    foreach (bool componentManager in new[] { false, true })
                    {
                        foreach (bool cancel in new[] { false, true })
                        {
                            data.Add(language, theme, componentManager, cancel);
                        }
                    }
                }
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Scenarios))]
    public async Task Sample_NavigatesAndReleasesEachScopeAfterUiCleanup(
        string language, string theme, bool componentManager, bool cancel)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = TestHelpers.GetExePath($"ApplicationBuilderScopes.{language}"),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add("--smoke");
        startInfo.ArgumentList.Add($"--{theme}");
        if (componentManager)
        {
            startInfo.ArgumentList.Add("--component-manager");
        }

        if (cancel)
        {
            startInfo.ArgumentList.Add("--cancel");
        }

        using Process process = TestHelpers.StartProcess(startInfo);
        Task<string> standardOutput = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        Task<string> standardError = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        using CancellationTokenSource watchdog = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        watchdog.CancelAfter(TimeSpan.FromSeconds(60));
        try
        {
            await process.WaitForExitAsync(watchdog.Token);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None);
            }
        }

        string transcript = await standardOutput;
        output.WriteLine(transcript);
        string errors = await standardError;
        output.WriteLine(errors);
        Assert.Equal(0, process.ExitCode);
        Assert.Empty(errors);
        string[] lines = transcript.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Contains($"Color: {theme}; component-manager={componentManager}", lines);
        Assert.Contains("Modeless scope completed", lines);
        Assert.Contains("Modal result: OK", lines);
        Assert.Contains("Navigation completed", lines);
        Assert.Equal(cancel, lines.Contains("Run canceled after UI cleanup"));
        Assert.Equal("PASS: 5 sessions disposed exactly once", lines[^1]);
        Assert.Equal(5, lines.Count(line => line.StartsWith("Session created:", StringComparison.Ordinal)));
        Assert.Equal(5, lines.Count(line => line.StartsWith("Session disposed:", StringComparison.Ordinal)));
        string[] formDisposals = lines.Where(line => line.StartsWith("Form UI disposed:", StringComparison.Ordinal)).ToArray();
        Assert.Equal(4, formDisposals.Length);
        foreach (string formDisposal in formDisposals)
        {
            string identity = formDisposal["Form UI disposed: ".Length..];
            int serviceDisposal = Array.IndexOf(lines, $"Session disposed: {identity}");
            int controlDisposal = Array.IndexOf(lines, $"UserControl disposed: {identity}");
            int componentDisposal = Array.IndexOf(lines, $"Component disposed: {identity}");
            Assert.True(controlDisposal >= 0 && controlDisposal < serviceDisposal);
            Assert.True(componentDisposal >= 0 && componentDisposal < serviceDisposal);
            Assert.True(Array.IndexOf(lines, formDisposal) < serviceDisposal);
        }
    }

    /// <summary>
    ///  Preserves local Designer metadata in memory without enabling legacy binary serialization.
    /// </summary>
    private sealed class SampleResourceService : IResourceService, IDisposable
    {
        private readonly StringWriter _text = new(CultureInfo.InvariantCulture);
        private ResXResourceWriter? _writer;

        public IResourceReader? GetResourceReader(CultureInfo info)
            => _text.GetStringBuilder().Length == 0
                ? null
                : new ResXResourceReader(new StringReader(_text.ToString()));

        public IResourceWriter GetResourceWriter(CultureInfo info)
            => _writer ??= new ResXResourceWriter(_text);

        public void Dispose()
        {
            _writer?.Dispose();
            _text.Dispose();
        }
    }
}
