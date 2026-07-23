using PdfImageRemoverForRag.Core.Models;
using PdfImageRemoverForRag.Core.Selection;
using Xunit;

namespace PdfImageRemoverForRag.Core.Tests;

// Spec §24 "削除対象選択モデル" and "削除不能画像の選択禁止判定" — the two
// gates the App must enforce before invoking IPdfDocumentCleaner.
public class RemovalSelectionValidatorTests
{
    static PdfImageGroup MakeGroup(string id, bool isSafelyRemovable, string? warningMessage = null)
    {
        return new PdfImageGroup(
            GroupId: id,
            Hash: "HASH_" + id,
            PixelWidth: 100, PixelHeight: 100,
            ColorSpace: "/DeviceRGB", BitsPerComponent: 8,
            Compression: "/FlateDecode", EstimatedSize: 1024,
            IsImageMask: false,
            IsPossibleFullPageImage: false,
            IsSafelyRemovable: isSafelyRemovable,
            WarningMessage: warningMessage,
            ThumbnailBytes: null,
            Occurrences: new[]
            {
                new PdfImageOccurrence(1, "1 0 R", "/Im1", 0, 0, 100, 100),
            });
    }

    [Fact]
    public void SelectingRemovableGroups_ProducesValidOutcome()
    {
        var groups = new[]
        {
            MakeGroup("IMG_001", isSafelyRemovable: true),
            MakeGroup("IMG_002", isSafelyRemovable: true),
        };
        var validator = new RemovalSelectionValidator(groups);
        var outcome = validator.Validate(new[]
        {
            new ImageRemovalSelection("IMG_001", groups[0].Occurrences),
        });
        Assert.True(outcome.IsValid);
        Assert.Empty(outcome.Errors);
        Assert.Single(outcome.Accepted);
    }

    [Fact]
    public void SelectingUnsafeGroup_MarksAsInvalidAndSurfacesSpecMessage()
    {
        // The spec wording must show through unchanged so the UI can display it verbatim.
        var groups = new[]
        {
            MakeGroup("IMG_001", isSafelyRemovable: false,
                warningMessage: "複雑なPDF構造のため、この画像は安全に削除できません。"),
        };
        var validator = new RemovalSelectionValidator(groups);
        var outcome = validator.Validate(new[]
        {
            new ImageRemovalSelection("IMG_001", groups[0].Occurrences),
        });
        Assert.False(outcome.IsValid);
        Assert.Empty(outcome.Accepted);
        var error = Assert.Single(outcome.Errors);
        Assert.Contains("削除できません", error);
    }

    [Fact]
    public void SelectingUnknownGroupId_IsRejected()
    {
        var validator = new RemovalSelectionValidator(new[]
        {
            MakeGroup("IMG_001", isSafelyRemovable: true),
        });
        var outcome = validator.Validate(new[]
        {
            new ImageRemovalSelection("IMG_099", Array.Empty<PdfImageOccurrence>()),
        });
        Assert.False(outcome.IsValid);
        var error = Assert.Single(outcome.Errors);
        Assert.Contains("IMG_099", error);
    }

    [Fact]
    public void MixedSelections_ReportEveryProblemAndAcceptOnlyTheValidRows()
    {
        var groups = new[]
        {
            MakeGroup("IMG_OK", isSafelyRemovable: true),
            MakeGroup("IMG_NG", isSafelyRemovable: false, warningMessage: "unsafe"),
        };
        var validator = new RemovalSelectionValidator(groups);
        var outcome = validator.Validate(new[]
        {
            new ImageRemovalSelection("IMG_OK", groups[0].Occurrences),
            new ImageRemovalSelection("IMG_NG", groups[1].Occurrences),
            new ImageRemovalSelection("IMG_MISSING", Array.Empty<PdfImageOccurrence>()),
        });
        Assert.False(outcome.IsValid);
        Assert.Equal(2, outcome.Errors.Count);
        var accepted = Assert.Single(outcome.Accepted);
        Assert.Equal("IMG_OK", accepted.GroupId);
    }
}
