// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Reflection;

namespace Microsoft.Extensions.WinForms;

/// <summary>
///  Provides metadata about the entry assembly of a Windows Forms application.
/// </summary>
public interface IWinFormsApplicationInfo
{
    /// <summary>
    ///  Gets the entry assembly's simple name.
    /// </summary>
    string Name { get; }

    /// <summary>
    ///  Gets the entry assembly's version.
    /// </summary>
    Version Version { get; }

    /// <summary>
    ///  Gets the application's base directory.
    /// </summary>
    string BaseDirectory { get; }

    /// <summary>
    ///  Gets the company metadata, or an empty string when it is not defined.
    /// </summary>
    string Company { get; }

    /// <summary>
    ///  Gets the product metadata, or an empty string when it is not defined.
    /// </summary>
    string Product { get; }

    /// <summary>
    ///  Gets the title metadata, or an empty string when it is not defined.
    /// </summary>
    string Title { get; }

    /// <summary>
    ///  Gets the description metadata, or an empty string when it is not defined.
    /// </summary>
    string Description { get; }

    /// <summary>
    ///  Gets the copyright metadata, or an empty string when it is not defined.
    /// </summary>
    string Copyright { get; }

    /// <summary>
    ///  Gets the trademark metadata, or an empty string when it is not defined.
    /// </summary>
    string Trademark { get; }

    /// <summary>
    ///  Gets a snapshot of the assemblies loaded in the current application domain.
    /// </summary>
    IReadOnlyList<Assembly> LoadedAssemblies { get; }
}
