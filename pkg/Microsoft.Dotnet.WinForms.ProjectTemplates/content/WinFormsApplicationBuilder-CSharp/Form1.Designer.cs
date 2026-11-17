#nullable enable

namespace Company.WinFormsBuilderApp1;

/// <summary>
///  Contains Designer-managed components and layout.
/// </summary>
partial class Form1
{
    private System.ComponentModel.IContainer? components;

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (disposing && components is not null)
        {
            components.Dispose();
        }

        base.Dispose(disposing);
    }

    #region Windows Form Designer generated code

    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(800, 450);
        Name = "Form1";
        Text = "Form1";
    }

    #endregion
}
