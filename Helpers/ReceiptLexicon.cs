using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using RaymarEquipmentInventory.Models; // RaymarInventoryDBContext + EF entities

namespace RaymarEquipmentInventory.Helpers
{
    /// <summary>
    /// DB-backed implementation of <see cref="IReceiptLexicon"/>.
    /// Reads from DI_* tables and exposes read-only collections for parsing/filtering.
    /// </summary>
    public sealed class ReceiptLexicon : IReceiptLexicon
    {
        private readonly RaymarInventoryDBContext _context;

        // In-memory caches (built once per scope)
        private readonly IReadOnlyDictionary<string, string[]> _fieldAliases;
        private readonly IReadOnlyDictionary<string, string> _vendorCategoryOverrides;

        private readonly IReadOnlyCollection<string> _restaurantItemKeywords;
        private readonly IReadOnlyCollection<string> _suppliesItemKeywords;

        private readonly IReadOnlyCollection<string> _stopWords;
        private readonly IReadOnlyCollection<string> _restaurantAllow;
        private readonly IReadOnlyCollection<string> _suppliesAllow;

        // Canadian Tire SKU (static pattern)
        private static readonly Regex s_canadianTireSkuRegex =
            new(@"(?<!\d)\d{3}-\d{4}-\d(?!\d)", RegexOptions.Compiled);

        public ReceiptLexicon(RaymarInventoryDBContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));

            // 1) Field aliases (no table — keep as code constants)
            _fieldAliases = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["MerchantName"] = new[] { "MerchantName" },
                ["MerchantAddress"] = new[] { "MerchantAddress", "Address" },
                ["ReceiptType"] = new[] { "ReceiptType", "Category" },

                // Money fields with common variations
                ["SubTotal"] = new[] { "SubTotal", "Subtotal", "Sub Total" },
                ["TotalTax"] = new[] { "TotalTax", "Tax", "Taxes", "HST", "GST", "PST" },
                ["Total"] = new[] { "Total", "Grand Total", "Amount Due" },

                ["TransactionDate"] = new[] { "TransactionDate", "Date", "Transaction Date" }
            };

            // 2) Categories lookup (Id -> Name), used everywhere
            var categories = _context.DiCategories
                .Where(c => c.IsActive == true)
                .Select(c => new { c.CategoryId, c.CategoryName })
                .AsEnumerable()
                .ToDictionary(x => x.CategoryId, x => x.CategoryName, comparer: EqualityComparer<int>.Default);

            string CatNameOr(string? fallback, int? id) =>
                (id.HasValue && categories.TryGetValue(id.Value, out var name)) ? name : (fallback ?? "Unknown");

            // 3) Vendor → Category overrides from DI_Places (+ aliases)
            //    Map both PlaceName and each Alias to the same category name.
            var placeRows = _context.DiPlaces
                .Where(p => p.IsActive == true)
                .Select(p => new { p.PlaceId, p.PlaceName, p.CategoryId })
                .ToList();

            var aliasRows = _context.DiPlaceAliases
                .Where(a => a.IsActive == true)
                .Select(a => new { a.PlaceId, a.Alias })
                .ToList();

            var vendorMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in placeRows)
            {
                var catName = CatNameOr("Unknown", p.CategoryId);
                if (!string.IsNullOrWhiteSpace(p.PlaceName))
                    vendorMap[p.PlaceName.Trim()] = catName;

                foreach (var a in aliasRows.Where(a => a.PlaceId == p.PlaceId))
                {
                    if (!string.IsNullOrWhiteSpace(a.Alias))
                        vendorMap[a.Alias.Trim()] = catName;
                }
            }
            _vendorCategoryOverrides = vendorMap;

            // Helpers to fetch keyword sets per category name
            static HashSet<string> CaseSet() => new(StringComparer.OrdinalIgnoreCase);

            HashSet<string> LoadKeywordsFrom<T>(
                IQueryable<T> queryable,
                int? categoryId,
                Func<T, string?> selector)
            {
                var set = CaseSet();
                if (categoryId is null) return set;

                foreach (var row in queryable)
                {
                    var word = selector(row);
                    if (!string.IsNullOrWhiteSpace(word)) set.Add(word.Trim());
                }
                return set;
            }

            int? CategoryIdByName(string name) =>
                categories.FirstOrDefault(kv => kv.Value.Equals(name, StringComparison.OrdinalIgnoreCase)).Key;

            var restaurantId = CategoryIdByName("Restaurant");
            var suppliesId = CategoryIdByName("Supplies");

            // 4) Stopwords
            _stopWords = _context.DiStopwords
                .Where(s => s.IsActive == true)
                .Select(s => s.Word)
                .AsEnumerable()
                .Where(w => !string.IsNullOrWhiteSpace(w))
                .Select(w => w!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            // 5) Allowed keywords by category (was RestaurantAllow / SuppliesAllow)
            _restaurantAllow = LoadKeywordsFrom(
                _context.DiAllowedKeywords.Where(k => k.IsActive == true && k.CategoryId == restaurantId),
                restaurantId,
                k => (k as dynamic).Keyword // EF type: DiAllowedKeyword
            ).ToList();

            _suppliesAllow = LoadKeywordsFrom(
                _context.DiAllowedKeywords.Where(k => k.IsActive == true && k.CategoryId == suppliesId),
                suppliesId,
                k => (k as dynamic).Keyword
            ).ToList();

            // 6) Item keywords by category (was RestaurantItemKeywords / SuppliesItemKeywords)
            _restaurantItemKeywords = LoadKeywordsFrom(
                _context.DiItemKeywords.Where(k => k.IsActive == true && k.CategoryId == restaurantId),
                restaurantId,
                k => (k as dynamic).Keyword // EF type: DiItemKeyword
            ).ToList();

            _suppliesItemKeywords = LoadKeywordsFrom(
                _context.DiItemKeywords.Where(k => k.IsActive == true && k.CategoryId == suppliesId),
                suppliesId,
                k => (k as dynamic).Keyword
            ).ToList();
        }

        // IReceiptLexicon implementation
        public IReadOnlyDictionary<string, string[]> FieldAliases => _fieldAliases;
        public IReadOnlyDictionary<string, string> VendorCategoryOverrides => _vendorCategoryOverrides;

        public IReadOnlyCollection<string> RestaurantItemKeywords => _restaurantItemKeywords;
        public IReadOnlyCollection<string> SuppliesItemKeywords => _suppliesItemKeywords;

        public IReadOnlyCollection<string> StopWords => _stopWords;
        public IReadOnlyCollection<string> RestaurantAllow => _restaurantAllow;
        public IReadOnlyCollection<string> SuppliesAllow => _suppliesAllow;

        public Regex CanadianTireSkuRegex => s_canadianTireSkuRegex;
    }
}
