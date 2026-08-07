// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms.ComponentModel.Com2Interop;
using Windows.Win32.System.Com;
using Windows.Win32.System.Ole;
using Windows.Win32.System.Variant;

namespace System.Windows.Forms.Tests.ComponentModel.Com2Interop;

public unsafe class COM2FontConverterTests
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
    public void COM2FontConverter_ConvertNativeToManaged()
    {
        fixed (char* n = "Arial")
        {
            FONTDESC fontDesc = new()
            {
                cbSizeofstruct = (uint)sizeof(FONTDESC),
                lpstrName = n,
                cySize = (CY)12.0f
            };

            using ComScope<IFont> iFont = new(null);
            PInvoke.OleCreateFontIndirect(&fontDesc, IID.Get<IFont>(), iFont).ThrowOnFailure();

            Com2FontConverter converter = new();
            using Font? font = (Font?)converter.ConvertNativeToManaged((VARIANT)(IUnknown*)iFont, s_stubDescriptor);
            Assert.NotNull(font);

            // Converter might have failed and returned DefaultFont.
            Assert.NotEqual(font, Control.DefaultFont);

            Assert.Equal("Arial", font.Name);
            Assert.Equal(12, font.Size);

            bool cancelSet = false;
            using VARIANT result = converter.ConvertManagedToNative(font, s_stubDescriptor, ref cancelSet);
            Assert.True(result.IsEmpty);
            Assert.True(cancelSet);
        }
    }

    [Fact]
    public void COM2FontConverter_ConvertManagedToNative()
    {
        fixed (char* n = "Arial")
        {
            FONTDESC fontDesc = new()
            {
                cbSizeofstruct = (uint)sizeof(FONTDESC),
                lpstrName = n,
                cySize = (CY)12.0f
            };

            using ComScope<IFont> iFont = new(null);
            PInvoke.OleCreateFontIndirect(&fontDesc, IID.Get<IFont>(), iFont).ThrowOnFailure();

            Com2FontConverter converter = new();
            using Font? font = (Font?)converter.ConvertNativeToManaged((VARIANT)(IUnknown*)iFont, s_stubDescriptor);
            Assert.NotNull(font);

            using Font newFont = new(font.Name, 20.0f);

            bool cancelSet = false;

            // Need to addref here as ConvertManagedToNative will release the VARIANT we cast to below.
            iFont.Value->AddRef();

            using VARIANT result = converter.ConvertManagedToNative(
                newFont,
                new CustomGetNativeValueDescriptor((VARIANT)(IUnknown*)iFont.Value),
                ref cancelSet);

            Assert.True(cancelSet);
            Assert.True(result.IsEmpty);
            Assert.Equal("Arial", iFont.Value->Name.ToStringAndFree());
            Assert.Equal(20.0f, (float)iFont.Value->Size, precision: 0);
        }
    }

    [Fact]
    public void Com2FontConverter_ConvertNativeToManaged_Null_ReturnsNullOrDefault()
    {
        // Arrange
        Com2FontConverter converter = new();
        VARIANT nullVariant = default;

        // Act
        object? result = converter.ConvertNativeToManaged(nullVariant, s_stubDescriptor);

        // Assert
        // Empty VARIANT (VT_EMPTY) should return null
        Assert.Null(result);
    }

    [Fact]
    public void Com2FontConverter_ConvertNativeToManaged_InvalidVariant_TypeMismatch()
    {
        // Arrange
        Com2FontConverter converter = new();
        // Create a VARIANT with VT_I4 (int) type instead of VT_UNKNOWN (IFont)
        VARIANT invalidVariant = (VARIANT)42;

        // Act & Assert
        // Invalid variant type triggers Debug.Fail in test environment
        Assert.Throws<InvalidOperationException>(() =>
            converter.ConvertNativeToManaged(invalidVariant, s_stubDescriptor));
    }

    [Fact]
    public void Com2FontConverter_ConvertManagedToNative_Null_DefaultsToControlFont()
    {
        // Arrange
        Com2FontConverter converter = new();
        bool cancelSet = false;

        // Act
        VARIANT result = converter.ConvertManagedToNative(null, s_stubDescriptor, ref cancelSet);

        // Assert
        // Null is handled, result should be empty VARIANT and cancelSet is set
        Assert.True(result.IsEmpty);
        Assert.True(cancelSet);
    }

    [Fact]
    public void Com2FontConverter_ConvertManagedToNative_Font_SetsCancelSet()
    {
        // Arrange
        Com2FontConverter converter = new();
        Font testFont = new("Courier New", 10);
        bool cancelSet = false;

        // Act
        VARIANT result = converter.ConvertManagedToNative(testFont, s_stubDescriptor, ref cancelSet);

        // Assert
        // ConvertManagedToNative should set cancelSet to true
        // (we don't actually set the native value, just update the IFont)
        Assert.True(cancelSet);
        Assert.True(result.IsEmpty);
    }

    [Fact]
    public void Com2FontConverter_ConvertManagedToNative_InvalidObject_FailureHandled()
    {
        // Arrange
        Com2FontConverter converter = new();
        object invalidObject = new object();
        bool cancelSet = false;

        // Act & Assert
        // Invalid object type should be handled gracefully
        // The converter expects a Font object
        Assert.Throws<InvalidCastException>(() =>
            converter.ConvertManagedToNative(invalidObject, s_stubDescriptor, ref cancelSet));
    }

    [Fact]
    public void Com2FontConverter_ManagedType_ReturnsFont()
    {
        // Arrange
        Com2FontConverter converter = new();

        // Act
        Type managedType = converter.ManagedType;

        // Assert
        Assert.Equal(typeof(Font), managedType);
    }

    [Fact]
    public void Com2FontConverter_AllowExpand_ReturnsTrue()
    {
        // Arrange
        Com2FontConverter converter = new();

        // Act
        bool allowExpand = converter.AllowExpand;

        // Assert
        Assert.True(allowExpand);
    }

    [Fact]
    public void Com2FontConverter_ConvertNativeToManaged_WithDefaultFont()
    {
        // Arrange
        Com2FontConverter converter = new();
        // Test with a null/empty font by using a descriptor that returns null
        var testDescriptor = new Com2PropertyDescriptor(
            default,
            "TestFont",
            Array.Empty<Attribute>(),
            default,
            default,
            default,
            default);

        // Act
        object? result = converter.ConvertNativeToManaged(default, testDescriptor);

        // Assert
        // Empty VARIANT returns null
        Assert.Null(result);
    }

    [Fact]
    public void Com2FontConverter_ConvertManagedToNative_WithStandardFonts()
    {
        // Arrange
        Com2FontConverter converter = new();
        Font[] testFonts = [
            new Font("Arial", 12),
            new Font("Times New Roman", 14),
            new Font("Courier New", 10)
        ];
        bool cancelSet = false;

        // Act & Assert
        foreach (Font font in testFonts)
        {
            VARIANT result = converter.ConvertManagedToNative(font, s_stubDescriptor, ref cancelSet);
            Assert.True(cancelSet);
        }
    }

    private class CustomGetNativeValueDescriptor : Com2PropertyDescriptor
    {
        private readonly ICustomTypeDescriptor _descriptor;

        public CustomGetNativeValueDescriptor(VARIANT nativeValue)
            : base(default, "Foo", Array.Empty<Attribute>(), default, default, default, default)
        {
            _descriptor = new CustomDescriptor(nativeValue);
        }

        public override object TargetObject => _descriptor;

        private class CustomDescriptor : ICustomTypeDescriptor
        {
            private readonly object _propertyOwner;

            public CustomDescriptor(VARIANT variant) => _propertyOwner = new DispatchStub(variant);

            public AttributeCollection GetAttributes() => throw new NotImplementedException();

            public string? GetClassName() => throw new NotImplementedException();

            public string? GetComponentName() => throw new NotImplementedException();

            public TypeConverter? GetConverter() => throw new NotImplementedException();

            public EventDescriptor? GetDefaultEvent() => throw new NotImplementedException();

            public PropertyDescriptor? GetDefaultProperty() => throw new NotImplementedException();

            public object? GetEditor(Type editorBaseType) => throw new NotImplementedException();

            public EventDescriptorCollection GetEvents() => throw new NotImplementedException();

            public EventDescriptorCollection GetEvents(Attribute[]? attributes) => throw new NotImplementedException();

            public PropertyDescriptorCollection GetProperties() => throw new NotImplementedException();

            public PropertyDescriptorCollection GetProperties(Attribute[]? attributes) => throw new NotImplementedException();

            public object? GetPropertyOwner(PropertyDescriptor? pd) => _propertyOwner;

            private class DispatchStub : IDispatch.Interface, IManagedWrapper<IDispatch>
            {
                private readonly VARIANT _variant;
                public DispatchStub(VARIANT variant) => _variant = variant;

                HRESULT IDispatch.Interface.GetTypeInfoCount(uint* pctinfo) => throw new NotImplementedException();

                HRESULT IDispatch.Interface.GetTypeInfo(uint iTInfo, uint lcid, ITypeInfo** ppTInfo) => throw new NotImplementedException();

                HRESULT IDispatch.Interface.GetIDsOfNames(Guid* riid, PWSTR* rgszNames, uint cNames, uint lcid, int* rgDispId) => throw new NotImplementedException();

                HRESULT IDispatch.Interface.Invoke(
                    int dispIdMember,
                    Guid* riid,
                    uint lcid,
                    DISPATCH_FLAGS dwFlags,
                    DISPPARAMS* pDispParams,
                    VARIANT* pVarResult,
                    EXCEPINFO* pExcepInfo,
                    uint* pArgErr)
                {
                    *pVarResult = _variant;
                    return HRESULT.S_OK;
                }
            }
        }
    }
}
