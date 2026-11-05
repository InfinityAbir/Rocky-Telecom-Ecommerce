using FluentAssertions;
using RockyTelecom.Application.Features.Admin.Catalog;

namespace RockyTelecom.UnitTests;

/// <summary>FR-22: the CSV reader must be exact, because an import is all-or-nothing.</summary>
public class ProductCsvTests
{
    [Fact]
    public void Parse_reads_quoted_commas_quotes_and_newlines()
    {
        var rows = ProductCsvFormat.Parse("a,b\r\n\"x, y\",\"he said \"\"hi\"\"\"\r\n\"line1\nline2\",z\r\n");

        rows.Should().HaveCount(3);
        rows[1].Should().Equal("x, y", "he said \"hi\"");
        rows[2].Should().Equal("line1\nline2", "z");
    }

    [Fact]
    public void Parse_skips_blank_lines_and_a_byte_order_mark()
    {
        var rows = ProductCsvFormat.Parse("\uFEFFname,sku\n\n ,\nPhone,RT-1\n");

        rows.Should().HaveCount(2);
        rows[0][0].Should().Be("name");
        rows[1].Should().Equal("Phone", "RT-1");
    }

    [Theory]
    [InlineData("=1+1")]
    [InlineData("+9")]
    [InlineData("-5")]
    [InlineData("@SUM(A1)")]
    public void Guard_prefixes_formula_triggers_and_unguard_reverses_it(string value)
    {
        var guarded = ProductCsvFormat.Guard(value);

        guarded.Should().Be("'" + value);
        ProductCsvFormat.Unguard(guarded).Should().Be(value);
    }

    [Fact]
    public void Guard_leaves_ordinary_text_alone()
    {
        ProductCsvFormat.Guard("Galaxy A15").Should().Be("Galaxy A15");
        ProductCsvFormat.Unguard("'tis the season").Should().Be("'tis the season");
    }
}
