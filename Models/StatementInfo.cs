using System;
using System.Collections.Generic;

namespace Sber2Excel.Models;

public class StatementInfo
{
    public string BankName { get; set; } = "";
    public string AccountHolder { get; set; } = "";
    public string AccountNumber { get; set; } = "";
    public string CardNumber { get; set; } = "";
    public string Currency { get; set; } = "";
    public DateTime PeriodFrom { get; set; }
    public DateTime PeriodTo { get; set; }
    public decimal OpeningBalance { get; set; }
    public decimal Deposits { get; set; }
    public decimal Withdrawals { get; set; }
    public decimal ClosingBalance { get; set; }
    public List<Transaction> Transactions { get; set; } = new();
}
