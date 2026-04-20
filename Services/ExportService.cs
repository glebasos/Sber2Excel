using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using ClosedXML.Excel;
using Sber2Excel.Models;

namespace Sber2Excel.Services;

public class ExportService
{
    public void ExportCsv(string filePath, StatementInfo info)
    {
        using var stream = File.Create(filePath);
        ExportCsv(stream, info);
    }

    public void ExportCsv(Stream stream, StatementInfo info)
    {
        using var writer = new StreamWriter(stream,
            encoding: new UTF8Encoding(encoderShouldEmitUTF8Identifier: true),
            leaveOpen: true);

        bool includeOperational = info.Transactions.Any(t => !string.IsNullOrEmpty(t.OperationalCurrency));
        bool includeSavings = info.Transactions.Any(t =>
            !string.IsNullOrEmpty(t.Code) || !string.IsNullOrEmpty(t.OffsettingAccount) || !string.IsNullOrEmpty(t.DocumentNumber));

        var headers = new List<string>
        {
            "Дата операции", "Дата обработки", "Код авторизации",
            "Категория", "Описание", "Сумма", "Остаток",
        };
        if (includeOperational) { headers.Add("Сумма в валюте операции"); headers.Add("Валюта операции"); }
        if (includeSavings) { headers.Add("Шифр"); headers.Add("К/с"); headers.Add("№ документа"); }

        writer.WriteLine(string.Join(";", headers));

        foreach (var tx in info.Transactions)
        {
            var row = new List<string>
            {
                Escape(tx.OperationDate.ToString("dd.MM.yyyy HH:mm")),
                Escape(tx.ProcessingDate?.ToString("dd.MM.yyyy") ?? ""),
                Escape(tx.AuthCode),
                Escape(tx.Category),
                Escape(tx.Description),
                tx.Amount.ToString("N2", CultureInfo.InvariantCulture),
                tx.Balance.ToString("N2", CultureInfo.InvariantCulture),
            };
            if (includeOperational)
            {
                row.Add(tx.OperationalAmount?.ToString("N2", CultureInfo.InvariantCulture) ?? "");
                row.Add(Escape(tx.OperationalCurrency));
            }
            if (includeSavings)
            {
                row.Add(Escape(tx.Code));
                row.Add(Escape(tx.OffsettingAccount));
                row.Add(Escape(tx.DocumentNumber));
            }
            writer.WriteLine(string.Join(";", row));
        }
    }

    public void ExportXlsx(string filePath, StatementInfo info)
    {
        using var stream = File.Create(filePath);
        ExportXlsx(stream, info);
    }

    public void ExportXlsx(Stream stream, StatementInfo info)
    {
        using var workbook = new XLWorkbook();
        AddInfoSheet(workbook, info);
        AddTransactionsSheet(workbook, info);
        workbook.SaveAs(stream);
    }

    private static string Escape(string value)
    {
        if (value.Contains(';') || value.Contains('"') || value.Contains('\n'))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
    }

    private static void AddInfoSheet(XLWorkbook wb, StatementInfo info)
    {
        var ws = wb.Worksheets.Add("Информация");

        (string Label, string Value)[] rows =
        [
            ("Владелец счёта",    info.AccountHolder),
            ("Номер счёта",       info.AccountNumber),
            ("Карта",             info.CardNumber),
            ("Валюта",            info.Currency),
            ("Период с",          info.PeriodFrom.ToString("dd.MM.yyyy")),
            ("Период по",         info.PeriodTo.ToString("dd.MM.yyyy")),
            ("Остаток на начало", info.OpeningBalance.ToString("N2", CultureInfo.InvariantCulture)),
            ("Пополнение",        info.Deposits.ToString("N2", CultureInfo.InvariantCulture)),
            ("Списание",          info.Withdrawals.ToString("N2", CultureInfo.InvariantCulture)),
            ("Остаток на конец",  info.ClosingBalance.ToString("N2", CultureInfo.InvariantCulture)),
            ("Кол-во операций",   info.Transactions.Count.ToString()),
        ];

        for (int i = 0; i < rows.Length; i++)
        {
            ws.Cell(i + 1, 1).Value = rows[i].Label;
            ws.Cell(i + 1, 2).Value = rows[i].Value;
            ws.Cell(i + 1, 1).Style.Font.Bold = true;
        }

        ws.Column(1).AdjustToContents();
        ws.Column(2).AdjustToContents();
    }

    private static void AddTransactionsSheet(XLWorkbook wb, StatementInfo info)
    {
        var ws = wb.Worksheets.Add("Операции");

        bool includeOperational = info.Transactions.Any(t => !string.IsNullOrEmpty(t.OperationalCurrency));
        bool includeSavings = info.Transactions.Any(t =>
            !string.IsNullOrEmpty(t.Code) || !string.IsNullOrEmpty(t.OffsettingAccount) || !string.IsNullOrEmpty(t.DocumentNumber));

        var headers = new List<string>
        {
            "Дата операции", "Дата обработки", "Код авторизации",
            "Категория", "Описание", "Сумма", "Остаток",
        };
        if (includeOperational) { headers.Add("Сумма в валюте операции"); headers.Add("Валюта операции"); }
        if (includeSavings) { headers.Add("Шифр"); headers.Add("К/с"); headers.Add("№ документа"); }

        for (int c = 0; c < headers.Count; c++)
        {
            var cell = ws.Cell(1, c + 1);
            cell.Value = headers[c];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#F2F2F2");
        }

        ws.SheetView.FreezeRows(1);

        for (int r = 0; r < info.Transactions.Count; r++)
        {
            var tx = info.Transactions[r];
            int row = r + 2;

            ws.Cell(row, 1).Value = tx.OperationDate;
            ws.Cell(row, 1).Style.DateFormat.Format = "dd.mm.yyyy hh:mm";

            if (tx.ProcessingDate.HasValue)
            {
                ws.Cell(row, 2).Value = tx.ProcessingDate.Value;
                ws.Cell(row, 2).Style.DateFormat.Format = "dd.mm.yyyy";
            }

            ws.Cell(row, 3).Value = tx.AuthCode;
            ws.Cell(row, 4).Value = tx.Category;
            ws.Cell(row, 5).Value = tx.Description;

            ws.Cell(row, 6).Value = tx.Amount;
            ws.Cell(row, 6).Style.NumberFormat.Format = "#,##0.00";
            ws.Cell(row, 6).Style.Font.FontColor = tx.IsCredit
                ? XLColor.FromHtml("#1A7F37")
                : XLColor.FromHtml("#CF1322");

            ws.Cell(row, 7).Value = tx.Balance;
            ws.Cell(row, 7).Style.NumberFormat.Format = "#,##0.00";

            int col = 8;
            if (includeOperational)
            {
                if (tx.OperationalAmount.HasValue)
                {
                    ws.Cell(row, col).Value = tx.OperationalAmount.Value;
                    ws.Cell(row, col).Style.NumberFormat.Format = "#,##0.00";
                }
                col++;
                ws.Cell(row, col++).Value = tx.OperationalCurrency;
            }
            if (includeSavings)
            {
                ws.Cell(row, col++).Value = tx.Code;
                ws.Cell(row, col++).Value = tx.OffsettingAccount;
                ws.Cell(row, col++).Value = tx.DocumentNumber;
            }
        }

        for (int c = 1; c <= headers.Count; c++)
            ws.Column(c).AdjustToContents();
        ws.Column(5).Width = 50;
    }
}
