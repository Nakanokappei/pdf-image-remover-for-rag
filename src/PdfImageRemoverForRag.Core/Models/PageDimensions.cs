namespace PdfImageRemoverForRag.Core.Models;

/// <summary>
/// Physical size of one PDF page in points (1 pt = 1/72 inch). Required by
/// <c>FullPageImageDetector</c> to decide whether an image occurrence covers
/// nearly the whole page.
/// </summary>
public readonly record struct PageDimensions(int PageNumber, double WidthPoints, double HeightPoints);
