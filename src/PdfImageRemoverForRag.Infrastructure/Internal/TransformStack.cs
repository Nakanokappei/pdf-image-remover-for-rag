namespace PdfImageRemoverForRag.Infrastructure.Internal;

/// <summary>
/// Push-down stack used to track the current PDF transformation matrix as we
/// walk a content stream. <c>q</c> pushes the current CTM; <c>Q</c> restores
/// the last saved one; <c>cm</c> concatenates onto the current CTM.
/// </summary>
internal sealed class TransformStack
{
    readonly Stack<AffineMatrix> _saved = new();
    AffineMatrix _current = AffineMatrix.Identity;

    public AffineMatrix Current => _current;

    public void Push() => _saved.Push(_current);

    public void Pop()
    {
        // A malformed content stream can pop more than it pushed. Treat as
        // "restore to identity" rather than crash — surfaced as a warning
        // upstream if diagnostics are enabled.
        if (_saved.Count > 0) _current = _saved.Pop();
    }

    public void Concat(AffineMatrix m) => _current = m.Multiply(_current);
}
