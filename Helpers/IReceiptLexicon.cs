using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace RaymarEquipmentInventory.Helpers
{
    /// <summary>
    /// Provides static/readonly receipt vocabularies and patterns used by parsing, filtering, and categorization.
    /// Swap this out later (e.g., DB-backed) without touching the parsing code.
    /// </summary>
    public interface IReceiptLexicon
    {
        IReadOnlyDictionary<string, string[]> FieldAliases { get; }
        IReadOnlyDictionary<string, string> VendorCategoryOverrides { get; }

        IReadOnlyCollection<string> RestaurantItemKeywords { get; }
        IReadOnlyCollection<string> SuppliesItemKeywords { get; }

        IReadOnlyCollection<string> StopWords { get; }
        IReadOnlyCollection<string> RestaurantAllow { get; }
        IReadOnlyCollection<string> SuppliesAllow { get; }

        Regex CanadianTireSkuRegex { get; }
    }
}

