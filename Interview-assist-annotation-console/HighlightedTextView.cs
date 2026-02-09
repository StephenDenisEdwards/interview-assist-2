using Terminal.Gui;

namespace InterviewAssist.AnnotationConsole;

public sealed class HighlightedTextView : View
{
    private string _text = "";
    private List<WrappedLine> _wrappedLines = new();
    private List<HighlightRegion> _highlights = new();
    private int _currentHighlightIndex = -1;
    private int _scrollOffset;
    private Color _backgroundColor = Color.Black;

    // Status-to-colour mapping
    private Func<int, ReviewDecision>? _getDecision;

    public new string Text
    {
        get => _text;
        set
        {
            _text = value ?? "";
            Rewrap();
            SetNeedsDisplay();
        }
    }

    public void SetHighlights(IReadOnlyList<HighlightRegion> highlights)
    {
        _highlights = highlights.ToList();
        SetNeedsDisplay();
    }

    public void SetCurrentHighlight(int index)
    {
        _currentHighlightIndex = index;
        SetNeedsDisplay();
    }

    public void SetDecisionProvider(Func<int, ReviewDecision> getDecision)
    {
        _getDecision = getDecision;
    }

    public void SetBackgroundColor(Color bg)
    {
        _backgroundColor = bg;
    }

    public void ScrollToHighlight(int highlightIndex)
    {
        if (highlightIndex < 0 || highlightIndex >= _highlights.Count)
            return;

        var region = _highlights[highlightIndex];
        // Find which wrapped line contains the start of this highlight
        int targetLine = FindWrappedLineForCharIndex(region.StartIndex);
        if (targetLine < 0) return;

        // Center the highlight vertically
        var visibleLines = Bounds.Height;
        _scrollOffset = Math.Max(0, targetLine - visibleLines / 2);
        _scrollOffset = Math.Min(_scrollOffset, Math.Max(0, _wrappedLines.Count - visibleLines));
        SetNeedsDisplay();
    }

    public override void LayoutSubviews()
    {
        base.LayoutSubviews();
        Rewrap();
    }

    public override bool ProcessKey(KeyEvent keyEvent)
    {
        var visibleLines = Bounds.Height;
        var maxScroll = Math.Max(0, _wrappedLines.Count - visibleLines);

        switch (keyEvent.Key)
        {
            case Key.CursorUp:
                if (_scrollOffset > 0)
                {
                    _scrollOffset--;
                    SetNeedsDisplay();
                }
                return true;

            case Key.CursorDown:
                if (_scrollOffset < maxScroll)
                {
                    _scrollOffset++;
                    SetNeedsDisplay();
                }
                return true;

            case Key.PageUp:
                _scrollOffset = Math.Max(0, _scrollOffset - visibleLines);
                SetNeedsDisplay();
                return true;

            case Key.PageDown:
                _scrollOffset = Math.Min(maxScroll, _scrollOffset + visibleLines);
                SetNeedsDisplay();
                return true;

            case Key.Home:
                _scrollOffset = 0;
                SetNeedsDisplay();
                return true;

            case Key.End:
                _scrollOffset = maxScroll;
                SetNeedsDisplay();
                return true;
        }

        return base.ProcessKey(keyEvent);
    }

    public override void Redraw(Rect bounds)
    {
        var driver = Application.Driver;

        var normalAttr = Terminal.Gui.Attribute.Make(Color.White, _backgroundColor);
        driver.SetAttribute(normalAttr);

        // Clear the view
        for (int row = 0; row < bounds.Height; row++)
        {
            Move(0, row);
            for (int col = 0; col < bounds.Width; col++)
                driver.AddRune(' ');
        }

        if (_wrappedLines.Count == 0)
            return;

        for (int row = 0; row < bounds.Height; row++)
        {
            var lineIndex = _scrollOffset + row;
            if (lineIndex >= _wrappedLines.Count)
                break;

            var line = _wrappedLines[lineIndex];
            Move(0, row);

            for (int col = 0; col < line.Text.Length && col < bounds.Width; col++)
            {
                var charIndex = line.OriginalStartIndex + col;
                var attr = GetAttributeForCharIndex(charIndex);
                driver.SetAttribute(attr);
                driver.AddRune(line.Text[col]);
            }
        }
    }

    private Terminal.Gui.Attribute GetAttributeForCharIndex(int charIndex)
    {
        for (int i = 0; i < _highlights.Count; i++)
        {
            var h = _highlights[i];
            if (charIndex >= h.StartIndex && charIndex < h.StartIndex + h.Length)
            {
                // This character is inside a highlight
                if (i == _currentHighlightIndex)
                {
                    // Current highlight: inverted black on bright yellow
                    return Terminal.Gui.Attribute.Make(Color.Black, Color.BrightYellow);
                }

                var decision = _getDecision?.Invoke(h.ReviewItemIndex) ?? ReviewDecision.Pending;
                return decision switch
                {
                    ReviewDecision.Accept => Terminal.Gui.Attribute.Make(Color.Green, _backgroundColor),
                    ReviewDecision.Reject => Terminal.Gui.Attribute.Make(Color.Red, _backgroundColor),
                    ReviewDecision.Modify => Terminal.Gui.Attribute.Make(Color.Cyan, _backgroundColor),
                    _ => Terminal.Gui.Attribute.Make(Color.BrightYellow, _backgroundColor)
                };
            }
        }

        return Terminal.Gui.Attribute.Make(Color.White, _backgroundColor);
    }

    private int FindWrappedLineForCharIndex(int charIndex)
    {
        for (int i = 0; i < _wrappedLines.Count; i++)
        {
            var line = _wrappedLines[i];
            if (charIndex >= line.OriginalStartIndex &&
                charIndex < line.OriginalStartIndex + line.Text.Length)
            {
                return i;
            }
        }
        return -1;
    }

    private void Rewrap()
    {
        _wrappedLines.Clear();
        if (string.IsNullOrEmpty(_text))
            return;

        var width = Math.Max(1, Bounds.Width);
        int pos = 0;

        while (pos < _text.Length)
        {
            // Find end of line (newline or width limit)
            var newlinePos = _text.IndexOf('\n', pos);
            var lineEnd = newlinePos >= 0 ? newlinePos : _text.Length;
            var remaining = lineEnd - pos;

            if (remaining <= width)
            {
                // Whole line fits
                _wrappedLines.Add(new WrappedLine(_text.Substring(pos, remaining), pos));
                pos = lineEnd + (newlinePos >= 0 ? 1 : 0);
            }
            else
            {
                // Need to wrap: find last space within width
                var wrapAt = _text.LastIndexOf(' ', pos + width - 1, width);
                if (wrapAt <= pos)
                {
                    // No space found, hard break at width
                    wrapAt = pos + width;
                }
                else
                {
                    wrapAt++; // Include the space in this line
                }

                var lineLength = wrapAt - pos;
                _wrappedLines.Add(new WrappedLine(_text.Substring(pos, lineLength), pos));
                pos = wrapAt;
            }
        }
    }

    /// <summary>
    /// Find the highlight index that corresponds to a given review item index.
    /// Returns -1 if no highlight exists for that review item.
    /// </summary>
    public int FindHighlightForReviewItem(int reviewItemIndex)
    {
        for (int i = 0; i < _highlights.Count; i++)
        {
            if (_highlights[i].ReviewItemIndex == reviewItemIndex)
                return i;
        }
        return -1;
    }
}

internal sealed record WrappedLine(string Text, int OriginalStartIndex);
