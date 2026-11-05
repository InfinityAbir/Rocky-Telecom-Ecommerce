using System.Globalization;
using System.Security;
using System.Text;

namespace RockyTelecom.Infrastructure.Persistence.Demo;

/// <summary>
/// Original, generated artwork for the demo catalogue: simple device illustrations, banners and
/// brand wordmarks drawn as SVG. Nothing here is copied from any manufacturer or retailer, so the
/// demo can be shown to a client without licensing questions. Real product photos replace these
/// through the admin panel.
/// </summary>
internal static class DemoArt
{
    private const string Font = "'DM Sans','Segoe UI',Arial,sans-serif";

    // ------------------------------------------------------------------ colours

    private static readonly (string Key, string Hex)[] ColorKeywords =
    {
        ("natural", "#b9aea0"), ("titanium", "#8d8b86"), ("midnight", "#1f2430"), ("starlight", "#e9e3d6"),
        ("graphite", "#41444a"), ("phantom", "#2b2d33"), ("onyx", "#1b1c20"), ("black", "#1d1f24"),
        ("white", "#f1f1ee"), ("cream", "#efe6d2"), ("lavender", "#b9a9d6"), ("violet", "#7c5cc4"),
        ("purple", "#7a4fb3"), ("pink", "#f2b5c6"), ("rose", "#e7a9a0"), ("red", "#c4312f"),
        ("orange", "#ee7b30"), ("yellow", "#f2d04b"), ("gold", "#d6b46a"), ("green", "#3f7d5c"),
        ("mint", "#a9d8c1"), ("teal", "#2f8f8b"), ("cyan", "#4fb7c9"), ("sky", "#8cc3e8"),
        ("blue", "#3b66c4"), ("navy", "#223a73"), ("silver", "#c9ccd1"), ("grey", "#7d838c"),
        ("gray", "#7d838c"), ("space", "#4a4d55"), ("bronze", "#a9714b"), ("sand", "#d8c3a0")
    };

    internal static string ColorHex(string colorName)
    {
        var name = colorName.ToLowerInvariant();

        foreach (var (key, hex) in ColorKeywords)
        {
            if (name.Contains(key, StringComparison.Ordinal))
            {
                return hex;
            }
        }

        return "#7d838c";
    }

    /// <summary>Stable pastel hue derived from a name, used for brand tints.</summary>
    private static int Hue(string seed)
    {
        unchecked
        {
            var hash = 17;
            foreach (var ch in seed.ToLowerInvariant())
            {
                hash = hash * 31 + ch;
            }

            return Math.Abs(hash) % 360;
        }
    }

    private static string Esc(string value) => SecurityElement.Escape(value) ?? string.Empty;

