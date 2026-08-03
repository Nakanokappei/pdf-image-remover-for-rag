namespace PdfImageRemoverForRag.App;

/// <summary>
/// A row of the object list that tells a screen reader the number the user can
/// see.
///
/// WinForms names a row from its zero-based index, so the first row — printed
/// "1" in its header — was announced as row 0, and every row after it was one
/// behind. The list is numbered like a spreadsheet on purpose, and a spoken
/// number that disagrees with the printed one is worse than no number.
///
/// The rows are created by <c>Rows.Add(values)</c>, which clones
/// <see cref="DataGridView.RowTemplate"/>; cloning a derived row goes through
/// its parameterless constructor, which is why this type has nothing else in it.
/// </summary>
internal sealed class ObjectListRow : DataGridViewRow
{
    protected override AccessibleObject CreateAccessibilityInstance() =>
        new ObjectListRowAccessibleObject(this);

    /// <summary>
    /// The row as UI Automation sees it. Only the name changes — everything
    /// else a grid row reports (its cells, its state, its bounds) is what the
    /// base class already gets right.
    /// </summary>
    sealed class ObjectListRowAccessibleObject : DataGridViewRowAccessibleObject
    {
        public ObjectListRowAccessibleObject(DataGridViewRow owner) : base(owner)
        {
        }

        public override string Name
        {
            get
            {
                // Index is -1 while the row is the template rather than a row in
                // the grid; there is no number to announce then.
                var index = Owner?.Index ?? -1;
                return index < 0 ? base.Name : L10n.RowNumber(index + 1);
            }
        }
    }
}
