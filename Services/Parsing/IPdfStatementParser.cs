using Sber2Excel.Models;

namespace Sber2Excel.Services.Parsing;

/// <summary>
/// Implemented by each bank/account-type-specific PDF parser.
/// To add a new format: create a class that implements this interface
/// and register it in <see cref="PdfParserFactory"/>.
/// </summary>
public interface IPdfStatementParser
{
    /// <summary>Human-readable name shown in status/error messages, e.g. "Сбербанк — дебетовая карта".</summary>
    string DisplayName { get; }

    /// <summary>
    /// Quick check: returns true if this parser recognises the PDF.
    /// Receives the concatenated text of the first page — should only scan for a
    /// unique fingerprint string, not do full parsing.
    /// </summary>
    bool CanParse(string firstPageText);

    /// <summary>Parses the entire file and returns a populated <see cref="StatementInfo"/>.</summary>
    StatementInfo Parse(string filePath);

    /// <summary>Parses from in-memory bytes (used in browser where there is no file path).</summary>
    StatementInfo Parse(byte[] pdfBytes);
}
