using Microsoft.Extensions.Logging;

namespace Company.WinFormsBuilderApp1;

/// <summary>
///  Keeps Designer construction independent from runtime service composition.
/// </summary>
public partial class Form1 : Form
{
    public Form1()
    {
        InitializeComponent();
    }

    internal Form1(ApplicationOptions options, ILogger<Form1> logger) : this()
    {
        Text = options.Title;
#pragma warning disable CA1848 // A single startup message keeps generated code approachable.
        logger.LogInformation("Main window created.");
#pragma warning restore CA1848
    }
}
