// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace System.Windows.Forms.Tests;

/// <summary>
///  Groups the Windows Forms UI scope and activation tests into one non-parallel collection.
/// </summary>
/// <remarks>
///  <para>
///   <see cref="Microsoft.Extensions.WinForms.UIScopeDiagnostics"/> counters are process-wide, so
///   the tests that assert exact scope, activation, and assignment counts must not run beside each
///   other or beside any other test that creates UI scopes.
///  </para>
/// </remarks>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class WinFormsUIScopeCollection
{
    /// <summary>
    ///  The collection name shared by every UI scope test class.
    /// </summary>
    public const string Name = "WinFormsUIScope";
}
