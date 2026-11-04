#nullable enable

namespace Company.WinFormsHybridApp1;

/// <summary>
///  Contains native Designer-managed layout only; no WebView or host is created here.
/// </summary>
partial class HybridForm
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
        contentHost = new Panel();
        SuspendLayout();
        contentHost.Dock = DockStyle.Fill;
        contentHost.Name = "contentHost";
        Controls.Add(contentHost);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(960, 600);
        Name = "HybridForm";
        Text = "Hybrid scaffold (disabled)";
        ResumeLayout(false);
    }

    #endregion

    private Panel contentHost = null!;
}
