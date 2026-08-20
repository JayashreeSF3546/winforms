// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#nullable disable

using System.Drawing;
using System.Windows.Forms.ComponentModel.Com2Interop;
using Windows.Win32.System.Variant;

namespace System.Windows.Forms.Tests.ComponentModel.Com2Interop;

public class Com2ColorConverterTests
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
    public void Com2ColorConverter_ConvertNativeToManaged_Null_ReturnsExpected()
    {
        // Arrange
        Com2ColorConverter converter = new();
        VARIANT nativeValue = default;

        // Act
        object result = converter.ConvertNativeToManaged(nativeValue, s_stubDescriptor);

        // Assert
        Assert.NotNull(result);
        Assert.IsType<Color>(result);
        // Default VARIANT (0) should convert to black
        Assert.Equal(ColorTranslator.FromOle(0), result);
    }

    [Fact]
    public void Com2ColorConverter_ConvertNativeToManaged_OleRed_ReturnsColorRed()
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
    public void Com2ColorConverter_ConvertNativeToManaged_OleBlue_ReturnsColorBlue()
    {
        // Arrange
        Com2ColorConverter converter = new();
        int oleColor = ColorTranslator.ToOle(Color.Blue);
        VARIANT nativeValue = (VARIANT)oleColor;

        // Act
        object result = converter.ConvertNativeToManaged(nativeValue, s_stubDescriptor);

        // Assert
        Assert.NotNull(result);
        Assert.IsType<Color>(result);
        Assert.Equal(Color.Blue, result);
    }

    [Fact]
    public void Com2ColorConverter_ConvertNativeToManaged_SystemColor_ReturnsSystemColor()
    {
        // Arrange
        Com2ColorConverter converter = new();
        int oleColor = ColorTranslator.ToOle(SystemColors.Control);
        VARIANT nativeValue = (VARIANT)oleColor;

        // Act
        object result = converter.ConvertNativeToManaged(nativeValue, s_stubDescriptor);

        // Assert
        Assert.NotNull(result);
        Assert.IsType<Color>(result);
        Color expectedColor = ColorTranslator.FromOle(oleColor);
        Assert.Equal(expectedColor, result);
    }

    [Fact]
    public void Com2ColorConverter_ConvertManagedToNative_ColorRed_ReturnsOleColor()
    {
        // Arrange
        Com2ColorConverter converter = new();
        Color managedColor = Color.Red;
        bool cancelSet = false;

        // Act
        VARIANT result = converter.ConvertManagedToNative(managedColor, s_stubDescriptor, ref cancelSet);

        // Assert
        Assert.False(cancelSet);
        int expectedOleColor = ColorTranslator.ToOle(Color.Red);
        Assert.Equal((VARIANT)expectedOleColor, result);
    }

    [Fact]
    public void Com2ColorConverter_ConvertManagedToNative_ColorEmpty_ReturnsExpected()
    {
        // Arrange
        Com2ColorConverter converter = new();
        Color managedColor = Color.Empty;
        bool cancelSet = false;

        // Act
        VARIANT result = converter.ConvertManagedToNative(managedColor, s_stubDescriptor, ref cancelSet);

        // Assert
        Assert.False(cancelSet);
        int expectedOleColor = ColorTranslator.ToOle(Color.Empty);
        Assert.Equal((VARIANT)expectedOleColor, result);
    }

    [Fact]
    public void Com2ColorConverter_ConvertManagedToNative_Null_ReturnsExpected()
    {
        // Arrange
        Com2ColorConverter converter = new();
        object managedValue = null;
        bool cancelSet = false;

        // Act
        VARIANT result = converter.ConvertManagedToNative(managedValue, s_stubDescriptor, ref cancelSet);

        // Assert
        // When null is passed, it defaults to Color.Black
        Assert.False(cancelSet);
        int expectedOleColor = ColorTranslator.ToOle(Color.Black);
        Assert.Equal((VARIANT)expectedOleColor, result);
    }

    [Fact]
    public void Com2ColorConverter_ConvertManagedToNative_InvalidObject_ThrowsOrDebugFails()
    {
        // Arrange
        Com2ColorConverter converter = new();
        object invalidValue = new object();
        bool cancelSet = false;

        // Act & Assert
        // The converter calls Debug.Fail for invalid objects, which throws in test environment
        Assert.Throws<InvalidOperationException>(() =>
            converter.ConvertManagedToNative(invalidValue, s_stubDescriptor, ref cancelSet));
    }

    [Fact]
    public void Com2ColorConverter_ManagedType_ReturnsColorType()
    {
        // Arrange
        Com2ColorConverter converter = new();

        // Act
        Type managedType = converter.ManagedType;

        // Assert
        Assert.Equal(typeof(Color), managedType);
    }

    [Fact]
    public void Com2ColorConverter_AllowExpand_ReturnsFalse()
    {
        // Arrange
        Com2ColorConverter converter = new();

        // Act
        bool allowExpand = converter.AllowExpand;

        // Assert
        Assert.False(allowExpand);
    }

    [Theory]
    [InlineData(0xFF0000)] // Red
    [InlineData(0x00FF00)] // Green
    [InlineData(0x0000FF)] // Blue
    public void Com2ColorConverter_RoundTrip_ColorConversion(int oleColor)
    {
        // Arrange
        Com2ColorConverter converter = new();
        VARIANT nativeValue = (VARIANT)oleColor;
        bool cancelSet = false;

        // Act
        object managedColor = converter.ConvertNativeToManaged(nativeValue, s_stubDescriptor);
        VARIANT resultNativeValue = converter.ConvertManagedToNative(managedColor, s_stubDescriptor, ref cancelSet);

        // Assert
        Assert.Equal(oleColor, (int)resultNativeValue);
    }
}
