using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Sber2Excel.Models;
using UglyToad.PdfPig;

namespace Sber2Excel.Services.Parsing;

/// <summary>
/// Port of Python Sberbank2Excel extractor SBER_SAVING_2303.
/// Sberbank "Выписка из лицевого счёта по вкладу" — deposit/savings account.
/// Shape: each transaction is two lines: a header row (date, description, code, value, remaining)
/// followed by a "к/с ... № ..." offsetting-account / document-number line.
/// No foreign currency, no auth code, no time component.
///
/// NOTE: This parser has not been validated against a real PDF yet — no fixture available.
/// It is ported by shape from the Python reference; regexes may need empirical tuning.
/// </summary>
public partial class SberSaving2303 : PdfParserBase
{
    public override string DisplayName => "Сбербанк — выписка по вкладу (2303)";

    public override bool CanParse(string firstPageText)
    {
        bool hasSavingHeader = firstPageText.Contains("Выписка из лицевого счёта по вкладу", StringComparison.Ordinal);
        // Anti-marker — present in the newer SBER_SAVING_2407 format.
        bool hasNewerFormatMarker = firstPageText.Contains("Дата предыдущей операции по счёту", StringComparison.Ordinal);
        return hasSavingHeader && !hasNewerFormatMarker;
    }

    // ── Regex ────────────────────────────────────────────────────────────────

    // Row 1: 27.07.2022 Списание 3 -230,00 10,00
    // Description may be one or more words. Code is a short digit sequence (2-6 digits seen in samples).
    [GeneratedRegex(@"^(\d{2}\.\d{2}\.\d{4})\s+(.+?)\s+(\d{1,6})\s+([+\-]?\d[\d\s\u00A0]*,\d{2})\s+([\d\s\u00A0]+,\d{2})\s*$")]
    private static partial Regex RowRegex();

    // Row 2: к/с 12345678901234567890 № 12345678-90
    [GeneratedRegex(@"к/с\s+(\S+)\s+№\s+(.+)$")]
    private static partial Regex KsRegex();

    [GeneratedRegex(@"Пополнение\s+([\d\s\u00A0]+,\d{2})\s+Списание\s+([\d\s\u00A0]+,\d{2})")]
    private static partial Regex PopolnenieSpisanieRegex();

    [GeneratedRegex(@"Остаток средств\s+([\d\s\u00A0]+,\d{2})\s+Остаток средств\s+([\d\s\u00A0]+,\d{2})")]
    private static partial Regex OstatkiRegex();

    [GeneratedRegex(@"ЗА ПЕРИОД\s+(\d{2}\.\d{2}\.\d{4})\s*[—\-–]\s*(\d{2}\.\d{2}\.\d{4})", RegexOptions.IgnoreCase)]
    private static partial Regex PeriodRegex();

    // ── Parse ────────────────────────────────────────────────────────────────

    public override StatementInfo Parse(string filePath) => ParseDocument(PdfDocument.Open(filePath));
    public override StatementInfo Parse(byte[] pdfBytes) => ParseDocument(PdfDocument.Open(pdfBytes));

    private StatementInfo ParseDocument(PdfDocument document)
    {
        var info = new StatementInfo { BankName = DisplayName };
        using var _ = document;

        // Collect all lines from all pages — savings statements are short, so we can afford this.
        var allLines = new List<string>();
        foreach (var page in document.GetPages())
            foreach (var line in ExtractLines(page))
                allLines.Add(line.FullText.Trim());

        ParseHeader(allLines, info);

        Transaction? pending = null;
        foreach (var line in allLines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            var rowMatch = RowRegex().Match(line);
            if (rowMatch.Success)
            {
                if (pending != null) info.Transactions.Add(pending);
                pending = new Transaction
                {
                    OperationDate = DateTime.ParseExact(rowMatch.Groups[1].Value, "dd.MM.yyyy", CultureInfo.InvariantCulture),
                    Description = rowMatch.Groups[2].Value.Trim(),
                    Code = rowMatch.Groups[3].Value,
                    Amount = ParseBalance(rowMatch.Groups[4].Value),
                    Balance = ParseBalance(rowMatch.Groups[5].Value),
                };
                continue;
            }

            var ksMatch = KsRegex().Match(line);
            if (ksMatch.Success && pending != null)
            {
                pending.OffsettingAccount = ksMatch.Groups[1].Value.Trim();
                pending.DocumentNumber = ksMatch.Groups[2].Value.Trim();
            }
        }

        if (pending != null) info.Transactions.Add(pending);
        return info;
    }

    private static void ParseHeader(List<string> lines, StatementInfo info)
    {
        var joined = string.Join("\n", lines);

        var period = PeriodRegex().Match(joined);
        if (period.Success)
        {
            info.PeriodFrom = ParseDate(period.Groups[1].Value);
            info.PeriodTo = ParseDate(period.Groups[2].Value);
        }

        var ps = PopolnenieSpisanieRegex().Match(joined);
        if (ps.Success)
        {
            info.Deposits = ParseBalance(ps.Groups[1].Value);
            info.Withdrawals = ParseBalance(ps.Groups[2].Value);
        }

        var os = OstatkiRegex().Match(joined);
        if (os.Success)
        {
            info.OpeningBalance = ParseBalance(os.Groups[1].Value);
            info.ClosingBalance = ParseBalance(os.Groups[2].Value);
        }
    }
}