    private static string F(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>Darkens or lightens a #rrggbb colour by a factor (-1..1).</summary>
    private static string Shade(string hex, double amount)
    {
        var r = Convert.ToInt32(hex.Substring(1, 2), 16);
        var g = Convert.ToInt32(hex.Substring(3, 2), 16);
        var b = Convert.ToInt32(hex.Substring(5, 2), 16);

        int Adjust(int channel) => amount >= 0
            ? (int)Math.Round(channel + (255 - channel) * amount)
            : (int)Math.Round(channel * (1 + amount));

        return $"#{Adjust(r):x2}{Adjust(g):x2}{Adjust(b):x2}";
    }

    // ------------------------------------------------------------------ product images

    /// <summary>One 800×800 illustration. <paramref name="view"/> 1 = front/open, 2 = back/alternate.</summary>
    internal static string Product(string category, string brand, string name, string colorHex, int view)
    {
        var hue = Hue(brand);
        var bgA = $"hsl({hue},45%,94%)";
        var bgB = $"hsl({(hue + 28) % 360},50%,86%)";
        var accent = $"hsl({(hue + 200) % 360},70%,55%)";

        var sb = new StringBuilder();
        sb.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 800 800\" role=\"img\" aria-label=\"")
          .Append(Esc(name)).Append("\">");
        sb.Append("<defs>");
        sb.Append($"<linearGradient id=\"bg\" x1=\"0\" y1=\"0\" x2=\"1\" y2=\"1\"><stop offset=\"0\" stop-color=\"{bgA}\"/><stop offset=\"1\" stop-color=\"{bgB}\"/></linearGradient>");
        sb.Append($"<linearGradient id=\"body\" x1=\"0\" y1=\"0\" x2=\"1\" y2=\"1\"><stop offset=\"0\" stop-color=\"{Shade(colorHex, 0.16)}\"/><stop offset=\"1\" stop-color=\"{Shade(colorHex, -0.22)}\"/></linearGradient>");
        sb.Append($"<linearGradient id=\"screen\" x1=\"0\" y1=\"0\" x2=\"1\" y2=\"1\"><stop offset=\"0\" stop-color=\"#0e1424\"/><stop offset=\"0.55\" stop-color=\"#1d2a52\"/><stop offset=\"1\" stop-color=\"{accent}\"/></linearGradient>");
        sb.Append("<filter id=\"soft\" x=\"-20%\" y=\"-20%\" width=\"140%\" height=\"140%\"><feGaussianBlur stdDeviation=\"14\"/></filter>");
        sb.Append("</defs>");
        sb.Append("<rect width=\"800\" height=\"800\" fill=\"url(#bg)\"/>");
        sb.Append($"<circle cx=\"{(view == 1 ? 610 : 190)}\" cy=\"170\" r=\"230\" fill=\"#ffffff\" opacity=\"0.35\"/>");
        sb.Append("<ellipse cx=\"400\" cy=\"700\" rx=\"230\" ry=\"22\" fill=\"#1f2937\" opacity=\"0.18\" filter=\"url(#soft)\"/>");

        switch (category)
        {
            case "phone": Phone(sb, view, 0.5, accent); break;
            case "tablet": Tablet(sb, view); break;
            case "laptop": Laptop(sb, view); break;
            case "watch": Watch(sb, view, colorHex); break;
            case "audio": Audio(sb, name, colorHex, view); break;
            case "acc": Accessory(sb, name, colorHex, view); break;
            case "tv": Television(sb, view); break;
            case "monitor": Monitor(sb, view); break;
            default: Phone(sb, view, 0.5, accent); break;
        }

        var label = name.Length > 34 ? name[..34] + "…" : name;
        sb.Append($"<text x=\"400\" y=\"752\" text-anchor=\"middle\" font-family=\"{Font}\" font-size=\"26\" font-weight=\"700\" fill=\"#1f2937\">{Esc(label)}</text>");
        sb.Append("</svg>");
        return sb.ToString();
    }

    private static void Phone(StringBuilder sb, int view, double scale, string accent)
    {
        sb.Append("<g>");
        sb.Append("<rect x=\"268\" y=\"96\" width=\"264\" height=\"560\" rx=\"46\" fill=\"url(#body)\"/>");
        if (view == 1)
        {
            sb.Append("<rect x=\"280\" y=\"108\" width=\"240\" height=\"536\" rx=\"36\" fill=\"url(#screen)\"/>");
            sb.Append("<rect x=\"372\" y=\"124\" width=\"56\" height=\"16\" rx=\"8\" fill=\"#05070d\"/>");
            sb.Append("<circle cx=\"350\" cy=\"430\" r=\"84\" fill=\"#ffffff\" opacity=\"0.10\"/>");
            sb.Append($"<circle cx=\"452\" cy=\"520\" r=\"58\" fill=\"{accent}\" opacity=\"0.35\"/>");
            sb.Append("<text x=\"400\" y=\"250\" text-anchor=\"middle\" font-family=\"" + Font + "\" font-size=\"64\" font-weight=\"300\" fill=\"#ffffff\" opacity=\"0.92\">9:41</text>");
            sb.Append("<rect x=\"372\" y=\"622\" width=\"56\" height=\"5\" rx=\"2.5\" fill=\"#ffffff\" opacity=\"0.6\"/>");
        }
        else
        {
            sb.Append("<rect x=\"290\" y=\"116\" width=\"110\" height=\"128\" rx=\"30\" fill=\"#000000\" opacity=\"0.22\"/>");
            sb.Append("<circle cx=\"322\" cy=\"150\" r=\"20\" fill=\"#0b0d12\"/><circle cx=\"322\" cy=\"150\" r=\"10\" fill=\"#2a3350\"/>");
            sb.Append("<circle cx=\"322\" cy=\"210\" r=\"20\" fill=\"#0b0d12\"/><circle cx=\"322\" cy=\"210\" r=\"10\" fill=\"#2a3350\"/>");
            sb.Append("<circle cx=\"374\" cy=\"180\" r=\"20\" fill=\"#0b0d12\"/><circle cx=\"374\" cy=\"180\" r=\"10\" fill=\"#2a3350\"/>");
            sb.Append("<circle cx=\"376\" cy=\"128\" r=\"7\" fill=\"#fff6c4\" opacity=\"0.9\"/>");
            sb.Append("<path d=\"M268 230 Q400 330 532 250 L532 300 Q400 400 268 290 Z\" fill=\"#ffffff\" opacity=\"0.07\"/>");
        }

        sb.Append("</g>");
        _ = scale;
    }

