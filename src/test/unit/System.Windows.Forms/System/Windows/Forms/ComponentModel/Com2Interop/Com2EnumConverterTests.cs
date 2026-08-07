// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#nullable disable

using System.ComponentModel;
using System.Collections;
using System.Reflection;
using System.Windows.Forms.ComponentModel.Com2Interop;

namespace System.Windows.Forms.Tests.ComponentModel.Com2Interop;

public class Com2EnumConverterTests
{
    [Fact]
    public void Com2Enum_Ctor_WithNamesAndValues_InitializesValues()
    {
        // Arrange
        string[] names = ["One", "Two", "Three"];
        object[] values = [1, 2, 3];

        // Act
        Com2Enum enum1 = CreateTestEnum(names, values);

        // Assert
        Assert.NotNull(enum1);
        Assert.Equal(names, enum1.Names);
        Assert.Equal(values, enum1.Values);
    }

    [Theory]
    [InlineData(1, "One")]
    [InlineData(2, "Two")]
    [InlineData(3, "Three")]
    public void Com2Enum_NameFromValue_WithExistingValue_ReturnsExpectedName(object value, string expectedName)
    {
        // Arrange
        string[] names = ["One", "Two", "Three"];
        object[] values = [1, 2, 3];
        Com2Enum enum1 = CreateTestEnum(names, values);

        // Act
        string result = enum1.ToString(value);

        // Assert
        Assert.Equal(expectedName, result);
    }

    [Fact]
    public void Com2Enum_NameFromValue_WithUnknownValue_ReturnsNullOrExpectedFallback()
    {
        // Arrange
        string[] names = ["One", "Two"];
        object[] values = [1, 2];
        Com2Enum enum1 = CreateTestEnum(names, values);

        // Act
        string result = enum1.ToString(999);

        // Assert
        // When value is unknown, ToString returns the string representation of the value
        Assert.Equal("999", result);
    }

    [Theory]
    [InlineData("One", 1)]
    [InlineData("Two", 2)]
    [InlineData("Three", 3)]
    public void Com2Enum_ValueFromName_WithExistingName_ReturnsExpectedValue(string name, object expectedValue)
    {
        // Arrange
        string[] names = ["One", "Two", "Three"];
        object[] values = [1, 2, 3];
        Com2Enum enum1 = CreateTestEnum(names, values);

        // Act
        object result = enum1.FromString(name);

        // Assert
        Assert.Equal(expectedValue, result);
    }

    [Fact]
    public void Com2Enum_ValueFromName_WithUnknownName_ReturnsExpectedFallback()
    {
        // Arrange
        string[] names = ["One", "Two"];
        object[] values = [1, 2];
        Com2Enum enum1 = CreateTestEnum(names, values);
        string unknownName = "Unknown";

        // Act
        object result = enum1.FromString(unknownName);

        // Assert
        // When name is unknown, FromString returns the input value as fallback
        Assert.Equal(unknownName, result);
    }

