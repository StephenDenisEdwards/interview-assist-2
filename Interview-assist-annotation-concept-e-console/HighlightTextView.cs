using Terminal.Gui;
using Attribute = Terminal.Gui.Attribute;

namespace InterviewAssist.AnnotationConceptEConsole;

/// <summary>
/// Custom view that renders text with word wrapping, inline highlight regions,
/// and keyboard-driven text selection. Terminal.Gui 1.x's TextView does not
/// support inline colour changes, so this renders character-by-character.
/// </summary>
public sealed class HighlightTextView : View
{
    private string _text = "";
    private readonly List<HighlightRegion> _highlights = new();
    private List<string> _wrappedLines = new();
    private List<int> _lineStartOffsets = new();
    private int _topLine;
    private int _cursorLine;
    private int _cursorCol;
    private int _selStart = -1;
    private int _selEnd = -1;
    private bool _marking; // true when in mark mode (anchor dropped)
    private int _lastKnownWidth;

    private Attribute _normalAttr;
    private Attribute _selectionAttr;
    private Attribute _cursorAttr;

    /// <summary>Fired when user presses S with an active selection. Args: (startOffset, endOffset).</summary>
    public event Action<int, int>? TextSelected;

    /// <summary>Fired when cursor enters a highlight region. Arg: highlight Id.</summary>
    public event Action<int>? HighlightEntered;

    public HighlightTextView()
    {
        CanFocus = true;
    }

    public new string Text
    {
        get => _text;
        set
        {
            _text = value ?? "";
            _lastKnownWidth = 0; // force re-wrap
            RewrapIfNeeded();
            SetNeedsDisplay();
        }
    }

    public void SetHighlights(IEnumerable<HighlightRegion> highlights)
    {
        _highlights.Clear();
        _highlights.AddRange(highlights);
        SetNeedsDisplay();
    }

    public void AddHighlight(HighlightRegion highlight)
    {
        _highlights.Add(highlight);
        SetNeedsDisplay();
    }

    public void RemoveHighlight(int id)
    {
        _highlights.RemoveAll(h => h.Id == id);
        SetNeedsDisplay();
    }

    public void ScrollToOffset(int charOffset)
    {
        // Find which wrapped line contains this offset
        for (int i = 0; i < _wrappedLines.Count; i++)
        {
            int lineEnd = _lineStartOffsets[i] + _wrappedLines[i].Length;
            if (charOffset >= _lineStartOffsets[i] && charOffset <= lineEnd)
            {
                // Center the line on screen
                int visibleLines = Bounds.Height;
                _topLine = Math.Max(0, i - visibleLines / 3);
                _cursorLine = i;
                _cursorCol = charOffset - _lineStartOffsets[i];
                SetNeedsDisplay();
                return;
            }
        }
    }

    public void ClearSelection()
    {
        _selStart = -1;
        _selEnd = -1;
        SetNeedsDisplay();
    }

    public override void Redraw(Rect bounds)
    {
        RewrapIfNeeded();

        _normalAttr = new Attribute(Color.White, Color.Black);
        _selectionAttr = new Attribute(Color.White, Color.Blue);
        _cursorAttr = new Attribute(Color.Black, Color.White);

        Driver.SetAttribute(_normalAttr);
        Clear();

        int visibleLines = bounds.Height;
        int cursorRow = _cursorLine - _topLine;

        for (int row = 0; row < visibleLines; row++)
        {
            int lineIdx = _topLine + row;
            if (lineIdx >= _wrappedLines.Count)
                break;

            var line = _wrappedLines[lineIdx];
            int lineStartOffset = _lineStartOffsets[lineIdx];

            Move(0, row);

            for (int col = 0; col < bounds.Width; col++)
            {
                // Is this the cursor position?
                bool isCursor = HasFocus && row == cursorRow && col == _cursorCol;

                if (col < line.Length)
                {
                    int charOffset = lineStartOffset + col;
                    var attr = isCursor ? _cursorAttr : GetAttributeForOffset(charOffset);
                    Driver.SetAttribute(attr);
                    Driver.AddRune(line[col]);
                }
                else
                {
                    Driver.SetAttribute(isCursor ? _cursorAttr : _normalAttr);
                    Driver.AddRune(isCursor ? '_' : ' ');
                }
            }
        }
    }

