using Terminal.Gui;

namespace InterviewAssist.AnnotationConsole;

public sealed class AnnotationApp
{
    private readonly ReviewSession _session;
    private readonly string _title;
    private readonly string _backgroundColorHex;

    private HighlightedTextView _transcriptView = null!;
    private Label _currentItemLabel = null!;
    private Label _statsLabel = null!;
    private Label _textLabel = null!;
    private Label _detailsLabel = null!;

    private static readonly string[] SubtypeOptions =
    {
        "Definition", "HowTo", "Compare", "Troubleshoot",
        "Rhetorical", "Clarification", "General", "(clear)"
    };

    public AnnotationApp(ReviewSession session, string evaluationFileName, string backgroundColorHex)
    {
        _session = session;
        _title = $"Review: {evaluationFileName}";
        _backgroundColorHex = backgroundColorHex;
    }

    public void Run()
    {
        var backgroundColor = ParseHexColor(_backgroundColorHex);

        var colorScheme = new ColorScheme
        {
            Normal = Terminal.Gui.Attribute.Make(Color.White, backgroundColor),
            Focus = Terminal.Gui.Attribute.Make(Color.White, backgroundColor),
            HotNormal = Terminal.Gui.Attribute.Make(Color.BrightYellow, backgroundColor),
            HotFocus = Terminal.Gui.Attribute.Make(Color.BrightYellow, backgroundColor),
            Disabled = Terminal.Gui.Attribute.Make(Color.Gray, backgroundColor)
        };

        var mainWindow = new Window(_title)
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            ColorScheme = colorScheme
        };

