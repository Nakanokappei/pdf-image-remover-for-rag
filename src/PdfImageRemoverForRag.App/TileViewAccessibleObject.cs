namespace PdfImageRemoverForRag.App;

/// <summary>
/// Publishes the painted tiles of <see cref="TileView"/> to UI Automation.
///
/// The tiles are pixels, not controls, so without this a screen reader sees one
/// empty pane. This exposes the view as a list with one child per tile — the
/// same "give UIA a custom object" technique proven on the ☑ column header
/// (<see cref="DeleteColumnHeaderCell"/>), now at list scale.
/// </summary>
internal sealed class TileViewAccessibleObject : Control.ControlAccessibleObject
{
    readonly TileView _owner;

    public TileViewAccessibleObject(TileView owner) : base(owner) => _owner = owner;

    public override AccessibleRole Role => AccessibleRole.List;

    public override int GetChildCount() => _owner.TileCount;

    public override AccessibleObject? GetChild(int index) =>
        index >= 0 && index < _owner.TileCount ? new TileAccessibleObject(_owner, index) : null;

    public override AccessibleObject? GetFocused() =>
        _owner.FocusedTileIndex >= 0 ? GetChild(_owner.FocusedTileIndex) : null;

    public override AccessibleObject? HitTest(int x, int y)
    {
        int index = _owner.TileIndexAt(_owner.PointToClient(new Point(x, y)));
        return index >= 0 ? GetChild(index) : this;
    }
}

/// <summary>
/// One tile as seen by a screen reader: a check button whose name describes the
/// object and whose state carries checked / not-removable. Toggling it marks the
/// object for removal, exactly as a click or the Space key does.
/// </summary>
internal sealed class TileAccessibleObject : AccessibleObject
{
    readonly TileView _owner;
    readonly int _index;

    public TileAccessibleObject(TileView owner, int index)
    {
        _owner = owner;
        _index = index;
    }

    public override AccessibleObject Parent => _owner.AccessibilityObject;

    // CheckButton so a screen reader announces the checked/unchecked state and
    // the "press Space to toggle" affordance in its own words and language.
    public override AccessibleRole Role => AccessibleRole.CheckButton;

    public override string? Name
    {
        get => _owner.TileAccessibleName(_index);
        set { /* fixed name; ignore assignment */ }
    }

    public override Rectangle Bounds => _owner.TileScreenBounds(_index);

    public override AccessibleStates State
    {
        get
        {
            var states = AccessibleStates.Focusable | AccessibleStates.Selectable;

            if (_owner.Focused && _owner.FocusedTileIndex == _index)
            {
                states |= AccessibleStates.Focused | AccessibleStates.Selected;
            }

            // A tile that cannot be safely removed is inert — reported as
            // unavailable rather than as an unchecked checkbox, so a screen
            // reader does not invite the user to toggle something that will not.
            if (!_owner.TileIsRemovable(_index))
            {
                states |= AccessibleStates.Unavailable;
            }
            else if (_owner.TileIsChecked(_index))
            {
                states |= AccessibleStates.Checked;
            }

            return states;
        }
    }

    public override void DoDefaultAction() => _owner.ToggleTile(_index);

    public override void Select(AccessibleSelection flags)
    {
        if ((flags & AccessibleSelection.TakeFocus) != 0)
        {
            _owner.Focus();
            _owner.SetFocusedTile(_index);
        }
    }
}