    private static void Tablet(StringBuilder sb, int view)
    {
        sb.Append("<rect x=\"170\" y=\"120\" width=\"460\" height=\"560\" rx=\"40\" fill=\"url(#body)\"/>");
        if (view == 1)
        {
            sb.Append("<rect x=\"186\" y=\"136\" width=\"428\" height=\"528\" rx=\"28\" fill=\"url(#screen)\"/>");
            sb.Append("<circle cx=\"400\" cy=\"150\" r=\"5\" fill=\"#05070d\"/>");
            sb.Append("<rect x=\"230\" y=\"230\" width=\"150\" height=\"90\" rx=\"16\" fill=\"#ffffff\" opacity=\"0.14\"/>");
            sb.Append("<rect x=\"400\" y=\"230\" width=\"170\" height=\"90\" rx=\"16\" fill=\"#ffffff\" opacity=\"0.09\"/>");
            sb.Append("<rect x=\"230\" y=\"350\" width=\"340\" height=\"150\" rx=\"20\" fill=\"#ffffff\" opacity=\"0.10\"/>");
        }
        else
        {
            sb.Append("<rect x=\"196\" y=\"146\" width=\"78\" height=\"78\" rx=\"22\" fill=\"#000000\" opacity=\"0.22\"/>");
            sb.Append("<circle cx=\"222\" cy=\"172\" r=\"14\" fill=\"#0b0d12\"/><circle cx=\"250\" cy=\"198\" r=\"14\" fill=\"#0b0d12\"/>");
        }
    }

    private static void Laptop(StringBuilder sb, int view)
    {
        if (view == 1)
        {
            sb.Append("<rect x=\"170\" y=\"190\" width=\"460\" height=\"300\" rx=\"18\" fill=\"url(#body)\"/>");
            sb.Append("<rect x=\"186\" y=\"204\" width=\"428\" height=\"272\" rx=\"8\" fill=\"url(#screen)\"/>");
            sb.Append("<circle cx=\"400\" cy=\"197\" r=\"3\" fill=\"#05070d\"/>");
            sb.Append("<rect x=\"230\" y=\"250\" width=\"150\" height=\"96\" rx=\"10\" fill=\"#ffffff\" opacity=\"0.12\"/>");
            sb.Append("<rect x=\"400\" y=\"250\" width=\"170\" height=\"36\" rx=\"10\" fill=\"#ffffff\" opacity=\"0.14\"/>");
            sb.Append("<rect x=\"400\" y=\"302\" width=\"170\" height=\"44\" rx=\"10\" fill=\"#ffffff\" opacity=\"0.08\"/>");
            sb.Append("<path d=\"M110 496 L690 496 L736 540 Q740 556 722 556 L78 556 Q60 556 64 540 Z\" fill=\"url(#body)\"/>");
            sb.Append("<rect x=\"350\" y=\"500\" width=\"100\" height=\"10\" rx=\"5\" fill=\"#000000\" opacity=\"0.18\"/>");
        }
        else
        {
            sb.Append("<path d=\"M130 250 L670 250 L700 560 L100 560 Z\" fill=\"url(#body)\"/>");
            sb.Append("<circle cx=\"400\" cy=\"400\" r=\"34\" fill=\"#ffffff\" opacity=\"0.22\"/>");
            sb.Append("<path d=\"M130 250 L670 250 L676 300 L124 300 Z\" fill=\"#ffffff\" opacity=\"0.08\"/>");
        }
    }

