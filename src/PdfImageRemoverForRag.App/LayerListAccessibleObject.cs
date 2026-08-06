namespace PdfImageRemoverForRag.App;

/// <summary>
/// Publishes the painted rows of <see cref="LayerListView"/> to assistive
/// technology.
///
/// The rows are pixels, not controls, so without this a screen reader sees one
/// empty pane where the Flatten panel is — the same hole
/// <see cref="TileViewAccessibleObject"/> fills for the tile view, and filled
/// the same way.
///
/// This is MSAA, which NVDA and JAWS read. Narrator reads UI Automation only,
/// and the UIA fragment API is internal to WinForms in .NET 8, so it cannot be
/// implemented from here; the object list stays the fully accessible route.
/// </summary>
internal sealed class LayerListAccessibleObject : Control.ControlAccessibleObject
{
    readonly LayerListView _owner;

    public LayerListAccessibleObject(LayerListView owner) : base(owner) => _owner = owner;

    public override AccessibleRole Role => AccessibleRole.List;

    public override int GetChildCount() => _owner.RowCount;

    public override AccessibleObject? GetChild(int index) =>
        index >= 0 && index < _owner.RowCount ? new LayerRowAccessibleObject(_owner, index) : null;

    public override AccessibleObject? GetFocused() =>
        _owner.FocusedRow >= 0 ? GetChild(_owner.FocusedRow) : null;

    public override AccessibleObject? HitTest(int x, int y)
    {
        int row = _owner.RowIndexAt(_owner.PointToClient(new Point(x, y)));
        return row >= 0 ? GetChild(row) : this;
    }
}

/// <summary>
/// One row as seen by a screen reader: a check button whose name says what the
/// row is and whose state carries shown, part-shown, selected, and — for a
/// folder — open or closed. Toggling it hides or shows the layer, exactly as
/// clicking its eye or pressing Space does.
/// </summary>
internal sealed class LayerRowAccessibleObject : AccessibleObject
{
    readonly LayerListView _owner;
    readonly int _row;

    public LayerRowAccessibleObject(LayerListView owner, int row)
    {
        _owner = owner;
        _row = row;
    }

    public override AccessibleObject Parent => _owner.AccessibilityObject;

    // CheckButton so a reader announces the shown/hidden state and the "press
    // Space to toggle" affordance in its own words and language. A folder is
    // also a container, but the eye is what the row DOES; its open/closed state
    // is reported separately below.
    public override AccessibleRole Role => AccessibleRole.CheckButton;

    public override string? Name
    {
        get => _owner.RowAccessibleName(_row);
        set { /* fixed name; ignore assignment */ }
    }

    public override Rectangle Bounds => _owner.RowScreenBounds(_row);

    public override AccessibleStates State
    {
        get
        {
            var states = AccessibleStates.Focusable | AccessibleStates.Selectable;

            if (_owner.Focused && _owner.FocusedRow == _row) states |= AccessibleStates.Focused;
            // Selection is its own thing here: the commands act on the selected
            // rows, and several can be selected at once.
            if (_owner.IsRowSelected(_row)) states |= AccessibleStates.Selected;

            var visual = _owner.RowVisual(_row);
            states |= visual.Visibility switch
            {
                LayerVisibility.Visible => AccessibleStates.Checked,
                // A folder holding some of each is neither shown nor hidden, and
                // reporting it as either would be a lie the sighted user is not
                // told — its eye shows the difference too.
                LayerVisibility.Mixed => AccessibleStates.Mixed,
                _ => AccessibleStates.None,
            };

            if (visual.IsGroup)
            {
                states |= visual.IsExpanded
                    ? AccessibleStates.Expanded
                    : AccessibleStates.Collapsed;
            }

            return states;
        }
    }

    public override void DoDefaultAction() => _owner.ToggleVisibility(_row);

    public override void Select(AccessibleSelection flags)
    {
        if ((flags & AccessibleSelection.TakeFocus) != 0)
        {
            _owner.Focus();
            _owner.SelectOnly(_row);
        }
    }
}
