namespace RockyTelecom.Infrastructure.Persistence.Demo;

/// <summary>
/// One demo product. <c>Configs</c> is "ram/storage:priceDelta;…" (phones, tablets, laptops) or empty
/// (one variant per colour). <c>Flags</c>: F featured, P pre-order (no stock), S sold out,
/// U used (good condition), L used (like new).
/// Prices and specifications are indicative demo values, not a live price list.
/// </summary>
internal sealed record DemoProduct(
    string Cat,
    string Brand,
    string Name,
    decimal Price,
    string Colors,
    string Configs,
    int Disc,
    string Badge,
    string Flags,
    string Specs);

internal sealed record DemoCategory(string Slug, string Name, string Key, int Sort, string SeoDesc);

internal static class DemoCatalogData
{
    internal static readonly DemoCategory[] Categories =
    {
        new("smartphones", "Smartphones", "phone", 1, "Genuine smartphones with official warranty at Rocky Telecom, Gaibandha."),
        new("tablets", "Tablets", "tablet", 2, "Tablets for study, work and play, with warranty and EMI."),
        new("laptops", "Laptops", "laptop", 3, "Laptops for office, study and gaming from trusted brands."),
        new("smart-watches", "Smart Watches", "watch", 4, "Smart watches and fitness bands for every budget."),
        new("audio", "Audio", "audio", 5, "Earbuds, headphones and speakers."),
        new("accessories", "Accessories", "acc", 6, "Chargers, power banks, cases and cables."),
        new("smart-tv", "Smart TVs", "tv", 7, "4K smart TVs with home delivery."),
        new("monitors", "Monitors", "monitor", 8, "Monitors for work, study and gaming.")
    };

    internal static readonly string[] Brands =
    {
        "Apple", "Samsung", "Xiaomi", "Realme", "OnePlus", "Oppo", "Vivo", "Google", "Nothing", "Infinix",
        "Honor", "Huawei", "iQOO", "Meizu", "Motorola", "Tecno", "ZTE",
        "Lenovo", "HP", "Asus", "Dell", "Acer", "Sony", "JBL", "Anker", "Baseus", "LG", "Spigen"
    };

    // ------------------------------------------------------------------ spec builders

    private static string Ph(string size, string type, int hz, string chip, string os, string cam, string selfie,
        int mah, int watt, int grams, string net, string water) =>
        $"display_size={size};display_type={type};refresh_rate_hz={hz};chipset={chip};os={os};main_camera={cam};selfie_camera={selfie};battery_mah={mah};charging_w={watt};weight_g={grams};network={net};water_rating={water}";

    private static string Tb(string size, string type, string chip, string os, int mah, int grams, string conn) =>
        $"display_size={size};display_type={type};chipset={chip};os={os};battery_mah={mah};weight_g={grams};connectivity={conn}";

    private static string Lp(string size, string type, string cpu, string gpu, string os, int wh, string kg, string ports) =>
        $"display_size={size};display_type={type};cpu={cpu};gpu={gpu};os={os};battery_wh={wh};weight_kg={kg};ports={ports}";

    private static string Wt(string size, string type, string life, string water, string sensors, string compat) =>
        $"display_size={size};display_type={type};battery_life={life};water_rating={water};sensors={sensors};compatibility={compat}";

    private static string Au(string type, string life, string anc, string conn, string water) =>
        $"audio_type={type};battery_life={life};noise_cancelling={anc};connectivity={conn};water_rating={water}";

    private static string Ac(string type, string compat, int watt, string conn) =>
        $"acc_type={type};compatibility={compat};power_w={watt};connector={conn}";

    private static string Tv(int size, string res, string panel, string os, int hz, string ports) =>
        $"screen_size_in={size};resolution={res};panel={panel};smart_os={os};refresh_rate_hz={hz};ports={ports}";

    private static string Mn(int size, string res, string panel, int hz, string ms, string ports) =>
        $"screen_size_in={size};resolution={res};panel={panel};refresh_rate_hz={hz};response_ms={ms};ports={ports}";

    private static DemoProduct P(string cat, string brand, string name, decimal price, string colors, string configs,
        int disc, string badge, string flags, string specs) =>
        new(cat, brand, name, price, colors, configs, disc, badge, flags, specs);

    // ------------------------------------------------------------------ products

