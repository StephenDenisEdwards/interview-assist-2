using System.Text.Json;
using Terminal.Gui;
using Attribute = Terminal.Gui.Attribute;

namespace InterviewAssist.AnnotationConceptEConsole;

/// <summary>
/// Main application class for the Concept E annotation console.
/// Two-panel layout: transcript with inline highlighting on the left,
/// synchronised question list on the right.
/// </summary>
public sealed class ConceptEApp
{
    private static readonly string[] Subtypes =
        { "Definition", "HowTo", "Compare", "Troubleshoot", "Rhetorical", "Clarification" };

    private readonly string _transcript;
    private readonly List<AnnotatedQuestion> _questions;
    private readonly string _fileName;
    private readonly string _recordingPath;

    private HighlightTextView _textView = null!;
    private QuestionListView _listView = null!;
    private FrameView _questionFrame = null!;
    private readonly List<string> _listItems = new();

    // Colour attributes for highlights
    private Attribute _llmHighlightAttr;
    private Attribute _userHighlightAttr;

    private int _nextId;
    private bool _suppressListSync;
    private bool _suppressHighlightSync;

    public ConceptEApp(
        string transcript,
        List<AnnotatedQuestion> questions,
        string fileName,
        string recordingPath)
    {
        _transcript = transcript;
        _questions = new List<AnnotatedQuestion>(questions);
        _fileName = fileName;
        _recordingPath = recordingPath;
        _nextId = questions.Count > 0 ? questions.Max(q => q.Id) + 1 : 1;
    }

    public void Run()
    {
        // Initialise colour attributes (must be done after Application.Init)
        _llmHighlightAttr = new Attribute(Color.BrightYellow, Color.DarkGray);
        _userHighlightAttr = new Attribute(Color.White, Color.DarkGray);

        var mainWindow = new Window($"Concept E: {_fileName}")
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill()
        };

        // Left panel: transcript with highlights
        var transcriptFrame = new FrameView($"Transcript ({_transcript.Length:N0} chars)")
        {
            X = 0,
            Y = 0,
            Width = Dim.Percent(60),
            Height = Dim.Fill()
        };

