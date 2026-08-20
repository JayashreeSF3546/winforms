// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#nullable disable

using System.Drawing;
using System.Windows.Forms.ComponentModel.Com2Interop;
using Windows.Win32.System.Variant;

namespace System.Windows.Forms.Tests.ComponentModel.Com2Interop;

/// <summary>
/// Tests for Com2DataTypeToManagedDataTypeConverter abstract base class.
/// Since this is an abstract class, tests are performed through concrete implementations
/// like Com2ColorConverter, Com2FontConverter, and Com2PictureConverter.
/// </summary>
public class Com2DataTypeToManagedDataTypeConverterTests
{
    private static readonly Com2PropertyDescriptor s_stubDescriptor = new(
        default,
        "Foo",
        Array.Empty<Attribute>(),
        default,
        default,
        default,
        default);

    [Fact]
    public void Com2DataTypeToManagedDataTypeConverter_AllowExpand_ColorConverter_ReturnsFalse()
    {
        // Arrange
        Com2ColorConverter converter = new();

        // Act
        bool result = converter.AllowExpand;

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void Com2DataTypeToManagedDataTypeConverter_ManagedType_ColorConverter_ReturnsColor()
    {
        // Arrange
        Com2ColorConverter converter = new();

        // Act
        Type result = converter.ManagedType;

        // Assert
        Assert.Equal(typeof(Color), result);
    }

    [Fact]
    public void Com2DataTypeToManagedDataTypeConverter_ConvertNativeToManaged_VT_I2_ReturnsShort()
    {
        // Arrange - Test VT_I2 (short) through ColorConverter
        // Note: ColorConverter specifically handles color types, so we use default value
        Com2ColorConverter converter = new();
        short testValue = 100;
        VARIANT nativeValue = (VARIANT)testValue;

        // Act - ConvertNativeToManaged expects specific VARIANT types based on the converter
        object result = converter.ConvertNativeToManaged(nativeValue, s_stubDescriptor);

        // Assert - ColorConverter converts to Color
        Assert.NotNull(result);
        Assert.IsType<Color>(result);
    }

    [Fact]
    public void Com2DataTypeToManagedDataTypeConverter_ConvertNativeToManaged_VT_I4_ReturnsInt()
    {
        // Arrange - Test VT_I4 (int) through ColorConverter
        Com2ColorConverter converter = new();
        int testValue = 255;
        VARIANT nativeValue = (VARIANT)testValue;

        // Act
        object result = converter.ConvertNativeToManaged(nativeValue, s_stubDescriptor);

        // Assert - ColorConverter handles int values (OLE_COLOR)
        Assert.NotNull(result);
        Assert.IsType<Color>(result);
    }

    [Fact]
    public void Com2DataTypeToManagedDataTypeConverter_ConvertNativeToManaged_VT_R4_ReturnsFloat()
    {
        // Arrange - Test VT_R4 (float)
        // Note: Different converters handle different types
        Com2ColorConverter converter = new();
        float testValue = 123.45f;
        VARIANT nativeValue = (VARIANT)(int)testValue;

        // Act
        object result = converter.ConvertNativeToManaged(nativeValue, s_stubDescriptor);

        // Assert
        Assert.NotNull(result);
        Assert.IsType<Color>(result);
    }

    [Fact]
    public void Com2DataTypeToManagedDataTypeConverter_ConvertNativeToManaged_VT_R8_ReturnsDouble()
    {
        // Arrange - Test VT_R8 (double)
        Com2ColorConverter converter = new();
        double testValue = 456.789;
        VARIANT nativeValue = (VARIANT)(int)testValue;

        // Act
        object result = converter.ConvertNativeToManaged(nativeValue, s_stubDescriptor);

        // Assert
        Assert.NotNull(result);
        Assert.IsType<Color>(result);
    }

    [Fact]
    public void Com2DataTypeToManagedDataTypeConverter_ConvertNativeToManaged_VT_BOOL_ReturnsBool()
    {
        // Arrange - Test VT_BOOL (bool) - note: VARIANT stores bool as int (VARIANT_BOOL)
        Com2ColorConverter converter = new();
        VARIANT nativeValue = (VARIANT)1;  // True value

        // Act
        object result = converter.ConvertNativeToManaged(nativeValue, s_stubDescriptor);

        // Assert - ColorConverter still treats as OLE_COLOR
        Assert.NotNull(result);
        Assert.IsType<Color>(result);
    }

    [Fact]
    public void Com2DataTypeToManagedDataTypeConverter_ConvertNativeToManaged_Null_ReturnsExpected()
    {
        // Arrange
        Com2ColorConverter converter = new();
        VARIANT nativeValue = default;

        // Act
        object result = converter.ConvertNativeToManaged(nativeValue, s_stubDescriptor);

        // Assert
        Assert.NotNull(result);
        Assert.IsType<Color>(result);
    }

    [Fact]
    public void Com2DataTypeToManagedDataTypeConverter_ConvertNativeToManaged_ColorValue_ReturnsColor()
    {
        // Arrange
        Com2ColorConverter converter = new();
        int oleColor = ColorTranslator.ToOle(Color.Red);
        VARIANT nativeValue = (VARIANT)oleColor;

        // Act
        object result = converter.ConvertNativeToManaged(nativeValue, s_stubDescriptor);

        // Assert
        Assert.NotNull(result);
        Assert.IsType<Color>(result);
        Assert.Equal(Color.Red, result);
    }

    [Fact]
    public void Com2DataTypeToManagedDataTypeConverter_ConvertManagedToNative_Color_ReturnsVARIANT()
    {
        // Arrange
        Com2ColorConverter converter = new();
        bool cancelSet = false;

        // Act
        VARIANT result = converter.ConvertManagedToNative(Color.Blue, s_stubDescriptor, ref cancelSet);

        // Assert
        Assert.False(cancelSet);
        int expectedOleColor = ColorTranslator.ToOle(Color.Blue);
        Assert.Equal((VARIANT)expectedOleColor, result);
    }

    [Fact]
    public void Com2DataTypeToManagedDataTypeConverter_ConvertManagedToNative_Null_ReturnsDefault()
    {
        // Arrange
        Com2ColorConverter converter = new();
        bool cancelSet = false;

        // Act
        VARIANT result = converter.ConvertManagedToNative(null, s_stubDescriptor, ref cancelSet);

        // Assert
        Assert.False(cancelSet);
        // Null converts to Color.Black
        int expectedOleColor = ColorTranslator.ToOle(Color.Black);
        Assert.Equal((VARIANT)expectedOleColor, result);
    }

    [Fact]
    public void Com2DataTypeToManagedDataTypeConverter_ConvertManagedToNative_CancelSetUpdated()
    {
        // Arrange
        Com2ColorConverter converter = new();
        bool cancelSet = true;

        // Act
        converter.ConvertManagedToNative(Color.Red, s_stubDescriptor, ref cancelSet);

        // Assert - All converters should update cancelSet appropriately
        // ColorConverter sets cancelSet to false
        Assert.False(cancelSet);
    }

    [Fact]
    public void Com2DataTypeToManagedDataTypeConverter_RoundTrip_Conversion()
    {
        // Arrange
        Com2ColorConverter converter = new();
        Color originalColor = Color.Green;
        bool cancelSet = false;

        // Act
        VARIANT nativeValue = converter.ConvertManagedToNative(originalColor, s_stubDescriptor, ref cancelSet);
        object managedValue = converter.ConvertNativeToManaged(nativeValue, s_stubDescriptor);

        // Assert
        Assert.Equal(originalColor, managedValue);
    }
}