    internal static readonly DemoProduct[] Products =
    {
        // ---------------- Smartphones: Apple
        P("phone", "Apple", "iPhone 15 Pro Max", 178000, "Natural Titanium;Blue Titanium;White Titanium;Black Titanium", "8/256:0;8/512:27000;8/1024:54000", 0, "Top Selling", "F",
            Ph("6.7", "LTPO Super Retina XDR OLED", 120, "Apple A17 Pro", "iOS 17", "48MP + 12MP + 12MP", "12MP", 4422, 27, 221, "5G, Wi-Fi 6E, NFC, USB-C", "IP68")),
        P("phone", "Apple", "iPhone 15 Pro", 149000, "Natural Titanium;Blue Titanium;White Titanium;Black Titanium", "8/128:0;8/256:15000;8/512:42000", 4, "Hot", "F",
            Ph("6.1", "LTPO Super Retina XDR OLED", 120, "Apple A17 Pro", "iOS 17", "48MP + 12MP + 12MP", "12MP", 3274, 27, 187, "5G, Wi-Fi 6E, NFC, USB-C", "IP68")),
        P("phone", "Apple", "iPhone 15", 109000, "Black;Blue;Green;Pink;Yellow", "6/128:0;6/256:16000;6/512:41000", 6, "New", "F",
            Ph("6.1", "Super Retina XDR OLED", 60, "Apple A16 Bionic", "iOS 17", "48MP + 12MP", "12MP", 3349, 20, 171, "5G, Wi-Fi 6, NFC, USB-C", "IP68")),
        P("phone", "Apple", "iPhone 14", 92000, "Midnight;Starlight;Blue;Purple;Red", "6/128:0;6/256:14000", 8, "", "",
            Ph("6.1", "Super Retina XDR OLED", 60, "Apple A15 Bionic", "iOS 16", "12MP + 12MP", "12MP", 3279, 20, 172, "5G, Wi-Fi 6, NFC", "IP68")),
        P("phone", "Apple", "iPhone 13", 74000, "Midnight;Starlight;Blue;Pink", "4/128:0;4/256:12000", 10, "Best Value", "",
            Ph("6.1", "Super Retina XDR OLED", 60, "Apple A15 Bionic", "iOS 15", "12MP + 12MP", "12MP", 3240, 20, 174, "5G, Wi-Fi 6, NFC", "IP68")),

        // ---------------- Smartphones: Samsung
        P("phone", "Samsung", "Galaxy S24 Ultra", 165000, "Titanium Gray;Titanium Black;Titanium Violet;Titanium Yellow", "12/256:0;12/512:20000;12/1024:45000", 5, "Top Selling", "F",
            Ph("6.8", "Dynamic LTPO AMOLED 2X", 120, "Snapdragon 8 Gen 3", "Android 14", "200MP + 50MP + 12MP + 10MP", "12MP", 5000, 45, 232, "5G, Wi-Fi 7, NFC, USB-C", "IP68")),
        P("phone", "Samsung", "Galaxy S24+", 132000, "Onyx Black;Marble Gray;Cobalt Violet;Amber Yellow", "12/256:0;12/512:18000", 7, "Hot", "F",
            Ph("6.7", "Dynamic LTPO AMOLED 2X", 120, "Exynos 2400", "Android 14", "50MP + 12MP + 10MP", "12MP", 4900, 45, 196, "5G, Wi-Fi 7, NFC, USB-C", "IP68")),
        P("phone", "Samsung", "Galaxy S24", 108000, "Onyx Black;Marble Gray;Cobalt Violet;Amber Yellow", "8/128:0;8/256:10000", 9, "New", "",
            Ph("6.2", "Dynamic LTPO AMOLED 2X", 120, "Exynos 2400", "Android 14", "50MP + 12MP + 10MP", "12MP", 4000, 25, 167, "5G, Wi-Fi 7, NFC, USB-C", "IP68")),
        P("phone", "Samsung", "Galaxy Z Flip5", 118000, "Mint;Graphite;Lavender;Cream", "8/256:0;8/512:14000", 12, "Hot", "",
            Ph("6.7", "Foldable Dynamic AMOLED 2X", 120, "Snapdragon 8 Gen 2", "Android 13", "12MP + 12MP", "10MP", 3700, 25, 187, "5G, Wi-Fi 6E, NFC", "IPX8")),
        P("phone", "Samsung", "Galaxy A55 5G", 56000, "Awesome Iceblue;Awesome Lilac;Awesome Navy;Awesome Lemon", "8/128:0;8/256:5000;12/256:9000", 8, "Best Value", "F",
            Ph("6.6", "Super AMOLED", 120, "Exynos 1480", "Android 14", "50MP + 12MP + 5MP", "32MP", 5000, 25, 213, "5G, Wi-Fi 6, NFC", "IP67")),
        P("phone", "Samsung", "Galaxy A35 5G", 44000, "Awesome Iceblue;Awesome Lilac;Awesome Navy;Awesome Lemon", "8/128:0;8/256:5000", 10, "", "",
            Ph("6.6", "Super AMOLED", 120, "Exynos 1380", "Android 14", "50MP + 8MP + 5MP", "13MP", 5000, 25, 209, "5G, Wi-Fi 6, NFC", "IP67")),
        P("phone", "Samsung", "Galaxy A15", 24500, "Blue Black;Light Blue;Yellow", "4/128:0;6/128:2500", 6, "Budget Pick", "",
            Ph("6.5", "Super AMOLED", 90, "MediaTek Helio G99", "Android 14", "50MP + 5MP + 2MP", "13MP", 5000, 25, 200, "4G, Wi-Fi 5, NFC", "None")),

        // ---------------- Smartphones: Xiaomi
        P("phone", "Xiaomi", "Xiaomi 14", 98000, "Black;White;Jade Green", "12/256:0;12/512:12000", 8, "New", "F",
            Ph("6.36", "LTPO AMOLED", 120, "Snapdragon 8 Gen 3", "Android 14 (HyperOS)", "50MP + 50MP + 50MP", "32MP", 4610, 90, 193, "5G, Wi-Fi 7, NFC", "IP68")),
        P("phone", "Xiaomi", "Redmi Note 13 Pro+ 5G", 48000, "Midnight Black;Aurora Purple;Fusion White", "8/256:0;12/512:7000", 10, "Hot", "F",
            Ph("6.67", "Curved AMOLED", 120, "MediaTek Dimensity 7200 Ultra", "Android 13 (MIUI 14)", "200MP + 8MP + 2MP", "16MP", 5000, 120, 204, "5G, Wi-Fi 6, NFC", "IP68")),
        P("phone", "Xiaomi", "Redmi Note 13", 24500, "Midnight Black;Ocean Teal;Arctic White", "6/128:0;8/256:3500", 7, "Best Value", "",
            Ph("6.67", "AMOLED", 120, "Snapdragon 685", "Android 13 (MIUI 14)", "108MP + 8MP + 2MP", "16MP", 5000, 33, 188, "4G, Wi-Fi 5, NFC", "IP54")),
        P("phone", "Xiaomi", "Redmi 13C", 14500, "Midnight Black;Navy Blue;Clover Green", "4/128:0;6/128:1500;8/256:3000", 8, "Budget Pick", "",
            Ph("6.74", "IPS LCD", 90, "MediaTek Helio G85", "Android 13 (MIUI 14)", "50MP + 2MP", "5MP", 5000, 18, 192, "4G, Wi-Fi 5", "None")),
        P("phone", "Xiaomi", "POCO X6 Pro 5G", 36500, "Racing Gray;Yellow;Black", "8/256:0;12/512:6000", 9, "Hot", "",
            Ph("6.67", "Flow AMOLED", 120, "MediaTek Dimensity 8300 Ultra", "Android 14 (HyperOS)", "64MP + 8MP + 2MP", "16MP", 5000, 67, 186, "5G, Wi-Fi 6, NFC", "IP54")),

        // ---------------- Smartphones: others
        P("phone", "Realme", "Realme 12 Pro+ 5G", 46000, "Submarine Blue;Navigator Beige", "8/256:0;12/512:7000", 8, "New", "",
            Ph("6.7", "Curved AMOLED", 120, "Snapdragon 7s Gen 2", "Android 14", "50MP + 64MP + 8MP", "32MP", 5000, 67, 196, "5G, Wi-Fi 6, NFC", "IP65")),
        P("phone", "Realme", "Realme Narzo 70 Pro 5G", 31500, "Glass Green;Glass Gold", "8/128:0;8/256:3000", 10, "", "",
            Ph("6.67", "AMOLED", 120, "MediaTek Dimensity 7050", "Android 14", "50MP + 8MP", "16MP", 5000, 67, 190, "5G, Wi-Fi 6, NFC", "None")),
        P("phone", "OnePlus", "OnePlus 12", 99000, "Silky Black;Flowy Emerald", "12/256:0;16/512:14000", 6, "Top Selling", "F",
            Ph("6.82", "LTPO AMOLED", 120, "Snapdragon 8 Gen 3", "Android 14 (OxygenOS)", "50MP + 48MP + 64MP", "32MP", 5400, 100, 220, "5G, Wi-Fi 7, NFC, USB-C", "IP65")),
        P("phone", "OnePlus", "OnePlus Nord CE4 5G", 36000, "Celadon Marble;Dark Chrome", "8/128:0;8/256:3500", 7, "", "",
            Ph("6.7", "AMOLED", 120, "Snapdragon 7 Gen 3", "Android 14 (OxygenOS)", "50MP + 8MP", "16MP", 5500, 100, 186, "5G, Wi-Fi 6, NFC", "None")),
        P("phone", "OnePlus", "OnePlus 13", 112000, "Black Eclipse;Arctic Dawn", "12/256:0;16/512:14000", 0, "Pre-order", "P",
            Ph("6.82", "LTPO AMOLED", 120, "Snapdragon 8 Elite", "Android 15 (OxygenOS)", "50MP + 50MP + 50MP", "32MP", 6000, 100, 213, "5G, Wi-Fi 7, NFC, USB-C", "IP69")),
        P("phone", "Oppo", "Oppo Reno11 F 5G", 38500, "Ocean Blue;Palm Green;Coral Purple", "8/256:0", 8, "", "",
            Ph("6.7", "AMOLED", 120, "MediaTek Dimensity 7050", "Android 14 (ColorOS)", "64MP + 8MP + 2MP", "32MP", 5000, 67, 177, "5G, Wi-Fi 6, NFC", "IP64")),
        P("phone", "Vivo", "Vivo V30 5G", 52000, "Andaman Blue;Peacock Green;Classic Black", "12/256:0;12/512:7000", 9, "New", "",
            Ph("6.78", "Curved AMOLED", 120, "Snapdragon 7 Gen 3", "Android 14 (Funtouch)", "50MP + 50MP", "50MP", 5000, 80, 186, "5G, Wi-Fi 6, NFC", "IP67")),
        P("phone", "Google", "Pixel 8", 94000, "Obsidian;Hazel;Rose", "8/128:0;8/256:9000", 8, "", "",
            Ph("6.2", "Actua OLED", 120, "Google Tensor G3", "Android 14", "50MP + 12MP", "10.5MP", 4575, 27, 187, "5G, Wi-Fi 7, NFC, USB-C", "IP68")),
        P("phone", "Google", "Pixel 8 Pro", 128000, "Obsidian;Porcelain;Bay", "12/128:0;12/256:9000;12/512:24000", 6, "Hot", "F",
            Ph("6.7", "LTPO OLED", 120, "Google Tensor G3", "Android 14", "50MP + 48MP + 48MP", "10.5MP", 5050, 30, 213, "5G, Wi-Fi 7, NFC, USB-C", "IP68")),
        P("phone", "Nothing", "Nothing Phone (3a)", 49000, "Black;White;Blue", "8/128:0;12/256:6000", 0, "Pre-order", "P",
            Ph("6.77", "Flexible AMOLED", 120, "Snapdragon 7s Gen 3", "Android 15 (Nothing OS)", "50MP + 50MP + 8MP", "32MP", 5000, 50, 201, "5G, Wi-Fi 6, NFC", "IP64")),
        P("phone", "Infinix", "Infinix Note 40 Pro", 26500, "Titan Gold;Vintage Green;Obsidian Black", "8/256:0;12/256:2000", 10, "Budget Pick", "",
            Ph("6.78", "AMOLED", 120, "MediaTek Helio G99 Ultimate", "Android 14 (XOS)", "108MP + 2MP", "32MP", 5000, 45, 195, "4G, Wi-Fi 5, NFC", "IP53")),

        // ---------------- Smartphones: more brands (indicative demo values)
        P("phone", "Honor", "Honor 200 Pro 5G", 72000, "Moonlight White;Ocean Cyan;Black", "12/512:0", 7, "New", "",
            Ph("6.78", "Curved OLED", 120, "Snapdragon 8s Gen 3", "Android 14 (MagicOS 8)", "50MP + 50MP + 12MP", "50MP", 5200, 100, 199, "5G, Wi-Fi 7, NFC", "IP65")),
        P("phone", "Honor", "Honor X9b 5G", 33500, "Midnight Black;Sunrise Orange", "8/256:0", 9, "", "",
            Ph("6.78", "Curved AMOLED", 120, "Snapdragon 6 Gen 1", "Android 13 (MagicOS 7.2)", "108MP + 5MP", "16MP", 5800, 35, 185, "5G, Wi-Fi 5, NFC", "IP53")),
        P("phone", "Huawei", "Huawei Nova 12i", 24500, "Black;Green", "8/256:0", 6, "Budget Pick", "",
            Ph("6.7", "IPS LCD", 90, "Snapdragon 680", "Android-based (EMUI 14)", "108MP + 2MP", "8MP", 5000, 40, 190, "4G, Wi-Fi 5, NFC", "None")),
        P("phone", "Huawei", "Huawei Pura 70", 118000, "Black;White;Green", "12/256:0;12/512:12000", 0, "Premium", "",
            Ph("6.6", "LTPO OLED", 120, "Kirin 9010", "HarmonyOS 4.2", "50MP + 12MP + 13MP", "13MP", 4900, 66, 207, "4G, Wi-Fi 6, NFC", "IP68")),
        P("phone", "iQOO", "iQOO 12 5G", 76000, "Legend White;Alpha Black", "12/256:0;16/512:9000", 8, "Hot", "F",
            Ph("6.78", "LTPO AMOLED", 144, "Snapdragon 8 Gen 3", "Android 14 (Funtouch OS 14)", "50MP + 50MP + 64MP", "16MP", 5000, 120, 203, "5G, Wi-Fi 7, NFC", "IP64")),
        P("phone", "iQOO", "iQOO Z9 5G", 31000, "Brushed Green;Graphene Blue", "8/128:0;8/256:3000", 10, "Best Value", "",
            Ph("6.67", "AMOLED", 120, "MediaTek Dimensity 7200", "Android 14 (Funtouch OS 14)", "50MP + 2MP", "16MP", 5000, 44, 190, "5G, Wi-Fi 6, NFC", "IP54")),
        P("phone", "Meizu", "Meizu 21 Pro", 89000, "Black;Silver", "12/256:0;16/512:12000", 5, "", "",
            Ph("6.79", "LTPO AMOLED", 120, "Snapdragon 8 Gen 3", "Android 14 (Flyme 11)", "50MP + 50MP + 13MP", "32MP", 5050, 80, 208, "5G, Wi-Fi 7, NFC", "IP68")),
        P("phone", "Motorola", "Motorola Edge 50 Pro", 56000, "Black Beauty;Luxe Lavender;Moonlight Pearl", "12/256:0;12/512:6000", 9, "New", "F",
            Ph("6.7", "pOLED", 144, "Snapdragon 7 Gen 3", "Android 14 (Hello UI)", "50MP + 13MP + 10MP", "50MP", 4500, 125, 186, "5G, Wi-Fi 6E, NFC", "IP68")),
        P("phone", "Motorola", "Moto G84 5G", 25500, "Midnight Blue;Viva Magenta", "8/256:0", 8, "Budget Pick", "",
            Ph("6.55", "pOLED", 120, "Snapdragon 695", "Android 13", "50MP + 8MP", "16MP", 5000, 30, 168, "5G, Wi-Fi 5, NFC", "IP54")),
        P("phone", "Tecno", "Tecno Camon 30 Pro 5G", 34500, "Alpine Snow;Basaltic Dark", "12/256:0", 10, "New", "",
            Ph("6.78", "Curved AMOLED", 144, "MediaTek Dimensity 8200", "Android 14 (HiOS 14)", "50MP + 50MP + 50MP", "50MP", 5000, 70, 190, "5G, Wi-Fi 6, NFC", "None")),
        P("phone", "Tecno", "Tecno Spark 20 Pro", 17500, "Magic Skin Green;Moonlit Black", "8/256:0", 7, "Budget Pick", "",
            Ph("6.78", "IPS LCD", 120, "MediaTek Helio G99", "Android 13 (HiOS 13.5)", "108MP + 2MP", "32MP", 5000, 33, 201, "4G, Wi-Fi 5, NFC", "None")),
        P("phone", "ZTE", "ZTE Nubia Z60 Ultra", 82000, "Black;Silver", "12/256:0;16/512:11000", 6, "", "",
            Ph("6.8", "AMOLED", 120, "Snapdragon 8 Gen 3", "Android 14 (Nebula OS)", "50MP + 50MP + 64MP", "16MP", 6000, 80, 228, "5G, Wi-Fi 7, NFC", "IP54")),
        P("phone", "Asus", "Asus ROG Phone 8 Pro", 128000, "Phantom Black;Rebel Grey", "16/512:0;24/1024:20000", 4, "Gaming", "",
            Ph("6.78", "LTPO AMOLED", 165, "Snapdragon 8 Gen 3", "Android 14 (ROG UI)", "50MP + 32MP + 13MP", "32MP", 5500, 65, 225, "5G, Wi-Fi 7, NFC", "IP68")),

        // ---------------- Smartphones: used
        P("phone", "Apple", "iPhone 12 (Used)", 46000, "Black;Blue;White", "4/128:0", 0, "Used", "U",
            Ph("6.1", "Super Retina XDR OLED", 60, "Apple A14 Bionic", "iOS 17", "12MP + 12MP", "12MP", 2815, 20, 164, "5G, Wi-Fi 6, NFC", "IP68")),
        P("phone", "Samsung", "Galaxy S22 Ultra (Used)", 62000, "Phantom Black;Burgundy", "12/256:0", 0, "Used", "L",
            Ph("6.8", "Dynamic AMOLED 2X", 120, "Snapdragon 8 Gen 1", "Android 14", "108MP + 12MP + 10MP + 10MP", "40MP", 5000, 45, 228, "5G, Wi-Fi 6E, NFC", "IP68")),

        // ---------------- Tablets
        P("tablet", "Apple", "iPad (10th generation)", 62000, "Silver;Blue;Pink;Yellow", "4/64:0;4/256:24000", 5, "Top Selling", "F",
            Tb("10.9", "Liquid Retina IPS LCD", "Apple A14 Bionic", "iPadOS 17", 7606, 477, "Wi-Fi 6, USB-C")),
        P("tablet", "Apple", "iPad Air 11-inch (M2)", 98000, "Space Gray;Blue;Purple;Starlight", "8/128:0;8/256:12000;8/512:30000", 4, "New", "F",
            Tb("11", "Liquid Retina IPS LCD", "Apple M2", "iPadOS 17", 7600, 462, "Wi-Fi 6E, USB-C")),
        P("tablet", "Samsung", "Galaxy Tab S9 FE", 56000, "Gray;Mint;Lavender;Silver", "6/128:0;8/256:7000", 8, "Hot", "",
            Tb("10.9", "TFT LCD", "Exynos 1380", "Android 13", 8000, 523, "Wi-Fi 6, 5G optional")),
        P("tablet", "Samsung", "Galaxy Tab A9+", 31500, "Graphite;Silver;Navy", "4/64:0;8/128:4000", 10, "Best Value", "",
            Tb("11", "TFT LCD", "Snapdragon 695", "Android 13", 7040, 480, "Wi-Fi 5, 5G optional")),
        P("tablet", "Xiaomi", "Xiaomi Pad 6", 41000, "Mist Blue;Graphite Gray;Gold", "6/128:0;8/256:5000", 9, "", "",
            Tb("11", "IPS LCD 144Hz", "Snapdragon 870", "Android 13 (MIUI Pad 14)", 8840, 490, "Wi-Fi 6")),
        P("tablet", "Lenovo", "Lenovo Tab M10 (3rd Gen)", 21500, "Storm Gray;Frost Blue", "3/32:0;4/64:2000", 6, "Budget Pick", "",
            Tb("10.1", "IPS LCD", "UNISOC T610", "Android 11", 5000, 460, "Wi-Fi 5, 4G optional")),

        // ---------------- Laptops
        P("laptop", "Apple", "MacBook Air 13-inch (M2)", 138000, "Midnight;Starlight;Space Gray;Silver", "8/256:0;8/512:20000;16/512:42000", 5, "Top Selling", "F",
            Lp("13.6", "Liquid Retina IPS 2560x1664", "Apple M2 8-core", "Apple 8-core GPU", "macOS Sonoma", 52, "1.24", "2x Thunderbolt, MagSafe, 3.5mm")),
        P("laptop", "Apple", "MacBook Air 13-inch (M3)", 152000, "Midnight;Starlight;Space Gray;Silver", "8/256:0;16/512:42000", 3, "New", "F",
            Lp("13.6", "Liquid Retina IPS 2560x1664", "Apple M3 8-core", "Apple 10-core GPU", "macOS Sonoma", 52, "1.24", "2x Thunderbolt, MagSafe, 3.5mm")),
        P("laptop", "Apple", "MacBook Pro 14-inch (M3)", 215000, "Space Gray;Silver", "8/512:0;16/1024:55000", 0, "", "",
            Lp("14.2", "Liquid Retina XDR 3024x1964", "Apple M3 8-core", "Apple 10-core GPU", "macOS Sonoma", 70, "1.55", "3x Thunderbolt, HDMI, SD, MagSafe")),
        P("laptop", "Lenovo", "Lenovo IdeaPad Slim 3 (Core i5)", 69500, "Arctic Gray;Abyss Blue", "8/512:0;16/512:7000", 8, "Best Value", "F",
            Lp("15.6", "FHD IPS 1920x1080", "Intel Core i5-13420H", "Intel UHD", "Windows 11 Home", 47, "1.62", "USB-C, 2x USB-A, HDMI, SD")),
        P("laptop", "HP", "HP Pavilion 15 (Core i5)", 78000, "Natural Silver;Warm Gold", "16/512:0;16/1024:9000", 7, "", "",
            Lp("15.6", "FHD IPS 1920x1080", "Intel Core i5-1335U", "Intel Iris Xe", "Windows 11 Home", 41, "1.74", "USB-C, 2x USB-A, HDMI, SD")),
        P("laptop", "Asus", "Asus Vivobook 15 (Ryzen 5)", 64000, "Cool Silver;Indie Black", "8/512:0;16/512:6000", 9, "Hot", "",
            Lp("15.6", "FHD IPS 1920x1080", "AMD Ryzen 5 7520U", "AMD Radeon", "Windows 11 Home", 42, "1.7", "USB-C, 2x USB-A, HDMI, SD")),
        P("laptop", "Dell", "Dell Inspiron 15 3520 (Core i5)", 72500, "Carbon Black;Platinum Silver", "8/512:0;16/512:6500", 6, "", "",
            Lp("15.6", "FHD WVA 120Hz", "Intel Core i5-1235U", "Intel Iris Xe", "Windows 11 Home", 41, "1.65", "USB-C, 2x USB-A, HDMI, SD")),
        P("laptop", "Acer", "Acer Aspire 5 (Core i3)", 52500, "Steel Gray;Pure Silver", "8/256:0;8/512:3500", 8, "Budget Pick", "",
            Lp("15.6", "FHD IPS 1920x1080", "Intel Core i3-1315U", "Intel UHD", "Windows 11 Home", 50, "1.76", "USB-C, 2x USB-A, HDMI")),
        P("laptop", "Asus", "Asus TUF Gaming F15 (RTX 3050)", 98000, "Graphite Black;Mecha Gray", "16/512:0;16/1024:9000", 6, "Gaming", "F",
            Lp("15.6", "FHD IPS 144Hz", "Intel Core i5-11400H", "NVIDIA RTX 3050 4GB", "Windows 11 Home", 48, "2.2", "USB-C, 3x USB-A, HDMI, RJ45")),

        // ---------------- Smart watches
        P("watch", "Apple", "Apple Watch Series 9 (45mm)", 62000, "Midnight;Starlight;Pink;Silver", "", 4, "New", "F",
            Wt("1.9", "Always-On LTPO OLED", "18 hours", "50m water resistant", "ECG, blood oxygen, heart rate, temperature", "iPhone only")),
        P("watch", "Apple", "Apple Watch SE (2nd generation)", 38500, "Midnight;Starlight;Silver", "", 6, "Best Value", "",
            Wt("1.78", "LTPO OLED", "18 hours", "50m water resistant", "Heart rate, fall detection", "iPhone only")),
        P("watch", "Samsung", "Galaxy Watch6 (44mm)", 36500, "Graphite;Silver;Gold", "", 10, "Hot", "",
            Wt("1.5", "Super AMOLED", "40 hours", "5ATM + IP68", "ECG, body composition, heart rate, sleep", "Android")),
        P("watch", "Samsung", "Galaxy Watch6 Classic (47mm)", 48500, "Black;Silver", "", 8, "", "",
            Wt("1.5", "Super AMOLED, rotating bezel", "40 hours", "5ATM + IP68", "ECG, body composition, heart rate, sleep", "Android")),
        P("watch", "Xiaomi", "Redmi Watch 4", 9500, "Obsidian Black;Silver Gray", "", 7, "Budget Pick", "",
            Wt("1.97", "AMOLED", "20 days", "5ATM", "Heart rate, SpO2, sleep, GPS", "Android and iOS")),
        P("watch", "Xiaomi", "Xiaomi Smart Band 8", 4800, "Graphite Black;Champagne Gold;Ivory", "", 5, "Best Value", "",
            Wt("1.62", "AMOLED", "16 days", "5ATM", "Heart rate, SpO2, sleep", "Android and iOS")),

        // ---------------- Audio
        P("audio", "Apple", "AirPods Pro (2nd generation)", 33500, "White", "", 5, "Top Selling", "F",
            Au("True wireless earbuds", "6 h (30 h with case)", "Active", "Bluetooth 5.3, USB-C", "IP54")),
        P("audio", "Apple", "AirPods (3rd generation)", 24500, "White", "", 8, "", "",
            Au("True wireless earbuds", "6 h (30 h with case)", "No", "Bluetooth 5.0, Lightning", "IPX4")),
        P("audio", "Samsung", "Galaxy Buds2 Pro", 18500, "Graphite;White;Bora Purple", "", 12, "Hot", "",
            Au("True wireless earbuds", "5 h (18 h with case)", "Active", "Bluetooth 5.3, USB-C", "IPX7")),
        P("audio", "Sony", "Sony WH-1000XM5", 41500, "Black;Silver", "", 7, "Premium", "F",
            Au("Over-ear headphones", "30 h", "Active", "Bluetooth 5.2, 3.5mm, USB-C", "None")),
        P("audio", "Sony", "Sony WF-1000XM5", 33500, "Black;Silver", "", 6, "New", "",
            Au("True wireless earbuds", "8 h (24 h with case)", "Active", "Bluetooth 5.3, USB-C", "IPX4")),
        P("audio", "JBL", "JBL Flip 6", 12500, "Black;Blue;Red;Teal", "", 10, "Top Selling", "F",
            Au("Portable speaker", "12 h", "No", "Bluetooth 5.1, USB-C", "IP67")),
        P("audio", "JBL", "JBL Tune 520BT", 4200, "Black;White;Blue;Pink", "", 8, "Budget Pick", "",
            Au("On-ear headphones", "57 h", "No", "Bluetooth 5.3, USB-C", "None")),
        P("audio", "Anker", "Anker Soundcore Life Q30", 8900, "Black;Blue", "", 10, "Best Value", "",
            Au("Over-ear headphones", "40 h", "Hybrid active", "Bluetooth 5.0, 3.5mm, USB-C", "None")),
        P("audio", "Nothing", "Nothing Ear (a)", 11500, "Black;White;Yellow", "", 8, "", "",
            Au("True wireless earbuds", "9.5 h (42.5 h with case)", "Active", "Bluetooth 5.3, USB-C", "IP54")),

        // ---------------- Accessories
        P("acc", "Anker", "Anker 20W USB-C Fast Charger", 1450, "White;Black", "", 8, "Best Value", "F",
            Ac("Wall charger", "iPhone, Android, tablets", 20, "USB-C")),
        P("acc", "Anker", "Anker PowerCore 20000 Power Bank", 4200, "Black;Blue", "", 10, "Top Selling", "F",
            Ac("Power bank", "Phones, tablets, earbuds", 22, "USB-C, 2x USB-A")),
        P("acc", "Apple", "Apple 20W USB-C Power Adapter", 2600, "White", "", 0, "", "",
            Ac("Wall charger", "iPhone, iPad", 20, "USB-C")),
        P("acc", "Apple", "Apple MagSafe Charger", 4800, "White", "", 0, "", "",
            Ac("Wireless charger", "iPhone 12 and newer", 15, "Magnetic, USB-C")),
        P("acc", "Baseus", "Baseus 65W GaN Fast Charger", 3600, "Black;White", "", 12, "Hot", "",
            Ac("Wall charger", "Laptops, phones, tablets", 65, "2x USB-C, 1x USB-A")),
        P("acc", "Samsung", "Samsung 25W Super Fast Charger", 1950, "Black;White", "", 6, "", "",
            Ac("Wall charger", "Galaxy phones and tablets", 25, "USB-C")),
        P("acc", "Spigen", "Spigen Ultra Hybrid Case (iPhone 15)", 2400, "Crystal Clear;Matte Black", "", 10, "", "",
            Ac("Phone case", "iPhone 15", 0, "Clear back, raised edges")),
        P("acc", "Baseus", "Baseus Tempered Glass (iPhone 15)", 950, "Clear", "", 0, "", "",
            Ac("Screen protector", "iPhone 15", 0, "9H tempered glass")),
        P("acc", "Anker", "Anker 322 USB-C to USB-C Cable (1.8m)", 1100, "Black;White", "", 8, "", "",
            Ac("Charging cable", "Phones, tablets, laptops", 60, "USB-C to USB-C")),

        // ---------------- Smart TVs
        P("tv", "Samsung", "Samsung Crystal 4K UHD TV 43-inch", 56500, "Black", "", 8, "Top Selling", "F",
            Tv(43, "4K UHD 3840x2160", "LED", "Tizen", 60, "3x HDMI, 1x USB, Wi-Fi")),
        P("tv", "LG", "LG UHD 4K Smart TV 43-inch", 52500, "Black", "", 9, "", "",
            Tv(43, "4K UHD 3840x2160", "LED", "webOS", 60, "3x HDMI, 2x USB, Wi-Fi")),
        P("tv", "Sony", "Sony Bravia 4K Google TV 50-inch", 98500, "Black", "", 5, "Premium", "",
            Tv(50, "4K UHD 3840x2160", "LED", "Google TV", 60, "3x HDMI, 2x USB, Wi-Fi")),
        P("tv", "Xiaomi", "Xiaomi TV A 43-inch", 36500, "Black", "", 10, "Best Value", "",
            Tv(43, "Full HD 1920x1080", "LED", "Google TV", 60, "2x HDMI, 2x USB, Wi-Fi")),

        // ---------------- Monitors
        P("monitor", "LG", "LG 24MP400 24-inch IPS Monitor", 14500, "Black", "", 8, "Best Value", "",
            Mn(24, "Full HD 1920x1080", "IPS", 75, "5 ms", "HDMI, D-Sub")),
        P("monitor", "Samsung", "Samsung Odyssey G5 27-inch Curved", 36500, "Black", "", 10, "Gaming", "F",
            Mn(27, "QHD 2560x1440", "VA curved", 144, "1 ms", "HDMI, DisplayPort")),
        P("monitor", "Dell", "Dell S2421HN 24-inch IPS Monitor", 16800, "Black", "", 6, "", "",
            Mn(24, "Full HD 1920x1080", "IPS", 75, "4 ms", "2x HDMI")),
        P("monitor", "Asus", "Asus ProArt PA278QV 27-inch", 42500, "Black", "", 5, "Creator", "",
            Mn(27, "QHD 2560x1440", "IPS", 75, "5 ms", "HDMI, DisplayPort, DVI"))
    };
}
