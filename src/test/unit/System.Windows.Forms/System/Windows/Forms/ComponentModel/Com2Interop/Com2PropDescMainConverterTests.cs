// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#nullable disable

using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Windows.Forms.ComponentModel.Com2Interop;

namespace System.Windows.Forms.Tests.ComponentModel.Com2Interop;

// Tests for the private nested TypeConverter Com2PropertyDescriptor.Com2PropDescMainConverter.
// This converter wraps the "real" converter for a COM property so that display values can be
// intercepted / customized and so sub-property expansion can be gated appropriately.
public class Com2PropDescMainConverterTests
{
    private static readonly Type s_mainConverterType = typeof(Com2PropertyDescriptor)
        .GetNestedType("Com2PropDescMainConverter", BindingFlags.NonPublic);

    private static readonly Type s_propertyDescriptorType = typeof(Com2PropertyDescriptor);

    static Com2PropDescMainConverterTests()
    {
        Assert.NotNull(s_mainConverterType);
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

    private static void SetLastValue(Com2PropertyDescriptor descriptor, object value)
    {
        FieldInfo field = s_propertyDescriptorType.GetField("_lastValue", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(field);
        field.SetValue(descriptor, value);
    }

    private static void SetValueConverter(Com2PropertyDescriptor descriptor, Com2DataTypeToManagedDataTypeConverter converter)
    {
        FieldInfo field = s_propertyDescriptorType.GetField("_valueConverter", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(field);
        field.SetValue(descriptor, converter);
    }

    private static TypeConverter CreateMainConverter(Com2PropertyDescriptor descriptor, TypeConverter baseConverter)
    {
        ConstructorInfo ctor = s_mainConverterType.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)[0];
        return (TypeConverter)ctor.Invoke([descriptor, baseConverter]);
    }

    [Fact]
    public void ConvertTo_NonStringDestination_DelegatesToBaseConverter()
    {
        // Arrange
        Com2PropertyDescriptor descriptor = CreateDescriptor(typeof(int));
        RecordingConverter inner = new() { ConvertToResult = 42 };
        TypeConverter converter = CreateMainConverter(descriptor, inner);

        // Act
        object result = converter.ConvertTo(context: null, culture: null, 42, typeof(object));

        // Assert - falls straight through to the inner converter; no display-value logic applies.
        result.Should().Be(42);
        inner.ConvertToCallCount.Should().Be(1);
    }

    [Fact]
    public void ConvertTo_StringDestination_NotLastKnownValue_ReturnsBaseConversion()
    {
        // Arrange
        Com2PropertyDescriptor descriptor = CreateDescriptor(typeof(int));
        SetLastValue(descriptor, 999); // Different from the value we convert below.
        TypeConverter converter = CreateMainConverter(descriptor, new Int32Converter());

        // Act
        object result = converter.ConvertTo(context: null, culture: null, 42, typeof(string));

        // Assert
        result.Should().Be("42");
    }

    [Fact]
    public void ConvertTo_StringDestination_LastKnownValue_PropertyTypeIsEnum_ReturnsBaseConversion()
    {
        // Arrange - Even though this is the last known value, an enum PropertyType should skip
        // the special-cased display-value logic entirely.
        object lastValue = DayOfWeek.Monday; // Boxed once so reference equality in IsLastKnownValue matches.
        Com2PropertyDescriptor descriptor = CreateDescriptor(typeof(DayOfWeek));
        SetLastValue(descriptor, lastValue);
        TypeConverter converter = CreateMainConverter(descriptor, new EnumConverter(typeof(DayOfWeek)));

        // Act
        object result = converter.ConvertTo(context: null, culture: null, lastValue, typeof(string));

        // Assert
        result.Should().Be("Monday");
    }

    [Fact]
    public void ConvertTo_StringDestination_LastKnownValue_WithWrappedEnumConverter_UsesEnumConverter()
    {
        // Arrange
        string[] names = ["One", "Two"];
        object[] values = [1, 2];
        Com2Enum com2Enum = new();
        typeof(Com2Enum).GetMethod("PopulateArrays", BindingFlags.NonPublic | BindingFlags.Instance)
            .Invoke(com2Enum, [names, values]);
        Com2EnumConverter enumConverter = new(com2Enum);

        object lastValue = 1; // Boxed once so reference equality in IsLastKnownValue matches.
        Com2PropertyDescriptor descriptor = CreateDescriptor(typeof(int));
        SetLastValue(descriptor, lastValue);
        TypeConverter converter = CreateMainConverter(descriptor, enumConverter);

        // Act
        object result = converter.ConvertTo(context: null, culture: null, lastValue, typeof(string));

        // Assert - result is the enum's display name, exercising the branch that walks the
        // Com2ExtendedTypeConverter chain looking for a wrapped Com2EnumConverter.
        result.Should().Be("One");
    }

    private static void AttachPropertyManager(Com2PropertyDescriptor descriptor)
        => _ = new Com2Properties(new object(), [descriptor], 0);

    [Fact]
    public void ConvertTo_StringDestination_LastKnownValue_NoWrappedEnumConverter_UsesDisplayValue()
    {
        // Arrange - inner converter is not (and does not wrap) a Com2EnumConverter, so the fallback
        // is Com2PropertyDescriptor.GetDisplayValue, which returns the default value when there are
        // no QueryGetDisplayValue subscribers. A valid PropertyManager is required because
        // GetDisplayValue asserts it is non-null before raising the event.
        object lastValue = 42; // Boxed once so reference equality in IsLastKnownValue matches.
        Com2PropertyDescriptor descriptor = CreateDescriptor(typeof(int));
        AttachPropertyManager(descriptor);
        SetLastValue(descriptor, lastValue);
        TypeConverter converter = CreateMainConverter(descriptor, new Int32Converter());

        // Act
        object result = converter.ConvertTo(context: null, culture: null, lastValue, typeof(string));

        // Assert
        result.Should().Be("42");
    }

    [Fact]
    public void ConvertTo_StringDestination_LastKnownValue_RaisesQueryGetDisplayValueEvent()
    {
        // Arrange
        object lastValue = 42; // Boxed once so reference equality in IsLastKnownValue matches.
        Com2PropertyDescriptor descriptor = CreateDescriptor(typeof(int));
        AttachPropertyManager(descriptor);
        SetLastValue(descriptor, lastValue);
        TypeConverter converter = CreateMainConverter(descriptor, new Int32Converter());

        descriptor.QueryGetDisplayValue += (sender, e) => e.Name = "Custom Display";

        // Act
        object result = converter.ConvertTo(context: null, culture: null, lastValue, typeof(string));

        // Assert
        result.Should().Be("Custom Display");
    }

    [Fact]
    public void GetPropertiesSupported_BaseConverterDoesNotSupport_ReturnsFalse()
    {
        // Arrange - a converter with no properties support (default TypeConverter behavior).
        Com2PropertyDescriptor descriptor = CreateDescriptor(typeof(int));
        TypeConverter converter = CreateMainConverter(descriptor, new TypeConverter());

        // Act
        bool result = converter.GetPropertiesSupported(context: null);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void GetPropertiesSupported_BaseConverterSupportsAndValueConverterAllowsExpand_ReturnsTrue()
    {
        // Arrange
        Com2PropertyDescriptor descriptor = CreateDescriptor(typeof(object));
        SetValueConverter(descriptor, new Com2FontConverter()); // AllowExpand == true
        TypeConverter converter = CreateMainConverter(descriptor, new ExpandableObjectConverter());

        // Act
        bool result = converter.GetPropertiesSupported(context: null);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void GetPropertiesSupported_BaseConverterSupportsButNoExpand_ReturnsFalse()
    {
        // Arrange - no _valueConverter and target object is null, so AllowChildProperties returns false.
        Com2PropertyDescriptor descriptor = CreateDescriptor(typeof(object));
        TypeConverter converter = CreateMainConverter(descriptor, new ExpandableObjectConverter());

        // Act
        bool result = converter.GetPropertiesSupported(context: null);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void GetPropertiesSupported_CachesResultAfterFirstCall()
    {
        // Arrange
        Com2PropertyDescriptor descriptor = CreateDescriptor(typeof(object));
        SetValueConverter(descriptor, new Com2FontConverter());
        TypeConverter converter = CreateMainConverter(descriptor, new ExpandableObjectConverter());

        // Act
        bool first = converter.GetPropertiesSupported(context: null);

        // Now remove the value converter -- if the result were recomputed, this would flip to false.
        SetValueConverter(descriptor, null);
        bool second = converter.GetPropertiesSupported(context: null);

        // Assert
        first.Should().BeTrue();
        second.Should().BeTrue();
    }

    [Fact]
    public void GetProperties_ReturnsSortedReadOnlyCollection()
    {
        // Arrange
        Com2PropertyDescriptor descriptor = CreateDescriptor(typeof(object));
        TypeConverter converter = CreateMainConverter(descriptor, new TypeConverter());
        TestObject value = new();

        // Act
        PropertyDescriptorCollection properties = converter.GetProperties(context: null, value, attributes: null);

        // Assert
        properties.Should().NotBeNull();
        properties.Count.Should().Be(2);
        properties[0].Name.Should().Be(nameof(TestObject.Alpha));
        properties[1].Name.Should().Be(nameof(TestObject.Beta));
    }

    private sealed class TestObject
    {
        public int Beta { get; set; }

        public int Alpha { get; set; }
    }

    private sealed class RecordingConverter : TypeConverter
    {
        public object ConvertToResult { get; init; }

        public int ConvertToCallCount { get; private set; }

        public override object ConvertTo(ITypeDescriptorContext context, CultureInfo culture, object value, Type destinationType)
        {
            ConvertToCallCount++;
            return ConvertToResult;
        }
    }
}
