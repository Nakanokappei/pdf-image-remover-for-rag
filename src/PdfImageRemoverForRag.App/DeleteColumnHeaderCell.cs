using System.Diagnostics.CodeAnalysis;

namespace PdfImageRemoverForRag.App;

/// <summary>
/// The header cell for the ☑ delete column, whose only job is to give the
/// column a spoken name.
///
/// The visible header is a ☑ glyph (U+2611); a screen reader reading the cell's
/// value announces "ballot box with check", which tells a blind user nothing.
/// `DataGridViewColumn` has no `AccessibleName`, and the header cell's accessible
/// name is fixed to its value — so the only way to change what UIA reports is a
/// custom header cell that overrides its accessibility object's Name.
///
/// Everything else is untouched: the cell still holds "☑" as its value, so the
/// glyph, its symbol font, alignment and sorting all behave exactly as before.
/// This is the small, self-contained instance of the "expose a custom name to
/// UIA" technique that the tile view (#1) will use at scale — verify it reaches
/// Narrator here first.
/// </summary>
internal sealed class DeleteColumnHeaderCell : DataGridViewColumnHeaderCell
{
    protected override AccessibleObject CreateAccessibilityInstance()
        => new DeleteHeaderAccessibleObject(this);

    sealed class DeleteHeaderAccessibleObject : DataGridViewColumnHeaderCellAccessibleObject
    {
        public DeleteHeaderAccessibleObject(DataGridViewColumnHeaderCell owner) : base(owner) { }

        // Report the localized remove/delete verb instead of the ☑ glyph.
        // The base getter is non-null and the setter accepts null ([AllowNull]),
        // so the override matches that shape.
        [AllowNull]
        public override string Name
        {
            get => L10n.AccessibleDeleteColumn;
            set { /* fixed name; ignore assignment */ }
        }
    }
}
