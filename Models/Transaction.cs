using System;

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
}
