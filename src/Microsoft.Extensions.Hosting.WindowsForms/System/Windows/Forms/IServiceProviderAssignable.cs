// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Extensions.WinForms;

/// <summary>
///  Implemented by forms, controls, and components that receive the service provider of the
///  <see cref="UIScope"/> that owns them.
/// </summary>
/// <remarks>
///  <para>
///   This contract exists because the Windows Forms Designer creates controls, components, and
///   inherited base forms with their public parameterless constructor. Types that must remain
///   designable therefore cannot require constructor injection, and receive their services after
///   construction instead.
///  </para>
///  <para>
///   <see cref="AssignServiceProvider(IServiceProvider)"/> is called after the constructor returns
///   and before the object is shown or its handle is required. It is never called at design time,
///   and it is never called for an application that does not use the WinForms application builder.
///   Every implementation must therefore remain fully functional when it is never called.
///  </para>
///  <para>
///   The contract is deliberately a method rather than a property: a public read/write property of
///   type <see cref="IServiceProvider"/> would appear in the Designer property grid and in
///   generated Designer code unless every implementer suppressed it.
///  </para>
/// </remarks>
public interface IServiceProviderAssignable
{
    /// <summary>
    ///  Provides the service provider of the owning <see cref="UIScope"/>.
    /// </summary>
    /// <param name="serviceProvider">
    ///  The scoped service provider. This value is never <see langword="null"/>.
    /// </param>
    void AssignServiceProvider(IServiceProvider serviceProvider);
}
