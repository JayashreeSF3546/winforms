// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#nullable disable

using System.ComponentModel;
using System.Reflection;
using System.Windows.Forms.ComponentModel.Com2Interop;

namespace System.Windows.Forms.Tests.ComponentModel.Com2Interop;

// Tests for the private nested TypeConverter
// Com2IPerPropertyBrowsingHandler.Com2IPerPropertyEnumConverter. This converter is used to identify enums
// built up from IPerPropertyBrowsing::GetPredefinedStrings/GetPredefinedValue and attempts to short-circuit
// display-string lookups through IPerPropertyBrowsing::GetDisplayString before falling back to the base
// Com2EnumConverter behavior.
public class Com2IPerPropertyEnumConverterTests
{
    private static readonly Type s_handlerType = typeof(Com2PropertyDescriptor).Assembly
        .GetType("System.Windows.Forms.ComponentModel.Com2Interop.Com2IPerPropertyBrowsingHandler");

    private static readonly Type s_browsingEnumType = s_handlerType.GetNestedType(
        "Com2IPerPropertyBrowsingEnum",
        BindingFlags.NonPublic);

    private static readonly Type s_converterType = s_handlerType.GetNestedType(
        "Com2IPerPropertyEnumConverter",
        BindingFlags.NonPublic);

    static Com2IPerPropertyEnumConverterTests()
    {
        Assert.NotNull(s_handlerType);
        Assert.NotNull(s_browsingEnumType);
        Assert.NotNull(s_converterType);
    }

    private static Com2PropertyDescriptor CreateDescriptor(Type propertyType)
        => new(
            default,
            "Foo",
            Array.Empty<Attribute>(),
            default,
            propertyType,
            default,
            default);

    private static object CreateBrowsingEnum(Com2PropertyDescriptor descriptor, string[] names, uint[] cookies)
    {
        ConstructorInfo ctor = s_browsingEnumType.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)[0];
        return ctor.Invoke([descriptor, names, cookies]);
    }

    private static TypeConverter CreateConverter(object browsingEnum)
    {
        ConstructorInfo ctor = s_converterType.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)[0];
        return (TypeConverter)ctor.Invoke([browsingEnum]);
    }

    [Fact]
    public void ConvertTo_NonStringDestination_DelegatesToBaseEnumConverter()
    {
        // Arrange
        Com2PropertyDescriptor descriptor = CreateDescriptor(typeof(int));
        object browsingEnum = CreateBrowsingEnum(descriptor, [], []);
        TypeConverter converter = CreateConverter(browsingEnum);

        // Act - non-string destination always falls through to Com2EnumConverter.ConvertTo, which for
        // an enum destinationType uses Enum.ToObject rather than any IPerPropertyBrowsing lookup.
        object result = converter.ConvertTo(context: null, culture: null, 1, typeof(DayOfWeek));

        // Assert
        result.Should().Be(DayOfWeek.Monday);
    }

    [Fact]
    public void ConvertTo_StringDestination_NullValue_ReturnsEmptyString()
    {
        // Arrange - TargetObject is null (no COM object wired up), so GetValue(null) returns null and
        // IPerPropertyBrowsing lookups fail gracefully, falling back to the base converter.
        Com2PropertyDescriptor descriptor = CreateDescriptor(typeof(int));
        object browsingEnum = CreateBrowsingEnum(descriptor, [], []);
        TypeConverter converter = CreateConverter(browsingEnum);

        // Act
        object result = converter.ConvertTo(context: null, culture: null, null, typeof(string));

        // Assert
        result.Should().Be(string.Empty);
    }

    [Fact]
    public void ConvertTo_StringDestination_NonMatchingValue_FallsBackToEnumToString()
    {
        // Arrange
        Com2PropertyDescriptor descriptor = CreateDescriptor(typeof(int));
        object browsingEnum = CreateBrowsingEnum(descriptor, [], []);
        TypeConverter converter = CreateConverter(browsingEnum);

        // Act - value doesn't match the (null) current value, so IPerPropertyBrowsing is never
        // consulted and the call flows through to Com2Enum.ToString, which returns the raw string
        // representation when there are no known values.
        object result = converter.ConvertTo(context: null, culture: null, 7, typeof(string));

        // Assert
        result.Should().Be("7");
    }

    [Fact]
    public void ConvertTo_StringDestination_AlreadyFetchedArrays_SkipsDisplayStringLookup()
    {
        // Arrange
        Com2PropertyDescriptor descriptor = CreateDescriptor(typeof(int));
        object browsingEnum = CreateBrowsingEnum(descriptor, [], []);
        TypeConverter converter = CreateConverter(browsingEnum);

        // Force ArraysFetched by reading the Values property (lazily populates via the overridden getter).
        PropertyInfo valuesProperty = s_browsingEnumType.GetProperty("Values", BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(valuesProperty);
        _ = valuesProperty.GetValue(browsingEnum);

        // Act
        object result = converter.ConvertTo(context: null, culture: null, 1, typeof(string));

        // Assert - once arrays are fetched the special-cased branch in Com2IPerPropertyEnumConverter is
        // bypassed entirely (per the `!_itemsEnum.ArraysFetched` guard), delegating straight to the base.
        result.Should().Be("1");
    }

    [Fact]
    public void GetStandardValuesSupported_ReturnsTrue()
    {
        // Arrange
        Com2PropertyDescriptor descriptor = CreateDescriptor(typeof(int));
        object browsingEnum = CreateBrowsingEnum(descriptor, [], []);
        TypeConverter converter = CreateConverter(browsingEnum);

        // Act
        bool result = converter.GetStandardValuesSupported(context: null);

        // Assert - inherited unchanged from Com2EnumConverter.
        result.Should().BeTrue();
    }
}