    private static void Watch(StringBuilder sb, int view, string colorHex)
    {
        sb.Append($"<rect x=\"325\" y=\"60\" width=\"150\" height=\"160\" rx=\"34\" fill=\"{Shade(colorHex, -0.35)}\"/>");
        sb.Append($"<rect x=\"325\" y=\"560\" width=\"150\" height=\"160\" rx=\"34\" fill=\"{Shade(colorHex, -0.35)}\"/>");
        sb.Append("<rect x=\"262\" y=\"180\" width=\"276\" height=\"420\" rx=\"84\" fill=\"url(#body)\"/>");
        sb.Append("<rect x=\"540\" y=\"340\" width=\"18\" height=\"70\" rx=\"9\" fill=\"#000000\" opacity=\"0.28\"/>");
        if (view == 1)
        {
            sb.Append("<rect x=\"280\" y=\"198\" width=\"240\" height=\"384\" rx=\"68\" fill=\"url(#screen)\"/>");
            sb.Append("<text x=\"400\" y=\"360\" text-anchor=\"middle\" font-family=\"" + Font + "\" font-size=\"88\" font-weight=\"300\" fill=\"#ffffff\">10:09</text>");
            sb.Append("<circle cx=\"340\" cy=\"468\" r=\"28\" fill=\"none\" stroke=\"#7ee0b3\" stroke-width=\"8\"/>");
            sb.Append("<circle cx=\"400\" cy=\"468\" r=\"28\" fill=\"none\" stroke=\"#ff8a9b\" stroke-width=\"8\"/>");
            sb.Append("<circle cx=\"460\" cy=\"468\" r=\"28\" fill=\"none\" stroke=\"#8fc5ff\" stroke-width=\"8\"/>");
        }
        else
        {
            sb.Append("<circle cx=\"400\" cy=\"390\" r=\"92\" fill=\"#000000\" opacity=\"0.22\"/>");
            sb.Append("<circle cx=\"400\" cy=\"390\" r=\"44\" fill=\"#7ee0b3\" opacity=\"0.55\"/>");
        }
    }

    private static void Audio(StringBuilder sb, string name, string colorHex, int view)
    {
        var n = name.ToLowerInvariant();
        var dark = Shade(colorHex, -0.45);

        if (n.Contains("flip") || n.Contains("speaker") || n.Contains("charge") || n.Contains("go "))
        {
            sb.Append("<rect x=\"170\" y=\"300\" width=\"460\" height=\"220\" rx=\"110\" fill=\"url(#body)\"/>");
            sb.Append("<g fill=\"#000000\" opacity=\"0.18\">");
            for (var x = 230; x <= 570; x += 28) sb.Append($"<circle cx=\"{x}\" cy=\"410\" r=\"7\"/>");
            for (var x = 244; x <= 556; x += 28) sb.Append($"<circle cx=\"{x}\" cy=\"380\" r=\"7\"/><circle cx=\"{x}\" cy=\"440\" r=\"7\"/>");
            sb.Append("</g>");
            sb.Append($"<rect x=\"360\" y=\"470\" width=\"80\" height=\"10\" rx=\"5\" fill=\"{dark}\"/>");
        }
        else if (n.Contains("buds") || n.Contains("airpods") || n.Contains("ear ") || n.Contains("ear(") || n.Contains("wf-") || n.Contains("pods"))
        {
            // Case + two buds
            sb.Append("<rect x=\"240\" y=\"360\" width=\"320\" height=\"240\" rx=\"96\" fill=\"url(#body)\"/>");
            sb.Append("<path d=\"M240 450 L560 450\" stroke=\"#000000\" stroke-opacity=\"0.14\" stroke-width=\"4\"/>");
            sb.Append($"<circle cx=\"400\" cy=\"520\" r=\"9\" fill=\"{dark}\"/>");
            var tilt = view == 1 ? 0 : 6;
            sb.Append($"<g transform=\"rotate({-14 + tilt} 300 260)\"><ellipse cx=\"300\" cy=\"230\" rx=\"60\" ry=\"70\" fill=\"url(#body)\"/><rect x=\"280\" y=\"270\" width=\"36\" height=\"110\" rx=\"18\" fill=\"url(#body)\"/></g>");
            sb.Append($"<g transform=\"rotate({14 - tilt} 500 260)\"><ellipse cx=\"500\" cy=\"230\" rx=\"60\" ry=\"70\" fill=\"url(#body)\"/><rect x=\"484\" y=\"270\" width=\"36\" height=\"110\" rx=\"18\" fill=\"url(#body)\"/></g>");
        }
        else
        {
            // Over-ear headphones
            sb.Append($"<path d=\"M230 440 Q230 170 400 170 Q570 170 570 440\" fill=\"none\" stroke=\"{dark}\" stroke-width=\"40\" stroke-linecap=\"round\"/>");
            sb.Append("<rect x=\"170\" y=\"360\" width=\"130\" height=\"240\" rx=\"62\" fill=\"url(#body)\"/>");
            sb.Append("<rect x=\"500\" y=\"360\" width=\"130\" height=\"240\" rx=\"62\" fill=\"url(#body)\"/>");
            sb.Append("<rect x=\"206\" y=\"410\" width=\"58\" height=\"140\" rx=\"29\" fill=\"#000000\" opacity=\"0.2\"/>");
            sb.Append("<rect x=\"536\" y=\"410\" width=\"58\" height=\"140\" rx=\"29\" fill=\"#000000\" opacity=\"0.2\"/>");
        }
    }

