using System.Text;
using Microsoft.EntityFrameworkCore;
using RockyTelecom.Domain.Entities;

namespace RockyTelecom.Infrastructure.Persistence.Demo;

/// <summary>
/// Demo content that sits beside the catalogue: a few helpful blog posts, approved reviews and the
/// showroom's map position. Every part is idempotent so it also runs on a database that already has
/// the catalogue. Text is original; replace it with the shop's own through the admin panel.
/// </summary>
internal static class DemoExtras
{
    private const string UploadsRoot = "/uploads/demo";

    private sealed record Post(string Slug, string Title, string Excerpt, int Hue, int DaysAgo, string Body);

    private static readonly Post[] Posts =
    {
        new("how-to-choose-your-next-smartphone", "How to choose your next smartphone",
            "Battery, camera, storage and warranty: five questions that make the choice easier.", 222, 3,
            "Choosing a phone is easier when you start with how you use it, not with the spec sheet.\n\n"
            + "## Start with your budget\n\nDecide the most you are comfortable paying, then look one step below it. Mid-range phones today have bright screens, good cameras and all-day batteries, so the gap to flagships is smaller than it used to be.\n\n"
            + "## Battery and charging\n\nA 5000 mAh battery comfortably lasts a full day for most people. Fast charging matters just as much: a phone that gets to half in half an hour is easier to live with than one with a slightly bigger battery that charges slowly.\n\n"
            + "## Cameras\n\nMegapixels are only part of the story. Look at sample photos in low light and ask to try the camera at the showroom. For video, check whether the phone offers stabilisation.\n\n"
            + "## Storage and memory\n\n128 GB is enough if you stream music and video. Choose 256 GB if you shoot a lot of video or keep many apps. More RAM helps with multitasking and keeps the phone smooth for longer.\n\n"
            + "## Warranty matters\n\nCheck whether the phone is an official variant with manufacturer or distributor warranty. The warranty type is shown on every product page, and you can bring any phone you buy from us to the showroom for help."),
        new("how-emi-works-at-rocky-telecom", "How EMI works at Rocky Telecom",
            "Spread the cost of a new phone or laptop over several months with a partner bank card.", 28, 9,
            "EMI lets you pay for a product in monthly instalments using a credit card from a partner bank.\n\n"
            + "## Using the calculator\n\nOn eligible product pages you will see an EMI table. Pick a bank and the number of months to see the estimated monthly amount. The table only appears when the price is above the bank's minimum.\n\n"
            + "## What the calculator shows\n\nThe calculator is an estimate. The final plan, any interest and the approval are set by your bank, so ask the showroom team to confirm the details before you order.\n\n"
            + "## Zero-interest plans\n\nSome banks offer short plans without interest on selected products. When a plan has no interest, the monthly amount is simply the price divided by the number of months.\n\n"
            + "## Ready to buy?\n\nOrder online and choose your payment method at checkout, or visit the showroom and our team will set up the EMI with you at the counter."),
        new("make-your-phone-battery-last-longer", "Make your phone battery last longer",
            "Simple habits that keep a battery healthy and your day uninterrupted.", 160, 16,
            "A few small habits make a real difference to battery life, today and over the years.\n\n"
            + "## Keep it cool\n\nHeat is the enemy of batteries. Avoid leaving your phone in a hot car or under a pillow while it charges, and take off a thick case if the phone gets warm.\n\n"
            + "## Charge sensibly\n\nYou do not need to wait until the battery is empty. Short top-ups are fine. If your phone has a charge-limit option, using it for overnight charging helps the battery age more slowly.\n\n"
            + "## Tame the screen\n\nThe display uses the most power. Lower the brightness, shorten the screen timeout and use a dark theme if your phone has an OLED screen.\n\n"
            + "## Use the original charger\n\nA good charger from the phone's maker, or a trusted brand, protects the battery and the phone. Cheap unbranded chargers can deliver unstable power."),
        new("buying-a-used-phone-what-to-check", "Buying a used phone: what to check",
            "A short checklist to run through before you pay for a second-hand handset.", 330, 24,
            "A used phone can be a great value if you check it properly.\n\n"
            + "## Check the screen and body\n\nLook for cracks, dead pixels and burn-in. Turn the brightness up and scroll through a white page to spot marks. Press all the buttons and test the speakers and microphone.\n\n"
            + "## Test the basics\n\nTry the cameras, the fingerprint or face unlock, the SIM slot, Wi-Fi and Bluetooth. Make a call and check the charging port with a cable.\n\n"
            + "## Battery health\n\nBattery health is usually shown in the settings. A heavily worn battery is a cost you will pay soon, so factor it into the price.\n\n"
            + "## Ask about warranty\n\nA short shop warranty gives you peace of mind. Our used phones are checked before they are listed and carry a short warranty from the showroom.")
    };

    internal static async Task SeedAsync(AppDbContext db, DateTime now, string artDir, CancellationToken ct)
    {
        await SeedBlogsAsync(db, now, artDir, ct);
        await SeedReviewsAsync(db, now, ct);
        await SeedStoreMapAsync(db, ct);
    }

