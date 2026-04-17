using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Sber2Excel.Models;
using UglyToad.PdfPig;

namespace Sber2Excel.Services.Parsing;

/// <summary>
/// Parses Sberbank debit-card statements (PDF exported from СберБанк Онлайн).
/// Fingerprint: "Выписка по счёту дебетовой карты" on page 1.
/// </summary>
public partial class SberbankDebitCardParser : PdfParserBase
{
    public override string DisplayName => "Сбербанк — дебетовая карта";

    public override bool CanParse(string firstPageText) =>
        firstPageText.Contains("Выписка по счёту дебетовой карты");

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

    // ── Main parse ────────────────────────────────────────────────────────────

    public override StatementInfo Parse(string filePath)
    {
        var info = new StatementInfo { BankName = DisplayName };

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

                // Processing line: DD.MM.YYYY AUTHCODE  description
                var procMatch = TxProcessStartRegex().Match(full);
                if (procMatch.Success && currentTx != null)
                {
                    currentTx.ProcessingDate = ParseDate(procMatch.Groups[1].Value);
                    currentTx.AuthCode = procMatch.Groups[2].Value;
                    currentTx.Description = full[procMatch.Length..].Trim();
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

    private static Transaction ParseTransactionHeader(System.Text.RegularExpressions.Match dateTimeMatch, string fullText)
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