    public override void PositionCursor()
    {
        int cursorRow = _cursorLine - _topLine;
        if (cursorRow >= 0 && cursorRow < Bounds.Height)
        {
            Move(Math.Min(_cursorCol, Bounds.Width - 1), cursorRow);
        }
    }

    public override bool ProcessKey(KeyEvent keyEvent)
    {
        // Strip modifier flags to get the base key for matching
        var baseKey = keyEvent.Key & ~Key.ShiftMask & ~Key.CtrlMask & ~Key.AltMask;

        // M toggles mark mode: drop anchor at cursor, then arrows extend selection
        if (keyEvent.Key == (Key)((int)'m') || keyEvent.Key == (Key)((int)'M'))
        {
            if (!_marking)
            {
                _marking = true;
                _selStart = GetCharOffset(_cursorLine, _cursorCol);
                _selEnd = _selStart;
                SetNeedsDisplay();
            }
            else
            {
                // Already marking — pressing M again cancels
                CancelMark();
            }
            return true;
        }

        // Escape cancels mark mode
        if (keyEvent.Key == Key.Esc)
        {
            if (_marking)
            {
                CancelMark();
                return true;
            }
        }

        // S confirms selection (only when marking with a non-empty range)
        if (keyEvent.Key == (Key)((int)'s') || keyEvent.Key == (Key)((int)'S'))
        {
            if (_marking && _selStart >= 0 && _selEnd >= 0 && _selStart != _selEnd)
            {
                int start = Math.Min(_selStart, _selEnd);
                int end = Math.Max(_selStart, _selEnd);
                _marking = false;
                TextSelected?.Invoke(start, end);
            }
            return true;
        }

        // Navigation keys — when marking, extend selection; otherwise just move
        if (baseKey == Key.CursorUp)
        {
            MoveCursor(_cursorLine - 1, _cursorCol, _marking);
            return true;
        }
        if (baseKey == Key.CursorDown)
        {
            MoveCursor(_cursorLine + 1, _cursorCol, _marking);
            return true;
        }
        if (baseKey == Key.CursorLeft)
        {
            if (_cursorCol > 0)
                MoveCursor(_cursorLine, _cursorCol - 1, _marking);
            else if (_cursorLine > 0)
                MoveCursor(_cursorLine - 1, GetLineLength(_cursorLine - 1), _marking);
            return true;
        }
        if (baseKey == Key.CursorRight)
        {
            if (_cursorCol < GetLineLength(_cursorLine))
                MoveCursor(_cursorLine, _cursorCol + 1, _marking);
            else if (_cursorLine < _wrappedLines.Count - 1)
                MoveCursor(_cursorLine + 1, 0, _marking);
            return true;
        }
        if (baseKey == Key.PageUp)
        {
            MoveCursor(_cursorLine - Bounds.Height, _cursorCol, _marking);
            return true;
        }
        if (baseKey == Key.PageDown)
        {
            MoveCursor(_cursorLine + Bounds.Height, _cursorCol, _marking);
            return true;
        }
        if (baseKey == Key.Home)
        {
            MoveCursor(0, 0, _marking);
            return true;
        }
        if (baseKey == Key.End)
        {
            MoveCursor(_wrappedLines.Count - 1, GetLineLength(_wrappedLines.Count - 1), _marking);
            return true;
        }

        return base.ProcessKey(keyEvent);
    }

    private void CancelMark()
    {
        _marking = false;
        _selStart = -1;
        _selEnd = -1;
        SetNeedsDisplay();
    }

    public override bool MouseEvent(MouseEvent me)
    {
        if (me.Flags.HasFlag(MouseFlags.WheeledUp))
        {
            _topLine = Math.Max(0, _topLine - 3);
            SetNeedsDisplay();
            return true;
        }
        if (me.Flags.HasFlag(MouseFlags.WheeledDown))
        {
            int maxTop = Math.Max(0, _wrappedLines.Count - Bounds.Height);
            _topLine = Math.Min(maxTop, _topLine + 3);
            SetNeedsDisplay();
            return true;
        }
        return base.MouseEvent(me);
    }

