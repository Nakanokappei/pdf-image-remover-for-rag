# License Notices (PDF Image Remover for RAG)

## Runtime dependencies bundled with the app

| Library | Version | License | Purpose |
| --- | --- | --- | --- |
| [PDFsharp](https://github.com/empira/PDFsharp) | 6.2.4 | [MIT](https://github.com/empira/PDFsharp/blob/master/LICENSE.md) | PDF parsing, content-stream editing, saving |
| [PdfPig](https://github.com/UglyToad/PdfPig) | 0.1.15 | [Apache-2.0](https://github.com/UglyToad/PdfPig/blob/master/LICENSE.md) | Thumbnail decoding, independent post-save verification |
| [Microsoft.Extensions.Logging](https://www.nuget.org/packages/Microsoft.Extensions.Logging) | 8.0.1 | MIT | Diagnostic logging |
| .NET 8 runtime (bundled by self-contained publish) | 8.0.x | MIT | Runtime |

All of these are permissive licenses that place no restrictions on commercial
use or redistribution, so they do not constrain how the app is distributed
(free, paid, or in-house).

## Development-only dependencies (not part of the distribution)

| Library | License | Purpose |
| --- | --- | --- |
| xunit / xunit.runner.visualstudio | Apache-2.0 | Unit tests |
| Microsoft.NET.Test.Sdk | MIT | Test host |
| coverlet.collector | MIT | Coverage measurement |

## Evaluated but not adopted

- **iText 9.x** — AGPL v3. Its source-disclosure obligations for derivative
  software would constrain the distribution model, so it was rejected.

## Attribution obligations

- MIT (PDFsharp and others): the copyright notice and license text must be
  retained → this file ships with the distribution.
- Apache-2.0 (PdfPig): same, plus any NOTICE file must be retained → the
  PdfPig repository contains no NOTICE file (as of 2026-07), so retaining
  the license text suffices.
