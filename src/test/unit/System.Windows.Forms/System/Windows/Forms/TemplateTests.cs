// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace System.Windows.Forms.Tests;

public class TemplateTests
{
    [Fact]
    public void WinFormsTemplate_CreatesAndBuilds()
    {
        string tempDir = DotNetCliHelper.CreateTempFolder();
        try
        {
            Assert.Equal(0, DotNetCliHelper.RunCommand("dotnet", "new winforms -n TestApp", tempDir));
            string projectDir = Path.Combine(tempDir, "TestApp");
            Assert.True(File.Exists(Path.Combine(projectDir, "Program.cs")));
            Assert.True(File.Exists(Path.Combine(projectDir, "Form1.cs")));

            Assert.Equal(0, DotNetCliHelper.RunCommand("dotnet", "build", projectDir));
        }
        catch
        {
            DotNetCliHelper.Dispose(tempDir);
        }
    }
}
