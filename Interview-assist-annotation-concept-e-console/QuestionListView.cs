using Terminal.Gui;
using Attribute = Terminal.Gui.Attribute;

namespace InterviewAssist.AnnotationConceptEConsole;

/// <summary>
/// Custom view that displays a list of question items with word wrapping.
/// Each item can span multiple display rows. The selected item is highlighted.
/// Supports Up/Down navigation between logical items (not rows).
/// </summary>
public sealed class QuestionListView : View
{
    private List<string> _items = new();
    private HashSet<int> _flaggedItems = new(); // indices of items needing attention
    private List<List<string>> _wrappedBlocks = new();
    // For each display row: which item index it belongs to, or -1 for separator lines
    private List<int> _rowToItem = new();
    private int _selectedIndex;
    private int _topLine;
    private int _lastKnownWidth;

    private Attribute _normalAttr;
    private Attribute _selectedAttr;
    private Attribute _flaggedAttr;

    /// <summary>Fired when the selected item changes. Arg: new selected index.</summary>
    public event Action<int>? SelectedItemChanged;

    /// <summary>Fired when D or T is pressed. Arg: the key.</summary>
    public event Action<Key>? ItemKeyPressed;

    public QuestionListView()
    {
        CanFocus = true;
    }

    public int SelectedItem
    {
        get => _selectedIndex;
        set
        {
            if (value < 0 || value >= _items.Count) return;
            _selectedIndex = value;
            EnsureSelectedVisible();
            SetNeedsDisplay();
        }
    }

    public int ItemCount => _items.Count;

    public void SetItems(List<string> items, HashSet<int>? flaggedIndices = null)
    {
        _items = items;
        _flaggedItems = flaggedIndices ?? new HashSet<int>();
        _lastKnownWidth = 0; // force re-wrap
        if (_selectedIndex >= _items.Count)
            _selectedIndex = Math.Max(0, _items.Count - 1);
        RewrapIfNeeded();
        SetNeedsDisplay();
    }

    public override void Redraw(Rect bounds)
    {
        RewrapIfNeeded();

        _normalAttr = new Attribute(Color.White, Color.Black);
        _selectedAttr = new Attribute(Color.Black, Color.White);
        _flaggedAttr = new Attribute(Color.BrightRed, Color.Black);

        Driver.SetAttribute(_normalAttr);
        Clear();

        int visibleRows = bounds.Height;
        for (int row = 0; row < visibleRows; row++)
        {
            int displayRow = _topLine + row;
            if (displayRow >= _rowToItem.Count)
                break;

            int itemIdx = _rowToItem[displayRow];
            bool isSelected = itemIdx >= 0 && itemIdx == _selectedIndex;
            bool isFlagged = itemIdx >= 0 && _flaggedItems.Contains(itemIdx);
            var attr = isSelected ? _selectedAttr : isFlagged ? _flaggedAttr : _normalAttr;

            Driver.SetAttribute(attr);
            Move(0, row);

            // Find the actual text line for this display row
            string lineText = GetDisplayLineText(displayRow);

            for (int col = 0; col < bounds.Width; col++)
            {
                if (col < lineText.Length)
                    Driver.AddRune(lineText[col]);
                else
                    Driver.AddRune(' ');
            }
        }

        // Position cursor on first row of selected item
        if (HasFocus && _items.Count > 0)
        {
            int selRow = GetFirstRowOfItem(_selectedIndex) - _topLine;
            if (selRow >= 0 && selRow < visibleRows)
                Move(0, selRow);
        }
    }

