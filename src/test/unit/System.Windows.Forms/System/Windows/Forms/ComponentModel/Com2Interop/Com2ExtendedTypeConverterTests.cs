// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#nullable disable

using System.Collections;
using System.ComponentModel;
using System.Globalization;
using System.Windows.Forms.ComponentModel.Com2Interop;

namespace System.Windows.Forms.Tests.ComponentModel.Com2Interop;

public class Com2ExtendedTypeConverterTests
{
    [Fact]
    public void Com2ExtendedTypeConverter_CanConvertFrom_DelegatesToBaseConverter()
    {
        // Arrange
        TestConverter innerConverter = new()
        {
            CanConvertFromResult = true
        };

        TypeConverter converter = CreateConverter(innerConverter);

        // Act
        bool result = converter.CanConvertFrom(typeof(string));

        // Assert
        Assert.True(result);
        Assert.Equal(1, innerConverter.CanConvertFromCallCount);
    }

    [Fact]
    public void Com2ExtendedTypeConverter_CanConvertFrom_WithNullInnerConverter_UsesBase()
    {
        // Arrange
        TypeConverter converter = new Com2ExtendedTypeConverter((TypeConverter)null);

        // Act
        bool result = converter.CanConvertFrom(typeof(string));

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void Com2ExtendedTypeConverter_CanConvertTo_DelegatesToBaseConverter()
    {
        // Arrange
        TestConverter innerConverter = new()
        {
            CanConvertToResult = true
        };

        TypeConverter converter = CreateConverter(innerConverter);

        // Act
        bool result = converter.CanConvertTo(typeof(string));

        // Assert
        Assert.True(result);
        Assert.Equal(1, innerConverter.CanConvertToCallCount);
    }

    [Fact]
    public void Com2ExtendedTypeConverter_CanConvertTo_WithNullInnerConverter_UsesBase()
    {
        // Arrange
        TypeConverter converter = new Com2ExtendedTypeConverter((TypeConverter)null);

        // Act
        bool result = converter.CanConvertTo(typeof(string));

        // Assert
        // Base TypeConverter returns true for string type
        Assert.True(result);
    }

    [Fact]
    public void Com2ExtendedTypeConverter_ConvertFrom_DelegatesToBaseConverter()
    {
        // Arrange
        TestConverter innerConverter = new()
        {
            ConvertFromResult = 10
        };

        TypeConverter converter = CreateConverter(innerConverter);

        // Act
        object result = converter.ConvertFrom("10");

        // Assert
        Assert.Equal(10, result);
        Assert.Equal(1, innerConverter.ConvertFromCallCount);
    }

    [Fact]
    public void Com2ExtendedTypeConverter_ConvertFrom_WithNullInnerConverter_UsesBase()
    {
        // Arrange
        TypeConverter converter = new Com2ExtendedTypeConverter((TypeConverter)null);

        // Act & Assert
        // Base TypeConverter throws NotSupportedException for conversions
        Assert.Throws<NotSupportedException>(() => converter.ConvertFrom("test"));
    }

    [Fact]
    public void Com2ExtendedTypeConverter_ConvertTo_DelegatesToBaseConverter()
    {
        // Arrange
        TestConverter innerConverter = new()
        {
            ConvertToResult = "10"
        };

        TypeConverter converter = CreateConverter(innerConverter);

        // Act
        object result = converter.ConvertTo(10, typeof(string));

        // Assert
        Assert.Equal("10", result);
        Assert.Equal(1, innerConverter.ConvertToCallCount);
    }

    [Fact]
    public void Com2ExtendedTypeConverter_ConvertTo_WithNullInnerConverter_UsesBase()
    {
        // Arrange
        TypeConverter converter = new Com2ExtendedTypeConverter((TypeConverter)null);

        // Act
        object result = converter.ConvertTo(10, typeof(string));

        // Assert
        // Base TypeConverter.ConvertTo returns the string representation
        Assert.Equal("10", result);
    }

    [Fact]
    public void Com2ExtendedTypeConverter_GetStandardValuesSupported_DelegatesToBaseConverter()
    {
        // Arrange
        TestConverter innerConverter = new()
        {
            GetStandardValuesSupportedResult = true
        };

        TypeConverter converter = CreateConverter(innerConverter);

        // Act
        bool result = converter.GetStandardValuesSupported();

        // Assert
        Assert.True(result);
        Assert.Equal(1, innerConverter.GetStandardValuesSupportedCallCount);
    }

    [Fact]
    public void Com2ExtendedTypeConverter_GetStandardValuesSupported_WithNullInnerConverter_UsesBase()
    {
        // Arrange
        TypeConverter converter = new Com2ExtendedTypeConverter((TypeConverter)null);

        // Act
        bool result = converter.GetStandardValuesSupported();

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void Com2ExtendedTypeConverter_GetStandardValuesExclusive_DelegatesToBaseConverter()
    {
        // Arrange
        TestConverter innerConverter = new()
        {
            GetStandardValuesExclusiveResult = true
        };

        TypeConverter converter = CreateConverter(innerConverter);

        // Act
        bool result = converter.GetStandardValuesExclusive();

        // Assert
        Assert.True(result);
        Assert.Equal(1, innerConverter.GetStandardValuesExclusiveCallCount);
    }

    [Fact]
    public void Com2ExtendedTypeConverter_GetStandardValuesExclusive_WithNullInnerConverter_UsesBase()
    {
        // Arrange
        TypeConverter converter = new Com2ExtendedTypeConverter((TypeConverter)null);

        // Act
        bool result = converter.GetStandardValuesExclusive();

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void Com2ExtendedTypeConverter_GetStandardValues_DelegatesToBaseConverter()
    {
        // Arrange
        TestConverter innerConverter = new();
        TypeConverter converter = CreateConverter(innerConverter);

        // Act
        ICollection result = converter.GetStandardValues();

        // Assert
        Assert.NotNull(result);
        Assert.Equal(3, result.Count);
        Assert.Equal(1, innerConverter.GetStandardValuesCallCount);
    }

    [Fact]
    public void Com2ExtendedTypeConverter_GetStandardValues_WithNullInnerConverter_UsesBase()
    {
        // Arrange
        TypeConverter converter = new Com2ExtendedTypeConverter((TypeConverter)null);

        // Act
        ICollection result = converter.GetStandardValues();

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void Com2ExtendedTypeConverter_GetPropertiesSupported_DelegatesToBaseConverter()
    {
        // Arrange
        TestConverter innerConverter = new()
        {
            GetPropertiesSupportedResult = true
        };

        TypeConverter converter = CreateConverter(innerConverter);

        // Act
        bool result = converter.GetPropertiesSupported();

        // Assert
        Assert.True(result);
        Assert.Equal(1, innerConverter.GetPropertiesSupportedCallCount);
    }

    [Fact]
    public void Com2ExtendedTypeConverter_GetPropertiesSupported_WithNullInnerConverter_UsesBase()
    {
        // Arrange
        TypeConverter converter = new Com2ExtendedTypeConverter((TypeConverter)null);

        // Act
        bool result = converter.GetPropertiesSupported();

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void Com2ExtendedTypeConverter_GetProperties_DelegatesToBaseConverter()
    {
        // Arrange
        TestConverter innerConverter = new();
        TypeConverter converter = CreateConverter(innerConverter);

        // Act
        PropertyDescriptorCollection result = converter.GetProperties(new object());

        // Assert
        Assert.NotNull(result);
        Assert.Equal(1, innerConverter.GetPropertiesCallCount);
    }

    [Fact]
    public void Com2ExtendedTypeConverter_GetProperties_WithNullInnerConverter_UsesBase()
    {
        // Arrange
        TypeConverter converter = new Com2ExtendedTypeConverter((TypeConverter)null);

        // Act
        PropertyDescriptorCollection result = converter.GetProperties(new object());

        // Assert
        // Base TypeConverter.GetProperties returns null when not supported
        Assert.Null(result);
    }

    [Fact]
    public void Com2ExtendedTypeConverter_CreateInstance_DelegatesToBaseConverter()
    {
        // Arrange
        TestConverter innerConverter = new()
        {
            CreateInstanceResult = new object()
        };

        TypeConverter converter = CreateConverter(innerConverter);
        IDictionary propertyValues = new Dictionary<string, object>();

        // Act
        object result = converter.CreateInstance(propertyValues);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(1, innerConverter.CreateInstanceCallCount);
    }

    [Fact]
    public void Com2ExtendedTypeConverter_CreateInstance_WithNullInnerConverter_UsesBase()
    {
        // Arrange
        TypeConverter converter = new Com2ExtendedTypeConverter((TypeConverter)null);
        IDictionary propertyValues = new Dictionary<string, object>();

        // Act
        object result = converter.CreateInstance(propertyValues);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void Com2ExtendedTypeConverter_GetCreateInstanceSupported_DelegatesToBaseConverter()
    {
        // Arrange
        TestConverter innerConverter = new()
        {
            GetCreateInstanceSupportedResult = true
        };

        TypeConverter converter = CreateConverter(innerConverter);

        // Act
        bool result = converter.GetCreateInstanceSupported();

        // Assert
        Assert.True(result);
        Assert.Equal(1, innerConverter.GetCreateInstanceSupportedCallCount);
    }

    [Fact]
    public void Com2ExtendedTypeConverter_GetCreateInstanceSupported_WithNullInnerConverter_UsesBase()
    {
        // Arrange
        TypeConverter converter = new Com2ExtendedTypeConverter((TypeConverter)null);

        // Act
        bool result = converter.GetCreateInstanceSupported();

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void Com2ExtendedTypeConverter_IsValid_DelegatesToBaseConverter()
    {
        // Arrange
        TestConverter innerConverter = new()
        {
            IsValidResult = true
        };

        TypeConverter converter = CreateConverter(innerConverter);

        // Act
        bool result = converter.IsValid("10");

        // Assert
        Assert.True(result);
        Assert.Equal(1, innerConverter.IsValidCallCount);
    }

    [Fact]
    public void Com2ExtendedTypeConverter_IsValid_WithNullInnerConverter_UsesBase()
    {
        // Arrange
        TypeConverter converter = new Com2ExtendedTypeConverter((TypeConverter)null);

        // Act
        bool result = converter.IsValid("test");

        // Assert
        // Base TypeConverter.IsValid returns false by default
        Assert.False(result);
    }

    [Fact]
    public void Com2ExtendedTypeConverter_InnerConverter_Property_ReturnsWrappedConverter()
    {
        // Arrange
        TestConverter innerConverter = new();
        Com2ExtendedTypeConverter converter = new(innerConverter);

        // Act
        TypeConverter result = converter.InnerConverter;

        // Assert
        Assert.Same(innerConverter, result);
    }

    [Fact]
    public void Com2ExtendedTypeConverter_InnerConverter_Property_ReturnsNullWhenNullConstructed()
    {
        // Arrange
        Com2ExtendedTypeConverter converter = new((TypeConverter)null);

        // Act
        TypeConverter result = converter.InnerConverter;

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void Com2ExtendedTypeConverter_GetWrappedConverter_FindsMatchingType()
    {
        // Arrange
        TestConverter innerConverter = new();
        Com2ExtendedTypeConverter converter = new(innerConverter);

        // Act
        TypeConverter result = converter.GetWrappedConverter(typeof(TestConverter));

        // Assert
        Assert.Same(innerConverter, result);
    }

    [Fact]
    public void Com2ExtendedTypeConverter_GetWrappedConverter_ReturnsNullForNonMatchingType()
    {
        // Arrange
        TestConverter innerConverter = new();
        Com2ExtendedTypeConverter converter = new(innerConverter);

        // Act
        TypeConverter result = converter.GetWrappedConverter(typeof(StringConverter));

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void Com2ExtendedTypeConverter_GetWrappedConverter_SearchesNestedConverters()
    {
        // Arrange
        TestConverter innerConverter = new();
        Com2ExtendedTypeConverter converter1 = new(innerConverter);
        Com2ExtendedTypeConverter converter2 = new(converter1);

        // Act
        TypeConverter result = converter2.GetWrappedConverter(typeof(TestConverter));

        // Assert
        Assert.Same(innerConverter, result);
    }

    [Fact]
    public void Com2ExtendedTypeConverter_CtorWithBaseType_UsesTypeDescriptorConverter()
    {
        // Arrange & Act - the Type-based constructor resolves the inner converter via
        // TypeDescriptor.GetConverter(baseType) rather than taking an explicit converter instance.
        Com2ExtendedTypeConverter converter = new(typeof(int));

        // Assert
        converter.InnerConverter.Should().BeOfType<Int32Converter>();
    }

    [Fact]
    public void Com2ExtendedTypeConverter_CtorWithBaseType_DelegatesConvertFrom()
    {
        // Arrange
        Com2ExtendedTypeConverter converter = new(typeof(int));

        // Act
        object result = converter.ConvertFrom("42");

        // Assert
        result.Should().Be(42);
    }

    private static TypeConverter CreateConverter(TypeConverter innerConverter)
    {
        return new Com2ExtendedTypeConverter(innerConverter);
    }

    private sealed class TestConverter : TypeConverter
    {
        public bool CanConvertFromResult { get; init; }
        public bool CanConvertToResult { get; init; }
        public object ConvertFromResult { get; init; }
        public object ConvertToResult { get; init; }
        public bool GetStandardValuesSupportedResult { get; init; }
        public bool GetStandardValuesExclusiveResult { get; init; }
        public bool GetPropertiesSupportedResult { get; init; }
        public bool GetCreateInstanceSupportedResult { get; init; }
        public object CreateInstanceResult { get; init; }
        public bool IsValidResult { get; init; }

        public int CanConvertFromCallCount { get; private set; }
        public int CanConvertToCallCount { get; private set; }
        public int ConvertFromCallCount { get; private set; }
        public int ConvertToCallCount { get; private set; }
        public int GetStandardValuesCallCount { get; private set; }
        public int GetStandardValuesSupportedCallCount { get; private set; }
        public int GetStandardValuesExclusiveCallCount { get; private set; }
        public int GetPropertiesCallCount { get; private set; }
        public int GetPropertiesSupportedCallCount { get; private set; }
        public int CreateInstanceCallCount { get; private set; }
        public int GetCreateInstanceSupportedCallCount { get; private set; }
        public int IsValidCallCount { get; private set; }

        public override bool CanConvertFrom(ITypeDescriptorContext context, Type sourceType)
        {
            CanConvertFromCallCount++;
            return CanConvertFromResult;
        }

        public override bool CanConvertTo(ITypeDescriptorContext context, Type destinationType)
        {
            CanConvertToCallCount++;
            return CanConvertToResult;
        }

        public override object ConvertFrom(ITypeDescriptorContext context, CultureInfo culture, object value)
        {
            ConvertFromCallCount++;
            return ConvertFromResult;
        }

        public override object ConvertTo(ITypeDescriptorContext context, CultureInfo culture, object value, Type destinationType)
        {
            ConvertToCallCount++;
            return ConvertToResult;
        }

        public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext context)
        {
            GetStandardValuesCallCount++;
            return new StandardValuesCollection(new object[] { 1, 2, 3 });
        }

        public override bool GetStandardValuesSupported(ITypeDescriptorContext context)
        {
            GetStandardValuesSupportedCallCount++;
            return GetStandardValuesSupportedResult;
        }

        public override bool GetStandardValuesExclusive(ITypeDescriptorContext context)
        {
            GetStandardValuesExclusiveCallCount++;
            return GetStandardValuesExclusiveResult;
        }

        public override PropertyDescriptorCollection GetProperties(ITypeDescriptorContext context, object value, Attribute[] attributes)
        {
            GetPropertiesCallCount++;
            return new PropertyDescriptorCollection(Array.Empty<PropertyDescriptor>());
        }

        public override bool GetPropertiesSupported(ITypeDescriptorContext context)
        {
            GetPropertiesSupportedCallCount++;
            return GetPropertiesSupportedResult;
        }

        public override object CreateInstance(ITypeDescriptorContext context, IDictionary propertyValues)
        {
            CreateInstanceCallCount++;
            return CreateInstanceResult;
        }

        public override bool GetCreateInstanceSupported(ITypeDescriptorContext context)
        {
            GetCreateInstanceSupportedCallCount++;
            return GetCreateInstanceSupportedResult;
        }

        public override bool IsValid(ITypeDescriptorContext context, object value)
        {
            IsValidCallCount++;
            return IsValidResult;
        }
    }
}
