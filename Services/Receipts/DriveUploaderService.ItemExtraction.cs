using System;
using Azure.AI.DocumentIntelligence;

namespace RaymarEquipmentInventory.Services
{
    public partial class DriveUploaderService
    {
        private bool TryGetFieldContentCI(AnalyzedDocument? doc, string canonicalName, out string content)
        {
            content = "";
            if (doc?.Fields == null) return false;

            // 1) Exact key (any casing) — short-circuit on first non-empty content
            if (doc.Fields.TryGetValue(canonicalName, out DocumentField? f) && f != null)
            {
                content = f.Content ?? "";
                if (!string.IsNullOrWhiteSpace(content)) return true;
            }

            // 2) Aliases from the lexicon
            if (_lex.FieldAliases.TryGetValue(canonicalName, out var aliases))
            {
                foreach (var alias in aliases)
                {
                    if (doc.Fields.TryGetValue(alias, out DocumentField? fa) && fa != null)
                    {
                        content = fa.Content ?? "";
                        if (!string.IsNullOrWhiteSpace(content)) return true;
                    }
                }
            }

            // 3) Last chance: scan keys case-insensitively (handles odd spacing/casing)
            foreach (var kvp in doc.Fields)
            {
                if (string.Equals(kvp.Key, canonicalName, StringComparison.OrdinalIgnoreCase))
                {
                    content = kvp.Value?.Content ?? "";
                    return !string.IsNullOrWhiteSpace(content);
                }
            }

            return false;
        }

        private string GetFieldContentCI(AnalyzedDocument? doc, string canonicalName)
        {
            return TryGetFieldContentCI(doc, canonicalName, out var c) ? c.Trim() : "";
        }

        private static bool TryGetFieldContent(AnalyzedDocument? doc, string fieldName, out string content)
        {
            content = "";
            if (doc == null || doc.Fields == null) return false;
            if (doc.Fields.TryGetValue(fieldName, out DocumentField? f) && f != null)
            {
                content = f.Content ?? "";
                return !string.IsNullOrWhiteSpace(content);
            }
            return false;
        }

        private static string GetFieldContent(AnalyzedDocument? doc, string fieldName)
            => TryGetFieldContent(doc, fieldName, out var c) ? c.Trim() : "";
    }
}
