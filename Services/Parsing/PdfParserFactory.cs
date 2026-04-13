using System.Collections.Generic;
using System.Linq;
using UglyToad.PdfPig;

namespace Sber2Excel.Services.Parsing;

/// <summary>
/// Detects the correct parser for a given PDF file.
///
/// To add a new bank / statement type:
///   1. Create a class that extends <see cref="PdfParserBase"/> (or implements <see cref="IPdfStatementParser"/>).
///   2. Add an instance to the <see cref="Parsers"/> list below.
/// </summary>
public static class PdfParserFactory
{
    // ── Register all known parsers here ───────────────────────────────────────
    private static readonly List<IPdfStatementParser> Parsers =
    [
        new SberbankDebitCardParser(),
        // new SberbankCreditCardParser(),
        // new TinkoffDebitCardParser(),
    ];

    /// <summary>All registered parsers (for display in UI if needed).</summary>
    public static IReadOnlyList<IPdfStatementParser> All => Parsers;

    /// <summary>
    /// Opens the PDF, reads the first page, and returns the first parser
    /// whose <see cref="IPdfStatementParser.CanParse"/> returns true.
    /// Returns <c>null</c> if no parser matches.
    /// </summary>
    public static IPdfStatementParser? Detect(string filePath)
    {
        string firstPageText;
        using (var doc = PdfDocument.Open(filePath))
        {
            var words = doc.GetPage(1).GetWords();
            firstPageText = string.Join(" ", words.Select(w => w.Text));
        }

        return Parsers.FirstOrDefault(p => p.CanParse(firstPageText));
    }
}
