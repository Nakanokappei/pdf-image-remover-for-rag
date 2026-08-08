using PdfImageRemoverForRag.Core.Models;

namespace PdfImageRemoverForRag.Core.Selection;

/// <summary>
/// Validates the user's <see cref="ObjectRemovalSelection"/> set against the
/// currently-open document. Two hard rules from the spec:
///
/// <list type="bullet">
///   <item>Every selection must map to a known group in the document.</item>
///   <item>The group must have <c>IsSafelyRemovable = true</c> (§14.3).</item>
/// </list>
///
/// The App calls <see cref="Validate"/> before enabling the "Remove Selected
/// &amp; Save" button and again just before invoking the cleaner, so any race
/// between "user checked the row" and "user hit save" is caught.
/// </summary>
public sealed class RemovalSelectionValidator
{
    readonly IReadOnlyDictionary<string, ObjectGroup> _groupsById;

    public RemovalSelectionValidator(IEnumerable<ObjectGroup> groups)
    {
        // Build the lookup once — the group set is fixed for the lifetime of
        // a document analysis session.
        _groupsById = groups.ToDictionary(g => g.GroupId, StringComparer.Ordinal);
    }

    public ValidationOutcome Validate(IEnumerable<ObjectRemovalSelection> selections)
    {
        var errors = new List<string>();
        var accepted = new List<ObjectRemovalSelection>();

        foreach (var selection in selections)
        {
            if (!_groupsById.TryGetValue(selection.GroupId, out var group))
            {
                errors.Add($"unknown group id: {selection.GroupId}");
                continue;
            }
            if (!group.IsSafelyRemovable)
            {
                // Matches spec §14.3 wording so the UI can surface it verbatim.
                errors.Add(
                    $"{selection.GroupId}: this image cannot be removed safely because of the PDF's complex structure.");
                continue;
            }
            accepted.Add(selection);
        }

        return new ValidationOutcome(
            IsValid: errors.Count == 0,
            Accepted: accepted,
            Errors: errors);
    }
}

/// <summary>
/// Result of validating a selection set. When <see cref="IsValid"/> is false
/// the caller should surface <see cref="Errors"/> and not proceed with
/// cleaning; <see cref="Accepted"/> is filled in either way so a "remove the
/// safe ones, skip the unsafe ones" mode could be added later without
/// changing this contract.
/// </summary>
public sealed record ValidationOutcome(
    bool IsValid,
    IReadOnlyList<ObjectRemovalSelection> Accepted,
    IReadOnlyList<string> Errors);
