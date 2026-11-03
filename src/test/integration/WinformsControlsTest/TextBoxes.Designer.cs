// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace WinFormsControlsTest;

partial class TextBoxes
{
    /// <summary>
    ///  Required designer variable.
    /// </summary>
    private System.ComponentModel.IContainer components = null;

    /// <summary>
    ///  Clean up any resources being used.
    /// </summary>
    /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
    protected override void Dispose(bool disposing)
    {
        if (disposing && (components != null))
        {
            components.Dispose();
        }
        base.Dispose(disposing);
    }

    #region Windows Form Designer generated code

    /// <summary>
    ///  Required method for Designer support - do not modify
    ///  the contents of this method with the code editor.
    /// </summary>
    private void InitializeComponent()
    {
        this.textBox1 = new System.Windows.Forms.TextBox();
        this.SuspendLayout();
        // 
        // textBox1
        // 
        this.textBox1.BorderStyle = System.Windows.Forms.BorderStyle.None;
        this.textBox1.Font = new System.Drawing.Font("Cascadia Code", 20F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
        this.textBox1.Location = new System.Drawing.Point(15, 5);
        this.textBox1.Margin = new System.Windows.Forms.Padding(6, 7, 6, 7);
        this.textBox1.Name = "textBox1";
        this.textBox1.Size = new System.Drawing.Size(1401, 62);
        this.textBox1.TabIndex = 0;
        this.textBox1.Text = "The quick brown fox jumps over the lazy dog";
        // 
        // Form1
        // 
        this.AutoScaleDimensions = new System.Drawing.SizeF(13F, 32F);
        this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
        this.ClientSize = new System.Drawing.Size(1733, 1108);
        this.Controls.Add(this.textBox1);
        this.Margin = new System.Windows.Forms.Padding(6, 7, 6, 7);
        this.Name = "Form1";
        this.Text = "WinForms";
        this.ResumeLayout(false);
        this.PerformLayout();
    }

    #endregion

    private System.Windows.Forms.TextBox textBox1;
}

