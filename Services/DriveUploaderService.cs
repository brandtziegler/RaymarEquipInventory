using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Threading.Tasks;
using RaymarEquipmentInventory.Models;
using RaymarEquipmentInventory.DTOs;
using System.Data.Odbc;
using System.Data;
using System.Reflection.PortableExecutable;
using RaymarEquipmentInventory.Helpers;
using Serilog;
using Microsoft.AspNetCore.Server.IISIntegration;
using Google.Apis.Drive.v3;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Newtonsoft.Json;
using Google;
using System.Text;
using Google.Apis.Download;
using Microsoft.Data.SqlClient;
using Google.Apis.Upload;
using Google.Apis.Drive.v3.Data; // 👈 This is where `Permission` lives
using Google.Cloud.Iam.Credentials.V1;
using Google.Protobuf.WellKnownTypes;
using Grpc.Auth;
using System.Diagnostics;
using Microsoft.SqlServer.Dac;  // DacFx (ExportBacpac)


using System.Globalization;
using System.Text.RegularExpressions;
using Azure;
using Azure.AI.DocumentIntelligence;        // new SDK
using CsvHelper;                             // for CSV (step 7 later)
using SixLabors.ImageSharp;                  // image load/resize
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;


namespace RaymarEquipmentInventory.Services
{
    public class DriveUploaderService : IDriveUploaderService
    {
        private readonly IQuickBooksConnectionService _quickBooksConnectionService;
        private readonly RaymarInventoryDBContext _context;
        private readonly IDriveAuthService _authService;
        private readonly IConfiguration _config;
        public DriveUploaderService(IQuickBooksConnectionService quickBooksConnectionService, RaymarInventoryDBContext context, IDriveAuthService authService, IConfiguration config)
        {
            _quickBooksConnectionService = quickBooksConnectionService;
            _context = context;
            _authService = authService;
            _config = config;
        }



        ///START OF BLOB SUBMISSION CODE.

        public string GenerateBatchId()
        {
            // compact, sortable-ish, globally unique
            return $"{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}";
        }

        // Image/receipt helpers
        private static bool IsImage(IFormFile file)
        {
            var ct = file.ContentType?.ToLowerInvariant() ?? "";
            if (ct.StartsWith("image/")) return true;

            var ext = Path.GetExtension(file.FileName)?.ToLowerInvariant();
            return ext is ".jpg" or ".jpeg" or ".png" or ".heic" or ".webp";
        }

        // Your rule: receipt images are prefixed with 25_
        private static bool IsReceiptImage(string fileName) =>
            fileName.StartsWith("25_", StringComparison.OrdinalIgnoreCase);

        // PDF helper
        private static bool IsPdf(IFormFile file)
        {
            var ct = file.ContentType?.ToLowerInvariant() ?? "";
            if (ct == "application/pdf" || ct.Contains("pdf")) return true;

            var ext = Path.GetExtension(file.FileName)?.ToLowerInvariant();
            return ext == ".pdf";
        }

        private (string container, string blobPrefix) DecideBlobRoutingCore(
            IFormFile file,
            string workOrderId,
            string batchId)
        {
            var fileName = file.FileName ?? string.Empty;

            // Containers from config, with safe fallbacks
            var receiptContainer = Environment.GetEnvironmentVariable("BlobContainer_Receipts");
            var partsContainer = Environment.GetEnvironmentVariable("BlobContainer_Parts");
            var pdfContainer = Environment.GetEnvironmentVariable("BlobContainer_PDFs");

            string container;

            if (IsPdf(file))
            {
                container = pdfContainer;
            }
            else if (IsImage(file) && IsReceiptImage(fileName))
            {
                container = receiptContainer;
            }
            else
            {
                container = partsContainer;
            }

            // <container>/<workOrderId>/<batchId>/<originalFileName>
            // (container is passed separately to the SDK; blob name is the prefix below)
            var blobPrefix = $"{workOrderId}/{batchId}/{fileName}";

            return (container, blobPrefix);
        }

        // DTOs used only for planning/preview
        public record PlannedBlob(string FileName, string Container, string BlobPath);
        public record UploadPlan(
    string BatchId,
    string WorkOrderId,
    string WorkOrderFolderId,
    string ImagesFolderId,
    string PdfFolderId,
    List<PlannedBlob> Files);


        public UploadPlan PlanBlobRouting(
            List<IFormFile> files,
            string workOrderId,
            string workOrderFolderId,
            string imagesFolderId,
            string pdfFolderId,
            string? batchId = null)
        {
            var id = string.IsNullOrWhiteSpace(batchId) ? GenerateBatchId() : batchId;
            var results = new List<PlannedBlob>(files?.Count ?? 0);

            foreach (var f in files ?? Enumerable.Empty<IFormFile>())
            {
                var (container, blobPath) = DecideBlobRoutingCore(f, workOrderId, id);
                results.Add(new PlannedBlob(f.FileName, container, blobPath));
            }

            return new UploadPlan(id, workOrderId, workOrderFolderId, imagesFolderId, pdfFolderId, results);
        }

