// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Reflection;
using System.Runtime.InteropServices;

namespace Microsoft.Extensions.WinForms;

/// <summary>
///  Snapshots assembly metadata and BCL runtime identity without inferring an entry assembly.
/// </summary>
/// <remarks>
///  <para>
///   Attribute absence is an empty string. Location absence is null, not the current directory.
///   This immutable snapshot owns no assembly, process, or runtime resources.
///  </para>
/// </remarks>
internal sealed class WinFormsApplicationInformation
{
    internal WinFormsApplicationInformation(Assembly applicationAssembly)
    {
        ArgumentNullException.ThrowIfNull(applicationAssembly);
        AssemblyName name = applicationAssembly.GetName();
        AssemblyName = name.Name ?? string.Empty;
        Version = name.Version;
        AssemblyCulture = name.CultureName ?? string.Empty;
        Title = applicationAssembly.GetCustomAttribute<AssemblyTitleAttribute>()?.Title ?? string.Empty;
        ProductName = applicationAssembly.GetCustomAttribute<AssemblyProductAttribute>()?.Product ?? string.Empty;
        CompanyName = applicationAssembly.GetCustomAttribute<AssemblyCompanyAttribute>()?.Company ?? string.Empty;
        Description = applicationAssembly.GetCustomAttribute<AssemblyDescriptionAttribute>()?.Description ?? string.Empty;
        Copyright = applicationAssembly.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright ?? string.Empty;
        Trademark = applicationAssembly.GetCustomAttribute<AssemblyTrademarkAttribute>()?.Trademark ?? string.Empty;
#pragma warning disable IL3000 // An absent assembly location is explicitly represented as null, never an executable path.
        string location = applicationAssembly.IsDynamic ? string.Empty : applicationAssembly.Location;
#pragma warning restore IL3000
        AssemblyLocation = string.IsNullOrEmpty(location) ? null : location;
        DirectoryPath = AssemblyLocation is null ? null : Path.GetDirectoryName(AssemblyLocation);
    }

    internal string AssemblyName { get; }
    internal Version? Version { get; }
    internal string AssemblyCulture { get; }
    internal string Title { get; }
    internal string ProductName { get; }
    internal string CompanyName { get; }
    internal string Description { get; }
    internal string Copyright { get; }
    internal string Trademark { get; }
    internal string? AssemblyLocation { get; }
    internal string? DirectoryPath { get; }
    internal string FrameworkDescription { get; } = RuntimeInformation.FrameworkDescription;
    internal string OSDescription { get; } = RuntimeInformation.OSDescription;
    internal Architecture ProcessArchitecture { get; } = RuntimeInformation.ProcessArchitecture;
    internal Architecture OSArchitecture { get; } = RuntimeInformation.OSArchitecture;
}