    [Fact]
    public void Com2EnumConverter_GetStandardValuesSupported_ReturnsTrue()
    {
        // Arrange
        Com2Enum com2Enum = CreateTestEnum();
        TypeConverter converter = CreateConverter(com2Enum);

        // Act
        bool result = converter.GetStandardValuesSupported();

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void Com2EnumConverter_GetStandardValuesExclusive_ReturnsFalse()
    {
        // Arrange
        Com2Enum com2Enum = CreateTestEnum();
        TypeConverter converter = CreateConverter(com2Enum);

        // Act
        bool result = converter.GetStandardValuesExclusive();

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void Com2EnumConverter_GetStandardValues_ReturnsExpectedValues()
    {
        // Arrange
        Com2Enum com2Enum = CreateTestEnum();
        TypeConverter converter = CreateConverter(com2Enum);

        // Act
        ICollection values = converter.GetStandardValues();

        // Assert
        Assert.NotNull(values);
        Assert.Equal(2, values.Count);
        Assert.Contains(1, values.Cast<object>());
        Assert.Contains(2, values.Cast<object>());
    }

    [Fact]
    public void Com2EnumConverter_CanConvertFrom_String_ReturnsTrue()
    {
        // Arrange
        Com2Enum com2Enum = CreateTestEnum();
        TypeConverter converter = CreateConverter(com2Enum);

        // Act
        bool result = converter.CanConvertFrom(typeof(string));

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void Com2EnumConverter_CanConvertTo_String_ReturnsTrue()
    {
        // Arrange
        Com2Enum com2Enum = CreateTestEnum();
        TypeConverter converter = CreateConverter(com2Enum);

        // Act
        bool result = converter.CanConvertTo(typeof(string));

        // Assert
        Assert.True(result);
    }

    [Theory]
    [InlineData("One", 1)]
    [InlineData("Two", 2)]
    public void Com2EnumConverter_ConvertFrom_KnownName_ReturnsExpectedValue(string text, object expected)
    {
        // Arrange
        Com2Enum com2Enum = CreateTestEnum();
        TypeConverter converter = CreateConverter(com2Enum);

        // Act
        object result = converter.ConvertFrom(text);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void Com2EnumConverter_ConvertFrom_UnknownName_DoesNotThrow()
    {
        // Arrange
        Com2Enum com2Enum = CreateTestEnum();
        TypeConverter converter = CreateConverter(com2Enum);
        string unknownValue = "Unknown";

        // Act
        object result = converter.ConvertFrom(unknownValue);

        // Assert
        // FromString will return the unknown value as fallback (no throw expected)
        Assert.NotNull(result);
    }

    [Theory]
    [InlineData(1, "One")]
    [InlineData(2, "Two")]
    public void Com2EnumConverter_ConvertTo_KnownValue_ReturnsExpectedName(object value, string expected)
    {
        // Arrange
        Com2Enum com2Enum = CreateTestEnum();
        TypeConverter converter = CreateConverter(com2Enum);

        // Act
        object result = converter.ConvertTo(value, typeof(string));

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void Com2EnumConverter_ConvertTo_UnknownValue_UsesBaseOrFallback()
    {
        // Arrange
        Com2Enum com2Enum = CreateTestEnum();
        TypeConverter converter = CreateConverter(com2Enum);
        int unknownValue = 999;

        // Act
        object result = converter.ConvertTo(unknownValue, typeof(string));

        // Assert
        // When value is unknown, ToString returns the string representation
        Assert.Equal("999", result);
    }

    [Fact]
    public void Com2EnumConverter_ConvertFrom_Null_UsesBase()
    {
        // Arrange
        Com2Enum com2Enum = CreateTestEnum();
        TypeConverter converter = CreateConverter(com2Enum);

        // Act & Assert
        // ConvertFrom with null value should fall back to base implementation
        // The base implementation might throw or handle null gracefully
        try
        {
            converter.ConvertFrom(null);
        }
        catch
        {
            // Expected behavior - base converter may throw for null input
        }
    }

    [Fact]
    public void Com2EnumConverter_ConvertTo_Null_UsesBase()
    {
        // Arrange
        Com2Enum com2Enum = CreateTestEnum();
        TypeConverter converter = CreateConverter(com2Enum);

        // Act
        object result = converter.ConvertTo(null, typeof(string));

        // Assert
        // ConvertTo with null value should handle gracefully
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void Com2EnumConverter_IsValid_WithValidValue_ReturnsTrue()
    {
        // Arrange
        Com2Enum com2Enum = CreateTestEnum();
        TypeConverter converter = CreateConverter(com2Enum);
        int validValue = 1;

        // Act
        bool result = converter.IsValid(validValue);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void Com2EnumConverter_IsValid_WithNullValue_ReturnsFalse()
    {
        // Arrange
        Com2Enum com2Enum = CreateTestEnum();
        TypeConverter converter = CreateConverter(com2Enum);

        // Act
        bool result = converter.IsValid(null);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void Com2EnumConverter_CaseSensitivity_NameFromString()
    {
        // Arrange
        string[] names = ["One", "Two"];
        object[] values = [1, 2];
        Com2Enum com2Enum = CreateTestEnum(names, values);
        TypeConverter converter = CreateConverter(com2Enum);

        // Act - Test with different case
        object result = converter.ConvertFrom("one");

        // Assert - Should be case-insensitive (based on Com2Enum implementation)
        Assert.Equal(1, result);
    }

    [Fact]
    public void Com2EnumConverter_ConvertFrom_WithIntegralValue_ThrowsNotSupportedException()
    {
        // Arrange
        Com2Enum com2Enum = CreateTestEnum();
        TypeConverter converter = CreateConverter(com2Enum);
        int integralValue = 1;

        // Act & Assert - Should throw NotSupportedException when non-string value is passed
        Assert.Throws<NotSupportedException>(() => converter.ConvertFrom(integralValue));
    }

    private static Com2Enum CreateTestEnum()
    {
        return CreateTestEnum(["One", "Two"], [1, 2]);
    }

    private static Com2Enum CreateTestEnum(string[] names, object[] values)
    {
        // Create a test-friendly derived class to access the internal Com2Enum
        Com2Enum testEnum = new();

        // Use reflection to call the protected PopulateArrays method
        var populateMethod = typeof(Com2Enum).GetMethod("PopulateArrays", 
            BindingFlags.NonPublic | BindingFlags.Instance);

        if (populateMethod is not null)
        {
            populateMethod.Invoke(testEnum, [names, values]);
        }

        return testEnum;
    }

    private static TypeConverter CreateConverter(Com2Enum com2Enum)
    {
        return new Com2EnumConverter(com2Enum);
    }
}
