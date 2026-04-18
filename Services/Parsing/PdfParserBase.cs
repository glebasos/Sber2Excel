using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using UglyToad.PdfPig.Content;

namespace Sber2Excel.Services.Parsing;

/// <summary>
/// Shared utilities for all PDF statement parsers:
/// page-line extraction, date/number parsing, and common regex patterns.
/// </summary>
public abstract partial class PdfParserBase : IPdfStatementParser
{
    public abstract string DisplayName { get; }
    public abstract bool CanParse(string firstPageText);
    public abstract Sber2Excel.Models.StatementInfo Parse(string filePath);
    public abstract Sber2Excel.Models.StatementInfo Parse(byte[] pdfBytes);

    // ── Shared regex ─────────────────────────────────────────────────────────

    /// Russian monetary number: optional +, 1-3 digits, (space + 3 digits)*, ,NN
    [GeneratedRegex(@"(\+?)\s*(\d{1,3}(?:[\s\u00A0]\d{3})*,\d{2})")]
    protected static partial Regex MoneyRegex();

    // ── Line extraction ───────────────────────────────────────────────────────

    /// <summary>
    /// Extracts visual lines from a PDF page by grouping words that share
    /// the same Y coordinate (within 5 pt), ordered top-to-bottom.
    /// </summary>
    protected static List<PageLine> ExtractLines(Page page)
    {
        var words = page.GetWords().ToList();

        return words
            .GroupBy(w => (int)Math.Round(w.BoundingBox.Bottom / 5.0) * 5)
            .OrderByDescending(g => g.Key)
            .Select(g =>
            {
                var text = string.Join(" ", g.OrderBy(w => w.BoundingBox.Left).Select(w => w.Text));
                return new PageLine(text);
            })
            .ToList();
    }

    // ── Number / date helpers ─────────────────────────────────────────────────

    protected static DateTime ParseDateTime(string date, string time)
        => DateTime.ParseExact($"{date} {time}", "dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture);

    protected static DateTime ParseDate(string date)
        => DateTime.ParseExact(date.Trim(), "dd.MM.yyyy", CultureInfo.InvariantCulture);

    protected static decimal ParseBalance(string text)
    {
        var normalized = text.Replace(" ", "").Replace("\u00A0", "").Replace(",", ".");
        return decimal.TryParse(normalized, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : 0;
    }

    // ── Shared data type ──────────────────────────────────────────────────────

    /// <summary>One visual row extracted from a PDF page.</summary>
    protected record PageLine(string FullText);
}
