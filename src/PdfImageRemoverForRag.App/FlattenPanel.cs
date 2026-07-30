using System.Runtime.InteropServices;
using PdfImageRemoverForRag.Core.Grouping;
using PdfImageRemoverForRag.Core.Models;

namespace PdfImageRemoverForRag.App;

/// <summary>
/// The 統合 (Flatten) tab: a tree of every place where objects of different
/// kinds overlap, with a preview of the page beside it.
///
/// Four levels — document, page, unit, object — because flattening acts on one
/// place on one page, not on an object wherever it appears. That is the
/// opposite of the delete tab, where one row is the same image in five files
/// and one tick removes it from all of them. Checkboxes sit on the unit and the
/// object: ticking a unit takes everything in it, ticking objects individually
/// takes only those. Nothing happens until the file is saved.
/// </summary>
internal sealed class FlattenPanel : UserControl
{
    // --- what a tree node stands for ---------------------------------------
    // Each carries enough to drive the preview: which file, which page, and
    // which rectangles to pick out on it.

    abstract record FlattenNode(string FilePath, int PageNumber)
    {
        public abstract IReadOnlyList<RectangleF> HighlightBoxes { get; }
    }

    sealed record DocumentNode(string FilePath) : FlattenNode(FilePath, 1)
    {
        // A whole document has no one place to point at, so its preview is just
        // the first page.
        public override IReadOnlyList<RectangleF> HighlightBoxes => Array.Empty<RectangleF>();
    }

    sealed record PageNode(string FilePath, int Page, IReadOnlyList<OverlapRegion> Regions)
        : FlattenNode(FilePath, Page)
    {
        public override IReadOnlyList<RectangleF> HighlightBoxes =>
            Regions.Select(RectOf).ToArray();
    }

    sealed record UnitNode(string FilePath, OverlapRegion Region)
        : FlattenNode(FilePath, Region.PageNumber)
    {
        public override IReadOnlyList<RectangleF> HighlightBoxes =>
            Region.Members.Select(RectOf).ToArray();
    }

    sealed record ObjectNode(string FilePath, OverlapRegion Region, PlacedObject Member)
        : FlattenNode(FilePath, Region.PageNumber)
    {
        public override IReadOnlyList<RectangleF> HighlightBoxes => new[] { RectOf(Member) };
    }

    static RectangleF RectOf(OverlapRegion r) =>
        new((float)r.X, (float)r.Y, (float)r.Width, (float)r.Height);

    static RectangleF RectOf(PlacedObject o) =>
        new((float)o.X, (float)o.Y, (float)o.Width, (float)o.Height);

    readonly TreeView _tree = new()
    {
        Dock = DockStyle.Fill,
        CheckBoxes = true,
        HideSelection = false,
        ShowLines = true,
        FullRowSelect = false,
    };
    readonly Label _description = new()
    {
        Dock = DockStyle.Top,
        AutoSize = false,
        Text = L10n.FlattenDescription,
        UseMnemonic = false,
    };
    readonly Label _emptyMessage = new()
    {
        Dock = DockStyle.Fill,
        Text = L10n.FlattenNoOverlaps,
        TextAlign = ContentAlignment.MiddleCenter,
        ForeColor = SystemColors.GrayText,
        Visible = false,
        UseMnemonic = false,
    };
    readonly SplitContainer _split = new() { Dock = DockStyle.Fill };
    readonly PreviewPane _preview = new() { Dock = DockStyle.Fill };

    // Guards the parent/child check propagation from re-entering itself.
    bool _syncingChecks;

    /// <summary>Raised whenever the set of checked objects changes.</summary>
    public event EventHandler? SelectionChanged;

