// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace ApplicationBuilderAdoption.Migration.CSharp;

/// <summary>
///  Keeps ownership of the original C# Form while selecting an incremental startup stage.
/// </summary>
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
        => SampleEntryPoint.Run(() =>
        {
            using LegacyForm form = new();

            return MigrationSample.Run(args, form);
        });
}