    private static void Accessory(StringBuilder sb, string name, string colorHex, int view)
    {
        var n = name.ToLowerInvariant();
        var dark = Shade(colorHex, -0.45);

        if (n.Contains("power bank") || n.Contains("powercore") || n.Contains("powerbank"))
        {
            sb.Append("<rect x=\"280\" y=\"130\" width=\"240\" height=\"500\" rx=\"44\" fill=\"url(#body)\"/>");
            sb.Append("<g fill=\"#7ee0b3\"><circle cx=\"330\" cy=\"190\" r=\"9\"/><circle cx=\"365\" cy=\"190\" r=\"9\"/><circle cx=\"400\" cy=\"190\" r=\"9\"/><circle cx=\"435\" cy=\"190\" r=\"9\" opacity=\"0.35\"/></g>");
            sb.Append($"<rect x=\"330\" y=\"580\" width=\"140\" height=\"14\" rx=\"7\" fill=\"{dark}\"/>");
        }
        else if (n.Contains("case") || n.Contains("cover"))
        {
            sb.Append("<rect x=\"268\" y=\"110\" width=\"264\" height=\"540\" rx=\"50\" fill=\"url(#body)\" opacity=\"0.92\"/>");
            sb.Append("<rect x=\"290\" y=\"130\" width=\"118\" height=\"132\" rx=\"32\" fill=\"#000000\" opacity=\"0.25\"/>");
            sb.Append("<circle cx=\"326\" cy=\"170\" r=\"20\" fill=\"#0b0d12\"/><circle cx=\"326\" cy=\"226\" r=\"20\" fill=\"#0b0d12\"/><circle cx=\"380\" cy=\"198\" r=\"20\" fill=\"#0b0d12\"/>");
        }
        else if (n.Contains("glass") || n.Contains("protector"))
        {
            sb.Append("<rect x=\"268\" y=\"110\" width=\"264\" height=\"540\" rx=\"46\" fill=\"#ffffff\" opacity=\"0.5\" stroke=\"#9aa6b8\" stroke-width=\"4\"/>");
            sb.Append("<path d=\"M290 140 L400 140 L290 330 Z\" fill=\"#ffffff\" opacity=\"0.6\"/>");
        }
        else if (n.Contains("cable"))
        {
            sb.Append($"<path d=\"M200 560 Q300 260 400 420 T600 240\" fill=\"none\" stroke=\"{colorHex}\" stroke-width=\"18\" stroke-linecap=\"round\"/>");
            sb.Append("<rect x=\"150\" y=\"540\" width=\"70\" height=\"48\" rx=\"10\" fill=\"#2a2f3a\"/><rect x=\"590\" y=\"214\" width=\"70\" height=\"48\" rx=\"10\" fill=\"#2a2f3a\"/>");
        }
        else
        {
            // Wall charger
            sb.Append("<rect x=\"290\" y=\"250\" width=\"220\" height=\"280\" rx=\"38\" fill=\"url(#body)\"/>");
            sb.Append("<rect x=\"352\" y=\"188\" width=\"22\" height=\"70\" rx=\"6\" fill=\"#c7ccd4\"/><rect x=\"426\" y=\"188\" width=\"22\" height=\"70\" rx=\"6\" fill=\"#c7ccd4\"/>");
            sb.Append($"<rect x=\"336\" y=\"470\" width=\"128\" height=\"34\" rx=\"17\" fill=\"{dark}\"/>");
            sb.Append("<circle cx=\"400\" cy=\"360\" r=\"46\" fill=\"#ffffff\" opacity=\"0.14\"/>");
        }

        _ = view;
    }