    private void MoveCursor(int newLine, int newCol, bool extendSelection)
    {
        if (_wrappedLines.Count == 0) return;

        newLine = Math.Clamp(newLine, 0, _wrappedLines.Count - 1);
        newCol = Math.Clamp(newCol, 0, GetLineLength(newLine));

        if (extendSelection)
        {
            if (_selStart < 0)
            {
                // Start new selection from current position
                _selStart = GetCharOffset(_cursorLine, _cursorCol);
            }
            _selEnd = GetCharOffset(newLine, newCol);
        }
        else
        {
            _selStart = -1;
            _selEnd = -1;
        }

        _cursorLine = newLine;
        _cursorCol = newCol;

        // Scroll to keep cursor visible
        EnsureCursorVisible();

        // Check if cursor is on a highlight
        int cursorOffset = GetCharOffset(_cursorLine, _cursorCol);
        var hit = _highlights.FirstOrDefault(h => cursorOffset >= h.Start && cursorOffset < h.End);
        if (hit != null)
        {
            HighlightEntered?.Invoke(hit.Id);
        }

        SetNeedsDisplay();
    }

    private void EnsureCursorVisible()
    {
        int visibleLines = Math.Max(1, Bounds.Height);
        if (_cursorLine < _topLine)
            _topLine = _cursorLine;
        else if (_cursorLine >= _topLine + visibleLines)
            _topLine = _cursorLine - visibleLines + 1;
    }

    private int GetCharOffset(int line, int col)
    {
        if (line < 0 || line >= _lineStartOffsets.Count) return 0;
        return _lineStartOffsets[line] + Math.Min(col, GetLineLength(line));
    }

    private int GetLineLength(int line)
    {
        if (line < 0 || line >= _wrappedLines.Count) return 0;
        return _wrappedLines[line].Length;
    }

    private Attribute GetAttributeForOffset(int charOffset)
    {
        // Selection takes priority
        if (_selStart >= 0 && _selEnd >= 0)
        {
            int selMin = Math.Min(_selStart, _selEnd);
            int selMax = Math.Max(_selStart, _selEnd);
            if (charOffset >= selMin && charOffset < selMax)
                return _selectionAttr;
        }

        // Then highlights
        foreach (var h in _highlights)
        {
            if (charOffset >= h.Start && charOffset < h.End)
                return h.Attr;
        }

        return _normalAttr;
    }

    private void RewrapIfNeeded()
    {
        int width = Math.Max(1, Bounds.Width);
        if (width == _lastKnownWidth && _wrappedLines.Count > 0)
            return;

        _lastKnownWidth = width;
        _wrappedLines = new List<string>();
        _lineStartOffsets = new List<int>();

        if (string.IsNullOrEmpty(_text))
            return;

        // Split on actual newlines, then wrap each line
        int offset = 0;
        var lines = _text.Split('\n');

        for (int li = 0; li < lines.Length; li++)
        {
            var line = lines[li];

            if (line.Length == 0)
            {
                _wrappedLines.Add("");
                _lineStartOffsets.Add(offset);
            }
            else
            {
                int pos = 0;
                while (pos < line.Length)
                {
                    int take = Math.Min(width, line.Length - pos);

                    // Try to break at a word boundary if not at end
                    if (pos + take < line.Length && take > 1)
                    {
                        int lastSpace = line.LastIndexOf(' ', pos + take - 1, Math.Min(take, take));
                        if (lastSpace > pos)
                            take = lastSpace - pos + 1;
                    }

                    _wrappedLines.Add(line.Substring(pos, take));
                    _lineStartOffsets.Add(offset + pos);
                    pos += take;
                }
            }

            // +1 for the '\n' character
            offset += line.Length + 1;
        }
    }
}

/// <summary>
/// A highlighted region in the transcript text.
/// </summary>
public sealed class HighlightRegion
{
    public int Id { get; init; }
    public int Start { get; init; }
    public int End { get; init; }
    public Attribute Attr { get; init; }
}
