' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.

Imports System.Text.Json
Imports System.Windows.Forms

Namespace ApplicationBuilderMigration.VisualBasic
    ''' <summary>
    '''  Displays preserved tokens, retained work state, and the selected driver's events.
    ''' </summary>
    Friend NotInheritable Class MainForm
        Inherits Form

        Private ReadOnly _session As MigrationSession
        Private ReadOnly _trace As New TextBox With {.Multiline = True, .ReadOnly = True, .Dock = DockStyle.Fill}
        Private ReadOnly _veto As New CheckBox With {.Text = "Veto ordinary close", .AutoSize = True}
        Private ReadOnly _process As New Button With {.Text = "Process item", .AutoSize = True}
        Private ReadOnly _close As New Button With {.Text = "Close", .AutoSize = True}

        Friend Sub New(session As MigrationSession, probe As Boolean, model As String)
            _session = session
            Text = $"VB — {model} (repository migration example)"
            Width = 720
            Height = 460
            Dim commands As New FlowLayoutPanel With {.Dock = DockStyle.Bottom, .AutoSize = True}
            commands.Controls.AddRange(New Control() {_process, _veto, _close})
            Controls.Add(_trace)
            Controls.Add(commands)
            AddHandler _process.Click,
                Sub(sender, e)
                    _session.ProcessItem()
                    Render()
                End Sub
            AddHandler _close.Click, Sub(sender, e) Close()
            AddHandler FormClosing,
                Sub(sender, e)
                    e.Cancel = _veto.Checked
                    _session.Events.Add(If(e.Cancel, "close-vetoed", "closing"))
                    Render()
                End Sub
            AddHandler Disposed, Sub(sender, e) _session.Events.Add("form-disposed")
            AddHandler Shown,
                Sub(sender, e)
                    _session.Events.Add("shown")
                    Render()
                    ' Explicit test-only mode. Normal launches wait for the user.
                    If probe Then
                        BeginInvoke(New Action(
                            Sub()
                                _process.PerformClick()
                                _veto.Checked = True
                                _close.PerformClick()
                                _veto.Checked = False
                                _close.PerformClick()
                            End Sub))
                    End If
                End Sub
        End Sub

        Private Sub Render()
            _trace.Text = $"Processed items: {_session.Count}{vbCrLf}" &
                $"Arguments (JSON tokens): {JsonSerializer.Serialize(_session.Arguments)}{vbCrLf}" &
                String.Join(vbCrLf, _session.Events)
        End Sub
    End Class
End Namespace
