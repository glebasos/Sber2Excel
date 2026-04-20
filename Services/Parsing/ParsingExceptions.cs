using System;

namespace Sber2Excel.Services.Parsing;

public class Bank2ExcelException : Exception
{
    public Bank2ExcelException(string message) : base(message) { }
    public Bank2ExcelException(string message, Exception inner) : base(message, inner) { }
}

public class InputFileStructureException : Bank2ExcelException
{
    public InputFileStructureException(string message) : base(message) { }
}
