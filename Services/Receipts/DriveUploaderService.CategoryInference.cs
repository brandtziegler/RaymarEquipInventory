using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace RaymarEquipmentInventory.Services
{
    public partial class DriveUploaderService
    {
        private bool TryFindVendorOverride(string merchant, out string category)
        {
            foreach (var kvp in _lex.VendorCategoryOverrides)
            {
                if (merchant.Contains(kvp.Key, StringComparison.OrdinalIgnoreCase))
                {
                    category = kvp.Value;
                    return true;
                }
            }
            category = "";
            return false;
        }

        private string GuessTypeFromBrandOrVendor(string brand, string merchant)
        {
            if (!string.IsNullOrWhiteSpace(merchant) &&
                _lex.VendorCategoryOverrides.TryGetValue(merchant, out var cat))
                return cat;

            if (!string.IsNullOrEmpty(brand) && !brand.Equals("Debit", StringComparison.OrdinalIgnoreCase))
                return "CardPayment";

            return "Supplies";
        }

        private string InferCategory(string merchant, IEnumerable<string> itemDescriptions)
        {
            // 1) Vendor override wins
            if (!string.IsNullOrWhiteSpace(merchant) &&
                _lex.VendorCategoryOverrides.TryGetValue(merchant, out var cat))
                return cat;

            // 2) Look at items for strong signals
            var flat = string.Join(" ", itemDescriptions).ToLowerInvariant();
            if (_lex.RestaurantItemKeywords.Any(k => flat.Contains(k))) return "Restaurant";
            if (_lex.SuppliesItemKeywords.Any(k => flat.Contains(k))) return "Supplies";

            // 3) Merchant name hints
            if (Regex.IsMatch(merchant ?? "", @"\b(GAS|FUEL|PETRO|ESSO|SHELL|CO-OP)\b", RegexOptions.IgnoreCase))
                return "Fuel";
            if (Regex.IsMatch(merchant ?? "", @"\b(PART|AUTO|TIRE|SUPPLY|TOOLS?)\b", RegexOptions.IgnoreCase))
                return "Supplies";
            if (Regex.IsMatch(merchant ?? "", @"\b(CAFE|COFFEE|GRILL|BURRITO|PIZZA|SUB|DINER|PUB|BAR)\b", RegexOptions.IgnoreCase))
                return "Restaurant";

            return "Unknown";
        }

        private static bool NearlyEqual(decimal a, decimal b, decimal eps = 0.01m)
            => Math.Abs(a - b) <= eps;
    }
}


