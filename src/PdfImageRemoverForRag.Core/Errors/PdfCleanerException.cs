namespace PdfImageRemoverForRag.Core.Errors;

/// <summary>
/// Domain exception raised by the Infrastructure layer and caught by the App.
/// Carrying a <see cref="Kind"/> alongside the message lets the UI look up
/// the correct localized text + suggested action without parsing strings.
/// </summary>
public sealed class PdfCleanerException : Exception
{
    public PdfCleanerErrorKind Kind { get; }

    public PdfCleanerException(PdfCleanerErrorKind kind, string message)
        : base(message)
    {
        Kind = kind;
    }

    public PdfCleanerException(PdfCleanerErrorKind kind, string message, Exception innerException)
        : base(message, innerException)
    {
        Kind = kind;
    }
}
