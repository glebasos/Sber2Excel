using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Sber2Excel.Models;
using UglyToad.PdfPig;

namespace Sber2Excel.Services.Parsing;

/// <summary>
/// Port of Python Sberbank2Excel extractor SBER_PAYMENT_DEBIT_2604b.
/// Covers modern (2026-era) Sberbank PDF statements where the same template is used
/// for debit cards and payment accounts.
/// Fingerprint (all must be present): "Сбербанк", "Выписка по счёту дебетовой карты" OR
/// "Выписка по платёжному счёту", "Дата формирования", "Для проверки подлинности документа".
/// Anti-fingerprint (must NOT be present): "ОСТАТОК ПО СЧЁТУ", "Дергунова К. А."
/// </summary>
public partial class SberPaymentDebit2604b : PdfParserBase
{
    public override string DisplayName => "Сбербанк — дебетовая карта / платёжный счёт (2604b)";

    public override bool CanParse(string firstPageText)
    {
        // Same signature as Python SBER_PAYMENT_DEBIT_2604b.check_specific_signatures.
        // Note: some markers (Дата формирования, Для проверки подлинности) live on the LAST page,
        // not the first — but the first page is all we have at detection time. Consumer calls Parse()
        // which may still accept documents that look close enough.
        bool hasBank = Regex.IsMatch(firstPageText, "сбербанк", RegexOptions.IgnoreCase);
        bool hasStatement =
            firstPageText.Contains("Выписка по счёту дебетовой карты", StringComparison.Ordinal) ||
            firstPageText.Contains("Выписка по платёжному счёту", StringComparison.Ordinal);
        bool hasOldBalanceMarker = firstPageText.Contains("ОСТАТОК ПО СЧЁТУ", StringComparison.Ordinal);
        bool hasDergunova = firstPageText.Contains("Дергунова К. А.", StringComparison.Ordinal);

        return hasBank && hasStatement && !hasOldBalanceMarker && !hasDergunova;
    }

    // ── Line-type detection ───────────────────────────────────────────────────

    [GeneratedRegex(@"^(\d{2}\.\d{2}\.\d{4})\s+(\d{2}:\d{2})")]
    private static partial Regex TxHeaderStartRegex();

    [GeneratedRegex(@"^(\d{2}\.\d{2}\.\d{4})\s+(\d{5,6})(?:\s|$)")]
    private static partial Regex TxProcessStartRegex();

    [GeneratedRegex(@"За период\s+(\d{2}\.\d{2}\.\d{4})\s*[—\-–]\s*(\d{2}\.\d{2}\.\d{4})")]
    private static partial Regex PeriodRegex();

    [GeneratedRegex(@"Остаток на (\d{2}\.\d{2}\.\d{4})\s+([\d\s\u00A0]+,\d{2})")]
    private static partial Regex BalanceLineRegex();

    [GeneratedRegex(@"Пополнение\s+([\d\s\u00A0]+,\d{2})")]
    private static partial Regex DepositsRegex();

    [GeneratedRegex(@"Списание\s+([\d\s\u00A0]+,\d{2})")]
    private static partial Regex WithdrawalsRegex();

    [GeneratedRegex(@"^.*?[•*\.]{2,}\s*\d{4}")]
    private static partial Regex CardNumberRegex();

    // Trailing "1 234,56 XYZ" (amount + currency symbol or ISO code) at end of description.
    [GeneratedRegex(@"\s+(\d[\d\s\u00A0]*,\d{2})\s+(\S{1,5})$")]
    private static partial Regex TrailingOperationalRegex();

    private static readonly string[] SkipKeywords =
    [
        "Выписка по счёту", "Выписка по платёжному",
        "ДАТА ОПЕРАЦИИ", "КАТЕГОРИЯ",
        "СУММА В ВАЛЮТЕ", "ОСТАТОК СРЕДСТВ", "Сумма в валюте",
        "В валюте счёта", "Продолжение на следующей",
        "Дата обработки", "и код авторизации", "Описание операции",
        "Действителен", "www.sberbank", "Вавилова", "Заказано",
        "Для проверки", "Зайдите", "Нажмите", "Получите", "Предоставляя",
        "Страница", "Дата формирования", "Реквизиты для"
    ];

    // ── Main parse ────────────────────────────────────────────────────────────

    public override StatementInfo Parse(string filePath) => ParseDocument(PdfDocument.Open(filePath));

    public override StatementInfo Parse(byte[] pdfBytes) => ParseDocument(PdfDocument.Open(pdfBytes));