    private static void Television(StringBuilder sb, int view)
    {
        sb.Append("<rect x=\"110\" y=\"170\" width=\"580\" height=\"360\" rx=\"16\" fill=\"#10131c\"/>");
        sb.Append("<rect x=\"122\" y=\"182\" width=\"556\" height=\"336\" rx=\"8\" fill=\"url(#screen)\"/>");
        sb.Append($"<path d=\"M122 400 Q300 {(view == 1 ? 300 : 340)} 460 410 T678 360 L678 518 L122 518 Z\" fill=\"#ffffff\" opacity=\"0.10\"/>");
        sb.Append("<circle cx=\"560\" cy=\"260\" r=\"44\" fill=\"#ffffff\" opacity=\"0.14\"/>");
        sb.Append("<path d=\"M330 560 L470 560 L500 600 L300 600 Z\" fill=\"#2a2f3a\"/>");
        sb.Append("<rect x=\"250\" y=\"600\" width=\"300\" height=\"14\" rx=\"7\" fill=\"#2a2f3a\"/>");
    }

    private static void Monitor(StringBuilder sb, int view)
    {
        sb.Append("<rect x=\"140\" y=\"180\" width=\"520\" height=\"320\" rx=\"20\" fill=\"#10131c\"/>");
        sb.Append("<rect x=\"152\" y=\"192\" width=\"496\" height=\"290\" rx=\"10\" fill=\"url(#screen)\"/>");
        sb.Append($"<rect x=\"190\" y=\"{(view == 1 ? 240 : 270)}\" width=\"200\" height=\"120\" rx=\"12\" fill=\"#ffffff\" opacity=\"0.12\"/>");
        sb.Append("<rect x=\"420\" y=\"240\" width=\"190\" height=\"50\" rx=\"12\" fill=\"#ffffff\" opacity=\"0.14\"/>");
        sb.Append("<rect x=\"372\" y=\"500\" width=\"56\" height=\"110\" fill=\"#2a2f3a\"/>");
        sb.Append("<rect x=\"290\" y=\"606\" width=\"220\" height=\"18\" rx=\"9\" fill=\"#2a2f3a\"/>");
    }

    // ------------------------------------------------------------------ banners & logos

    /// <summary>A 1600×520 promotional banner with a headline, a sub-line and decorative shapes.</summary>
    internal static string Banner(string title, string subtitle, string cta, int hue, int style)
    {
        var a = $"hsl({hue},62%,32%)";
        var b = $"hsl({(hue + 40) % 360},70%,48%)";
        var c = $"hsl({(hue + 320) % 360},80%,70%)";

        var sb = new StringBuilder();
        sb.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 1600 520\" role=\"img\" aria-label=\"").Append(Esc(title)).Append("\">");
        sb.Append($"<defs><linearGradient id=\"g\" x1=\"0\" y1=\"0\" x2=\"1\" y2=\"1\"><stop offset=\"0\" stop-color=\"{a}\"/><stop offset=\"1\" stop-color=\"{b}\"/></linearGradient></defs>");
        sb.Append("<rect width=\"1600\" height=\"520\" fill=\"url(#g)\"/>");

