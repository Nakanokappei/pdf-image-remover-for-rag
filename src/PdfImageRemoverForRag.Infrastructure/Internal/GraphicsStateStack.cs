using PdfImageRemoverForRag.Core.Models;

namespace PdfImageRemoverForRag.Infrastructure.Internal;

/// <summary>
/// Tracks the graphics-state values that make two same-shaped paths distinct:
/// line width and stroke/fill color (as RGB). Pushed/popped alongside the CTM
/// on <c>q</c>/<c>Q</c> so a shape's signature and rendered color reflect the
/// state in effect when it is painted.
/// </summary>
internal sealed class GraphicsStateStack
{
    readonly Stack<State> _saved = new();
    State _current = State.Default;

    public double LineWidth => _current.LineWidth;
    public RgbColor? StrokeColor => _current.StrokeColor;
    public RgbColor? FillColor => _current.FillColor;

    public void Push() => _saved.Push(_current);

    public void Pop()
    {
        if (_saved.Count > 0) _current = _saved.Pop();
    }

    public void SetLineWidth(double width) => _current = _current with { LineWidth = width };
    public void SetStrokeColor(RgbColor color) => _current = _current with { StrokeColor = color };
    public void SetFillColor(RgbColor color) => _current = _current with { FillColor = color };

    readonly record struct State(double LineWidth, RgbColor? StrokeColor, RgbColor? FillColor)
    {
        public static State Default => new(1.0, null, null);
    }
}
