using Microsoft.Extensions.Logging;
using PdfImageRemoverForRag.Core.Models;

namespace PdfImageRemoverForRag.App;

internal sealed partial class MainForm
{
    // =======================================================================
    // Posing for a photograph
    // =======================================================================
    //
    // The store listing wants a handful of screens in sixteen languages, and
    // nobody is taking eighty pictures by hand. So the app can be told to open a
    // document, arrange itself into a named pose, photograph itself and quit.
    //
    // Every pose is arranged by calling what a user's click would call. Nothing
    // here reaches around the UI to draw something the app cannot otherwise
    // show: a screenshot that cannot be reproduced by using the app is a
    // photograph of something that does not exist.

    /// <summary>How many objects to tick, so the list shows a selection in use.</summary>
    const int TickedForTheCamera = 6;

    /// <summary>
    /// Hold the pose and press the shutter. Runs once, after the document is
    /// open, and closes the window when the file is written — one run of the
    /// app is one picture.
    /// </summary>
    async Task TakeScreenshotAsync(ScreenshotRequest request)
    {
        // Nothing may come between the camera and the subject. What is
        // photographed is the screen, so a notification, a terminal or another
        // application taking the foreground lands in the picture — one did,
        // covering a third of it.
        TopMost = true;
        await ScreenshotCamera.SizeVisibleFrameAsync(this, request.Width, request.Height);
        Activate();

        // Thumbnails arrive after the open finishes, on their own timer, and a
        // picture of half-drawn rows is worse than a slow run.
        await Task.Delay(request.SettleMilliseconds);

        var subject = Pose(request);
        // Let the pose reach the screen before the shutter: what is
        // photographed is the screen, not the app's intentions. The same
        // patience the document got, because a pose is not only a rearrangement
        // — selecting an object renders a page of the PDF beside it, and
        // opening the usage window renders one per row.
        await Task.Delay(request.SettleMilliseconds);

        var written = ScreenshotCamera.Capture(subject, request.OutputPath);
        // Logged as an error when it is not what was asked for: eighty pictures
        // are taken unattended, and one that is the wrong size has to say so
        // rather than be found in the store listing.
        if (written.Width != request.Width || written.Height != request.Height)
        {
            _logger.LogError(
                "screenshot: asked for {Wanted} and wrote {Written} — view={View}",
                $"{request.Width}x{request.Height}", $"{written.Width}x{written.Height}",
                request.View);
        }
        _logger.LogInformation(
            "screenshot: view={View} language={Language} size={Width}x{Height} file={File}",
            request.View, System.Globalization.CultureInfo.CurrentUICulture.Name,
            written.Width, written.Height, request.OutputPath);

        if (!ReferenceEquals(subject, this)) subject.Close();
        Close();
    }

    /// <summary>
    /// Arrange the named pose and answer with the window to photograph — which
    /// is this one, except for the pose that IS another window.
    /// </summary>
    Form Pose(ScreenshotRequest request)
    {
        switch (request.View.ToLowerInvariant())
        {
            case ScreenshotViews.Table:
                TickAFewObjects();
                // On a row that overlaps something, so the panel beside the
                // list has something to show rather than saying it has nothing.
                FocusAnOverlappingObject();
                return this;

            case ScreenshotViews.Tiles:
                TickAFewObjects();
                _tileViewMenuItem.PerformClick();
                // After the switch, so the focus lands on a tile rather than on
                // a row nobody can see — and on one that overlaps something, so
                // the panel beside it is not a third of the picture saying it
                // has nothing to show.
                FocusAnOverlappingObject();
                return this;

            case ScreenshotViews.Objects:
                FocusAnOverlappingObject();
                _flattenPanel.SelectFirstObject();
                return this;

            case ScreenshotViews.ShownTypes:
                // The menu the item lives under, found through the item itself
                // so the two cannot drift apart.
                FocusAnOverlappingObject();
                var viewMenu = _menuStrip.Items.OfType<ToolStripMenuItem>()
                    .First(m => m.DropDownItems.Contains(_shownTypesMenuItem));
                viewMenu.ShowDropDown();
                _shownTypesMenuItem.ShowDropDown();
                return this;

            case ScreenshotViews.Usage:
                return OpenUsageWindowForTheCamera(request);

            default:
                return this;
        }
    }

    /// <summary>
    /// Tick the first few removable objects, so the list is photographed doing
    /// what it is for rather than sitting untouched.
    /// </summary>
    void TickAFewObjects()
    {
        foreach (var group in _displayGroups.Where(g => g.IsSafelyRemovable).Take(TickedForTheCamera))
        {
            SetSelected(group.Hash, true);
        }
        SyncAllViewCheckStates();
        UpdateSelectionState();
        RefreshSelectionStatus();
    }

    /// <summary>
    /// Put the cursor on an object that overlaps something, which is the only
    /// kind of row the graphics-objects panel has anything to say about.
    /// </summary>
    void FocusAnOverlappingObject()
    {
        var members = _workflow.OpenDocuments
            .SelectMany(d => d.OverlapRegions)
            .SelectMany(r => r.Members);
        foreach (var member in members)
        {
            if (_workflow.ImageGroups.FirstOrDefault(g => g.Matches(member)) is not { } group) continue;
            if (!_displayGroups.Contains(group)) continue;

            FocusRowFor(group);
            return;
        }
    }

    /// <summary>
    /// The usage window, shown without waiting on it. The menu command opens it
    /// modally — which is right for a person and useless for a camera, because
    /// the shutter is on the other side of that call.
    /// </summary>
    Form OpenUsageWindowForTheCamera(ScreenshotRequest request)
    {
        var group = _displayGroups
            .OrderByDescending(g => g.FileOccurrences.Sum(f => f.Occurrences.Count))
            .First();

        var window = new UsageLocationsDialog(UsageWindowTitle(group), BuildUsageRows(group))
        {
            StartPosition = FormStartPosition.Manual,
            Location = Location,
            TopMost = true,
        };
        window.Show(this);
        // Not awaited: the pose is arranged synchronously and the shutter waits
        // afterwards, which is the same patience this needs.
        _ = ScreenshotCamera.SizeVisibleFrameAsync(window, request.Width, request.Height);
        window.Activate();
        return window;
    }
}
