using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Sber2Excel.Models;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace Sber2Excel.Services;

public partial class PdfParserService
{
    // ── Line-type detection patterns (match on full line text) ──────────────

    /// Transaction header: line starts with DD.MM.YYYY HH:MM
    [GeneratedRegex(@"^(\d{2}\.\d{2}\.\d{4})\s+(\d{2}:\d{2})")]
    private static partial Regex TxHeaderStartRegex();

    /// Processing line: line starts with DD.MM.YYYY NNNNNN (5-6 digit auth code, no colon)
    [GeneratedRegex(@"^(\d{2}\.\d{2}\.\d{4})\s+(\d{5,6})(?:\s|$)")]
    private static partial Regex TxProcessStartRegex();

    /// Russian monetary number: optional +, then 1-3 digits, then groups of (space + 3 digits), then ,NN
    [GeneratedRegex(@"(\+?)\s*(\d{1,3}(?:[\s\u00A0]\d{3})*,\d{2})")]
    private static partial Regex MoneyRegex();

    /// Statement period header
    [GeneratedRegex(@"За период\s+(\d{2}\.\d{2}\.\d{4})\s*[—\-–]\s*(\d{2}\.\d{2}\.\d{4})")]
    private static partial Regex PeriodRegex();

    [GeneratedRegex(@"Остаток на (\d{2}\.\d{2}\.\d{4})\s+([\d\s\u00A0]+,\d{2})")]
    private static partial Regex BalanceRegex();

    [GeneratedRegex(@"Пополнение\s+([\d\s\u00A0]+,\d{2})")]
    private static partial Regex DepositsRegex();

    [GeneratedRegex(@"Списание\s+([\d\s\u00A0]+,\d{2})")]
    private static partial Regex WithdrawalsRegex();

    // Lines that should never be treated as transaction data
    private static readonly string[] SkipKeywords =
    [
        "Выписка по счёту", "ДАТА ОПЕРАЦИИ", "КАТЕГОРИЯ",
        "СУММА В ВАЛЮТЕ", "ОСТАТОК СРЕДСТВ", "Сумма в валюте",
        "В валюте счёта", "Продолжение на следующей",
        "Дата обработки", "и код авторизации", "Описание операции",
        "Действителен", "www.sberbank", "Вавилова", "Заказано",
        "Для проверки", "Зайдите", "Нажмите", "Получите", "Предоставляя",
        "Страница"
    ];

    // ── Public API ────────────────────────────────────────────────────────────

    public StatementInfo ParseStatement(string filePath)
    {
        var info = new StatementInfo();

        using var document = PdfDocument.Open(filePath);

        Transaction? currentTx = null;
        bool awaitingProcessingLine = false;
        bool headerParsed = false;

        foreach (var page in document.GetPages())
        {
            var lines = ExtractLines(page);

            if (!headerParsed)
            {
                ParseStatementHeader(lines, info);
                headerParsed = true;
            }

            foreach (var line in lines)
            {
                var full = line.FullText.Trim();
                if (string.IsNullOrWhiteSpace(full)) continue;
                if (IsSkippable(full)) continue;

                // ── Transaction header: DD.MM.YYYY HH:MM ... amount balance ──
                var txMatch = TxHeaderStartRegex().Match(full);
                if (txMatch.Success)
                {
                    if (currentTx != null)
                        info.Transactions.Add(currentTx);

                    currentTx = ParseTransactionHeader(txMatch, full);
                    awaitingProcessingLine = true;
                    continue;
                }

                // ── Processing / auth line: DD.MM.YYYY AUTHCODE description ──
                var procMatch = TxProcessStartRegex().Match(full);
                if (procMatch.Success && currentTx != null)
                {
                    currentTx.ProcessingDate = ParseDate(procMatch.Groups[1].Value);
                    currentTx.AuthCode = procMatch.Groups[2].Value;
                    currentTx.Description = full.Substring(procMatch.Length).Trim();
                    awaitingProcessingLine = false;
                    continue;
                }

                // ── Description continuation (wraps to next line) ─────────────
                if (!awaitingProcessingLine && currentTx != null)
                {
                    currentTx.Description = (currentTx.Description + " " + full).Trim();
                }
            }
        }

        if (currentTx != null)
            info.Transactions.Add(currentTx);

        return info;
    }

    // ── Header parsing ────────────────────────────────────────────────────────

