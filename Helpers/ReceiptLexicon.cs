using RaymarEquipmentInventory.Helpers;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace RaymarEquipmentInventory.Helpers
{
    /// <summary>
    /// Default in-memory implementation of <see cref="IReceiptLexicon"/>.
    /// All collections are case-insensitive where applicable and exposed read-only.
    /// </summary>
    public sealed class ReceiptLexicon : IReceiptLexicon
    {
        // ---- Field aliases for Azure Document Intelligence fields -----------------
        private static readonly Dictionary<string, string[]> s_fieldAliases =
            new(StringComparer.OrdinalIgnoreCase)
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

        // ---- Vendor → category overrides -----------------------------------------
        private static readonly Dictionary<string, string> s_vendorCategoryOverrides =
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

        // ---- Keyword signals used in category inference --------------------------
        private static readonly string[] s_restaurantItemKeywords =
        {
            "burger","burrito","wrap","taco","fries","poutine","coffee","latte","tea",
            "pop","soda","drink","combo","nugget","chicken","sandwich","sub","salad","pizza",
            "soup","donut","muffin","cookie"
        };

        private static readonly string[] s_suppliesItemKeywords =
        {
            "bolt","screw","hose","valve","fitting","clamp","gasket","filter","sensor",
            "seal","plug","brake","bearing","pipe","coupler","nozzle","o-ring","adhesive"
        };

        // ---- Item filtering wordlists --------------------------------------------
        private static readonly HashSet<string> s_stopWords =
            new(StringComparer.OrdinalIgnoreCase)
            {
                "reprint","qty","quantity","description","total","subtotal","grand","tax","hst","gst","pst",
                "auth","approval","approved","change","tender","merchant","card","mastercard","visa","debit",
                "balance","thank","survey","copy","customer","invoice","receipt","order","payment"
            };

        private static readonly HashSet<string> s_restaurantAllow =
            new(StringComparer.OrdinalIgnoreCase)
            {
                // Original core
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

        private static readonly HashSet<string> s_suppliesAllow =
            new(StringComparer.OrdinalIgnoreCase)
            {
                // Original core
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

        // ---- Canadian Tire SKU pattern (e.g., 123-4567-8) ------------------------
        private static readonly Regex s_canadianTireSkuRegex =
            new(@"(?<!\d)\d{3}-\d{4}-\d(?!\d)", RegexOptions.Compiled);

        // ---- IReceiptLexicon implementation --------------------------------------
        public IReadOnlyDictionary<string, string[]> FieldAliases => s_fieldAliases;
        public IReadOnlyDictionary<string, string> VendorCategoryOverrides => s_vendorCategoryOverrides;

        public IReadOnlyCollection<string> RestaurantItemKeywords => s_restaurantItemKeywords;
        public IReadOnlyCollection<string> SuppliesItemKeywords => s_suppliesItemKeywords;

        public IReadOnlyCollection<string> StopWords => s_stopWords;
        public IReadOnlyCollection<string> RestaurantAllow => s_restaurantAllow;
        public IReadOnlyCollection<string> SuppliesAllow => s_suppliesAllow;

        public Regex CanadianTireSkuRegex => s_canadianTireSkuRegex;
    }
}
