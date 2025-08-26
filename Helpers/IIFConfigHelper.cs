using System;
using System.Globalization;
using RaymarEquipmentInventory.DTOs;

public static class IIFConfigHelper
{
    public static IIFConfig GetIifConfig()
    {
        static string Get(string key, string fallback) =>
            Environment.GetEnvironmentVariable(key) is { Length: > 0 } v ? v : fallback;

        var cfg = new IIFConfig
        {
            // Accounts
            ArAccount = Get("IIF_AR_ACCOUNT", "Accounts Receivable"),

            // Items
            LabourItem = Get("IIF_ITEM_LABOUR", "Labour"),
            LabourOtItem = Get("IIF_ITEM_LABOUR_OT", "Labour OT"),
            TravelTimeItem = Get("IIF_ITEM_TRAVEL_TIME", "Technician Travel Time"),
            MileageItem = Get("IIF_ITEM_MILEAGE", "Mileage"),
            MiscPartItem = Get("IIF_ITEM_MISC_PART", "Misc"),
            FeeItem = Get("IIF_ITEM_FEE", "WorkOrderFee"),

            // Sales tax
            HstItem = Get("IIF_ITEM_HST", "HST 13% Only"),
            HstRate = decimal.TryParse(
                                Get("IIF_HST_RATE", "0.13"),
                                NumberStyles.Any,
                                CultureInfo.InvariantCulture,
                                out var r)
                                ? r : 0.13m
        };

        return cfg;
    }
}
