using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using RockyTelecom.Domain.Entities;

namespace RockyTelecom.Infrastructure.Persistence.Demo;

/// <summary>
/// The phone spec sheet has eleven groups (Body, Comms, Sound, Memory, Battery, Display, Network, Features,
/// Platform, Main camera, Selfie camera). The curated catalogue rows only carry the headline specs, so the
/// remaining lines are derived from them with conservative rules. They are indicative demo values for a
/// showcase catalogue, not manufacturer data; the admin can overwrite any line.
/// </summary>
internal static class DemoPhoneSpecs
{
    /// <summary>Spec keys this class can fill in, in sheet order.</summary>
    internal static readonly string[] Keys =
    {
        "build", "sim", "wlan", "bluetooth", "positioning", "nfc", "usb", "loudspeaker", "audio_jack",
        "memory", "card_slot", "battery_type", "wireless_charging", "resolution", "protection",
        "sensors", "main_camera_features", "main_video", "selfie_video"
    };

    private static readonly Regex WifiGeneration = new(@"Wi-Fi\s*(\d+E?)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    internal static Dictionary<string, string> Extras(DemoProduct item, IReadOnlyDictionary<string, string> specs)
    {
        string V(string key) => specs.TryGetValue(key, out var v) ? v : string.Empty;

        var network = V("network");
        var apple = item.Brand == "Apple";
        var flagship = item.Price >= 100000;
        var upper = item.Price >= 60000;
        var budget = item.Price < 30000;
        var pro = item.Name.Contains("Pro", StringComparison.OrdinalIgnoreCase) || item.Name.Contains("Ultra", StringComparison.OrdinalIgnoreCase);
        var size = decimal.TryParse(V("display_size"), NumberStyles.Number, CultureInfo.InvariantCulture, out var s) ? s : 6.5m;

        var wifi = WifiGeneration.Match(network);
        var wifiGen = wifi.Success ? wifi.Groups[1].Value : "5";
        var wifiNumber = int.TryParse(wifiGen.TrimEnd('E', 'e'), out var n) ? n : 5;

        var extras = new Dictionary<string, string>
        {
            ["build"] = apple
                ? $"Glass front (Ceramic Shield), glass back, {(pro ? "titanium" : "aluminum")} frame"
                : flagship ? "Glass front, glass back, aluminum frame" : upper ? "Glass front, glass back, plastic frame" : "Glass front, plastic back, plastic frame",
            ["sim"] = apple ? "Nano-SIM + eSIM" : upper ? "Nano-SIM + Nano-SIM (dual stand-by), eSIM on select markets" : "Nano-SIM + Nano-SIM (dual stand-by)",
            ["wlan"] = wifiNumber >= 6
                ? $"Wi-Fi {wifiGen} (802.11 a/b/g/n/ac/{(wifiNumber >= 7 ? "be" : "ax")}), dual-band"
                : "Wi-Fi 802.11 a/b/g/n/ac, dual-band",
            ["bluetooth"] = wifiNumber >= 7 ? "5.4, A2DP, LE" : wifiNumber >= 6 ? "5.3, A2DP, LE" : "5.1, A2DP, LE",
            ["positioning"] = apple ? "GPS, GLONASS, GALILEO, QZSS, BDS" : "GPS, GLONASS, GALILEO, BDS",
            ["nfc"] = network.Contains("NFC", StringComparison.OrdinalIgnoreCase) ? "Yes" : "No",
            ["usb"] = network.Contains("USB-C", StringComparison.OrdinalIgnoreCase) || !apple
                ? (flagship ? "USB Type-C 3.2" : "USB Type-C 2.0")
                : "Lightning",
            ["loudspeaker"] = upper ? "Yes, with stereo speakers" : "Yes",
            ["audio_jack"] = budget && !apple && item.Brand is not ("Google" or "Nothing") ? "Yes" : "No",
            ["memory"] = MemoryLine(item),
            ["card_slot"] = budget && !apple && item.Brand is not ("Google" or "Nothing") ? "microSDXC (shared SIM slot)" : "No",
            ["battery_type"] = apple ? "Li-Ion" : "Li-Po",
            ["wireless_charging"] = flagship || (upper && pro) ? "Yes, 15W" : "No",
            ["resolution"] = Resolution(apple, flagship, upper, size),
            ["protection"] = apple
                ? "Ceramic Shield glass"
                : item.Brand == "Samsung" && flagship ? "Corning Gorilla Armor"
                : flagship ? "Corning Gorilla Glass Victus"
                : upper ? "Corning Gorilla Glass 5"
                : "Glass front (protection not specified)",
            ["sensors"] = apple
                ? "Face ID, accelerometer, gyro, proximity, compass, barometer"
                : "Fingerprint (under display), accelerometer, gyro, proximity, compass",
            ["main_camera_features"] = flagship ? "Laser/PDAF autofocus, OIS, LED flash, HDR, panorama" : upper ? "PDAF, OIS, LED flash, HDR, panorama" : "PDAF, LED flash, HDR, panorama",
            ["main_video"] = flagship ? "8K@30fps, 4K@60fps, 1080p@240fps, gyro-EIS, HDR" : upper ? "4K@60fps, 1080p@120fps, gyro-EIS" : "4K@30fps, 1080p@60fps",
            ["selfie_video"] = upper ? "4K@30fps, 1080p@60fps" : "1080p@30fps"
        };

        return extras;
    }

    private static string MemoryLine(DemoProduct item)
    {
        if (string.IsNullOrWhiteSpace(item.Configs))
        {
            return string.Empty;
        }

        var lines = item.Configs.Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(token => token.Split(':')[0].Split('/'))
            .Where(parts => parts.Length == 2)
            .Select(parts => $"{Format(parts[1])} {Format(parts[0])} RAM")
            .ToList();

        var storage = item.Brand == "Apple" ? "NVMe" : item.Price >= 60000 ? "UFS 4.0" : "UFS 3.1";
        return lines.Count == 0 ? string.Empty : $"{string.Join(", ", lines)}; {storage}";
    }

    private static string Format(string size) =>
        int.TryParse(size, out var gb) ? (gb >= 1024 ? $"{gb / 1024}TB" : $"{gb}GB") : size;

    private static string Resolution(bool apple, bool flagship, bool upper, decimal size)
    {
        if (apple)
        {
            return size >= 6.5m ? "1290 x 2796 pixels" : "1179 x 2556 pixels";
        }

        return flagship ? "1440 x 3120 pixels (QHD+)" : upper ? "1264 x 2780 pixels (FHD+)" : "1080 x 2400 pixels (FHD+)";
    }

    /// <summary>
    /// Adds the derived lines to phones that were seeded before the sheet grew. Only missing lines are added,
    /// so anything the admin typed in is never overwritten.
    /// </summary>
    internal static async Task<int> BackfillAsync(AppDbContext db, CancellationToken ct)
    {
        var phoneCategory = await db.Categories.FirstOrDefaultAsync(c => c.Slug == "smartphones", ct);
        if (phoneCategory is null)
        {
            return 0;
        }

        var template = await db.SpecTemplates
            .Include(t => t.Groups).ThenInclude(g => g.Fields)
            .FirstOrDefaultAsync(t => t.CategoryId == phoneCategory.Id, ct);
        if (template is null)
        {
            return 0;
        }

        var fields = template.Groups.SelectMany(g => g.Fields).ToDictionary(f => f.Key, f => f);
        var catalogue = DemoCatalogData.Products
            .Where(p => p.Cat == "phone")
            .ToDictionary(p => DemoDataSeeder.SlugFor(p.Name), p => p);

        var products = await db.Products
            .Include(p => p.Specs)
            .Where(p => p.CategoryId == phoneCategory.Id)
            .ToListAsync(ct);

        var added = 0;
        foreach (var product in products)
        {
            if (!catalogue.TryGetValue(product.Slug, out var item))
            {
                continue;
            }

            var current = product.Specs.Select(s => s.SpecFieldId).ToHashSet();
            var headline = item.Specs.Split(';', StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Split('=', 2))
                .Where(x => x.Length == 2)
                .ToDictionary(x => x[0], x => x[1]);

            var order = product.Specs.Count == 0 ? 1 : product.Specs.Max(s => s.SortOrder) + 1;
            foreach (var (key, value) in Extras(item, headline))
            {
                if (string.IsNullOrWhiteSpace(value) || !fields.TryGetValue(key, out var field) || current.Contains(field.Id))
                {
                    continue;
                }

                product.Specs.Add(new ProductSpec { SpecFieldId = field.Id, Value = value, SortOrder = order++ });
                added++;
            }
        }

        if (added > 0)
        {
            await db.SaveChangesAsync(ct);
        }

        return added;
    }
}
