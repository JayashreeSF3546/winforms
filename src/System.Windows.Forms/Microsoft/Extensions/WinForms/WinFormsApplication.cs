// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Windows.Forms;

namespace Microsoft.Extensions.WinForms;

/// <summary>
///  Owns a built Windows Forms application's startup selection and lifetime notifications.
/// </summary>
/// <remarks>
///  <para>
///   This contract prototype supports composition, deferred startup activation, and disposal.
///   Message-loop execution is not yet implemented. Use and dispose the application on its owning thread.
///  </para>
/// </remarks>
public sealed class WinFormsApplication : IDisposable
{
    private readonly Thread _ownerThread;
    private WinFormsApplicationOptions? _options;
    private ApplicationContext? _context;
    private bool _activationAttempted;
    private bool _activating;
    private bool _disposed;

    internal WinFormsApplication(WinFormsApplicationOptions options)
    {
        _ownerThread = options.OwnerThread;
        _options = options;
        Lifetime = new WinFormsApplicationLifetime();
    }

    /// <summary>
    ///  Creates a builder on the current thread without creating UI resources.
    /// </summary>
    /// <returns>A new builder.</returns>
    public static WinFormsApplicationBuilder CreateBuilder()
        => new();

    /// <summary>
    ///  Gets this application's lifetime notifications.
    /// </summary>
    public WinFormsApplicationLifetime Lifetime { get; }

    /// <summary>
    ///  Runs the application on its owning thread.
    /// </summary>
    /// <remarks>
    ///  <para>
    ///   Execution is reserved for the runtime implementation following this contract prototype.
    ///   Calling this method does not activate startup UI or raise lifetime events.
    ///  </para>
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The application is disposed.</exception>
    /// <exception cref="InvalidOperationException">The calling thread does not own the application.</exception>
    /// <exception cref="NotSupportedException">The prototype does not implement message-loop execution.</exception>
    public void Run()
    {
        VerifyAccess();
        ObjectDisposedException.ThrowIf(_disposed, this);
        throw new NotSupportedException(SR.WinFormsApplication_RuntimeUnavailable);
    }

    internal ApplicationContext ActivateStartup()
    {
        VerifyAccess();
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_activationAttempted)
        {
            throw new InvalidOperationException(SR.WinFormsApplication_StartupAlreadyActivated);
        }

        _activationAttempted = true;
        _activating = true;
        try
        {
            WinFormsApplicationOptions options = _options!;
            if (options.StartupForm is { } form)
            {
                WinFormsApplicationOptions.ValidateStartupForm(form);
                _context = new ApplicationContext(form);
            }
            else
            {
                ApplicationContext context = options.ContextFactory!()
                    ?? throw new InvalidOperationException(SR.WinFormsApplication_NullContext);

                if (context.MainForm is { } mainForm)
                {
                    WinFormsApplicationOptions.ValidateStartupForm(mainForm);
                }

                _context = context;
            }

            return _context;
        }
        finally
        {
            _activating = false;
        }
    }

    /// <summary>
    ///  Disposes startup resources owned by this application without raising lifecycle notifications.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    ///  The calling thread does not own the application, or startup activation is in progress.
    /// </exception>
    public void Dispose()
    {
        VerifyAccess();
        if (_disposed)
        {
            return;
        }

        if (_activating)
        {
            throw new InvalidOperationException(SR.WinFormsApplication_ActivationInProgress);
        }

        _disposed = true;
        ApplicationContext? context = _context;
        Form? startupForm = _options?.StartupForm;
        _context = null;
        _options = null;

        if (context is not null)
        {
            context.Dispose();
        }
        else if (startupForm is { IsDisposed: false })
        {
            startupForm.Dispose();
        }
    }

    private void VerifyAccess()
    {
        if (Thread.CurrentThread != _ownerThread)
        {
            throw new InvalidOperationException(SR.WinFormsApplication_WrongThread);
        }
    }
}