        // Transcript panel (top 65%)
        var transcriptFrame = new FrameView("Transcript")
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Percent(65)
        };

        _transcriptView = new HighlightedTextView
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            CanFocus = true
        };
        _transcriptView.SetBackgroundColor(backgroundColor);
        _transcriptView.Text = !string.IsNullOrEmpty(_session.FullTranscript)
            ? _session.FullTranscript
            : "(no transcript)";
        _transcriptView.SetHighlights(_session.Highlights);
        _transcriptView.SetDecisionProvider(idx =>
            idx >= 0 && idx < _session.Items.Count ? _session.Items[idx].Decision : ReviewDecision.Pending);

        transcriptFrame.Add(_transcriptView);

        // Current item panel (middle 25%)
        var itemFrame = new FrameView("Current Item")
        {
            X = 0,
            Y = Pos.Bottom(transcriptFrame),
            Width = Dim.Fill(),
            Height = Dim.Percent(25)
        };

        _currentItemLabel = new Label("")
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = 1
        };

        _statsLabel = new Label("")
        {
            X = 0,
            Y = 1,
            Width = Dim.Fill(),
            Height = 1
        };

        _textLabel = new Label("")
        {
            X = 0,
            Y = 2,
            Width = Dim.Fill(),
            Height = 2
        };

        _detailsLabel = new Label("")
        {
            X = 0,
            Y = 4,
            Width = Dim.Fill(),
            Height = 1
        };

        itemFrame.Add(_currentItemLabel, _statsLabel, _textLabel, _detailsLabel);

        // Status bar
        var statusBar = new StatusBar(new StatusItem[]
        {
            new(Key.A, "~A~ccept", () => DoAccept()),
            new(Key.X, "~X~ Reject", () => DoReject()),
            new(Key.E, "~E~dit", () => DoEdit()),
            new(Key.S, "~S~ubtype", () => DoSubtype()),
            new(Key.N, "~N~ext", () => DoNext()),
            new(Key.P, "~P~rev", () => DoPrev()),
            new(Key.M, "~M~ Add", () => DoAddMissed()),
            new(Key.F, "~F~inalise", () => DoFinalise()),
            new(Key.Q | Key.CtrlMask, "~Ctrl+Q~ Quit", () => Application.RequestStop()),
        });

        mainWindow.Add(transcriptFrame, itemFrame);
        Application.Top.Add(mainWindow, statusBar);

        // Handle keyboard shortcuts on top-level
        Application.Top.KeyPress += OnKeyPress;

        // Initial display
        UpdateDisplay();

        Application.Run();
    }

    private void OnKeyPress(View.KeyEventEventArgs args)
    {
        // Only handle single key presses (no Ctrl modifiers except Ctrl+Q)
        switch (args.KeyEvent.Key)
        {
            case Key.A:
            case Key.a:
                DoAccept();
                args.Handled = true;
                break;
            case Key.X:
            case Key.x:
                DoReject();
                args.Handled = true;
                break;
            case Key.E:
            case Key.e:
                DoEdit();
                args.Handled = true;
                break;
            case Key.S:
            case Key.s:
                DoSubtype();
                args.Handled = true;
                break;
            case Key.N:
            case Key.n:
                DoNext();
                args.Handled = true;
                break;
            case Key.P:
            case Key.p:
                DoPrev();
                args.Handled = true;
                break;
            case Key.M:
            case Key.m:
                DoAddMissed();
                args.Handled = true;
                break;
            case Key.F:
            case Key.f:
                DoFinalise();
                args.Handled = true;
                break;
        }
    }

    private void DoAccept()
    {
        _session.Accept();
        AutoSave();
        UpdateDisplay();
    }

    private void DoReject()
    {
        _session.Reject();
        AutoSave();
        UpdateDisplay();
    }

    private void DoEdit()
    {
        if (_session.Count == 0) return;

        var current = _session.Items[_session.CurrentIndex];
        var textField = new TextField(current.ModifiedText ?? current.Text)
        {
            X = 1,
            Y = 1,
            Width = Dim.Fill(2)
        };

        var dialog = new Dialog("Edit Question Text", 70, 7);
        dialog.Add(new Label("Text:") { X = 1, Y = 0 }, textField);

        var okButton = new Button("OK", is_default: true);
        okButton.Clicked += () =>
        {
            var newText = textField.Text?.ToString()?.Trim();
            if (!string.IsNullOrEmpty(newText))
            {
                _session.ModifyText(newText);
                _transcriptView.SetHighlights(_session.Highlights);
                AutoSave();
                UpdateDisplay();
            }
            Application.RequestStop();
        };

        var cancelButton = new Button("Cancel");
        cancelButton.Clicked += () => Application.RequestStop();

        dialog.AddButton(okButton);
        dialog.AddButton(cancelButton);

        Application.Run(dialog);
    }

    private void DoSubtype()
    {
        if (_session.Count == 0) return;

        var current = _session.Items[_session.CurrentIndex];
        var currentSubtype = current.ModifiedSubtype ?? current.Subtype ?? "";

        var listView = new ListView(SubtypeOptions)
        {
            X = 1,
            Y = 1,
            Width = Dim.Fill(2),
            Height = SubtypeOptions.Length
        };

        // Try to select current subtype
        for (int i = 0; i < SubtypeOptions.Length; i++)
        {
            if (SubtypeOptions[i].Equals(currentSubtype, StringComparison.OrdinalIgnoreCase))
            {
                listView.SelectedItem = i;
                break;
            }
        }

        var dialog = new Dialog("Set Subtype", 40, SubtypeOptions.Length + 5);
        dialog.Add(new Label("Select subtype:") { X = 1, Y = 0 }, listView);

        var okButton = new Button("OK", is_default: true);
        okButton.Clicked += () =>
        {
            var selected = SubtypeOptions[listView.SelectedItem];
            _session.SetSubtype(selected == "(clear)" ? null : selected);
            AutoSave();
            UpdateDisplay();
            Application.RequestStop();
        };

        var cancelButton = new Button("Cancel");
        cancelButton.Clicked += () => Application.RequestStop();

        dialog.AddButton(okButton);
        dialog.AddButton(cancelButton);

        Application.Run(dialog);
    }

    private void DoNext()
    {
        _session.NavigateNext();
        UpdateDisplay();
    }

    private void DoPrev()
    {
        _session.NavigatePrevious();
        UpdateDisplay();
    }

    private void DoAddMissed()
    {
        var textField = new TextField("")
        {
            X = 1,
            Y = 1,
            Width = Dim.Fill(2)
        };

        var subtypeField = new TextField("")
        {
            X = 1,
            Y = 3,
            Width = Dim.Fill(2)
        };

        var dialog = new Dialog("Add Missed Question", 70, 9);
        dialog.Add(
            new Label("Question text:") { X = 1, Y = 0 },
            textField,
            new Label("Subtype (optional):") { X = 1, Y = 2 },
            subtypeField);

        var okButton = new Button("OK", is_default: true);
        okButton.Clicked += () =>
        {
            var text = textField.Text?.ToString()?.Trim();
            if (!string.IsNullOrEmpty(text))
            {
                var subtype = subtypeField.Text?.ToString()?.Trim();
                _session.AddMissedQuestion(text, string.IsNullOrEmpty(subtype) ? null : subtype);
                _transcriptView.SetHighlights(_session.Highlights);
                AutoSave();
                UpdateDisplay();
            }
            Application.RequestStop();
        };

        var cancelButton = new Button("Cancel");
        cancelButton.Clicked += () => Application.RequestStop();

        dialog.AddButton(okButton);
        dialog.AddButton(cancelButton);

        Application.Run(dialog);
    }

    private void DoFinalise()
    {
        var pendingCount = _session.PendingCount;
        var message = pendingCount > 0
            ? $"Accepted: {_session.AcceptedCount}  Rejected: {_session.RejectedCount}  Modified: {_session.ModifiedCount}\n" +
              $"WARNING: {pendingCount} items still pending.\n\nFinalise anyway?"
            : $"Accepted: {_session.AcceptedCount}  Rejected: {_session.RejectedCount}  Modified: {_session.ModifiedCount}\n\n" +
              "Save validated ground truth and quit?";

        var result = MessageBox.Query("Finalise Review", message, "Save & Quit", "Cancel");
        if (result == 0)
        {
            try
            {
                _session.FinaliseAsync().GetAwaiter().GetResult();
                _session.SaveSessionAsync().GetAwaiter().GetResult();
                var path = _session.GetValidatedFilePath();
                MessageBox.Query("Saved", $"Validated ground truth saved to:\n{path}", "OK");
            }
            catch (Exception ex)
            {
                MessageBox.Query("Error", $"Error saving: {ex.Message}", "OK");
            }
            Application.RequestStop();
        }
    }

    private void UpdateDisplay()
    {
        if (_session.Count == 0)
        {
            _currentItemLabel.Text = "No items to review";
            _statsLabel.Text = "";
            _textLabel.Text = "";
            _detailsLabel.Text = "";
            return;
        }

        var current = _session.Items[_session.CurrentIndex];
        var displayText = current.ModifiedText ?? current.Text;

        _currentItemLabel.Text = $"CURRENT ITEM ({_session.CurrentIndex + 1} of {_session.Count})";
        _statsLabel.Text = $"Accepted: {_session.AcceptedCount}  Rejected: {_session.RejectedCount}  " +
                          $"Modified: {_session.ModifiedCount}  Pending: {_session.PendingCount}";
        _textLabel.Text = $"Text: \"{Truncate(displayText, 80)}\"";
        _detailsLabel.Text = $"Subtype: {current.ModifiedSubtype ?? current.Subtype ?? "(none)"}    " +
                            $"Confidence: {current.Confidence:F2}    " +
                            $"Status: {current.Decision}    " +
                            $"Source: {current.Source}";

        // Update transcript highlight
        var highlightIdx = _transcriptView.FindHighlightForReviewItem(_session.CurrentIndex);
        _transcriptView.SetCurrentHighlight(highlightIdx);
        if (highlightIdx >= 0)
        {
            _transcriptView.ScrollToHighlight(highlightIdx);
        }
    }

    private void AutoSave()
    {
        // Fire-and-forget save
        _ = Task.Run(async () =>
        {
            try
            {
                await _session.SaveSessionAsync();
            }
            catch
            {
                // Silently ignore save failures
            }
        });
    }

    private static string Truncate(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
            return text;
        return text[..maxLength] + "...";
    }

    private static Color ParseHexColor(string hex)
    {
        hex = hex.TrimStart('#');
        if (hex.Length != 6) return Color.Black;

        try
        {
            var r = Convert.ToInt32(hex[..2], 16);
            var g = Convert.ToInt32(hex[2..4], 16);
            var b = Convert.ToInt32(hex[4..6], 16);
            var brightness = (r + g + b) / 3;

            if (brightness < 50) return Color.Black;
            if (brightness < 100) return Color.DarkGray;
            if (brightness < 160) return Color.Gray;
            return Color.White;
        }
        catch
        {
            return Color.Black;
        }
    }
}