        public async Task UploadFileToBlobAsync(
    string containerName,
    string blobPath,
    IFormFile file,
    CancellationToken ct = default)
        {
            try
            {
                var connStr = Environment.GetEnvironmentVariable("AZURE_STORAGE_CONNECTION_STRING");
                var containerClient = new BlobContainerClient(connStr, containerName);

                await containerClient.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: ct);

                var blobClient = containerClient.GetBlobClient(blobPath);

                using var stream = file.OpenReadStream();
                await blobClient.UploadAsync(stream, new BlobHttpHeaders
                {
                    ContentType = file.ContentType
                }, cancellationToken: ct);

                Log.Information($"✅ Uploaded '{blobPath}' to container '{containerName}'.");
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"🔥 Failed to upload '{blobPath}' to container '{containerName}'.");
                throw;
            }
        }
        ///END OF BLOB SUBMISSION CODE.
        ///START OF DOCUMENT INTELLIGENCE CODE
        private DocumentIntelligenceClient CreateDocIntelClient()
        {
            var endpoint = Environment.GetEnvironmentVariable("AZURE_FORMRECOGNIZER_ENDPOINT");
            var key = Environment.GetEnvironmentVariable("AZURE_FORMRECOGNIZER_KEY");
            return new DocumentIntelligenceClient(new Uri(endpoint), new AzureKeyCredential(key));
        }


        ///START OF DOCUMENT INTELLIGENCE CODE

        // Map aliases you want to support regardless of casing/spaces
        private static readonly Dictionary<string, string[]> FieldAliases = new(StringComparer.OrdinalIgnoreCase)
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

        private static readonly Dictionary<string, string> VendorCategoryOverrides =
    new(StringComparer.OrdinalIgnoreCase)
    {
        // Supplies / Parts chains
        ["CANADIAN TIRE"] = "Supplies",
        ["CANADIAN TIRE GAS"] = "Fuel",
        ["PRINCESS AUTO"] = "Supplies",
        ["HOME DEPOT"] = "Supplies",
        ["HOME HARDWARE"] = "Supplies",
        ["LOWE'S"] = "Supplies",
        ["RONA"] = "Supplies",
        ["ACKLANDS"] = "Supplies",
        ["FASTENAL"] = "Supplies",
        ["GRAINGER"] = "Supplies",
        ["NAPA"] = "Supplies",
        ["PARTSOURCE"] = "Supplies",
        ["MARK'S"] = "Supplies",
        ["PEAVEY"] = "Supplies",
        ["BOLT SUPPLY"] = "Supplies",

        // Fuel
        ["ESSO"] = "Fuel",
        ["SHELL"] = "Fuel",
        ["PETRO-CANADA"] = "Fuel",
        ["HUSKY"] = "Fuel",
        ["CO-OP GAS"] = "Fuel",

        // Restaurants / coffee chains
        ["TIM HORTONS"] = "Restaurant",
        ["MCDONALD"] = "Restaurant",
        ["SUBWAY"] = "Restaurant",
        ["STARBUCKS"] = "Restaurant",
        ["BARBURRITO"] = "Restaurant",
        ["A&W"] = "Restaurant",
        ["BURGER KING"] = "Restaurant",
        ["WENDY"] = "Restaurant",
        ["DAIRY QUEEN"] = "Restaurant",
    };

        private static readonly string[] RestaurantItemKeywords =
{
    "burger","burrito","wrap","taco","fries","poutine","coffee","latte","tea",
    "pop","soda","drink","combo","nugget","chicken","sandwich","sub","salad","pizza",
    "soup","donut","muffin","cookie"
};

        private static readonly string[] SuppliesItemKeywords =
        {
    "bolt","screw","hose","valve","fitting","clamp","gasket","filter","sensor",
    "seal","plug","brake","bearing","pipe","coupler","nozzle","o-ring","adhesive"
};

        // --- Item filtering dictionaries (keep small; tune over time) ---
        private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
{
    "reprint","qty","quantity","description","total","subtotal","grand","tax","hst","gst","pst",
    "auth","approval","approved","change","tender","merchant","card","mastercard","visa","debit",
    "balance","thank","survey","copy","customer","invoice","receipt","order","payment"
};

        private static readonly HashSet<string> RestaurantAllow = new(StringComparer.OrdinalIgnoreCase)
{
    // --- Original core
    "burger","burrito","wrap","taco","fries","poutine","coffee","latte","tea","drink","pop","soda",
    "combo","nugget","chicken","sandwich","sub","salad","pizza","soup","donut","muffin","cookie",
    "breakfast","rice","beans","sauce",

    // Short forms / slang
    "burg","chk","chx","sammie","wich","sub","pie","za","pza","bkfst","bfast","brkfst","cof","lat","cap",
    "esp","mt","mtl","spag","las","alf","alfdo","tend","nugg","nugs","ff","fr","pou","sou","don","muf",
    "ckie","brwnie","bcuit","crois","bag","toa","panck","waff","crep","omlt","scrmb","hbrn","cer","yog",
    "smth","mshake","icrm","gela","sorb","brwn","ck","pud","cust","ccake","truf","eclr",

    // Expanded fast food & casual dining
    "steak","stk","ribs","pork","prk","fish","fsh","seafood","sf","shrimp","shp","lobster","lob","crab","crb",
    "clam","clm","oyster","oys","calamari","cal","pasta","pas","spaghetti","spag","lasagna","las",
    "fettuccine","fett","alfredo","alf","meatball","mb","veal","sausage","saus","brisket","brsk",
    "hotdog","hdog","dog","kebab","kb","gyro","gr","shawarma","shaw","falafel","fala","hummus","hum",
    "naan","nan","curry","cur","pad thai","pth","ramen","ram","udon","pho","sushi","sus","sashimi","sash",
    "nigiri","nig","tempura","temp","dumpling","dump","spring roll","spr","egg roll","eggr",

    // Bakery & dessert
    "biscuit","bis","croissant","crois","bagel","bag","toast","toa","pancake","pan","waffle","waf",
    "crepe","crep","omelette","oml","scramble","scrmb","hashbrown","hbrn","cereal","cer","yogurt","yog",
    "parfait","parf","smoothie","smth","milkshake","mshake","ice cream","icrm","gelato","gela","sorbet","sorb",
    "brownie","brwn","cake","ck","pie","p","tart","trt","cupcake","cck","pudding","pud","custard","cust",
    "cheesecake","ccake","truffle","truf","eclair","eclr",

    // Beverages & bar
    "beer","br","lager","ale","ipa","stout","sto","cider","cid","wine","red wine","white wine","rose","ros",
    "whiskey","whisk","vodka","vod","rum","rm","tequila","teq","gin","gn","cocktail","ctail","martini","mart",
    "margarita","marg","mojito","moj","cola","col","root beer","rb","ginger ale","ga","lemonade","lem","juice",
    "cranberry","cran","orange juice","oj","apple juice","aj","grape juice","gj"
};

        private static readonly HashSet<string> SuppliesAllow = new(StringComparer.OrdinalIgnoreCase)
{
    // --- Original core
    "bolt","screw","hose","valve","fitting","clamp","gasket","filter","sensor","seal","plug","brake",
    "bearing","pipe","coupler","nozzle","o-ring","adhesive","tape","epoxy","blade","bit","battery",
    "cable","wire","connector","fuse","cleaner","solvent","gloves",

    // Short forms / slang
    "blt","scr","hs","vlv","fit","clp","gsk","flt","sen","sl","plg","brk","bear","pip","cpl","nzl","orng",
    "adh","tp","epx","bld","bt","bat","cbl","wir","conn","fus","cln","solv","glv",

    // Hardware & fasteners
    "nut","nt","washer","wshr","lag bolt","lb","anchor","anch","nail","nl","rivet","riv","stud","std",
    "bracket","brkt","hinge","hng","latch","ltch","lock","lk","chain","chn","rope","rp","cord","crd",
    "twine","twn","strap","strp","bungee","bng","zip tie","zt","clip","clp","spring","spr","gear","gr",
    "pulley","ply","winch","wnch","hook","hk","eyelet","eylt","shim","shm","spacer","spc","grub screw","gs",
    "set screw","ss",

    // Tools & equipment
    "drill","dr","saw","sw","hammer","hmmr","wrench","wrn","ratchet","rtch","socket","skt","pliers","plr",
    "cutter","ctr","snips","snp","level","lvl","tape measure","tm","square","sq","chisel","chl","file","fl",
    "sander","sndr","router","rtr","planer","plnr","grinder","grnd","torch","trch","welder","wldr",
    "solder","sldr","multimeter","mm","gauge","gag","caliper","cal","vise","vs","clamp meter","cm",

    // Automotive & shop supplies
    "oil","ol","grease","grs","lubricant","lube","coolant","clnt","antifreeze","anti","belt","blt",
    "chain lube","chlube","spark plug","sp","air filter","af","fuel filter","ff","oil filter","of","shock",
    "shk","strut","str","spring","spr","axle","axl","hub","hb","driveshaft","ds","u-joint","uj","tie rod","tr",
    "bushing","bsh","control arm","ca","rotor","rtr","pad","pd","drum","drm","caliper","cal","sensor","sen",
    "relay","rly","switch","swt","harness","hrn","grommet","grm","clip","clp","trim","trm","fastener kit","fk",

    // Electrical & misc
    "breaker","brk","outlet","otl","switch plate","sp","junction box","jb","conduit","cnd","wire nut","wn",
    "terminal","trmnl","lug","lg","shrink tube","stb","sleeve","slv","ferrule","frl","crimp","crm","panel","pnl",
    "faceplate","fp","transformer","trns","adapter","adp","charger","chrgr","inverter","inv","power supply","ps",
    "light","lt","bulb","blb","led","tube","tb","fixture","fx","ballast","blst"
};

        // Allow partial matches (SKU anywhere in the line)
        private static readonly Regex CanadianTireSkuRegex =
            new(@"(?<!\d)\d{3}-\d{4}-\d(?!\d)", RegexOptions.Compiled);



        // Updated filter: if category = Supplies and the line CONTAINS a CT SKU, allow it.
        private static List<string> FilterItemsByCategory(IEnumerable<string> rawItems, string category)
        {
            IEnumerable<string> allow = category.Equals("Restaurant", StringComparison.OrdinalIgnoreCase)
                ? RestaurantAllow
                : category.Equals("Supplies", StringComparison.OrdinalIgnoreCase)
                    ? SuppliesAllow
                    : Array.Empty<string>();

            var filtered = new List<string>();

            foreach (var line in rawItems)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var t = line.Trim();

                // Always keep lines that contain a CT-style SKU when Supplies
                if (category.Equals("Supplies", StringComparison.OrdinalIgnoreCase) &&
                    CanadianTireSkuRegex.IsMatch(t))
                {
                    filtered.Add(t);
                    continue;
                }

                // Remove obvious non-item boilerplate
                if (StopWords.Any(sw => Regex.IsMatch(t, $@"\b{Regex.Escape(sw)}\b", RegexOptions.IgnoreCase)))
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


        private static bool TryGetFieldContentCI(AnalyzedDocument? doc, string canonicalName, out string content)
        {
            content = "";
            if (doc?.Fields == null) return false;

            // 1) Exact key (any casing)
            if (doc.Fields.TryGetValue(canonicalName, out DocumentField? f) && f != null)
            {
                content = f.Content ?? "";
                return !string.IsNullOrWhiteSpace(content);
            }

            // 2) Aliases
            if (FieldAliases.TryGetValue(canonicalName, out var aliases))
            {
                foreach (var alias in aliases)
                {
                    if (doc.Fields.TryGetValue(alias, out var fa) && fa != null)
                    {
                        content = fa.Content ?? "";
                        if (!string.IsNullOrWhiteSpace(content)) return true;
                    }
                }
            }

            // 3) Last chance: scan keys case-insensitively (handles odd spacing)
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

        private static string GetFieldContentCI(AnalyzedDocument? doc, string canonicalName)
            => TryGetFieldContentCI(doc, canonicalName, out var c) ? c.Trim() : "";



        private static async Task<MemoryStream> BuildCsvAsync(List<ReceiptCsvRow> rows, string fileName, CancellationToken ct)
        {
            var ms = new MemoryStream();
            await using var writer = new StreamWriter(ms, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), leaveOpen: true);
            using var csv = new CsvWriter(writer, CultureInfo.InvariantCulture);
            await csv.WriteRecordsAsync(rows, ct);
            await writer.FlushAsync();
            ms.Position = 0;
            return ms;
        }

        private string GetModelId() => Environment.GetEnvironmentVariable("AZURE_FORMRECOGNIZER_MODEL");

        private static async Task RespectRateAsync(DateTime lastCallUtc, int minMsBetweenCalls, CancellationToken ct)
        {
            if (lastCallUtc == DateTime.MinValue) return;
            var elapsed = (int)(DateTime.UtcNow - lastCallUtc).TotalMilliseconds;
            var wait = minMsBetweenCalls - elapsed;
            if (wait > 0) await Task.Delay(wait, ct);
        }

        // --- Generic field helpers -------------------------
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

        private static string NormalizeMerchant(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "";
            var up = Regex.Replace(s, @"[^\p{L}\p{N}\s]", " ").ToUpperInvariant(); // drop punctuation
            up = Regex.Replace(up, @"\s+", " ").Trim();
            return up;
        }

        private static bool TryFindVendorOverride(string merchant, out string category)
        {
            foreach (var kvp in VendorCategoryOverrides)
            {
                if (merchant.Contains(kvp.Key, StringComparison.OrdinalIgnoreCase))
                { category = kvp.Value; return true; }
            }
            category = "";
            return false;
        }

        private static string GetFieldContent(AnalyzedDocument? doc, string fieldName)
            => TryGetFieldContent(doc, fieldName, out var c) ? c.Trim() : "";

        // --- Parsing helpers --------------------------------
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

        private static string GuessTypeFromBrandOrVendor(string brand, string merchant)
        {
            if (!string.IsNullOrWhiteSpace(merchant) &&
                VendorCategoryOverrides.TryGetValue(merchant, out var cat))
                return cat;

            if (!string.IsNullOrEmpty(brand) && !brand.Equals("Debit", StringComparison.OrdinalIgnoreCase))
                return "CardPayment";

            return "Supplies";
        }
        private static bool NearlyEqual(decimal a, decimal b, decimal eps = 0.01m)
            => Math.Abs(a - b) <= eps;

        private static string InferCategory(string merchant, IEnumerable<string> itemDescriptions)
        {
            // 1) Vendor override wins
            if (!string.IsNullOrWhiteSpace(merchant) &&
                VendorCategoryOverrides.TryGetValue(merchant, out var cat))
                return cat;

            // 2) Look at items for strong signals
            var flat = string.Join(" ", itemDescriptions).ToLowerInvariant();
            if (RestaurantItemKeywords.Any(k => flat.Contains(k))) return "Restaurant";
            if (SuppliesItemKeywords.Any(k => flat.Contains(k))) return "Supplies";

            // 3) Merchant name hints
            if (Regex.IsMatch(merchant ?? "", @"\b(GAS|FUEL|PETRO|ESSO|SHELL|CO-OP)\b", RegexOptions.IgnoreCase))
                return "Fuel";
            if (Regex.IsMatch(merchant ?? "", @"\b(PART|AUTO|TIRE|SUPPLY|TOOLS?)\b", RegexOptions.IgnoreCase))
                return "Supplies";
            if (Regex.IsMatch(merchant ?? "", @"\b(CAFE|COFFEE|GRILL|BURRITO|PIZZA|SUB|DINER|PUB|BAR)\b", RegexOptions.IgnoreCase))
                return "Restaurant";

            return "Unknown";
        }


        private static ReceiptCsvRow MapParsedToCsvRow(AnalyzeResult result, out bool needsReview)
        {
            var doc = result.Documents.FirstOrDefault();
            var raw = result.Content ?? string.Empty;
            needsReview = false;

            // --- Basic fields -------------------------------------------------------
            var merchantRaw = GetFieldContentCI(doc, "MerchantName");
            // Normalize to help vendor overrides like "CANADIAN TIRE #123"
            string NormalizeMerchant(string? s)
            {
                if (string.IsNullOrWhiteSpace(s)) return "";
                var up = Regex.Replace(s, @"[^\p{L}\p{N}\s]", " ").ToUpperInvariant();
                return Regex.Replace(up, @"\s+", " ").Trim();
            }
            var merchant = NormalizeMerchant(merchantRaw);

            var address = GetFieldContentCI(doc, "MerchantAddress");
            var city = ExtractCityFromAddressSmart(address);
            var typedByModel = GetFieldContentCI(doc, "ReceiptType"); // may be flaky

            var subTotal = ParseCurrency(GetFieldContentCI(doc, "SubTotal"));
            var totalTax = ParseCurrency(GetFieldContentCI(doc, "TotalTax"));
            var total = ParseCurrency(GetFieldContentCI(doc, "Total"));
            var txDate = ParseDate(GetFieldContentCI(doc, "TransactionDate"));

            // Derive subtotal if missing (common case when only Total/Tax are present)
            if (subTotal == 0 && total > 0 && totalTax >= 0)
            {
                var computed = total - totalTax;
                if (computed > 0) subTotal = computed;
            }

            // Strict masked last-4 (only when "************1234" style is present)
            var last4 = ExtractMaskedLast4Strict(raw, out _);

            // --- Items (structured or fallback “desc + price” lines) ----------------
            var items = ExtractItems(doc, raw);

            // --- Decide category, then filter items by category ---------------------
            // 1) Try vendor override using contains-match to handle store numbers etc.
            string? overrideCat = null;
            foreach (var kvp in VendorCategoryOverrides)
            {
                if (merchant.IndexOf(kvp.Key, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    overrideCat = kvp.Value;
                    break;
                }
            }

            // 2) If no override, use your heuristics (items + merchant hints)
            var inferred = overrideCat ?? InferCategory(merchant, items);

            // 3) Fallback to model’s type only if our inference is Unknown/empty
            var finalType = string.IsNullOrWhiteSpace(inferred) || inferred.Equals("Unknown", StringComparison.OrdinalIgnoreCase)
                            ? (string.IsNullOrWhiteSpace(typedByModel) ? "Unknown" : typedByModel)
                            : inferred;

            // 4) Now filter noisy lines based on the decided category (allow/stop lists)
            var filteredItems = FilterItemsByCategory(items, finalType);
            var itemsJoined = string.Join(", ", filteredItems);

            // --- Review heuristics ---------------------------------------------------
            if (string.IsNullOrWhiteSpace(merchant) || total <= 0) needsReview = true;
            if (subTotal > 0 && totalTax == 0 && !NearlyEqual(total, subTotal)) needsReview = true;
            if (subTotal > 0 && totalTax > 0 && !NearlyEqual(subTotal + totalTax, total)) needsReview = true;

            return new ReceiptCsvRow
            {
                MerchantName = merchantRaw, // preserve original casing in CSV
                City = city,
                ReceiptType = finalType,
                SubTotal = subTotal,
                TotalTax = totalTax,
                Total = total,
                TransactionDate = txDate,
                CardNumberMasked = last4,
                Items = itemsJoined
            };
        }




        private static readonly JpegEncoder Jpeg85 = new JpegEncoder { Quality = 85 };

        private static async Task<MemoryStream> NormalizeImageAsync(IFormFile file, CancellationToken ct)
        {
            // Read original bytes
            using var inStream = new MemoryStream();
            await file.CopyToAsync(inStream, ct);
            inStream.Position = 0;

            // Optionally detect HEIC and convert (requires HEIC plugin)
            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            // if (ext == ".heic") { /* decode with plugin -> Image */ }

            // Load with ImageSharp
            inStream.Position = 0;
            using var image = await Image.LoadAsync(inStream, ct);

            // Heuristic resize: if width > 2000px, scale down
            const int targetMaxWidth = 2000;
            if (image.Width > targetMaxWidth)
            {
                var targetHeight = (int)Math.Round(image.Height * (targetMaxWidth / (double)image.Width));
                image.Mutate(x => x.Resize(targetMaxWidth, targetHeight));
            }

            // Save JPEG @ ~85%
            var outStream = new MemoryStream();
            await image.SaveAsJpegAsync(outStream, Jpeg85, ct);
            outStream.Position = 0;

            // If still > 4 MB (rare), you can reduce quality to ~80 or 75 and re-save.
            return outStream;
        }


        private static async Task<AnalyzeResult> AnalyzeReceiptAsync(
            DocumentIntelligenceClient client,
            string modelId,
            Stream imageStream,
            CancellationToken ct)
        {
            var op = await client.AnalyzeDocumentAsync(
    WaitUntil.Completed,
    modelId,
    BinaryData.FromStream(imageStream),
    cancellationToken: ct);
            return op.Value;
        }
        public async Task<ReceiptConfirm> ParseReceiptsBuildCsvAsync(List<IFormFile> files, CancellationToken ct = default)
        {
            var confirm = new ReceiptConfirm
            {
                BatchId = Guid.NewGuid().ToString("N"),
                ReceivedCount = files.Count,
                CsvFileName = $"Receipts_{DateTime.UtcNow:yyyyMMdd_HHmmss}.csv"
            };

            var client = CreateDocIntelClient();
            var modelId = GetModelId();

            // Throttle to respect F0: <= 20 calls/min. Simple: 1 at a time, ~3s spacing.
            const int minMsBetweenCalls = 3100;

            var rows = new List<ReceiptCsvRow>();
            var lastCall = DateTime.MinValue;

            foreach (var file in files)
            {
                try
                {
                    // 3) Normalize (HEIC->JPG, resize/compress if > 4MB)
                    await RespectRateAsync(lastCall, minMsBetweenCalls, ct);
                    using var normalized = await NormalizeImageAsync(file, ct);

                    // 4) Call Document Intelligence
                    lastCall = DateTime.UtcNow;
                    var parsed = await AnalyzeReceiptAsync(client, modelId, normalized, ct);

                    // 5) Post-process into one CSV row
                    var row = MapParsedToCsvRow(parsed, out bool needsReview);
                    if (needsReview) confirm.NeedsReviewCount++;
                    rows.Add(row);
                    confirm.ProcessedCount++;
                }
                catch (Exception ex)
                {
                    confirm.Errors.Add($"{file.FileName}: {ex.Message}");
                }
            }

            // (You’ll generate CSV in step 7; for now just compute size when you write it)
            using var csvStream = await BuildCsvAsync(rows, confirm.CsvFileName, ct);
            confirm.CsvSizeBytes = csvStream.Length;

            // TODO: email or return csvStream to caller depending on your controller design
            // (You can stash csvStream in temp storage or return as FileStreamResult)

            return confirm;
        }


        // in DriveUploaderService
        public async Task<(MemoryStream Csv, ReceiptConfirm Confirm)> ParseReceiptsAndReturnCsvAsync(List<IFormFile> files, CancellationToken ct = default)
        {
            var confirm = new ReceiptConfirm
            {
                BatchId = Guid.NewGuid().ToString("N"),
                ReceivedCount = files.Count,
                CsvFileName = $"Receipts_{DateTime.UtcNow:yyyyMMdd_HHmmss}.csv"
            };

            var client = CreateDocIntelClient();
            var modelId = GetModelId();

            const int minMsBetweenCalls = 3100;
            var rows = new List<ReceiptCsvRow>();
            var lastCall = DateTime.MinValue;

            foreach (var file in files)
            {
                try
                {
                    await RespectRateAsync(lastCall, minMsBetweenCalls, ct);
                    using var normalized = await NormalizeImageAsync(file, ct);

                    lastCall = DateTime.UtcNow;
                    var parsed = await AnalyzeReceiptAsync(client, modelId, normalized, ct);

                    var row = MapParsedToCsvRow(parsed, out bool needsReview);
                    if (needsReview) confirm.NeedsReviewCount++;
                    rows.Add(row);
                    confirm.ProcessedCount++;
                }
                catch (Exception ex)
                {
                    confirm.Errors.Add($"{file.FileName}: {ex.Message}");
                }
            }

            var csvStream = await BuildCsvAsync(rows, confirm.CsvFileName, ct); // <- DO NOT dispose
            confirm.CsvSizeBytes = csvStream.Length;

            return (csvStream, confirm);
        }


        ///END  OF DOCUMENT INTELLIGENCE CODE
        public async Task<List<DTOs.FileMetadata>> ListFileUrlsAsync(int sheetId, int? labourTypeId, List<string> tags)
        {
            try
            {
                Log.Information($"Machine UTC Time: {DateTime.UtcNow:O}");
                Log.Information($"Machine Local Time: {DateTime.Now:O}");

                var driveService = await _authService.GetDriveServiceFromUserTokenAsync();

                var listRequest = driveService.Files.List();
                var templatesFolderId = _config["GoogleDrive:TemplatesFolderId"]
                    ?? throw new InvalidOperationException("Missing config: GoogleDrive:TemplatesFolderId");

                listRequest.Q = $"'{templatesFolderId}' in parents and trashed=false and mimeType != 'application/vnd.google-apps.folder'";
                listRequest.Fields = "files(id,name,description,modifiedTime,lastModifyingUser(displayName),mimeType,webContentLink,webViewLink)";

                var result = await listRequest.ExecuteAsync();
                if (result.Files == null || result.Files.Count == 0)
                {
                    Log.Information("No files found in the TechPDFs folder.");
                    return new List<DTOs.FileMetadata>();
                }

                var localZone = TimeZoneInfo.Local;

                var templates = result.Files.Select(file => new DTOs.FileMetadata
                {
                    Id = file.Id,
                    PDFName = file.Name,
                    fileDescription = file.Description ?? string.Empty,
                    dateLastEdited = file.ModifiedTimeDateTimeOffset.HasValue
                        ? TimeZoneInfo.ConvertTime(file.ModifiedTimeDateTimeOffset.Value, localZone).ToString("o")
                        : string.Empty,
                    lastEditTechName = file.LastModifyingUser?.DisplayName ?? "Unknown",
                    MimeType = file.MimeType,
                    WebContentLink = file.WebContentLink,
                    WebViewLink = file.WebViewLink,
                    sheetId = sheetId
                }).ToList();

                // 🧩 Step 1: Filter by LabourTypeID
                if (labourTypeId.HasValue)
                {
                    var matchingFileNames = await _context.Pdftags
                        .Where(p => p.LabourTypeId == labourTypeId.Value)
                        .Select(p => p.FileName)
                        .ToListAsync();

                    templates = templates
                        .Where(t => matchingFileNames.Contains(t.PDFName, StringComparer.OrdinalIgnoreCase))
                        .ToList();
                }

                // 🧩 Step 2: Filter by tags parsed from Categories: before Notes:
                if (tags != null && tags.Any())
                {
                    templates = templates.Where(t =>
                    {
                        var description = t.fileDescription ?? string.Empty;
                        string categoriesPart = string.Empty;

                        var startIndex = description.IndexOf("Categories:", StringComparison.OrdinalIgnoreCase);
                        if (startIndex >= 0)
                        {
                            var afterCategories = description.Substring(startIndex + "Categories:".Length);

                            // ✅ Fix: strip off any trailing Notes: section, even if no comma
                            var notesIndex = afterCategories.IndexOf("Notes:", StringComparison.OrdinalIgnoreCase);
                            if (notesIndex >= 0)
                                afterCategories = afterCategories.Substring(0, notesIndex);

                            categoriesPart = afterCategories.Trim();
                        }

                        if (string.IsNullOrWhiteSpace(categoriesPart))
                            return false;

                        var fileTags = categoriesPart.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                        return fileTags.Any(tag => tags.Contains(tag, StringComparer.OrdinalIgnoreCase));
                    }).ToList();
                }

                // 🧩 Step 3: Merge filled file data for sheetId
                var filledDocs = await _context.Pdfdocuments
                    .Where(p => p.SheetId == sheetId)
                    .ToListAsync();

                if (sheetId != 0)
                {
                    foreach (var filled in filledDocs)
                    {
                        var match = templates.FirstOrDefault(t => t.PDFName == filled.FileName);
                        if (match != null)
                        {
                            match.WebContentLink = filled.FileUrl;
                            match.WebViewLink = filled.FileUrl;
                            match.fileDescription = string.IsNullOrWhiteSpace(filled.Description)
                                ? match.fileDescription
                                : filled.Description;
                            match.dateLastEdited = filled.UploadDate.ToString("o");
                            match.lastEditTechName = filled.UploadedBy ?? match.lastEditTechName;
                        }
                    }
                }

                return templates;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error occurred while listing files");
                throw;
            }
        }

        public async Task UpdateFolderIdsInPDFDocumentAsync(
            string fileName,
            string extension,
            string workOrderId,
            string workOrderFolderId,
            string pdfFolderId,
            string blobPath,
            CancellationToken ct = default)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(fileName))
                {
                    Log.Warning("⚠️ Skipping PDF folder-id update — fileName is empty.");
                    return;
                }

                if (string.IsNullOrWhiteSpace(workOrderId))
                {
                    Log.Warning("⚠️ Skipping PDF folder-id update — workOrderId is empty.");
                    return;
                }

                if (!int.TryParse(workOrderId, out var woNumber))
                {
                    Log.Warning($"⚠️ workOrderId '{workOrderId}' is not a valid integer WorkOrderNumber.");
                    return;
                }

                var sheetId = await _context.WorkOrderSheets
                    .Where(w => w.WorkOrderNumber == woNumber)
                    .Select(w => (int?)w.SheetId)
                    .FirstOrDefaultAsync(ct);

                if (sheetId is null)
                {
                    Log.Warning($"⚠️ No SheetID found for WorkOrderNumber: {workOrderId}. Skipping '{fileName}'.");
                    return;
                }

                var doc = await _context.Pdfdocuments
                    .Where(p => p.FileName == fileName && p.SheetId == sheetId)
                    .OrderByDescending(p => p.PdfdocumentId)
                    .FirstOrDefaultAsync(ct);

                if (doc is null)
                {
                    Log.Warning($"⚠️ No PDFDocument found for '{fileName}' tied to SheetID: {sheetId}.");
                    return;
                }

                doc.WorkOrderFolderId = workOrderFolderId;
                doc.PdffolderId = pdfFolderId;
                doc.AzureBlobPath = blobPath;

                await _context.SaveChangesAsync(ct);

                Log.Information($"📄 Stamped folder IDs and blobPath for PDF '{fileName}' (WO#: {workOrderId})");
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"🔥 Exception while updating PDF folder IDs for '{fileName}' (WO#: {workOrderId})");
            }
        }



        public async Task UpdateFileUrlInPDFDocumentAsync(PDFUploadRequest request)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request.FileId))
                {
                    Log.Warning($"⚠️ Skipping update for '{request.FileName}' — fileId is null or empty.");
                    return;
                }

                string downloadUrl = $"https://drive.google.com/uc?export=download&id={request.FileId}";

                var sheetId = await _context.WorkOrderSheets
                    .Where(w => w.WorkOrderNumber == Convert.ToInt32(request.WorkOrderId))
                    .Select(w => (int?)w.SheetId)
                    .FirstOrDefaultAsync();

                if (sheetId == null)
                {
                    Log.Warning($"⚠️ No SheetID found for WorkOrderNumber: {request.WorkOrderId}. Skipping update for '{request.FileName}'.");
                    return;
                }

                var existingPdf = await _context.Pdfdocuments
                    .FirstOrDefaultAsync(p => p.FileName == request.FileName && p.SheetId == sheetId);

                if (existingPdf != null)
                {
                    existingPdf.FileUrl = downloadUrl;
                    existingPdf.UploadedBy = request.UploadedBy;
                    existingPdf.Description = request.Description;
                    existingPdf.UploadDate = DateTime.UtcNow;
                    existingPdf.DriveFileId = request.FileId;

                    Log.Information($"🔁 Updated existing PDFDocument for '{request.FileName}' (SheetID: {sheetId})");
                }
                else
                {
                    _context.Pdfdocuments.Add(new Pdfdocument
                    {
                        SheetId = sheetId.Value,
                        FileName = request.FileName,
                        FileUrl = downloadUrl,
                        UploadedBy = request.UploadedBy,
                        Description = request.Description,
                        UploadDate = DateTime.UtcNow,
                        DriveFileId = request.FileId
                    });

                    Log.Information($"➕ Inserted new PDFDocument for '{request.FileName}' (SheetID: {sheetId})");
                }

                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"🔥 Exception while updating PDFDocument for '{request.FileName}' (WO#: {request.WorkOrderId})");
            }
        }

        public async Task UpdateFolderIdsInPartsDocumentAsync(
            string fileName,
            string extension,
            string workOrderId,
            string workOrderFolderId,
            string expenseFolderId,
            string imagesFolderId,
            string blobPath,
            CancellationToken ct = default)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(fileName))
                {
                    Log.Warning("⚠️ Skipping folder-id update — fileName is empty.");
                    return;
                }

                if (string.IsNullOrWhiteSpace(workOrderId))
                {
                    Log.Warning("⚠️ Skipping folder-id update — workOrderId is empty.");
                    return;
                }

                if (!int.TryParse(workOrderId, out var woNumber))
                {
                    Log.Warning($"⚠️ workOrderId '{workOrderId}' is not a valid integer WorkOrderNumber.");
                    return;
                }

                var sheetId = await _context.WorkOrderSheets
                    .Where(w => w.WorkOrderNumber == woNumber)
                    .Select(w => (int?)w.SheetId)
                    .FirstOrDefaultAsync(ct);

                if (sheetId is null)
                {
                    Log.Warning($"⚠️ No SheetID found for WorkOrderNumber: {workOrderId}. Skipping '{fileName}'.");
                    return;
                }

                var partUsedIds = await _context.PartsUseds
                    .Where(p => p.SheetId == sheetId.Value)
                    .Select(p => p.PartUsedId)
                    .ToListAsync(ct);

                if (partUsedIds.Count == 0)
                {
                    Log.Warning($"⚠️ No PartsUseds found for SheetID: {sheetId}. Skipping '{fileName}'.");
                    return;
                }

                var doc = await _context.PartsDocuments
                    .Where(p => p.FileName == fileName && partUsedIds.Contains(p.PartUsedId))
                    .OrderByDescending(p => p.PartsDocumentId)
                    .FirstOrDefaultAsync(ct);

                if (doc is null)
                {
                    Log.Warning($"⚠️ No PartsDocument found for '{fileName}' tied to SheetID: {sheetId}.");
                    return;
                }

                doc.WorkOrderFolderId = workOrderFolderId;
                doc.ExpensesFolderId = expenseFolderId;
                if (extension is ".jpg" or ".jpeg" or ".png")
                doc.ImagesFolderId = imagesFolderId;
            
                doc.AzureBlobPath = blobPath;

                await _context.SaveChangesAsync(ct);

                Log.Information($"📁 Stamped folder IDs and blobPath for '{fileName}' (WO#: {workOrderId})");
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"🔥 Exception while updating folder IDs for '{fileName}' (WO#: {workOrderId})");
            }
        }



        public async Task UpdateFileUrlInPartsDocumentAsync(string fileName, string fileId, string extension, string workOrderId)
        {
            try
            {
               
                if (string.IsNullOrWhiteSpace(fileId))
                {
                    Log.Warning($"⚠️ Skipping update for '{fileName}' — fileId is null or empty.");
                    return;
                }

                if (extension is not (".jpg" or ".jpeg" or ".png"))
                {
                    Log.Warning($"⚠️ Skipping update for '{fileName}' — unsupported extension '{extension}'.");
                    return;
                }

                string downloadUrl = $"https://drive.google.com/uc?export=download&id={fileId}";

                // Get SheetID from WorkOrderNumber
                var sheetId = await _context.WorkOrderSheets
                    .Where(w => w.WorkOrderNumber == Convert.ToInt32(workOrderId))
                    .Select(w => (int?)w.SheetId)
                    .FirstOrDefaultAsync();

                if (sheetId == null)
                {
                    Log.Warning($"⚠️ No SheetID found for WorkOrderNumber: {workOrderId}. Skipping update for '{fileName}'.");
                    return;
                }

                // Get all PartUsedIds for this sheet
                var partUsedIds = await _context.PartsUseds
                    .Where(p => p.SheetId == sheetId.Value)
                    .Select(p => p.PartUsedId)
                    .ToListAsync();

                // Find matching PartsDocument
                var doc = await _context.PartsDocuments
                    .FirstOrDefaultAsync(p => p.FileName == fileName && partUsedIds.Contains(p.PartUsedId));

                if (doc == null)
                {
                    Log.Warning($"⚠️ No matching PartsDocument found for '{fileName}' tied to SheetID: {sheetId}");
                    return;
                }

                doc.FileUrl = downloadUrl;
                await _context.SaveChangesAsync();

                Log.Information($"🖼️ Updated FileUrl for '{fileName}' → (SheetID: {sheetId})");
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"🔥 Exception occurred while updating FileUrl for '{fileName}' (WO#: {workOrderId})");
            }
        }
        public async Task ClearImageFolderAsync(string custPath, string workOrderId)
        {
            var driveService = await _authService.GetDriveServiceFromUserTokenAsync();

            Log.Information("🧭 Resolving folder path for image cleanup...");

            string rootFolderId = _config["GoogleDrive:RootFolderId"]
            ?? throw new InvalidOperationException("Missing required config: GoogleDrive:RootFolderId");

            string[] pathSegments = custPath.Split('>');
            string? currentParentId = rootFolderId;

            foreach (var segment in pathSegments)
            {
                currentParentId = await TryResolveFolderAsync(segment.Trim(), currentParentId, driveService);
                if (currentParentId == null)
                {
                    Log.Warning($"❌ Folder segment '{segment}' not found. Aborting clear.");
                    return;
                }
            }

            var workOrderFolderId = await TryResolveFolderAsync(workOrderId, currentParentId, driveService);
            if (workOrderFolderId == null)
            {
                Log.Warning($"❌ Work order folder '{workOrderId}' not found. Nothing to clear.");
                return;
            }

            var imagesFolderId = await TryResolveFolderAsync("Images", workOrderFolderId, driveService);
            if (imagesFolderId == null)
            {
                Log.Warning("📭 'Images' folder not found. Nothing to clear.");
                return;
            }

            Log.Information("🧼 Fetching files in 'Images' folder for deletion...");

            var listRequest = driveService.Files.List();
            listRequest.Q = $"'{imagesFolderId}' in parents and trashed = false";
            listRequest.Fields = "files(id, name, owners)";
            var fileList = await listRequest.ExecuteAsync();

            if (fileList.Files.Count == 0)
            {
                Log.Information("📭 No files found to delete in 'Images' folder.");
                return;
            }

            foreach (var file in fileList.Files)
            {
                try
                {
                    var ownerEmail = file.Owners?.FirstOrDefault()?.EmailAddress ?? "unknown";

                    // ⚠️ With OAuth, you may not need to check this anymore.
                    // But if you're still enforcing only-your-files deletion:
                    //if (!ownerEmail.Contains("raymarequip@gmail.com"))
                    //{
                    //    Log.Warning($"🚫 Skipping file not owned by authorized user: {file.Name} (Owner: {ownerEmail})");
                    //    continue;
                    //}

                    await driveService.Files.Delete(file.Id).ExecuteAsync();
                    Log.Information($"🗑️ Deleted image file: {file.Name} (ID: {file.Id})");
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, $"⚠️ Failed to delete image file: {file.Name}");
                }
            }

            Log.Information($"✅ Image folder cleanup complete. {fileList.Files.Count} file(s) processed.");
        }


        public async Task<DTOs.GoogleDriveFolderDTO> PrepareGoogleDriveFoldersAsync(string custPath, string workOrderId)
        {
            //Prepare Google Drive folders...
            var debugLog = new List<string>();
            try
            {
                debugLog.Add($"📁 Preparing Google Drive folders for {custPath} → WorkOrder {workOrderId}");
                debugLog.Add($"⏱ Machine UTC: {DateTime.UtcNow:O}");
                debugLog.Add($"🧠 Local Time: {DateTime.Now:O}");

                debugLog.Add("🔐 Using OAuth2 encrypted token via DriveAuthService");

                var driveService = await _authService.GetDriveServiceFromUserTokenAsync();

                debugLog.Add("🚀 DriveService initialized");

                string rootFolderId = _config["GoogleDrive:RootFolderId"]
                 ?? throw new InvalidOperationException("Missing required config: GoogleDrive:RootFolderId");
                string[] pathSegments = custPath.Split('>');
                string currentParentId = rootFolderId;

                foreach (var segment in pathSegments)
                {
                    debugLog.Add($"📂 EnsureFolderExists: {segment.Trim()} under {currentParentId}");
                    currentParentId = await EnsureFolderExistsAsync(segment.Trim(), currentParentId, driveService);
                }

                await driveService.Permissions.Create(new Permission
                {
                    Type = "user",
                    Role = "writer",
                    EmailAddress = _config["GoogleDrive:SharedEmail"]
                }, currentParentId).ExecuteAsync();

                debugLog.Add($"👥 Granted 'writer' to email.");

                string workOrderFolderId = await EnsureFolderExistsAsync(workOrderId, currentParentId, driveService);
                debugLog.Add($"📁 WorkOrder folder created: {workOrderFolderId}");

                string pdfFolderId = await EnsureFolderExistsAsync("PDFs", workOrderFolderId, driveService);
                debugLog.Add($"📁 PDFs folder created: {pdfFolderId}");

                string imagesFolderId = await EnsureFolderExistsAsync("Images", workOrderFolderId, driveService);
                debugLog.Add($"📁 Images folder created: {imagesFolderId}");


                string expenseTrackingFolderId = await EnsureFolderExistsAsync("Expense Tracking", workOrderFolderId, driveService);
                debugLog.Add($"📁 Expense Tracking folder created: {expenseTrackingFolderId}");

                return new GoogleDriveFolderDTO
                {
                    WorkOrderFolderId = workOrderFolderId,
                    PdfFolderId = pdfFolderId,
                    ImagesFolderId = imagesFolderId,
                    ExpenseTrackingFolderId = expenseTrackingFolderId,
                    stupidLogErrors = debugLog
                };
            }
            catch (Exception ex)
            {
                debugLog.Add($"💥 EXCEPTION: {ex.Message}");
                debugLog.Add($"🧵 STACK TRACE: {ex.StackTrace}");
                Log.Error(ex, "❌ Error in PrepareGoogleDriveFoldersAsync.");

                return new GoogleDriveFolderDTO
                {
                    WorkOrderFolderId = "",
                    PdfFolderId = "",
                    ImagesFolderId = "",
                    ExpenseTrackingFolderId = "",
                    stupidLogErrors = debugLog,
                    HasCriticalError = true
                };
            }
        }
     

        private static string GetMimeTypeFromExtension(string filename)
        {
            var ext = Path.GetExtension(filename).ToLowerInvariant();
            return ext switch
            {
                ".pdf" => "application/pdf",
                ".png" => "image/png",
                ".jpg" => "image/jpeg",
                ".jpeg" => "image/jpeg",
                _ => "application/octet-stream"
            };
        }

        public async Task<List<FileUpload>> UploadFilesAsync(
      List<IFormFile> files,
      string workOrderId,
      string workOrderFolderId,
      string pdfFolderId,
      string imagesFolderId)
        {
            var newUploads = new List<FileUpload>();
            var encFilePath = Path.Combine(AppContext.BaseDirectory, "raymaroutgoing.json.enc");

            try
            {
                Log.Information($"📦 Uploading {files.Count} file(s) to Google Drive...");

                var driveService = await _authService.GetDriveServiceFromUserTokenAsync();

                foreach (var file in files)
                {
                    var logDump = new FileUpload
                    {
                        FileName = file.FileName,
                        Extension = Path.GetExtension(file.FileName)?.ToLower() ?? "",
                        WorkOrderId = workOrderId,
                        stupidLogErrors = new List<string>()
                    };

                    try
                    {
                        string ext = logDump.Extension;
                        string targetFolderId = ext switch
                        {
                            ".pdf" => pdfFolderId,
                            ".jpg" or ".jpeg" or ".png" => imagesFolderId,
                            _ => workOrderFolderId
                        };

                        logDump.stupidLogErrors.Add($"📦 Routing to folder ID: {targetFolderId}");

                        using var stream = file.OpenReadStream();
                        logDump.stupidLogErrors.Add($"📄 File: {file.FileName} | Length: {file.Length}");

                        if (ext == ".pdf")
                        {
                            var checkExisting = driveService.Files.List();
                            checkExisting.Q = $"name = '{file.FileName}' and '{targetFolderId}' in parents and trashed = false";
                            checkExisting.Fields = "files(id, name)";
                            var existing = await checkExisting.ExecuteAsync();

                            logDump.stupidLogErrors.Add($"🧹 Found {existing.Files.Count} matching PDF(s) to delete");

                            foreach (var match in existing.Files)
                            {
                                logDump.stupidLogErrors.Add($"🗑️ Deleting: {match.Name} (ID: {match.Id})");
                                await driveService.Files.Delete(match.Id).ExecuteAsync();
                            }
                        }

                        var metadata = new Google.Apis.Drive.v3.Data.File
                        {
                            Name = file.FileName,
                            Parents = new List<string> { targetFolderId }
                        };

                        string contentType = string.IsNullOrWhiteSpace(file.ContentType)
                            ? GetMimeTypeFromExtension(file.FileName)
                            : file.ContentType;

                        stream.Position = 0;

                        var uploadReq = driveService.Files.Create(metadata, stream, contentType);
                        uploadReq.Fields = "id, webViewLink";

                        var progress = await uploadReq.UploadAsync();
                        logDump.stupidLogErrors.Add($"📥 Upload status: {progress.Status}");

                        if (uploadReq.ResponseBody == null)
                        {
                            logDump.stupidLogErrors.Add("⚠️ ResponseBody is null after upload");
                        }
                        else
                        {
                            logDump.ResponseBodyId = uploadReq.ResponseBody.Id;
                            logDump.stupidLogErrors.Add($"📎 File ID: {uploadReq.ResponseBody.Id}");
                            logDump.stupidLogErrors.Add($"🔗 WebViewLink: {uploadReq.ResponseBody.WebViewLink}");
                        }

                        if (progress.Exception != null)
                        {
                            logDump.stupidLogErrors.Add($"🚨 Google API Exception: {progress.Exception.Message}");
                            logDump.stupidLogErrors.Add($"🧵 {progress.Exception.StackTrace}");
                        }

                        if (progress.Status == UploadStatus.Completed)
                        {
                            logDump.stupidLogErrors.Add($"✅ Upload completed: {file.FileName}");
                        }
                        else
                        {
                            logDump.stupidLogErrors.Add($"⚠️ Upload incomplete: {progress.Status}");
                        }
                    }
                    catch (Exception ex)
                    {
                        var err = $"💥 File error: {ex.Message}";
                        logDump.stupidLogErrors.Add(err);
                        logDump.stupidLogErrors.Add($"🧵 Stack: {ex.StackTrace}");
                        Log.Warning(ex, err);
                    }

                    newUploads.Add(logDump);
                }

                return newUploads;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "🔥 Fatal error during UploadFilesAsync");
                throw;
            }
        }




        private async Task<string?> TryResolveFolderAsync(string folderName, string parentId, DriveService driveService)
        {
            var listRequest = driveService.Files.List();
            listRequest.Q = $"mimeType='application/vnd.google-apps.folder' and name='{folderName}' and '{parentId}' in parents and trashed=false";
            listRequest.Fields = "files(id)";
            listRequest.SupportsAllDrives = true;
            listRequest.IncludeItemsFromAllDrives = true;

            try
            {
                var result = await listRequest.ExecuteAsync();
                return result.Files.FirstOrDefault()?.Id;
            }
            catch (GoogleApiException ex)
            {
                Log.Warning(ex, $"⚠️ Failed to resolve folder '{folderName}' under '{parentId}'");
                return null;
            }
        }


        //private async Task<string> EnsureFolderExistsAsync(string folderName, string parentId, DriveService driveService)
        //{
        //    var cached = await _context.GoogleDriveFolders
        //        .FirstOrDefaultAsync(f => f.FolderName == folderName && f.ParentFolderId == parentId);

        //    if (cached != null)
        //    {
        //        Log.Information($"📦 SQL Cache Hit: {folderName} under {parentId} (ID: {cached.FolderId})");
        //        return cached.FolderId;
        //    }

        //    // Helper to list folder from Drive
        //    async Task<string?> TryFindFolderOnDriveAsync(string name, string parent)
        //    {
        //        var request = driveService.Files.List();
        //        request.Q = $"mimeType='application/vnd.google-apps.folder' and name='{name}' and '{parent}' in parents and trashed=false";
        //        request.Fields = "files(id)";
        //        request.SupportsAllDrives = true;
        //        request.IncludeItemsFromAllDrives = true;

        //        try
        //        {
        //            var response = await request.ExecuteAsync();
        //            return response.Files.FirstOrDefault()?.Id;
        //        }
        //        catch (Google.GoogleApiException ex)
        //        {
        //            Log.Warning($"🔁 Drive API list failed for '{name}' under '{parent}': {ex.Message}");
        //            return null;
        //        }
        //    }

        //    // Step 1: Try immediately
        //    var existingId = await TryFindFolderOnDriveAsync(folderName, parentId);
        //    if (existingId != null)
        //    {
        //        Log.Information($"📁 Found in Drive: {folderName} under {parentId} (ID: {existingId})");
        //        await CacheFolderAsync(folderName, parentId, existingId);
        //        return existingId;
        //    }

        //    // Step 2: Retry with jitter
        //    var retryDelay = new Random().Next(500, 850); // jittered retry
        //    Log.Debug($"🕒 Retrying after {retryDelay}ms delay for '{folderName}'");
        //    await Task.Delay(retryDelay);

        //    var retryId = await TryFindFolderOnDriveAsync(folderName, parentId);
        //    if (retryId != null)
        //    {
        //        Log.Warning($"⚠️ Found after retry: {folderName} (ID: {retryId})");
        //        await CacheFolderAsync(folderName, parentId, retryId);
        //        return retryId;
        //    }

        //    // Step 3: Create
        //    Log.Information($"📂 Creating new folder: {folderName} under {parentId}");

        //    var newFolder = new Google.Apis.Drive.v3.Data.File
        //    {
        //        Name = folderName,
        //        MimeType = "application/vnd.google-apps.folder",
        //        Parents = new List<string> { parentId }
        //    };

        //    try
        //    {
        //        var createRequest = driveService.Files.Create(newFolder);
        //        createRequest.Fields = "id";
        //        createRequest.SupportsAllDrives = true;

        //        var created = await createRequest.ExecuteAsync();
        //        Log.Information($"✅ Created folder: {folderName} (ID: {created.Id})");

        //        await CacheFolderAsync(folderName, parentId, created.Id);
        //        return created.Id;
        //    }
        //    catch (Google.GoogleApiException ex)
        //    {
        //        Log.Error(ex, $"❌ Failed to create folder '{folderName}' under '{parentId}': {ex.Message}");
        //        throw;
        //    }
        //}


        // inside class DriveUploaderService …
        public async Task<string?> BackupDatabaseToGoogleDriveAsync(CancellationToken ct = default)
        {
            // 1) Target Drive folder (env var first, then appsettings fallback)
            var driveFolderId = Environment.GetEnvironmentVariable("GOOGLE_DBBackups")
                ?? _config["GoogleDrive:DBBackupsFolderId"];

            if (string.IsNullOrWhiteSpace(driveFolderId))
                throw new InvalidOperationException("Missing GOOGLE_DBBackups (or GoogleDrive:DBBackupsFolderId).");

            // 2) Connection string from EF context
            var connString = _context.Database.GetDbConnection().ConnectionString;
            var csb = new SqlConnectionStringBuilder(connString);
            var dbName = csb.InitialCatalog ?? "Database";
            var server = csb.DataSource ?? "server";

            var stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
            var fileName = $"{dbName}_{stamp}.bacpac";
            var tempPath = Path.Combine(Path.GetTempPath(), fileName);

            Log.Information("Starting .bacpac export for {Db} on {Server} -> {Path}", dbName, server, tempPath);

            try
            {
                // 3) Export .bacpac (DacFx is sync; run on background thread)
                await Task.Run(() =>
                {
                    var dac = new DacServices(connString);
                    dac.Message += (s, e) => Log.Information("DacFx: {Msg}", e.Message);
                    dac.ExportBacpac(tempPath, dbName);
                }, ct);

                var size = new FileInfo(tempPath).Length;
                Log.Information("Bacpac created: {Path} ({Size} bytes)", tempPath, size);

                // 4) Auth to Drive using your existing OAuth2 user token
                DriveService drive = await _authService.GetDriveServiceFromUserTokenAsync();

                // 5) Upload (resumable)
                var meta = new Google.Apis.Drive.v3.Data.File
                {
                    Name = fileName,
                    Parents = new[] { driveFolderId }
                };

                using var fs = new FileStream(tempPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                var upload = drive.Files.Create(meta, fs, "application/octet-stream");
                upload.Fields = "id,name,parents,size,createdTime,webViewLink";
                upload.ChunkSize = ResumableUpload.MinimumChunkSize * 8; // ~8MB chunks

                Log.Information("Uploading {File} to Drive folder {Folder}…", fileName, driveFolderId);
                var result = await upload.UploadAsync(ct);

                if (result.Status != Google.Apis.Upload.UploadStatus.Completed)
                {
                    Log.Error(result.Exception, "Google Drive upload failed with status {Status}", result.Status);
                    return null;
                }

                var created = upload.ResponseBody;
                Log.Information("✅ Backup uploaded. fileId={FileId} size={Size}", created.Id, created.Size);

                return created.Id;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "BackupDatabaseToGoogleDriveAsync failed.");
                return null;
            }
            finally
            {
                try
                {
                    if (System.IO.File.Exists(tempPath))
                        System.IO.File.Delete(tempPath);
                }
                catch (Exception cleanupEx)
                {
                    Log.Warning(cleanupEx, "Failed to remove temp {Path}", tempPath);
                }
            }
        }

        private async Task<string> EnsureFolderExistsAsync(string folderName, string parentId, DriveService driveService)
        {
            // 1) Check cache, but verify it still has the right parent
            var cached = await _context.GoogleDriveFolders
                .FirstOrDefaultAsync(f => f.FolderName == folderName && f.ParentFolderId == parentId);

            if (cached != null)
            {
                try
                {
                    var g = driveService.Files.Get(cached.FolderId);
                    g.Fields = "id,parents,trashed";
                    var meta = await g.ExecuteAsync();

                    // stale/trashed? purge and fallthrough to lookup/create
                    if (meta == null || meta.Trashed == true)
                    {
                        _context.GoogleDriveFolders.Remove(cached);
                        await _context.SaveChangesAsync();
                    }
                    else
                    {
                        var parents = meta.Parents ?? new List<string>();
                        if (!parents.Contains(parentId))
                        {
                            // 💡 auto-correct drift: move to the intended parent
                            var upd = driveService.Files.Update(new Google.Apis.Drive.v3.Data.File(), cached.FolderId);
                            upd.AddParents = parentId;
                            if (parents.Count > 0) upd.RemoveParents = string.Join(",", parents);
                            upd.SupportsAllDrives = true;
                            await upd.ExecuteAsync();

                            cached.ParentFolderId = parentId;
                            await _context.SaveChangesAsync();
                        }
                        return cached.FolderId;
                    }
                }
                catch (Google.GoogleApiException ex) when ((int)ex.HttpStatusCode == 404)
                {
                    // cache points to a deleted/missing folder → purge & continue
                    _context.GoogleDriveFolders.Remove(cached);
                    await _context.SaveChangesAsync();
                }
            }

            // 2) Look for an existing folder *under this parent ID*
            async Task<string?> TryFindFolderOnDriveAsync(string name, string parent)
            {
                var escaped = name.Replace("'", "\\'");
                var request = driveService.Files.List();
                request.Q = $"mimeType='application/vnd.google-apps.folder' and name='{escaped}' and '{parent}' in parents and trashed=false";
                request.Fields = "files(id,parents)";
                request.SupportsAllDrives = true;
                request.IncludeItemsFromAllDrives = true;

                var response = await request.ExecuteAsync();
                return response.Files.FirstOrDefault()?.Id;
            }

            var existingId = await TryFindFolderOnDriveAsync(folderName, parentId);
            if (existingId != null)
            {
                await CacheFolderAsync(folderName, parentId, existingId);
                return existingId;
            }

            // 3) Create new under the intended parent
            var create = driveService.Files.Create(new Google.Apis.Drive.v3.Data.File
            {
                Name = folderName,
                MimeType = "application/vnd.google-apps.folder",
                Parents = new List<string> { parentId }
            });
            create.Fields = "id,parents";
            create.SupportsAllDrives = true;

            var created = await create.ExecuteAsync();
            await CacheFolderAsync(folderName, parentId, created.Id);
            return created.Id;
        }

        // Small helper to DRY up caching
        private async Task CacheFolderAsync(string name, string parentId, string folderId)
        {
            _context.GoogleDriveFolders.Add(new GoogleDriveFolder
            {
                FolderName = name,
                ParentFolderId = parentId,
                FolderId = folderId,
                CreatedAt = DateTime.UtcNow
            });
            await _context.SaveChangesAsync();
        }



        private async Task<string> EnsureFolderBackupExistsAsync(string folderName, string parentId, DriveService driveService)
        {
            // First attempt: check if folder already exists
            var listRequest = driveService.Files.List();
            listRequest.Q = $"mimeType='application/vnd.google-apps.folder' and name='{folderName}' and '{parentId}' in parents and trashed=false";
            listRequest.Fields = "files(id)";
            var result = await listRequest.ExecuteAsync();

            if (result.Files.Count > 0)
            {
                Log.Information($"📁 Found existing folder '{folderName}' under parent '{parentId}' (ID: {result.Files[0].Id})");
                return result.Files[0].Id;
            }

            // Wait briefly for any in-flight folders being created in parallel
            await Task.Delay(750);

            // Second check after delay (Google Drive is eventually consistent)
            var retryList = driveService.Files.List();
            retryList.Q = $"mimeType='application/vnd.google-apps.folder' and name='{folderName}' and '{parentId}' in parents and trashed=false";
            retryList.Fields = "files(id)";
            var retryResult = await retryList.ExecuteAsync();

            if (retryResult.Files.Count > 0)
            {
                Log.Warning($"⚠️ Found folder '{folderName}' after delay — race avoided (ID: {retryResult.Files[0].Id})");
                return retryResult.Files[0].Id;
            }

            // Still nothing — safe to create
            Log.Information($"📂 Creating folder '{folderName}' under parent '{parentId}'");

            var newFolder = new Google.Apis.Drive.v3.Data.File
            {
                Name = folderName,
                MimeType = "application/vnd.google-apps.folder",
                Parents = new List<string> { parentId }
            };

            var createRequest = driveService.Files.Create(newFolder);
            createRequest.Fields = "id";
            var created = await createRequest.ExecuteAsync();

            Log.Information($"✅ Created folder '{folderName}' (ID: {created.Id})");
            await driveService.Permissions.Create(new Permission
            {
                Type = "user",
                Role = "writer",
                EmailAddress = "taskfuel.files@gmail.com"
            }, created.Id).ExecuteAsync();
            return created.Id;
        }

    }

}