        switch (style % 3)
        {
            case 0:
                sb.Append($"<circle cx=\"1320\" cy=\"140\" r=\"300\" fill=\"{c}\" opacity=\"0.22\"/>");
                sb.Append("<circle cx=\"1480\" cy=\"430\" r=\"190\" fill=\"#ffffff\" opacity=\"0.10\"/>");
                sb.Append("<rect x=\"1180\" y=\"90\" width=\"170\" height=\"340\" rx=\"30\" fill=\"#0e1424\" opacity=\"0.85\" transform=\"rotate(10 1265 260)\"/>");
                sb.Append($"<rect x=\"1196\" y=\"106\" width=\"138\" height=\"308\" rx=\"22\" fill=\"{c}\" opacity=\"0.55\" transform=\"rotate(10 1265 260)\"/>");
                break;
            case 1:
                sb.Append("<path d=\"M900 520 Q1120 200 1600 280 L1600 520 Z\" fill=\"#ffffff\" opacity=\"0.10\"/>");
                sb.Append($"<circle cx=\"1240\" cy=\"250\" r=\"150\" fill=\"{c}\" opacity=\"0.35\"/>");
                sb.Append("<circle cx=\"1240\" cy=\"250\" r=\"96\" fill=\"#ffffff\" opacity=\"0.14\"/>");
                sb.Append("<rect x=\"1050\" y=\"330\" width=\"380\" height=\"24\" rx=\"12\" fill=\"#ffffff\" opacity=\"0.25\"/>");
                break;
            default:
                sb.Append($"<rect x=\"1080\" y=\"60\" width=\"380\" height=\"250\" rx=\"22\" fill=\"#0e1424\" opacity=\"0.85\"/>");
                sb.Append($"<rect x=\"1096\" y=\"76\" width=\"348\" height=\"218\" rx=\"12\" fill=\"{c}\" opacity=\"0.5\"/>");
                sb.Append("<path d=\"M1020 330 L1520 330 L1570 400 L970 400 Z\" fill=\"#0e1424\" opacity=\"0.85\"/>");
                sb.Append("<circle cx=\"1500\" cy=\"450\" r=\"120\" fill=\"#ffffff\" opacity=\"0.08\"/>");
                break;
        }

        sb.Append($"<text x=\"110\" y=\"230\" font-family=\"{Font}\" font-size=\"84\" font-weight=\"800\" fill=\"#ffffff\">{Esc(title)}</text>");
        sb.Append($"<text x=\"112\" y=\"300\" font-family=\"{Font}\" font-size=\"34\" font-weight=\"500\" fill=\"#ffffff\" opacity=\"0.88\">{Esc(subtitle)}</text>");

        if (!string.IsNullOrWhiteSpace(cta))
        {
            var width = 60 + cta.Length * 17;
            sb.Append($"<rect x=\"112\" y=\"350\" width=\"{width}\" height=\"64\" rx=\"32\" fill=\"#ffffff\"/>");
            sb.Append($"<text x=\"{112 + width / 2}\" y=\"392\" text-anchor=\"middle\" font-family=\"{Font}\" font-size=\"26\" font-weight=\"700\" fill=\"{a}\">{Esc(cta)}</text>");
        }

        sb.Append("</svg>");
        return sb.ToString();
    }

    /// <summary>Text wordmark tile for a brand (no real logos are reproduced).</summary>
    internal static string BrandMark(string brand)
    {
        var hue = Hue(brand);
        var size = brand.Length > 9 ? 44 : brand.Length > 6 ? 54 : 64;

        return "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 320 120\" role=\"img\" aria-label=\"" + Esc(brand) + "\">"
               + $"<text x=\"160\" y=\"76\" text-anchor=\"middle\" font-family=\"{Font}\" font-size=\"{size}\" font-weight=\"800\" letter-spacing=\"-1\" fill=\"hsl({hue},55%,32%)\">{Esc(brand)}</text>"
               + "</svg>";
    }
}
