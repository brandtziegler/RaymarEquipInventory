using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Azure.AI.DocumentIntelligence;

namespace RaymarEquipmentInventory.Services
{
    public partial class DriveUploaderService
    {
        // Updated filter: if category = Supplies and the line CONTAINS a CT SKU, allow it.
        private List<string> FilterItemsByCategory(IEnumerable<string> rawItems, string category)
        {
            IEnumerable<string> allow =
                category.Equals("Restaurant", StringComparison.OrdinalIgnoreCase)
                    ? _lex.RestaurantAllow
                    : category.Equals("Supplies", StringComparison.OrdinalIgnoreCase)
                        ? _lex.SuppliesAllow
                        : Array.Empty<string>();

            var filtered = new List<string>();

            foreach (var line in rawItems)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var t = line.Trim();

                // Always keep lines that contain a CT-style SKU when Supplies
                if (category.Equals("Supplies", StringComparison.OrdinalIgnoreCase) &&
                    _lex.CanadianTireSkuRegex.IsMatch(t))
                {
                    filtered.Add(t);
                    continue;
                }

                // Remove obvious non-item boilerplate
                if (_lex.StopWords.Any(sw => Regex.IsMatch(t, $@"\b{Regex.Escape(sw)}\b", RegexOptions.IgnoreCase)))
                    continue;

                // Category allow-list
                if (allow.Any() &&
                    !allow.Any(k => Regex.IsMatch(t, $@"\b{Regex.Escape(k)}\b", RegexOptions.IgnoreCase)))
                    continue;

                filtered.Add(t);
            }

            return filtered.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        // Updated extractor: optional SKU prefix, then desc, then price.
        // We'll keep "SKU + desc" if SKU is present, otherwise just desc.
        private static List<string> ExtractItems(AnalyzedDocument? doc, string rawContent)
        {
            var items = new List<string>();
            if (string.IsNullOrWhiteSpace(rawContent)) return items;

            var rx = new Regex(
                @"(?mi)^\s*
              (?!.*\b(sub\s*total|subtotal|total\s*tax|hst|gst|pst|tax|total|grand\s*total|
                      mastercard|visa|debit|change|cash|tender|auth|approval)\b)
              (?:(?<sku>\d{3}-\d{4}-\d)\s+)?         # optional CT SKU prefix
              (?<desc>[A-Za-z][\w\-\s/()#&']{2,80}?) # description-ish
              \s+\$?\s*\d{1,5}(?:[.,]\d{2})\s*$      # price
            ",
                RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.IgnorePatternWhitespace);

            foreach (Match m in rx.Matches(rawContent))
            {
                var sku = m.Groups["sku"]?.Value?.Trim() ?? "";
                var desc = m.Groups["desc"]?.Value?.Trim() ?? "";
                if (string.IsNullOrWhiteSpace(desc)) continue;

                var line = string.IsNullOrEmpty(sku) ? desc : $"{sku} {desc}";
                line = Regex.Replace(line, @"\s+", " ").Trim();

                if (line.Length > 2 && line.Length <= 120)
                    items.Add(line);
            }

            return items
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }
}
