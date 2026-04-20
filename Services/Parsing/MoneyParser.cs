using System.Globalization;

namespace Sber2Excel.Services.Parsing;

/// <summary>
/// Port of Python utils.get_decimal_from_money.
/// Parses Russian-formatted money strings like "1 189,40" or "+21 107,75" into decimal.
/// </summary>
public static class MoneyParser
{
    /// <param name="treatNoSignAsNegative">
    /// If true, numbers without an explicit '+' are negated. Used for debit-card "amount" columns
    /// where expenses are shown unsigned and income is prefixed with '+'.
    /// </param>
    public static decimal GetDecimal(string moneyStr, bool treatNoSignAsNegative = false)
    {
        if (string.IsNullOrEmpty(moneyStr))
            throw new InputFileStructureException("Empty money string");

        // Strip all whitespace (regular space + NBSP) and normalise decimal separator.
        var cleaned = moneyStr
            .Replace(" ", "")
            .Replace("\u00A0", "")
            .Replace('\u2212', '-') // Unicode minus
            .Replace(',', '.')
            .Trim();

        bool leadingPlus = cleaned.Length > 0 && cleaned[0] == '+';

        if (!decimal.TryParse(cleaned, NumberStyles.Any, CultureInfo.InvariantCulture, out var value))
            throw new InputFileStructureException($"Cannot parse money value: '{moneyStr}'");

        if (treatNoSignAsNegative && !leadingPlus)
            value = -value;

        return value;
    }
}