    private StatementInfo ParseDocument(PdfDocument document)
    {
        var info = new StatementInfo { BankName = DisplayName };

        using var _ = document;

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

                // Footer marker — finalize the last transaction and stop reading.
                if (full.Contains("Дата формирования", StringComparison.Ordinal))
                {
                    if (currentTx != null) { info.Transactions.Add(currentTx); currentTx = null; }
                    return info;
                }

                if (SkipKeywords.Any(k => full.Contains(k, StringComparison.Ordinal))) continue;

                // Transaction header: DD.MM.YYYY HH:MM  category  amount  balance
                var txMatch = TxHeaderStartRegex().Match(full);
                if (txMatch.Success)
                {
                    if (currentTx != null) info.Transactions.Add(currentTx);
                    currentTx = ParseTransactionHeader(txMatch, full);
                    awaitingProcessingLine = true;
                    continue;
                }

                // Processing line: DD.MM.YYYY AUTHCODE  description  [amount currency]
                var procMatch = TxProcessStartRegex().Match(full);
                if (procMatch.Success && currentTx != null)
                {
                    currentTx.ProcessingDate = ParseDate(procMatch.Groups[1].Value);
                    currentTx.AuthCode = procMatch.Groups[2].Value;

                    var descPart = full[procMatch.Length..].Trim();
                    ExtractTrailingOperational(descPart, currentTx);
                    awaitingProcessingLine = false;
                    continue;
                }

                // Description continuation (wrapped line)
                if (!awaitingProcessingLine && currentTx != null)
                    currentTx.Description = (currentTx.Description + " " + full).Trim();
            }
        }

        if (currentTx != null) info.Transactions.Add(currentTx);

        return info;
    }

    /// <summary>
    /// If <paramref name="descPart"/> ends with a money-and-currency pattern like "2,09 €" or "6,00 BYN",
    /// split it off into OperationalAmount/OperationalCurrency; otherwise treat the whole string as description.
    /// </summary>
    private static void ExtractTrailingOperational(string descPart, Transaction tx)
    {
        var m = TrailingOperationalRegex().Match(descPart);
        if (m.Success && LooksLikeCurrency(m.Groups[2].Value))
        {
            tx.Description = descPart[..m.Index].Trim();
            tx.OperationalAmount = ParseBalance(m.Groups[1].Value);
            tx.OperationalCurrency = m.Groups[2].Value;
        }
        else
        {
            tx.Description = descPart;
        }
    }

    private static bool LooksLikeCurrency(string token)
    {
        if (string.IsNullOrEmpty(token)) return false;
        // €, $, ₽, £, ¥ etc. — single non-ASCII-letter symbol
        if (token.Length == 1 && !char.IsLetterOrDigit(token[0])) return true;
        // 3-letter ISO code like BYN, USD, EUR
        if (token.Length == 3 && token.All(char.IsUpper)) return true;
        return false;
    }

    // ── Header ────────────────────────────────────────────────────────────────

    private static void ParseStatementHeader(List<PageLine> lines, StatementInfo info)
    {
        var text = string.Join("\n", lines.Select(l => l.FullText));

        var periodMatch = PeriodRegex().Match(text);
        if (periodMatch.Success)
        {
            info.PeriodFrom = ParseDate(periodMatch.Groups[1].Value);
            info.PeriodTo = ParseDate(periodMatch.Groups[2].Value);
        }

        foreach (Match m in BalanceLineRegex().Matches(text))
        {
            var date = ParseDate(m.Groups[1].Value);
            var amount = ParseBalance(m.Groups[2].Value);
            if (date == info.PeriodFrom) info.OpeningBalance = amount;
            else if (date == info.PeriodTo) info.ClosingBalance = amount;
        }

        var dep = DepositsRegex().Match(text);
        if (dep.Success) info.Deposits = ParseBalance(dep.Groups[1].Value);

        var wd = WithdrawalsRegex().Match(text);
        if (wd.Success) info.Withdrawals = ParseBalance(wd.Groups[1].Value);

        ParseAccountInfo(lines, info);
    }

    private static void ParseAccountInfo(List<PageLine> lines, StatementInfo info)
    {
        bool nextIsHolder = false;
        foreach (var line in lines)
        {
            var full = line.FullText.Trim();
            if (string.IsNullOrWhiteSpace(full)) continue;

            if (full.Contains("Владелец счёта"))        { nextIsHolder = true; continue; }
            if (nextIsHolder)                            { info.AccountHolder = full; nextIsHolder = false; continue; }
            if (full.StartsWith("Номер счёта"))          { info.AccountNumber = full["Номер счёта".Length..].Trim(); continue; }
            if (full.StartsWith("Карта") && !full.Contains("Дата"))
            {
                var rest = full["Карта".Length..].Trim();
                var cardMatch = CardNumberRegex().Match(rest);
                info.CardNumber = cardMatch.Success ? cardMatch.Value.Trim() : rest;
                continue;
            }
            if (full.StartsWith("Валюта"))               { info.Currency = full["Валюта".Length..].Trim(); }
        }
    }

    // ── Transaction header parsing ────────────────────────────────────────────

    private static Transaction ParseTransactionHeader(Match dateTimeMatch, string fullText)
    {
        var opDate = ParseDateTime(dateTimeMatch.Groups[1].Value, dateTimeMatch.Groups[2].Value);
        var afterDateTime = fullText[dateTimeMatch.Length..].Trim();

        var moneyMatches = MoneyRegex().Matches(afterDateTime);

        decimal balance = moneyMatches.Count >= 1 ? ParseBalance(moneyMatches[^1].Groups[2].Value) : 0;
        decimal amount = 0;
        if (moneyMatches.Count >= 2)
        {
            bool isCredit = moneyMatches[^2].Groups[1].Value == "+";
            amount = ParseBalance(moneyMatches[^2].Groups[2].Value);
            if (!isCredit) amount = -amount;
        }

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
}