    private static void ParseStatementHeader(List<PageLine> lines, StatementInfo info)
    {
        var fullPageText = string.Join("\n", lines.Select(l => l.FullText));

        var periodMatch = PeriodRegex().Match(fullPageText);
        if (periodMatch.Success)
        {
            info.PeriodFrom = ParseDate(periodMatch.Groups[1].Value);
            info.PeriodTo = ParseDate(periodMatch.Groups[2].Value);
        }

        foreach (Match m in BalanceRegex().Matches(fullPageText))
        {
            var date = ParseDate(m.Groups[1].Value);
            var amount = ParseBalance(m.Groups[2].Value);
            if (date == info.PeriodFrom) info.OpeningBalance = amount;
            else if (date == info.PeriodTo) info.ClosingBalance = amount;
        }

        var depositsMatch = DepositsRegex().Match(fullPageText);
        if (depositsMatch.Success)
            info.Deposits = ParseBalance(depositsMatch.Groups[1].Value);

        var withdrawalsMatch = WithdrawalsRegex().Match(fullPageText);
        if (withdrawalsMatch.Success)
            info.Withdrawals = ParseBalance(withdrawalsMatch.Groups[1].Value);

        ParseAccountInfo(lines, info);
    }

    private static void ParseAccountInfo(List<PageLine> lines, StatementInfo info)
    {
        bool nextIsHolder = false;
        foreach (var line in lines)
        {
            var full = line.FullText.Trim();
            if (string.IsNullOrWhiteSpace(full)) continue;

            if (full.Contains("Владелец счёта")) { nextIsHolder = true; continue; }
            if (nextIsHolder) { info.AccountHolder = full; nextIsHolder = false; continue; }
            if (full.StartsWith("Номер счёта")) { info.AccountNumber = full["Номер счёта".Length..].Trim(); continue; }
            if (full.StartsWith("Карта") && !full.Contains("Дата")) { info.CardNumber = full["Карта".Length..].Trim(); continue; }
            if (full.StartsWith("Валюта")) { info.Currency = full["Валюта".Length..].Trim(); }
        }
    }

    // ── Transaction header parsing from a full-line string ───────────────────

    private static Transaction ParseTransactionHeader(Match dateTimeMatch, string fullText)
    {
        var opDate = ParseDateTime(dateTimeMatch.Groups[1].Value, dateTimeMatch.Groups[2].Value);

        // Text after "DD.MM.YYYY HH:MM "
        var afterDateTime = fullText[dateTimeMatch.Length..].Trim();

        // Collect all monetary amounts from right to left
        var moneyMatches = MoneyRegex().Matches(afterDateTime);

        decimal balance = 0, amount = 0;

        if (moneyMatches.Count >= 1)
            balance = ParseBalance(moneyMatches[^1].Groups[2].Value);

        if (moneyMatches.Count >= 2)
        {
            bool isCredit = moneyMatches[^2].Groups[1].Value == "+";
            amount = ParseBalance(moneyMatches[^2].Groups[2].Value);
            if (!isCredit) amount = -amount;
        }

        // Category = everything before the first monetary amount
        var category = moneyMatches.Count > 0
            ? afterDateTime[..moneyMatches[0].Index].Trim()
            : afterDateTime;

        return new Transaction
        {
            OperationDate = opDate,
            Category = category,
            Amount = amount,
            Balance = balance,
        };
    }

    // ── Page text extraction ──────────────────────────────────────────────────

    private static List<PageLine> ExtractLines(Page page)
    {
        var words = page.GetWords().ToList();

        // Group by rounded Y (5pt tolerance)
        var grouped = words
            .GroupBy(w => (int)Math.Round(w.BoundingBox.Bottom / 5.0) * 5)
            .OrderByDescending(g => g.Key);  // top of page first

        var result = new List<PageLine>();
        foreach (var group in grouped)
        {
            var sorted = group.OrderBy(w => w.BoundingBox.Left).ToList();
            var full = string.Join(" ", sorted.Select(w => w.Text));
            result.Add(new PageLine(full));
        }
        return result;
    }

    private static bool IsSkippable(string text) =>
        SkipKeywords.Any(k => text.Contains(k, StringComparison.Ordinal));

    // ── Number parsers ────────────────────────────────────────────────────────

    private static DateTime ParseDateTime(string date, string time)
        => DateTime.ParseExact($"{date} {time}", "dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture);

    private static DateTime ParseDate(string date)
        => DateTime.ParseExact(date.Trim(), "dd.MM.yyyy", CultureInfo.InvariantCulture);

    private static decimal ParseBalance(string text)
    {
        var normalized = text.Replace(" ", "").Replace("\u00A0", "").Replace(",", ".");
        return decimal.TryParse(normalized, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : 0;
    }

    private record PageLine(string FullText);
}
