using Terminal.Gui;
using Attribute = Terminal.Gui.Attribute;

namespace InterviewAssist.TranscriptionDetectionConsole;

/// <summary>
/// Custom view that renders text with word wrapping, inline highlight regions,
/// and scrolling. Terminal.Gui 1.x's TextView does not support inline colour
/// changes, so this renders character-by-character.
/// </summary>
public sealed class HighlightTextView : View
{
    private string _text = "";
    private readonly List<HighlightRegion> _highlights = new();
    private List<string> _wrappedLines = new();
    private List<int> _lineStartOffsets = new();
    private int _topLine;
    private int _lastKnownWidth;

    /// <summary>Attribute used for non-highlighted text.</summary>
    public Attribute NormalAttribute { get; set; }

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

    /// <summary>
    /// Appends text, re-wraps, and auto-scrolls to the bottom.
    /// </summary>
    public void AppendText(string text)
    {
        _text += text;
        _lastKnownWidth = 0; // force re-wrap
        RewrapIfNeeded();
        ScrollToEnd();
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

    /// <summary>
    /// Scrolls so the last page of text is visible.
    /// </summary>
    public void ScrollToEnd()
    {
        int visibleLines = Math.Max(1, Bounds.Height);
        _topLine = Math.Max(0, _wrappedLines.Count - visibleLines);
    }

    public override void Redraw(Rect bounds)
    {
        RewrapIfNeeded();

        var normalAttr = NormalAttribute;
        Driver.SetAttribute(normalAttr);
        Clear();

        int visibleLines = bounds.Height;

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
                if (col < line.Length)
                {
                    int charOffset = lineStartOffset + col;
                    var attr = GetAttributeForOffset(charOffset, normalAttr);
                    Driver.SetAttribute(attr);
                    Driver.AddRune(line[col]);
                }
                else
                {
                    Driver.SetAttribute(normalAttr);
                    Driver.AddRune(' ');
                }
            }
        }
    }

    public override bool ProcessKey(KeyEvent keyEvent)
    {
        var baseKey = keyEvent.Key & ~Key.ShiftMask & ~Key.CtrlMask & ~Key.AltMask;

        if (baseKey == Key.CursorUp)
        {
            _topLine = Math.Max(0, _topLine - 1);
            SetNeedsDisplay();
            return true;
        }
        if (baseKey == Key.CursorDown)
        {
            int maxTop = Math.Max(0, _wrappedLines.Count - Bounds.Height);
            _topLine = Math.Min(maxTop, _topLine + 1);
            SetNeedsDisplay();
            return true;
        }
        if (baseKey == Key.PageUp)
        {
            _topLine = Math.Max(0, _topLine - Bounds.Height);
            SetNeedsDisplay();
            return true;
        }
        if (baseKey == Key.PageDown)
        {
            int maxTop = Math.Max(0, _wrappedLines.Count - Bounds.Height);
            _topLine = Math.Min(maxTop, _topLine + Bounds.Height);
            SetNeedsDisplay();
            return true;
        }
        if (baseKey == Key.Home)
        {
            _topLine = 0;
            SetNeedsDisplay();
            return true;
        }
        if (baseKey == Key.End)
        {
            ScrollToEnd();
            SetNeedsDisplay();
            return true;
        }

        return base.ProcessKey(keyEvent);
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

    private Attribute GetAttributeForOffset(int charOffset, Attribute normalAttr)
    {
        foreach (var h in _highlights)
        {
            if (charOffset >= h.Start && charOffset < h.End)
                return h.Attr;
        }

        return normalAttr;
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
