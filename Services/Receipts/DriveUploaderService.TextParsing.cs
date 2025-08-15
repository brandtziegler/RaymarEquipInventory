using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace RaymarEquipmentInventory.Services
{
    public partial class DriveUploaderService
    {
        private static string ExtractCityFromAddressSmart(string address)
        {
            if (string.IsNullOrWhiteSpace(address)) return "";

            // Try “..., CITY, PROV ...”
            var m = Regex.Match(address, @"^[^\n,]+,\s*([A-Za-z][A-Za-z\s'\.-]+?),\s*(?:ON|QC|BC|AB|SK|MB|NS|NB|NL|PE|YT|NT|NU)\b",
                                RegexOptions.Multiline | RegexOptions.IgnoreCase);
            if (m.Success) return m.Groups[1].Value.Trim();

            // Try line starting with "City"
            var m2 = Regex.Match(address, @"(?mi)^\s*City\s*[:\-]?\s*([A-Za-z][A-Za-z\s'\.-]+)$");
            if (m2.Success) return m2.Groups[1].Value.Trim();

            // Fallback: take the word before province code
            var m3 = Regex.Match(address, @"\b([A-Za-z][A-Za-z\s'\.-]+)\s*,\s*(ON|QC|BC|AB|SK|MB|NS|NB|NL|PE|YT|NT|NU)\b",
                                 RegexOptions.IgnoreCase);
            if (m3.Success) return m3.Groups[1].Value.Trim();

            return "";
        }

        private static string ExtractCityFromAddress(string address)
        {
            if (string.IsNullOrWhiteSpace(address)) return "";
            // Common “Street, CITY, PROV …” pattern; grab the middle token
            var m = Regex.Match(address, @"^[^\n,]+,\s*([A-Za-z\s'\.-]+),\s*[A-Za-z]{2,}", RegexOptions.Multiline);
            if (m.Success) return m.Groups[1].Value.Trim();
            // Fallback: pick the ALLCAPS-ish word that isn’t ON/AB/BC/etc.
            var caps = Regex.Matches(address, @"\b[A-Z][A-Z'\-]+(?:\s+[A-Z'\-]+)*\b");
            foreach (Match c in caps)
            {
                var t = c.Value.Trim();
                if (!Regex.IsMatch(t, @"^(ON|QC|BC|AB|SK|MB|NS|NB|NL|PE|YT|NT|NU|CA|CANADA)$")) return t;
            }
            return "";
        }

        private static string ExtractMaskedLast4Strict(string content, out string brandGuess)
        {
            brandGuess = "";
            if (string.IsNullOrWhiteSpace(content)) return "";

            // Exactly 12 asterisks followed by 4 digits (allow spaces between groups of asterisks)
            var rx = new Regex(@"\*{4}\s*\*{4}\s*\*{4}\s*(\d{4})\b");
            var m = rx.Match(content);
            string last4 = m.Success ? m.Groups[1].Value : "";

            if (Regex.IsMatch(content, @"MASTERCARD|MASTER CARD|MC\b", RegexOptions.IgnoreCase)) brandGuess = "Mastercard";
            else if (Regex.IsMatch(content, @"\bVISA\b", RegexOptions.IgnoreCase)) brandGuess = "Visa";
            else if (Regex.IsMatch(content, @"AMEX|AMERICAN EXPRESS", RegexOptions.IgnoreCase)) brandGuess = "Amex";
            else if (Regex.IsMatch(content, @"\bDEBIT\b", RegexOptions.IgnoreCase)) brandGuess = "Debit";

            return string.IsNullOrEmpty(last4) ? "" : $"**** **** **** {last4}";
        }

        private static string ExtractMaskedLast4(string content, out string brandGuess)
        {
            brandGuess = "";
            if (string.IsNullOrWhiteSpace(content)) return "";

            // Catch **** 4434, XXXX-XXXX-XXXX-4434, •••• 4434, etc.
            var masked = Regex.Match(content, @"(?:[*xX•]\s*){4,}\s*[-\s]*\d{4}");
            string last4 = "";
            if (masked.Success)
            {
                var m4 = Regex.Match(masked.Value, @"(\d{4})\b");
                if (m4.Success) last4 = m4.Groups[1].Value;
            }

            if (Regex.IsMatch(content, @"MASTERCARD|MASTER CARD|MC\b", RegexOptions.IgnoreCase)) brandGuess = "Mastercard";
            else if (Regex.IsMatch(content, @"\bVISA\b", RegexOptions.IgnoreCase)) brandGuess = "Visa";
            else if (Regex.IsMatch(content, @"AMEX|AMERICAN EXPRESS", RegexOptions.IgnoreCase)) brandGuess = "Amex";
            else if (Regex.IsMatch(content, @"\bDEBIT\b", RegexOptions.IgnoreCase)) brandGuess = "Debit";

            return string.IsNullOrEmpty(last4) ? "" : $"**** **** **** {last4}";
        }

        private static decimal ParseCurrency(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return 0m;
            // Remove currency symbols/labels like "CAD", "$", "Total:", spaces
            var clean = Regex.Replace(s, @"(?i)\bCAD\b|USD|TOTAL|SUBTOTAL|AMOUNT|HST|GST|PST|TAX|[:]", "")
                             .Replace("$", "")
                             .Trim();
            // Keep digits, minus, dot, comma; normalize comma-decimal
            clean = Regex.Replace(clean, @"[^\d\-,.]", "");
            // If both comma and dot, assume comma is thousands sep
            if (clean.Contains(",") && clean.Contains("."))
                clean = clean.Replace(",", "");
            else if (clean.Count(c => c == ',') == 1 && !clean.Contains("."))
                clean = clean.Replace(',', '.');

            return decimal.TryParse(clean, NumberStyles.Number | NumberStyles.AllowCurrencySymbol,
                                    CultureInfo.InvariantCulture, out var val) ? val : 0m;
        }

        private static DateTime? ParseDate(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            var formats = new[]
            {
                "yyyy-MM-dd","yyyy/MM/dd","MM/dd/yyyy","dd/MM/yyyy","yyyy-MM-ddTHH:mm:ss",
                "yyyy/MM/dd HH:mm","MM-dd-yyyy","dd-MMM-yyyy","MMM dd, yyyy","yyyyMMdd"
            };
            if (DateTime.TryParseExact(s.Trim(), formats, CultureInfo.InvariantCulture,
                                       DateTimeStyles.AssumeLocal, out var dt)) return dt;
            if (DateTime.TryParse(s, CultureInfo.CurrentCulture, DateTimeStyles.AssumeLocal, out dt)) return dt;
            return null;
        }

        private static string NormalizeMerchant(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "";
            var up = Regex.Replace(s, @"[^\p{L}\p{N}\s]", " ").ToUpperInvariant(); // drop punctuation
            up = Regex.Replace(up, @"\s+", " ").Trim();
            return up;
        }
    }
}