        _textView = new HighlightTextView
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            Text = _transcript
        };

        _textView.TextSelected += OnTextSelected;
        _textView.HighlightEntered += OnHighlightEntered;

        transcriptFrame.Add(_textView);

        // Right panel: question list
        _questionFrame = new FrameView("Questions")
        {
            X = Pos.Right(transcriptFrame),
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill()
        };
        UpdateQuestionFrameTitle();

        RefreshListItems();

        _listView = new QuestionListView
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill()
        };
        _listView.SetItems(_listItems, GetFlaggedIndices());

        _listView.SelectedItemChanged += OnListSelectionChanged;
        _listView.ItemKeyPressed += OnListKeyPressed;

        _questionFrame.Add(_listView);

        mainWindow.Add(transcriptFrame, _questionFrame);

        // Apply highlights for initial questions
        RefreshHighlights();

        // Status bar
        var statusBar = new StatusBar(new StatusItem[]
        {
            new(Key.Null, "~M~ Mark", null),
            new(Key.Null, "~S~ Select", null),
            new(Key.Null, "~D~ Delete", null),
            new(Key.Null, "~T~ Type", null),
            new(Key.Null, "~Tab~ Focus", null),
            new(Key.F2, "~F2~ Save", () => SaveAnnotations()),
            new(Key.S | Key.CtrlMask, "~Ctrl+S~ Save", () => SaveAnnotations()),
            new(Key.Q | Key.CtrlMask, "~Ctrl+Q~ Quit", () => Application.RequestStop()),
        });

        Application.Top.Add(mainWindow, statusBar);
        Application.Run();
    }

    private void OnTextSelected(int start, int end)
    {
        if (start == end) return;

        int selStart = Math.Min(start, end);
        int selEnd = Math.Max(start, end);

        // Extract the selected text
        string selectedText = _transcript.Substring(selStart, selEnd - selStart).Trim();
        if (string.IsNullOrWhiteSpace(selectedText)) return;

        // Create a new user-added question
        var question = new AnnotatedQuestion(
            Id: _nextId++,
            Text: selectedText,
            OriginalText: selectedText,
            Subtype: null,
            Confidence: 1.0,
            TranscriptStartOffset: selStart,
            TranscriptEndOffset: selEnd,
            Source: QuestionSource.UserAdded);

        _questions.Add(question);
        _textView.ClearSelection();

        RefreshHighlights();
        RefreshListItems();
        _listView.SetItems(_listItems, GetFlaggedIndices());
        _listView.SelectedItem = _questions.Count - 1;
        UpdateQuestionFrameTitle();
        _listView.SetNeedsDisplay();
    }

    private void OnHighlightEntered(int highlightId)
    {
        if (_suppressHighlightSync) return;

        int idx = _questions.FindIndex(q => q.Id == highlightId);
        if (idx >= 0)
        {
            _suppressListSync = true;
            _listView.SelectedItem = idx;
            _listView.SetNeedsDisplay();
            _suppressListSync = false;
        }
    }

    private void OnListSelectionChanged(int selectedIndex)
    {
        if (_suppressListSync) return;
        if (selectedIndex < 0 || selectedIndex >= _questions.Count) return;

        var question = _questions[selectedIndex];

        _suppressHighlightSync = true;
        _textView.ScrollToOffset(question.TranscriptStartOffset);
        _suppressHighlightSync = false;
    }

    private void OnListKeyPressed(Key key)
    {
        if (key == (Key)((int)'d') || key == (Key)((int)'D'))
            DeleteSelectedQuestion();
        else if (key == (Key)((int)'t') || key == (Key)((int)'T'))
            CycleSelectedQuestionSubtype();
    }

    private void DeleteSelectedQuestion()
    {
        if (_questions.Count == 0) return;
        int idx = _listView.SelectedItem;
        if (idx < 0 || idx >= _questions.Count) return;

        var question = _questions[idx];
        _textView.RemoveHighlight(question.Id);
        _questions.RemoveAt(idx);

        RefreshListItems();
        _listView.SetItems(_listItems, GetFlaggedIndices());

        if (_questions.Count > 0)
            _listView.SelectedItem = Math.Min(idx, _questions.Count - 1);

        UpdateQuestionFrameTitle();
        _listView.SetNeedsDisplay();
        _textView.SetNeedsDisplay();
    }

    private void CycleSelectedQuestionSubtype()
    {
        if (_questions.Count == 0) return;
        int idx = _listView.SelectedItem;
        if (idx < 0 || idx >= _questions.Count) return;

        var question = _questions[idx];

        // Cycle: Definition -> HowTo -> Compare -> Troubleshoot -> Rhetorical -> Clarification -> null -> Definition
        string? nextSubtype;
        if (question.Subtype == null)
        {
            nextSubtype = Subtypes[0];
        }
        else
        {
            int subtypeIdx = Array.IndexOf(Subtypes, question.Subtype);
            if (subtypeIdx < 0 || subtypeIdx >= Subtypes.Length - 1)
                nextSubtype = subtypeIdx == Subtypes.Length - 1 ? null : Subtypes[0];
            else
                nextSubtype = Subtypes[subtypeIdx + 1];
        }

        _questions[idx] = question with { Subtype = nextSubtype };

        RefreshListItems();
        _listView.SetItems(_listItems, GetFlaggedIndices());
        _listView.SelectedItem = idx;
        UpdateQuestionFrameTitle();
        _listView.SetNeedsDisplay();
    }

    private void RefreshHighlights()
    {
        var highlights = _questions.Select(q => new HighlightRegion
        {
            Id = q.Id,
            Start = q.TranscriptStartOffset,
            End = q.TranscriptEndOffset,
            Attr = q.Source == QuestionSource.LlmDetected ? _llmHighlightAttr : _userHighlightAttr
        });

        _textView.SetHighlights(highlights);
    }

    private void RefreshListItems()
    {
        _listItems.Clear();
        foreach (var q in _questions)
        {
            var subtypeLabel = FormatSubtypeLabel(q.Subtype);
            var confidenceLabel = FormatConfidenceLabel(q.Confidence);
            var sourceLabel = q.Source == QuestionSource.LlmDetected ? "LLM" : "USR";

            var sb = new System.Text.StringBuilder();
            sb.Append($"[{sourceLabel} | {subtypeLabel} | {confidenceLabel}]");
            sb.Append($"\n  Reformulated: {q.Text}");
            sb.Append($"\n  Original:     {q.OriginalText ?? "(not available)"}");
            _listItems.Add(sb.ToString());
        }
    }

    private static string FormatSubtypeLabel(string? subtype) => subtype switch
    {
        "Definition" => "Asking for definition",
        "HowTo" => "Asking how-to",
        "Compare" => "Asking to compare",
        "Troubleshoot" => "Troubleshooting",
        "Rhetorical" => "Rhetorical",
        "Clarification" => "Clarification",
        null => "General question",
        _ => subtype
    };

    private static string FormatConfidenceLabel(double confidence) => confidence switch
    {
        >= 0.9 => $"High confidence ({confidence:F1})",
        >= 0.7 => $"Medium confidence ({confidence:F1})",
        _ => $"Low confidence ({confidence:F1})"
    };

    private HashSet<int> GetFlaggedIndices()
    {
        var flagged = new HashSet<int>();
        for (int i = 0; i < _questions.Count; i++)
        {
            if (_questions[i].Subtype == null)
                flagged.Add(i);
        }
        return flagged;
    }

    private void UpdateQuestionFrameTitle()
    {
        int untagged = _questions.Count(q => q.Subtype == null);
        string warning = untagged > 0 ? $" - {untagged} untagged" : "";
        _questionFrame.Title = $"Questions ({_questions.Count}{warning})";
        _questionFrame.SetNeedsDisplay();
    }

    private void SaveAnnotations()
    {
        try
        {
            var outputPath = Path.ChangeExtension(_recordingPath, null) + "-annotations.json";

            var output = new
            {
                sourceRecording = _fileName,
                annotatedAt = DateTime.UtcNow.ToString("o"),
                questions = _questions.Select(q => new
                {
                    text = q.Text,
                    subtype = q.Subtype,
                    confidence = q.Confidence,
                    source = q.Source == QuestionSource.LlmDetected ? "llm-detected" : "user-added",
                    transcriptStart = q.TranscriptStartOffset,
                    transcriptEnd = q.TranscriptEndOffset
                }).ToArray()
            };

            var json = JsonSerializer.Serialize(output, new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            File.WriteAllText(outputPath, json);

            MessageBox.Query("Saved", $"Saved {_questions.Count} questions to:\n{Path.GetFullPath(outputPath)}", "OK");
        }
        catch (Exception ex)
        {
            MessageBox.ErrorQuery("Save Failed", $"Error saving annotations:\n{ex.Message}", "OK");
        }
    }
}

/// <summary>
/// Represents an annotated question with its transcript position.
/// </summary>
public sealed record AnnotatedQuestion(
    int Id,
    string Text,
    string? OriginalText,
    string? Subtype,
    double Confidence,
    int TranscriptStartOffset,
    int TranscriptEndOffset,
    QuestionSource Source);

/// <summary>
/// Source of a question annotation.
/// </summary>
public enum QuestionSource
{
    LlmDetected,
    UserAdded
}
