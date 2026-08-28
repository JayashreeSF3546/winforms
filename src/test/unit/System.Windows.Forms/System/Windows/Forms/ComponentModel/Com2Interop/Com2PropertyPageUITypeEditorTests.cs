// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#nullable disable

using System.ComponentModel;
using System.Drawing.Design;
using System.Windows.Forms.ComponentModel.Com2Interop;

namespace System.Windows.Forms.Tests.ComponentModel.Com2Interop;

// Tests for Com2PropertyPageUITypeEditor (declared in COM2PropertyPageUITypeConverter.cs). This editor
// invokes native COM property-page APIs (OleCreatePropertyFrame) when actually editing a value, so this
// suite is intentionally limited to behavior that doesn't require live COM objects or a message loop.
public class Com2PropertyPageUITypeEditorTests
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
    public void GetEditStyle_ReturnsModal()
    {
        // Arrange
        Com2PropertyPageUITypeEditor editor = new(s_stubDescriptor, Guid.NewGuid(), baseEditor: null);

        // Act
        UITypeEditorEditStyle style = editor.GetEditStyle(context: null);

        // Assert
        style.Should().Be(UITypeEditorEditStyle.Modal);
    }

    [Fact]
    public void GetEditStyle_WithBaseEditor_StillReturnsModal()
    {
        // Arrange - the base editor's edit style is intentionally ignored; property pages are always modal.
        Com2PropertyPageUITypeEditor editor = new(s_stubDescriptor, Guid.NewGuid(), new NonModalEditor());

        // Act
        UITypeEditorEditStyle style = editor.GetEditStyle(context: null);

        // Assert
        style.Should().Be(UITypeEditorEditStyle.Modal);
    }

    [Fact]
    public void Ctor_DoesNotThrow()
    {
        // Act
        Action action = () => new Com2PropertyPageUITypeEditor(s_stubDescriptor, Guid.Empty, null);

        // Assert
        action.Should().NotThrow();
    }

    private sealed class NonModalEditor : UITypeEditor
    {
        public override UITypeEditorEditStyle GetEditStyle(ITypeDescriptorContext context) => UITypeEditorEditStyle.DropDown;
    }
}
