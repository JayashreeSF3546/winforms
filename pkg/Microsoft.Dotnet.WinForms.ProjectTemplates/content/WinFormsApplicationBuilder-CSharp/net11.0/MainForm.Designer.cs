using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

#nullable enable

namespace WinFormsApplicationBuilderApp;

partial class MainForm
{
    private IContainer? components;
    private Label _greetingLabel = null!;
    private TextBox _nameTextBox = null!;
    private Button _greetButton = null!;
    private Button _saveButton = null!;
    private Label _statusLabel = null!;

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
        _greetingLabel = new Label();
        _nameTextBox = new TextBox();
        _greetButton = new Button();
        _saveButton = new Button();
        _statusLabel = new Label();
        SuspendLayout();
        //
        // _greetingLabel
        //
        _greetingLabel.AutoSize = true;
        _greetingLabel.Location = new Point(20, 20);
        _greetingLabel.Name = "_greetingLabel";
        _greetingLabel.Size = new Size(115, 15);
        _greetingLabel.Text = "Hello, developer!";
        //
        // _nameTextBox
        //
        _nameTextBox.Location = new Point(20, 55);
        _nameTextBox.Name = "_nameTextBox";
        _nameTextBox.PlaceholderText = "Your name";
        _nameTextBox.Size = new Size(220, 23);
        //
        // _greetButton
        //
        _greetButton.Location = new Point(255, 54);
        _greetButton.Name = "_greetButton";
        _greetButton.Size = new Size(90, 25);
        _greetButton.Text = "Greet";
        _greetButton.UseVisualStyleBackColor = true;
        _greetButton.Click += GreetButton_Click;
        //
        // _saveButton
        //
        _saveButton.Location = new Point(355, 54);
        _saveButton.Name = "_saveButton";
        _saveButton.Size = new Size(110, 25);
        _saveButton.Text = "Save name";
        _saveButton.UseVisualStyleBackColor = true;
        _saveButton.Click += SaveButton_Click;
        //
        // _statusLabel
        //
        _statusLabel.AutoSize = true;
        _statusLabel.Location = new Point(20, 100);
        _statusLabel.Name = "_statusLabel";
        _statusLabel.Size = new Size(0, 15);
        //
        // MainForm
        //
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(490, 145);
        Controls.Add(_statusLabel);
        Controls.Add(_saveButton);
        Controls.Add(_greetButton);
        Controls.Add(_nameTextBox);
        Controls.Add(_greetingLabel);
        Name = "MainForm";
        Text = "WinForms Application Builder";
        Load += MainForm_Load;
        ResumeLayout(false);
        PerformLayout();
    }
}
