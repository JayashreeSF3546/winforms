// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
using System.ComponentModel;
using System.Globalization;
using System.Windows.Forms.ComponentModel.Com2Interop;

namespace System.Windows.Forms.Tests.ComponentModel.Com2Interop;

public class Com2IDispatchConverterTests
{
    [Fact]
    public void Ctor_BoolAndTypeConverter_DoesNotThrow()
    {
        // Arrange
        RecordingTypeConverter baseConverter = new();

        // Act
        Action action = () => new Com2IDispatchConverter(
            allowExpand: true,
            baseConverter);

        // Assert
        action.Should().NotThrow();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void GetPropertiesSupported_ReturnsAllowExpand(bool allowExpand)
    {
        // Arrange
        RecordingTypeConverter baseConverter = new();
        Com2IDispatchConverter converter = new(
            allowExpand,
            baseConverter);

        // Act
        bool result = converter.GetPropertiesSupported(context: null);

        // Assert
        result.Should().Be(allowExpand);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CanConvertFrom_ReturnsFalse(bool expected)
    {
        // Arrange
        RecordingTypeConverter baseConverter = new()
        {
            CanConvertFromResult = expected
        };

        Com2IDispatchConverter converter = new(
            allowExpand: true,
            baseConverter);

        // Act
        bool result = converter.CanConvertFrom(
            context: null,
            sourceType: typeof(string));

        // Assert
        result.Should().BeFalse();
        baseConverter.CanConvertFromCallCount.Should().Be(0);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CanConvertTo_ReturnsTrueOnlyForString(bool expected)
    {
        // Arrange
        RecordingTypeConverter baseConverter = new()
        {
            CanConvertToResult = expected
        };

        Com2IDispatchConverter converter = new(
            allowExpand: true,
            baseConverter);

        // Act
        bool result = converter.CanConvertTo(
            context: null,
            destinationType: typeof(string));

        // Assert
        result.Should().BeTrue();
        baseConverter.CanConvertToCallCount.Should().Be(0);
    }

    [Fact]
    public void ConvertTo_DelegatesToBaseConverter_ForNonStringDestination()
    {
        // Arrange
        object value = new();
        const string convertedValue = "Converted value";

        RecordingTypeConverter baseConverter = new()
        {
            ConvertToResult = convertedValue
        };

        Com2IDispatchConverter converter = new(
            allowExpand: true,
            baseConverter);

        // Act
        object? result = converter.ConvertTo(
            context: null,
            culture: CultureInfo.InvariantCulture,
            value,
            destinationType: typeof(int));

        // Assert
        result.Should().Be(convertedValue);
        baseConverter.ConvertToCallCount.Should().Be(1);
        baseConverter.LastConvertToValue.Should().BeSameAs(value);
        baseConverter.LastDestinationType.Should().Be(typeof(int));
        baseConverter.LastCulture.Should().Be(CultureInfo.InvariantCulture);
    }

    [Fact]
    public void ConvertTo_StringDestination_Null_ReturnsNoneText()
    {
        // Arrange
        RecordingTypeConverter baseConverter = new();
        Com2IDispatchConverter converter = new(
            allowExpand: true,
            baseConverter);

        // Act
        object? result = converter.ConvertTo(
            context: null,
            culture: CultureInfo.InvariantCulture,
            value: null,
            destinationType: typeof(string));

        // Assert
        result.Should().Be(SR.toStringNone);
        baseConverter.ConvertToCallCount.Should().Be(0);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void GetStandardValuesSupported_ReturnsFalse(bool expected)
    {
        // Arrange
        RecordingTypeConverter baseConverter = new()
        {
            StandardValuesSupportedResult = expected
        };

        Com2IDispatchConverter converter = new(
            allowExpand: true,
            baseConverter);

        // Act
        bool result = converter.GetStandardValuesSupported(context: null);

        // Assert
        result.Should().BeFalse();
        baseConverter.GetStandardValuesSupportedCallCount.Should().Be(0);
    }

    [Fact]
    public void GetProperties_NullAttributes_ReturnsObjectProperties()
    {
        // Arrange
        RecordingTypeConverter baseConverter = new();
        Com2IDispatchConverter converter = new(
            allowExpand: true,
            baseConverter);

        TestComponent value = new()
        {
            Name = "COM object",
            Number = 42
        };

        // Act
        PropertyDescriptorCollection properties = converter.GetProperties(
            context: null,
            value,
            attributes: null);

        // Assert
        properties[nameof(TestComponent.Name)].Should().NotBeNull();
        properties[nameof(TestComponent.Number)].Should().NotBeNull();
    }

    [Fact]
    public void GetProperties_WithEmptyAttributes_ReturnsObjectProperties()
    {
        // Arrange
        RecordingTypeConverter baseConverter = new();
        Com2IDispatchConverter converter = new(
            allowExpand: true,
            baseConverter);

        TestComponent value = new();

        // Act
        PropertyDescriptorCollection properties = converter.GetProperties(
            context: null,
            value,
            attributes: []);

        // Assert
        properties[nameof(TestComponent.Name)].Should().NotBeNull();
        properties[nameof(TestComponent.Number)].Should().NotBeNull();
    }

    [Fact]
    public void GetProperties_WithBrowsableAttribute_FiltersProperties()
    {
        // Arrange
        RecordingTypeConverter baseConverter = new();

        Com2IDispatchConverter converter = new(
            allowExpand: true,
            baseConverter);

        TestComponent value = new();

        Attribute[] attributes =
        [
            BrowsableAttribute.Yes
        ];

        // Act
        PropertyDescriptorCollection properties = converter.GetProperties(
            context: null,
            value,
            attributes);

        // Assert
        properties[nameof(TestComponent.Name)].Should().NotBeNull();
        properties[nameof(TestComponent.Number)].Should().NotBeNull();
        properties[nameof(TestComponent.Hidden)].Should().BeNull();
    }

    private sealed class TestComponent
    {
        public string? Name { get; set; }

        public int Number { get; set; }

        [Browsable(false)]
        public string? Hidden { get; set; }
    }

    [Fact]
    public void ConvertTo_StringDestination_NonNull_ReturnsStringRepresentation()
    {
        // Arrange
        RecordingTypeConverter baseConverter = new();
        Com2IDispatchConverter converter = new(true, baseConverter);

        object value = new();

        // Act
        object? result = converter.ConvertTo(
            null,
            CultureInfo.InvariantCulture,
            value,
            typeof(string));

        // Assert
        result.Should().NotBeNull();
    }

    [Fact]
    public void CanConvertTo_NullDestinationType_ReturnsFalse()
    {
        RecordingTypeConverter baseConverter = new();
        Com2IDispatchConverter converter = new(true, baseConverter);

        bool result = converter.CanConvertTo(null, null);

        result.Should().BeFalse();
    }

    [Fact]
    public void CanConvertTo_NonString_ReturnsBaseBehavior()
    {
        RecordingTypeConverter baseConverter = new();

        Com2IDispatchConverter converter = new(
            true,
            baseConverter);

        bool result = converter.CanConvertTo(
            null,
            typeof(int));

        result.Should().BeFalse();
    }

    [Theory]
    [InlineData(typeof(int))]
    [InlineData(typeof(DateTime))]
    [InlineData(typeof(object))]
    public void CanConvertFrom_AdditionalTypes_ReturnsFalse(Type sourceType)
    {
        RecordingTypeConverter baseConverter = new();

        Com2IDispatchConverter converter = new(
            true,
            baseConverter);

        bool result = converter.CanConvertFrom(
            null,
            sourceType);

        result.Should().BeFalse();
    }

    [Fact]
    public void GetProperties_WithBrowsableFalse_FiltersOutVisibleProperties()
    {
        RecordingTypeConverter baseConverter = new();

        Com2IDispatchConverter converter =
            new(true, baseConverter);

        TestComponent value = new();

        Attribute[] attributes =
        [
            BrowsableAttribute.No
        ];

        PropertyDescriptorCollection properties =
            converter.GetProperties(
                null,
                value,
                attributes);

        properties.Should().NotBeNull();
    }

    [Fact]
    public void ConvertTo_ObjectDestination_DelegatesToBaseConverter()
    {
        RecordingTypeConverter baseConverter = new()
        {
            ConvertToResult = "ObjectResult"
        };

        Com2IDispatchConverter converter =
            new(true, baseConverter);

        object? result = converter.ConvertTo(
            null,
            CultureInfo.InvariantCulture,
            new object(),
            typeof(object));

        result.Should().Be("ObjectResult");
    }

    [Fact]
    public void CanConvertTo_Int_ReturnsFalse()
    {
        RecordingTypeConverter baseConverter = new();

        Com2IDispatchConverter converter = new(
            allowExpand: true,
            baseConverter);

        bool result = converter.CanConvertTo(
            context: null,
            destinationType: typeof(int));

        result.Should().BeFalse();
    }

    [Fact]
    public void ConvertTo_StringDestination_WithValue_ReturnsString()
    {
        RecordingTypeConverter baseConverter = new();

        Com2IDispatchConverter converter = new(
            allowExpand: true,
            baseConverter);

        object value = new();

        object? result = converter.ConvertTo(
            null,
            CultureInfo.InvariantCulture,
            value,
            typeof(string));

        result.Should().NotBeNull();
        result.Should().BeOfType<string>();
    }

    [Fact]
    public void Ctor_NullConverter_DoesNotThrow()
    {
        Action action = () =>
            new Com2IDispatchConverter(
                true,
                null!);

        action.Should().NotThrow();
    }

    [Fact]
    public void GetProperties_NullValue_ReturnsCollection()
    {
        RecordingTypeConverter baseConverter = new();

        Com2IDispatchConverter converter = new(
            true,
            baseConverter);

        PropertyDescriptorCollection properties =
            converter.GetProperties(
                null,
                null!,
                null);

        properties.Should().NotBeNull();
    }

    [Fact]
    public void GetProperties_AllowExpandFalse_ReturnsCollection()
    {
        RecordingTypeConverter baseConverter = new();

        Com2IDispatchConverter converter =
            new(false, baseConverter);

        TestComponent value = new();

        PropertyDescriptorCollection properties =
            converter.GetProperties(
                null,
                value,
                null);

        properties.Should().NotBeNull();
    }

    [Fact]
    public void GetProperties_WithBrowsableNo_ReturnsCollection()
    {
        RecordingTypeConverter baseConverter = new();

        Com2IDispatchConverter converter =
            new(true, baseConverter);

        PropertyDescriptorCollection properties =
            converter.GetProperties(
                null,
                new TestComponent(),
                [BrowsableAttribute.No]);

        properties.Should().NotBeNull();
    }

    private sealed class RecordingTypeConverter : TypeConverter
    {
        public bool CanConvertFromResult { get; init; }

        public bool CanConvertToResult { get; init; }

        public bool StandardValuesSupportedResult { get; init; }

        public object? ConvertToResult { get; init; }

        public int CanConvertFromCallCount { get; private set; }

        public int CanConvertToCallCount { get; private set; }

        public int ConvertToCallCount { get; private set; }

        public int GetStandardValuesSupportedCallCount { get; private set; }

        public Type? LastSourceType { get; private set; }

        public Type? LastDestinationType { get; private set; }

        public object? LastConvertToValue { get; private set; }

        public CultureInfo? LastCulture { get; private set; }

        public override bool CanConvertFrom(
            ITypeDescriptorContext? context,
            Type sourceType)
        {
            CanConvertFromCallCount++;
            LastSourceType = sourceType;
            return CanConvertFromResult;
        }

        public override bool CanConvertTo(
            ITypeDescriptorContext? context,
            Type? destinationType)
        {
            CanConvertToCallCount++;
            LastDestinationType = destinationType;
            return CanConvertToResult;
        }

        public override object? ConvertTo(
            ITypeDescriptorContext? context,
            CultureInfo? culture,
            object? value,
            Type destinationType)
        {
            ConvertToCallCount++;
            LastCulture = culture;
            LastConvertToValue = value;
            LastDestinationType = destinationType;
            return ConvertToResult;
        }

        public override bool GetStandardValuesSupported(
            ITypeDescriptorContext? context)
        {
            GetStandardValuesSupportedCallCount++;
            return StandardValuesSupportedResult;
        }
    }
}
