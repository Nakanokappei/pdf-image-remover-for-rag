using System.Diagnostics.CodeAnalysis;
using PdfImageRemoverForRag.Core.Models;

namespace PdfImageRemoverForRag.App;

/// <summary>
/// The サムネイル column's cell, whose only job is to give the painted
/// thumbnail a spoken name.
///
/// The cell deliberately holds no value — <c>PaintThumbnailCell</c> asks the
/// bitmap cache at paint time — so a screen reader would announce an empty
/// cell (accessibility review #5). As with <see cref="DeleteColumnHeaderCell"/>,
/// the route to what UIA reports is a custom cell overriding its accessibility
/// object's Name. Painting, tooltips and sizing are untouched.
/// </summary>
internal sealed class ThumbnailCell : DataGridViewImageCell
{
    protected override AccessibleObject CreateAccessibilityInstance()
        => new ThumbnailCellAccessibleObject(this);

    sealed class ThumbnailCellAccessibleObject : DataGridViewImageCellAccessibleObject
    {
        public ThumbnailCellAccessibleObject(DataGridViewCell owner) : base(owner) { }

        // Say what the cell shows: the string itself for text rows (it is
        // drawn as text), the localized type + object id for images and
        // shapes. Composed from already-localized pieces, so no new
        // translated string is needed. The base getter is non-null and the
        // setter accepts null ([AllowNull]), so the override matches.
        [AllowNull]
        public override string Name
        {
            get
            {
                if (Owner?.OwningRow?.Tag is not CrossFileImageGroup group)
                {
                    return base.Name ?? string.Empty;
                }
                return group.Kind == RemovableKind.Text
                    ? $"{ImageListRow.TypeLabel(group)}: {group.TextValue}"
                    : $"{ImageListRow.TypeLabel(group)} {group.GroupId}";
            }
            set { /* fixed name; ignore assignment */ }
        }
    }
}