    public FlattenPanel()
    {
        _tree.AccessibleName = L10n.TabFlatten;
        _tree.AccessibleDescription = L10n.FlattenDescription;
        _preview.AccessibleName = L10n.AccessibleFlattenPreview;

        _tree.AfterSelect += OnAfterSelect;
        _tree.BeforeCheck += OnBeforeCheck;
        _tree.AfterCheck += OnAfterCheck;
        // A node's checkbox can only be hidden once the item exists in the
        // native control, and neither moment is when the tree is filled: this
        // tab's handle is not created until it is first displayed, and the items
        // under a collapsed node are not created until it opens.
        _tree.HandleCreated += (_, _) => HideCheckBoxesOnGroupingNodes();
        _tree.AfterExpand += (_, _) => HideCheckBoxesOnGroupingNodes();

        var treeSide = new Panel { Dock = DockStyle.Fill };
        treeSide.Controls.Add(_tree);
        treeSide.Controls.Add(_emptyMessage);
        _split.Panel1.Controls.Add(treeSide);
        _split.Panel2.Controls.Add(_preview);

        Controls.Add(_split);
        Controls.Add(_description);
    }

    int Dip(int logical) => LogicalToDeviceUnits(logical);

    /// <summary>
    /// Size what this panel measures by hand. The description's height depends
    /// on the font, and the splitter's opening position on the window, so both
    /// are set here rather than baked in at 96 DPI.
    /// </summary>
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyDpiDependentLayout();
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        ApplyDpiDependentLayout();
    }

    void ApplyDpiDependentLayout()
    {
        _description.Padding = new Padding(Dip(8), Dip(6), Dip(8), Dip(6));
        _description.Height = TextRenderer.MeasureText(
            _description.Text, _description.Font,
            new Size(Math.Max(Dip(200), Width - Dip(16)), int.MaxValue),
            TextFormatFlags.WordBreak).Height + Dip(14);
        _split.SplitterWidth = Math.Max(4, Dip(4));
        _split.Panel1MinSize = Dip(180);
        _split.Panel2MinSize = Dip(180);
        // Roughly two fifths to the tree: the labels are long (a text object
        // shows its string) and the preview still needs to be a readable page.
        int wanted = Math.Max(_split.Panel1MinSize, (int)(Width * 0.4));
        if (Width > _split.Panel1MinSize + _split.Panel2MinSize + _split.SplitterWidth)
        {
            _split.SplitterDistance = Math.Min(
                wanted, Width - _split.Panel2MinSize - _split.SplitterWidth);
        }
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        // Only the description's wrapped height depends on the width; the
        // splitter keeps wherever the user dragged it.
        if (IsHandleCreated) _description.Height = TextRenderer.MeasureText(
            _description.Text, _description.Font,
            new Size(Math.Max(Dip(200), Width - Dip(16)), int.MaxValue),
            TextFormatFlags.WordBreak).Height + Dip(14);
    }

    // =======================================================================
    // Building the tree
    // =======================================================================

    /// <summary>
    /// Rebuild from the open documents. Checks do not survive: the workspace
    /// they referred to is gone, and silently carrying ticks over to a
    /// different set of regions would flatten something nobody chose.
    /// </summary>
    public void SetDocuments(IReadOnlyList<PdfDocumentInfo> documents)
    {
        _tree.BeginUpdate();
        try
        {
            _tree.Nodes.Clear();
            foreach (var document in documents)
            {
                if (document.OverlapRegions.Count == 0) continue;
                _tree.Nodes.Add(BuildDocumentNode(document));
            }
        }
        finally
        {
            _tree.EndUpdate();
        }

        bool anything = _tree.Nodes.Count > 0;
        _tree.Visible = anything;
        _emptyMessage.Visible = !anything;
        HideCheckBoxesOnGroupingNodes();

        // Open the first document so the panel is not a wall of collapsed
        // nodes, but leave the pages closed — a 176-page file would fill the
        // pane with page numbers before showing anything to act on.
        //
        // The preview is pointed at that document directly rather than through
        // TreeView.SelectedNode: assigning that puts the focus in the tree, and
        // a TabControl brings forward whichever page holds the focused control —
        // so the window opened on this tab instead of the object list.
        if (anything)
        {
            _tree.Nodes[0].Expand();
            var first = (DocumentNode)_tree.Nodes[0].Tag!;
            _preview.Show(first.FilePath, first.PageNumber, first.HighlightBoxes);
        }
        else
        {
            _preview.Clear();
        }
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    static TreeNode BuildDocumentNode(PdfDocumentInfo document)
    {
        var documentNode = new TreeNode(Path.GetFileName(document.FilePath))
        {
            Tag = new DocumentNode(document.FilePath),
        };

        foreach (var page in document.OverlapRegions
                     .GroupBy(r => r.PageNumber)
                     .OrderBy(g => g.Key))
        {
            var regions = page.ToArray();
            var pageNode = new TreeNode(L10n.UsagePageLabel(page.Key))
            {
                Tag = new PageNode(document.FilePath, page.Key, regions),
            };

            // Units are numbered within their page, so the label matches what
            // the user is looking at rather than a running total across a file.
            for (int i = 0; i < regions.Length; i++)
            {
                pageNode.Nodes.Add(BuildUnitNode(document.FilePath, regions[i], i + 1));
            }
            documentNode.Nodes.Add(pageNode);
        }
        return documentNode;
    }

    static TreeNode BuildUnitNode(string filePath, OverlapRegion region, int number)
    {
        var unitNode = new TreeNode($"{L10n.FlattenUnitLabel(number)} ({KindSummary(region)})")
        {
            Tag = new UnitNode(filePath, region),
        };
        foreach (var member in region.Members)
        {
            unitNode.Nodes.Add(new TreeNode(ObjectLabel(member))
            {
                Tag = new ObjectNode(filePath, region, member),
            });
        }
        return unitNode;
    }

    /// <summary>The kinds in the unit, in list order, e.g. "image + text".</summary>
    static string KindSummary(OverlapRegion region) => string.Join(" + ", region.Members
        .Select(m => m.Kind)
        .Distinct()
        .OrderBy(k => k)
        .Select(KindLabel));

    static string KindLabel(RemovableKind kind) => kind switch
    {
        RemovableKind.Text => L10n.TypeText,
        RemovableKind.Shape => L10n.TypeShape,
        _ => L10n.TypeImage,
    };

    /// <summary>
    /// An object's label: its kind, then what identifies it to a person. For
    /// text that is the string itself — quoted, so a run of spaces reads as
    /// content rather than as a missing label — and for the other kinds the
    /// size, since one image looks like another in a list of words.
    /// </summary>
    static string ObjectLabel(PlacedObject member)
    {
        if (member.Kind == RemovableKind.Text)
        {
            const int maxShown = 40;
            var value = member.Identity;
            if (value.Length > maxShown) value = value[..maxShown] + "…";
            return $"{KindLabel(member.Kind)}  \"{value}\"";
        }
        return $"{KindLabel(member.Kind)}  "
             + L10n.ShapeSize((int)Math.Round(member.Width), (int)Math.Round(member.Height));
    }

    // =======================================================================
    // Checking
    // =======================================================================

    void OnBeforeCheck(object? sender, TreeViewCancelEventArgs e)
    {
        // Document and page nodes group; they are not things to flatten. Their
        // checkboxes are hidden below, but the keyboard can still reach them.
        if (e.Node?.Tag is DocumentNode or PageNode) e.Cancel = true;
    }

    void OnAfterCheck(object? sender, TreeViewEventArgs e)
    {
        if (_syncingChecks || e.Node is null) return;

        _syncingChecks = true;
        try
        {
            // A unit is a bulk switch for its objects; an object ticked on its
            // own leaves its unit checked only when nothing is left unticked,
            // so the unit's box always answers "is all of this being flattened".
            if (e.Node.Tag is UnitNode)
            {
                foreach (TreeNode child in e.Node.Nodes) child.Checked = e.Node.Checked;
            }
            else if (e.Node.Tag is ObjectNode && e.Node.Parent is { } unit)
            {
                unit.Checked = unit.Nodes.Cast<TreeNode>().All(n => n.Checked);
            }
        }
        finally
        {
            _syncingChecks = false;
        }
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Tick every object in the tree.</summary>
    public void CheckAll() => SetAllChecks(true);

    /// <summary>Clear every tick. Called after a save, and by the toolbar.</summary>
    public void ClearChecks() => SetAllChecks(false);

    void SetAllChecks(bool value)
    {
        _syncingChecks = true;
        try
        {
            foreach (var node in AllNodes(_tree.Nodes))
            {
                if (node.Tag is UnitNode or ObjectNode) node.Checked = value;
            }
        }
        finally
        {
            _syncingChecks = false;
        }
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    static IEnumerable<TreeNode> AllNodes(TreeNodeCollection nodes)
    {
        foreach (TreeNode node in nodes)
        {
            yield return node;
            foreach (var descendant in AllNodes(node.Nodes)) yield return descendant;
        }
    }

    /// <summary>How many individual objects are ticked.</summary>
    public int CheckedObjectCount =>
        AllNodes(_tree.Nodes).Count(n => n.Tag is ObjectNode && n.Checked);

    /// <summary>Whether there is anything at all to tick.</summary>
    public bool HasAnyObject => AllNodes(_tree.Nodes).Any(n => n.Tag is ObjectNode);

    /// <summary>
    /// The regions to flatten, per source file — each covering only the objects
    /// that are actually ticked, which is also all that will be deleted. A unit
    /// with nothing ticked is not in the result at all.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<OverlapRegion>> SelectedRegionsByFile()
    {
        var byFile = new Dictionary<string, List<OverlapRegion>>(StringComparer.OrdinalIgnoreCase);
        foreach (var unit in AllNodes(_tree.Nodes).Where(n => n.Tag is UnitNode))
        {
            var tag = (UnitNode)unit.Tag!;
            var members = unit.Nodes.Cast<TreeNode>()
                .Where(n => n.Checked)
                .Select(n => ((ObjectNode)n.Tag!).Member)
                .ToArray();
            if (members.Length == 0) continue;

            if (!byFile.TryGetValue(tag.FilePath, out var regions))
            {
                regions = new List<OverlapRegion>();
                byFile[tag.FilePath] = regions;
            }
            regions.Add(OverlapDetector.RegionCovering(tag.Region.PageNumber, members));
        }
        return byFile.ToDictionary(
            kv => kv.Key,
            kv => (IReadOnlyList<OverlapRegion>)kv.Value,
            StringComparer.OrdinalIgnoreCase);
    }

    // =======================================================================
    // Preview
    // =======================================================================

    void OnAfterSelect(object? sender, TreeViewEventArgs e)
    {
        if (e.Node?.Tag is FlattenNode node)
        {
            _preview.Show(node.FilePath, node.PageNumber, node.HighlightBoxes);
        }
    }

    // =======================================================================
    // Hiding the checkbox on the grouping levels
    // =======================================================================
    //
    // TreeView.CheckBoxes is all-or-nothing, and a checkbox on a document or a
    // page would promise a bulk action the design does not have. The state
    // image index is per item, though, and setting it to zero hides the box —
    // the documented way to do this.

    const int TvFirst = 0x1100;
    const int TvmSetItemW = TvFirst + 63;
    const int TvifState = 0x0008;
    const int TvisStateImageMask = 0xF000;

    [StructLayout(LayoutKind.Sequential)]
    struct TvItem
    {
        public int Mask;
        public IntPtr Item;
        public int State;
        public int StateMask;
        public IntPtr Text;
        public int TextMax;
        public int Image;
        public int SelectedImage;
        public int Children;
        public IntPtr LParam;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, ref TvItem lParam);

    void HideCheckBoxesOnGroupingNodes()
    {
        if (!_tree.IsHandleCreated) return;
        foreach (var node in AllNodes(_tree.Nodes))
        {
            if (node.Tag is not (DocumentNode or PageNode)) continue;
            var item = new TvItem
            {
                Item = node.Handle,
                Mask = TvifState,
                StateMask = TvisStateImageMask,
                State = 0,
            };
            SendMessage(_tree.Handle, TvmSetItemW, IntPtr.Zero, ref item);
        }
    }

    // =======================================================================
    // The preview pane
    // =======================================================================

    /// <summary>
    /// One page of one file, drawn with the selected node's rectangles picked
    /// out. Exactly one rendered page is held at a time — the same
    /// viewport-bounded memory rule the rest of the app follows.
    /// </summary>
    sealed class PreviewPane : Panel
    {
        // Control.Margin is a different thing (spacing outside the control), so
        // this one says what it measures: the gap between the pane's edge and
        // the page drawn inside it.
        const int PageInset = 12;

        readonly PdfPageRenderer _renderer = new();
        RenderedPage? _page;
        IReadOnlyList<RectangleF> _boxesInPoints = Array.Empty<RectangleF>();
        string? _filePath;
        int _pageNumber;
        int _renderedWidth;
        // Only the newest request may install its result: the user can click
        // through the tree faster than a page renders.
        int _requestId;
        bool _closed;

        public PreviewPane()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                   | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = SystemColors.Window;
        }

        int Dip(int logical) => LogicalToDeviceUnits(logical);

        public void Show(string filePath, int pageNumber, IReadOnlyList<RectangleF> boxesInPoints)
        {
            _boxesInPoints = boxesInPoints;
            // Same page, different boxes (a different node on the same page):
            // repaint, do not render again.
            if (_filePath == filePath && _pageNumber == pageNumber && _page is not null)
            {
                Invalidate();
                return;
            }
            _filePath = filePath;
            _pageNumber = pageNumber;
            Invalidate();
            _ = RenderAsync();
        }

        public void Clear()
        {
            _filePath = null;
            _page?.Bitmap.Dispose();
            _page = null;
            _boxesInPoints = Array.Empty<RectangleF>();
            Invalidate();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            // Re-render only when the pane has grown past what was rendered:
            // shrinking scales down cleanly, growing would show a blurry page.
            if (_filePath is not null && ClientSize.Width > _renderedWidth + Dip(32))
            {
                _ = RenderAsync();
            }
        }

        async Task RenderAsync()
        {
            if (_filePath is null || _closed) return;
            int width = Math.Max(1, ClientSize.Width - (Dip(PageInset) * 2));
            int id = ++_requestId;

            var rendered = await _renderer.RenderAsync(_filePath, _pageNumber, width);
            if (_closed || id != _requestId)
            {
                rendered?.Bitmap.Dispose();
                return;
            }

            _page?.Bitmap.Dispose();
            _page = rendered;
            _renderedWidth = width;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var area = new Rectangle(
                Dip(PageInset), Dip(PageInset),
                Math.Max(1, ClientSize.Width - (Dip(PageInset) * 2)),
                Math.Max(1, ClientSize.Height - (Dip(PageInset) * 2)));

            if (_page is null)
            {
                TextRenderer.DrawText(e.Graphics,
                    _filePath is null ? string.Empty : L10n.ThumbnailPending,
                    Font, area, SystemColors.GrayText,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                return;
            }

            var display = FitInside(_page.Bitmap.Size, area);
            var boxes = PageHighlightPainter.MapToDisplay(
                display, _page.PageWidthPoints, _page.PageHeightPoints, _boxesInPoints, Dip(4));
            e.Graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            PageHighlightPainter.DrawPage(e.Graphics, _page.Bitmap, display, boxes);
            using (var frame = new Pen(SystemColors.ControlDark))
            {
                e.Graphics.DrawRectangle(frame, display.X, display.Y, display.Width - 1, display.Height - 1);
            }
            PageHighlightPainter.DrawOutlines(e.Graphics, boxes, Dip(3));
        }

        static Rectangle FitInside(Size imageSize, Rectangle area)
        {
            double scale = Math.Min(
                (double)area.Width / imageSize.Width,
                (double)area.Height / imageSize.Height);
            int w = Math.Max(1, (int)(imageSize.Width * scale));
            int h = Math.Max(1, (int)(imageSize.Height * scale));
            return new Rectangle(
                area.X + ((area.Width - w) / 2), area.Y + ((area.Height - h) / 2), w, h);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _closed = true;
                _page?.Bitmap.Dispose();
                _page = null;
            }
            base.Dispose(disposing);
        }
    }
}