    public override bool ProcessKey(KeyEvent keyEvent)
    {
        var baseKey = keyEvent.Key & ~Key.ShiftMask & ~Key.CtrlMask & ~Key.AltMask;

        if (baseKey == Key.CursorUp)
        {
            if (_selectedIndex > 0)
            {
                _selectedIndex--;
                EnsureSelectedVisible();
                SetNeedsDisplay();
                SelectedItemChanged?.Invoke(_selectedIndex);
            }
            return true;
        }
        if (baseKey == Key.CursorDown)
        {
            if (_selectedIndex < _items.Count - 1)
            {
                _selectedIndex++;
                EnsureSelectedVisible();
                SetNeedsDisplay();
                SelectedItemChanged?.Invoke(_selectedIndex);
            }
            return true;
        }
        if (baseKey == Key.PageUp)
        {
            _selectedIndex = Math.Max(0, _selectedIndex - Math.Max(1, Bounds.Height / 3));
            EnsureSelectedVisible();
            SetNeedsDisplay();
            SelectedItemChanged?.Invoke(_selectedIndex);
            return true;
        }
        if (baseKey == Key.PageDown)
        {
            _selectedIndex = Math.Min(_items.Count - 1, _selectedIndex + Math.Max(1, Bounds.Height / 3));
            EnsureSelectedVisible();
            SetNeedsDisplay();
            SelectedItemChanged?.Invoke(_selectedIndex);
            return true;
        }
        if (baseKey == Key.Home)
        {
            _selectedIndex = 0;
            EnsureSelectedVisible();
            SetNeedsDisplay();
            SelectedItemChanged?.Invoke(_selectedIndex);
            return true;
        }
        if (baseKey == Key.End)
        {
            _selectedIndex = Math.Max(0, _items.Count - 1);
            EnsureSelectedVisible();
            SetNeedsDisplay();
            SelectedItemChanged?.Invoke(_selectedIndex);
            return true;
        }

        if (keyEvent.Key == (Key)((int)'d') || keyEvent.Key == (Key)((int)'D'))
        {
            ItemKeyPressed?.Invoke(keyEvent.Key);
            return true;
        }
        if (keyEvent.Key == (Key)((int)'t') || keyEvent.Key == (Key)((int)'T'))
        {
            ItemKeyPressed?.Invoke(keyEvent.Key);
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
            int maxTop = Math.Max(0, _rowToItem.Count - Bounds.Height);
            _topLine = Math.Min(maxTop, _topLine + 3);
            SetNeedsDisplay();
            return true;
        }
        if (me.Flags.HasFlag(MouseFlags.Button1Clicked))
        {
            int displayRow = _topLine + me.Y;
            if (displayRow >= 0 && displayRow < _rowToItem.Count)
            {
                int itemIdx = _rowToItem[displayRow];
                if (itemIdx >= 0 && itemIdx != _selectedIndex)
                {
                    _selectedIndex = itemIdx;
                    SetNeedsDisplay();
                    SelectedItemChanged?.Invoke(_selectedIndex);
                }
            }
            return true;
        }
        return base.MouseEvent(me);
    }

    private void EnsureSelectedVisible()
    {
        if (_items.Count == 0 || _wrappedBlocks.Count == 0) return;

        int firstRow = GetFirstRowOfItem(_selectedIndex);
        int lastRow = GetLastRowOfItem(_selectedIndex);
        int visibleRows = Math.Max(1, Bounds.Height);

        if (firstRow < _topLine)
            _topLine = firstRow;
        else if (lastRow >= _topLine + visibleRows)
            _topLine = lastRow - visibleRows + 1;
    }

    private int GetFirstRowOfItem(int itemIndex)
    {
        for (int r = 0; r < _rowToItem.Count; r++)
        {
            if (_rowToItem[r] == itemIndex)
                return r;
        }
        return 0;
    }

    private int GetLastRowOfItem(int itemIndex)
    {
        int last = 0;
        for (int r = 0; r < _rowToItem.Count; r++)
        {
            if (_rowToItem[r] == itemIndex)
                last = r;
        }
        return last;
    }

    private string GetDisplayLineText(int displayRow)
    {
        int itemIdx = _rowToItem[displayRow];
        if (itemIdx < 0)
            return ""; // separator line

        // Count which line within this item's block this display row is
        int firstRow = GetFirstRowOfItem(itemIdx);
        int lineWithinBlock = displayRow - firstRow;

        if (itemIdx < _wrappedBlocks.Count && lineWithinBlock < _wrappedBlocks[itemIdx].Count)
            return _wrappedBlocks[itemIdx][lineWithinBlock];

        return "";
    }

    private void RewrapIfNeeded()
    {
        int width = Math.Max(1, Bounds.Width);
        if (width == _lastKnownWidth && _wrappedBlocks.Count == _items.Count)
            return;

        _lastKnownWidth = width;
        _wrappedBlocks = new List<List<string>>();
        _rowToItem = new List<int>();

        for (int i = 0; i < _items.Count; i++)
        {
            // Add separator before each item except the first
            if (i > 0)
                _rowToItem.Add(-1);

            var lines = WrapText(_items[i], width);
            _wrappedBlocks.Add(lines);

            foreach (var _ in lines)
                _rowToItem.Add(i);
        }
    }

    private static List<string> WrapText(string text, int width)
    {
        var result = new List<string>();
        if (string.IsNullOrEmpty(text))
        {
            result.Add("");
            return result;
        }

        // Split on newlines first, then wrap each line
        var rawLines = text.Split('\n');
        foreach (var rawLine in rawLines)
        {
            if (rawLine.Length <= width)
            {
                result.Add(rawLine);
                continue;
            }

            int pos = 0;
            while (pos < rawLine.Length)
            {
                int take = Math.Min(width, rawLine.Length - pos);

                // Try to break at a word boundary
                if (pos + take < rawLine.Length && take > 1)
                {
                    int lastSpace = rawLine.LastIndexOf(' ', pos + take - 1, take);
                    if (lastSpace > pos)
                        take = lastSpace - pos + 1;
                }

                result.Add(rawLine.Substring(pos, take));
                pos += take;
            }
        }

        return result;
    }
}
