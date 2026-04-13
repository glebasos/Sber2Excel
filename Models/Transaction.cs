using System;
using System.Globalization;

namespace Sber2Excel.Models;

public class Transaction
{
    public DateTime OperationDate { get; set; }
    public DateTime? ProcessingDate { get; set; }
    public string AuthCode { get; set; } = "";
    public string Category { get; set; } = "";
    public string Description { get; set; } = "";
    /// <summary>Positive = credit (income), negative = debit (expense).</summary>
    public decimal Amount { get; set; }
    public decimal Balance { get; set; }

    public bool IsCredit => Amount >= 0;

    // Pre-formatted strings used by the DataGrid — avoids StringFormat in XAML
    // which causes a binding feedback loop on Linux (Avalonia X11).
    public string OperationDateStr => OperationDate.ToString("dd.MM.yyyy HH:mm");
    public string ProcessingDateStr => ProcessingDate?.ToString("dd.MM.yyyy") ?? "";
    public string AmountStr => Amount.ToString("N2", CultureInfo.CurrentCulture);
    public string BalanceStr => Balance.ToString("N2", CultureInfo.CurrentCulture);
}