    private static async Task SeedBlogsAsync(AppDbContext db, DateTime now, string artDir, CancellationToken ct)
    {
        if (await db.BlogPosts.AnyAsync(ct))
        {
            return;
        }

        var style = 0;
        foreach (var post in Posts)
        {
            var file = $"blog-{post.Slug}.svg";
            await File.WriteAllTextAsync(Path.Combine(artDir, file), DemoArt.Banner(post.Title, "Rocky Telecom guide", "", post.Hue, style++), new UTF8Encoding(false), ct);

            db.BlogPosts.Add(new BlogPost
            {
                Slug = post.Slug,
                Title = post.Title,
                Excerpt = post.Excerpt,
                Cover = $"{UploadsRoot}/{file}",
                Body = post.Body,
                IsPublished = true,
                PublishedAt = now.AddDays(-post.DaysAgo),
                SeoTitle = post.Title.Length <= 70 ? post.Title : post.Title[..70],
                SeoDesc = post.Excerpt,
                CreatedAt = now.AddDays(-post.DaysAgo)
            });
        }

        await db.SaveChangesAsync(ct);
    }

    private static async Task SeedReviewsAsync(AppDbContext db, DateTime now, CancellationToken ct)
    {
        if (await db.Reviews.AnyAsync(ct))
        {
            return;
        }

        (string Product, int Rating, string Body)[] reviews =
        {
            ("Galaxy A55 5G", 5, "Smooth screen and the battery easily lasts the day. Great value for the price."),
            ("Galaxy A55 5G", 4, "Camera is very good in daylight. Delivery to Gaibandha was quick."),
            ("Galaxy A55 5G", 5, "Genuine product with warranty, exactly as described."),
            ("iPhone 15 Pro Max", 5, "Excellent camera and the titanium body feels premium. Worth it."),
            ("iPhone 15 Pro Max", 5, "Fast, bright display and great battery life for a phone this size."),
            ("iPhone 15 Pro Max", 4, "Great phone. A little heavy but I got used to it."),
            ("Redmi Note 13 Pro+ 5G", 5, "Best phone I have used at this price. The 200MP camera is impressive."),
            ("Redmi Note 13 Pro+ 5G", 4, "Charges very fast. Software has a few extra apps but you can remove them."),
            ("Galaxy S24 Ultra", 5, "The zoom camera is unbelievable. Screen is the best I have seen."),
            ("Galaxy S24 Ultra", 5, "Everything is fast and the S Pen is handy for notes."),
            ("OnePlus 12", 5, "Very smooth and charges in about half an hour."),
            ("JBL Flip 6", 5, "Loud and clear for its size, and it survived the rain at a picnic."),
            ("JBL Flip 6", 4, "Good bass. Battery lasts a long party."),
            ("AirPods Pro (2nd generation)", 5, "Noise cancelling is excellent on the bus. Comfortable for hours."),
            ("AirPods Pro (2nd generation)", 4, "Sound is great. Wish the case was a bit smaller."),
            ("MacBook Air 13-inch (M2)", 5, "Silent, fast and the battery lasts all day at college."),
            ("MacBook Air 13-inch (M2)", 5, "Perfect for coding and writing. Very light to carry."),
            ("Anker PowerCore 20000 Power Bank", 5, "Charges my phone four times. Solid build."),
            ("Sony WH-1000XM5", 5, "Best noise cancelling I have tried, and very comfortable."),
            ("Lenovo IdeaPad Slim 3 (Core i5)", 4, "Good everyday laptop for office work and Zoom classes.")
        };

        var names = reviews.Select(r => r.Product).Distinct().ToList();
        var products = await db.Products.Where(p => names.Contains(p.Name)).Select(p => new { p.Id, p.Name }).ToListAsync(ct);
        var perProduct = new Dictionary<int, int>();

        foreach (var review in reviews)
        {
            var product = products.FirstOrDefault(p => p.Name == review.Product);
            if (product is null)
            {
                continue;
            }

            perProduct[product.Id] = perProduct.GetValueOrDefault(product.Id) + 1;

            db.Reviews.Add(new Review
            {
                ProductId = product.Id,
                // Demo reviewers are anonymous ids, never real accounts.
                UserId = $"demo-buyer-{product.Id}-{perProduct[product.Id]}",
                Rating = review.Rating,
                Body = review.Body,
                IsApproved = true,
                CreatedAt = now.AddDays(-(3 + perProduct[product.Id] * 4 + product.Id % 5))
            });
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Approximate position of Gaibandha town so the store page can show a map. The owner should set the
    /// exact showroom coordinates in the admin settings.
    /// </summary>
    private static async Task SeedStoreMapAsync(AppDbContext db, CancellationToken ct)
    {
        var store = await db.StoreInfos.OrderBy(s => s.Id).FirstOrDefaultAsync(ct);
        if (store is null || store.MapLat.HasValue)
        {
            return;
        }

        store.MapLat = 25.3297m;
        store.MapLng = 89.5430m;
        await db.SaveChangesAsync(ct);
    }
}
