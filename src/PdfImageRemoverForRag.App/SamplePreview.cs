using System.Drawing.Drawing2D;

namespace PdfImageRemoverForRag.App;

/// <summary>
/// A window onto one sample picture in the settings dialog: the whole thing at
/// 1x, and a draggable view of part of it as the magnification goes up.
///
/// It exists because neither end of the range answers the question on its own.
/// The whole picture shows what the sample IS, which is what makes the artifact
/// mean anything; a magnified corner shows the artifact, which at true size is
/// below what an eye resolves. So the reader gets both and moves between them.
///
/// Nothing here is a setting. Panning and magnifying change what is looked at
/// and never what is saved, which is why the control keeps no state the dialog
/// reads back.
/// </summary>
internal sealed class SamplePreview : Control
{
    /// <summary>How far the view moves per arrow key, as a share of the window.</summary>
    const float KeyboardStep = 0.15f;

    Image? _image;
    int _magnification = 1;

    /// <summary>
    /// Where the window is centred, as a share of the picture in each
    /// direction. Kept normalized rather than in pixels so it survives a change
    /// of magnification, of control size, and of the picture itself.
    /// </summary>
    PointF _centre = new(0.5f, 0.5f);

    Point _draggingFrom;
    bool _dragging;

    public SamplePreview()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.ResizeRedraw, true);
        BackColor = Color.White;
        // Reachable by keyboard: panning is an interaction, and one that only a
        // mouse can perform is one some people cannot perform at all.
        TabStop = true;
    }

    /// <summary>
    /// The spoken name, which has to be the window text and nothing else.
    ///
    /// Measured through UI Automation: a plain Control subclass is exposed by
    /// the generic HWND provider, which reads its name from the window text.
    /// Neither <see cref="Control.AccessibleName"/>, nor
    /// <see cref="Control.AccessibleRole"/>, nor a ControlAccessibleObject
    /// declared through CreateAccessibilityInstance reached a UIA client - all
    /// three were tried, and the control kept arriving in the tab order as a
    /// pane with no name. Setting the text is what named it.
    ///
    /// Nothing paints the text: this control draws itself and never calls the
    /// base painting, so the caption is heard and not seen.
    /// </summary>
    public void Announce(string caption) => Text = caption;

    /// <summary>The picture to show. The control takes ownership of it.</summary>
    public void Show(Image? image)
    {
        _image?.Dispose();
        _image = image;
        Invalidate();
    }

    public int Magnification
    {
        get => _magnification;
        set
        {
            int wanted = Math.Max(1, value);
            if (wanted == _magnification) return;
            _magnification = wanted;
            ClampCentre();
            UpdateCursor();
            Invalidate();
        }
    }

    /// <summary>
    /// How large the picture is actually being drawn, as a multiple of its own
    /// pixels — 1.0 when one stored pixel covers one screen pixel.
    ///
    /// This is not the magnification the slider carries, and the difference is
    /// the whole reason it is here. A 1200 px sample in a 350 px band is
    /// already down at 0.29 before the slider is touched, and a window that
    /// called that "1x" would be claiming life size for something it had
    /// shrunk to under a third.
    /// </summary>
    public double DrawnScale => _image is null
        ? 0
        : FittedBounds(_image).Width / (double)Math.Max(1, Window(_image).Width);

    /// <summary>
    /// The largest rectangle inside the control with the picture's proportions.
    /// Fitting rather than filling: a sample stretched out of shape is a sample
    /// nobody can judge, and the bars of a chart would not even be square.
    /// </summary>
    Rectangle FittedBounds(Image image)
    {
        double scale = Math.Min(
            Width / (double)image.Width, Height / (double)image.Height);
        int width = Math.Max(1, (int)Math.Round(image.Width * scale));
        int height = Math.Max(1, (int)Math.Round(image.Height * scale));
        return new Rectangle((Width - width) / 2, (Height - height) / 2, width, height);
    }

    /// <summary>The part of the picture currently in the window.</summary>
    Rectangle Window(Image image)
    {
        int width = Math.Max(1, image.Width / _magnification);
        int height = Math.Max(1, image.Height / _magnification);
        return new Rectangle(
            (int)Math.Round((_centre.X * image.Width) - (width / 2.0)),
            (int)Math.Round((_centre.Y * image.Height) - (height / 2.0)),
            width,
            height);
    }

    /// <summary>Keep the window inside the picture, so no edge shows blank.</summary>
    void ClampCentre()
    {
        float half = 0.5f / _magnification;
        _centre = new PointF(
            Math.Clamp(_centre.X, half, 1f - half),
            Math.Clamp(_centre.Y, half, 1f - half));
    }

    void UpdateCursor() =>
        Cursor = _magnification > 1 ? Cursors.Hand : Cursors.Default;

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.Clear(BackColor);
        if (_image is null) return;

        var destination = FittedBounds(_image);
        var window = Window(_image);

        // Nearest neighbour only when the picture is being made BIGGER, which is
        // the point of magnifying: the stored pixels are what is being judged,
        // and smoothing them would hide exactly what the reader is looking for.
        // Going the other way it would alias the sample into noise of its own.
        e.Graphics.InterpolationMode = window.Width < destination.Width
            ? InterpolationMode.NearestNeighbor
            : InterpolationMode.HighQualityBicubic;
        e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        e.Graphics.DrawImage(_image, destination, window, GraphicsUnit.Pixel);

        if (Focused)
        {
            ControlPaint.DrawFocusRectangle(e.Graphics, destination);
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        if (e.Button != MouseButtons.Left || _magnification == 1) return;
        _dragging = true;
        _draggingFrom = e.Location;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_dragging || _image is null) return;

        // A drag moves the picture under the window, so the window moves the
        // other way. Converted through the drawn size, so a pixel of mouse
        // travel moves the same distance of picture whatever the magnification.
        var destination = FittedBounds(_image);
        _centre = new PointF(
            _centre.X - ((e.X - _draggingFrom.X) / (float)destination.Width / _magnification),
            _centre.Y - ((e.Y - _draggingFrom.Y) / (float)destination.Height / _magnification));
        _draggingFrom = e.Location;

        ClampCentre();
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        _dragging = false;
    }

    /// <summary>Arrow keys pan, for anyone not using a mouse.</summary>
    protected override bool IsInputKey(Keys key) => key switch
    {
        Keys.Left or Keys.Right or Keys.Up or Keys.Down => true,
        _ => base.IsInputKey(key),
    };

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_magnification == 1) return;

        float step = KeyboardStep / _magnification;
        _centre = e.KeyCode switch
        {
            Keys.Left => _centre with { X = _centre.X - step },
            Keys.Right => _centre with { X = _centre.X + step },
            Keys.Up => _centre with { Y = _centre.Y - step },
            Keys.Down => _centre with { Y = _centre.Y + step },
            _ => _centre,
        };
        ClampCentre();
        Invalidate();
        e.Handled = true;
    }

    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        Invalidate();
    }

    protected override void OnLostFocus(EventArgs e)
    {
        base.OnLostFocus(e);
        Invalidate();
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        UpdateCursor();
        Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _image?.Dispose();
        base.Dispose(disposing);
    }
}
