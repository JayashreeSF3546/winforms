using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using Microsoft.AspNetCore.Components.WebView.WindowsForms;

#nullable enable

namespace WinFormsBlazorHybridApp;

partial class MainForm
{
    private IContainer? components;
    private BlazorWebView _blazorWebView = null!;

    protected override void Dispose(bool disposing)
    {
        if (disposing && components is not null)
        {
            components.Dispose();
        }

        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        components = new Container();
        _blazorWebView = new BlazorWebView();
        SuspendLayout();
        //
        // _blazorWebView
        //
        _blazorWebView.Dock = DockStyle.Fill;
        _blazorWebView.Location = new Point(0, 0);
        _blazorWebView.Name = "_blazorWebView";
        _blazorWebView.Size = new Size(900, 560);
        _blazorWebView.TabIndex = 0;
        //
        // MainForm
        //
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(900, 560);
        Controls.Add(_blazorWebView);
        Name = "MainForm";
        Text = "WinForms Blazor Hybrid";
        ResumeLayout(false);
    }
}
