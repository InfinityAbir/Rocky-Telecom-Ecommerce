using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;

namespace RockyTelecom.IntegrationTests;

/// <summary>End-to-end checks of the public API through the real HTTP pipeline and a real database.</summary>
[Collection("api")]
public sealed class StorefrontApiTests
{
    private readonly ApiFactory _factory;

    public StorefrontApiTests(ApiFactory factory) => _factory = factory;

    private static JsonElement Content(string json) => JsonDocument.Parse(json).RootElement.GetProperty("content");

    private async Task<HttpClient> AdminClientAsync()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { phone = ApiFactory.AdminPhone, password = ApiFactory.AdminPassword });
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var token = Content(await response.Content.ReadAsStringAsync()).GetProperty("accessToken").GetString();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    [SqlFact]
    public async Task Health_endpoint_reports_healthy()
    {
        var response = await _factory.CreateClient().GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [SqlFact]
    public async Task Responses_carry_the_security_headers()
    {
        var response = await _factory.CreateClient().GetAsync("/api/v1/categories");

        response.Headers.GetValues("X-Content-Type-Options").Should().Contain("nosniff");
        response.Headers.GetValues("X-Frame-Options").Should().Contain("DENY");
        response.Headers.Contains("Content-Security-Policy").Should().BeTrue();
        response.Headers.Contains("Referrer-Policy").Should().BeTrue();
    }

    [SqlFact]
    public async Task Admin_endpoints_reject_anonymous_callers()
    {
        var client = _factory.CreateClient();

        (await client.GetAsync("/api/v1/admin/products")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await client.GetAsync("/api/v1/admin/products/export")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await client.PostAsync("/api/v1/admin/products/import", new MultipartFormDataContent())).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [SqlFact]
    public async Task Category_menu_lists_brands_per_category()
    {
        var response = await _factory.CreateClient().GetAsync("/api/v1/categories/menu");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var menu = Content(await response.Content.ReadAsStringAsync());
        var phones = menu.EnumerateArray().First(c => c.GetProperty("slug").GetString() == "smartphones");
        phones.GetProperty("brands").GetArrayLength().Should().BeGreaterThan(5);
    }

    [SqlFact]
    public async Task Memory_and_storage_filter_options_are_sorted_by_size_not_alphabetically()
    {
        var response = await _factory.CreateClient().GetAsync("/api/v1/products?categorySlug=smartphones");
        var filters = Content(await response.Content.ReadAsStringAsync()).GetProperty("filters");

        var storage = filters.GetProperty("storage").EnumerateArray().Select(x => x.GetString()!).ToList();
        storage.IndexOf("128GB").Should().BeLessThan(storage.IndexOf("256GB"));
        storage.IndexOf("256GB").Should().BeLessThan(storage.IndexOf("1TB"));

        var ram = filters.GetProperty("ram").EnumerateArray().Select(x => x.GetString()!).ToList();
        ram.IndexOf("4GB").Should().BeLessThan(ram.IndexOf("12GB"));
    }

    [SqlFact]
    public async Task Phone_detail_has_the_full_spec_sheet()
    {
        var response = await _factory.CreateClient().GetAsync("/api/v1/products/iphone-15-pro");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var groups = Content(await response.Content.ReadAsStringAsync()).GetProperty("product").GetProperty("specGroups")
            .EnumerateArray().Select(g => g.GetProperty("name").GetString()).ToList();

        groups.Should().Contain(new[] { "Body", "Comms", "Memory", "Battery", "Display", "Platform", "Main camera", "Selfie camera" });
    }

    [SqlFact]
    public async Task Unknown_product_is_not_served()
    {
        var response = await _factory.CreateClient().GetAsync("/api/v1/products/this-does-not-exist");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [SqlFact]
    public async Task Exported_csv_can_be_re_imported_without_problems()
    {
        var client = await AdminClientAsync();

        var export = await client.GetAsync("/api/v1/admin/products/export?categorySlug=smartphones");
        export.StatusCode.Should().Be(HttpStatusCode.OK);
        var csv = await export.Content.ReadAsByteArrayAsync();

        using var form = new MultipartFormDataContent { { new ByteArrayContent(csv), "file", "products.csv" } };
        var report = await client.PostAsync("/api/v1/admin/products/import?dryRun=true", form);
        report.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = Content(await report.Content.ReadAsStringAsync());
        body.GetProperty("errors").GetArrayLength().Should().Be(0);
        body.GetProperty("productsToCreate").GetInt32().Should().Be(0);
        body.GetProperty("productsToUpdate").GetInt32().Should().BeGreaterThan(20);
    }

    [SqlFact]
    public async Task A_csv_with_errors_changes_nothing_and_lists_every_problem()
    {
        var client = await AdminClientAsync();
        var before = await ProductTotalAsync();

        const string bad =
            "name,slug,brand,category,sku,regular_price,stock\n" +
            "Ghost Phone,ghost-phone,NoSuchBrand,smartphones,RT-GHOST-1,abc,1\n" +
            "Ghost Phone,ghost-phone,Samsung,smartphones,RT-GHOST-1,1000,-5\n";

        using var form = new MultipartFormDataContent { { new ByteArrayContent(Encoding.UTF8.GetBytes(bad)), "file", "bad.csv" } };
        var response = await client.PostAsync("/api/v1/admin/products/import?dryRun=false", form);

        var report = Content(await response.Content.ReadAsStringAsync());
        report.GetProperty("applied").GetBoolean().Should().BeFalse();
        var columns = report.GetProperty("errors").EnumerateArray().Select(e => e.GetProperty("column").GetString()).ToList();
        columns.Should().Contain(new[] { "brand", "regular_price", "stock", "sku" });

        (await ProductTotalAsync()).Should().Be(before, "an import with any error must not write anything (FR-22)");
    }

    private async Task<int> ProductTotalAsync()
    {
        var response = await _factory.CreateClient().GetAsync("/api/v1/products?pageSize=1");
        return Content(await response.Content.ReadAsStringAsync()).GetProperty("page").GetProperty("total").GetInt32();
    }
}
