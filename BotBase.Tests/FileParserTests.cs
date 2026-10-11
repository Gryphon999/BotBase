using BotBase.Api.Services;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace BotBase.Tests;

public class FileParserTests
{
    static MemoryStream Docx(params OpenXmlElement[] body)
    {
        var ms = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(ms, WordprocessingDocumentType.Document))
        {
            doc.AddMainDocumentPart().Document = new Document(new Body(body));
        }
        ms.Position = 0;
        return ms;
    }

    static Paragraph P(string t) => new(new Run(new Text(t)));
    static TableRow Row(params string[] cells) => new(cells.Select(c => new TableCell(P(c))));

    [Fact]
    public void Word_paragraphs_and_table_cells_are_not_glued_together()
    {
        using var file = Docx(
            P("Парковка: бесплатная во дворе."),
            P("Телефон: +374 00 000000."),
            new Table(Row("Услуга", "Длительность", "Цена"), Row("Маникюр с покрытием", "60 мин", "2500")));

        var text = new FileParserService().ExtractText(file, "kb.docx");

        Assert.Contains("во дворе.\nТелефон", text);
        Assert.Contains("Маникюр с покрытием | 60 мин | 2500", text);
        Assert.DoesNotContain("покрытием60", text);
    }

    [Theory]
    [InlineData("windows-1251")]
    [InlineData("utf-8")]
    [InlineData("utf-8-bom")]
    [InlineData("utf-16")]
    public void Russian_txt_is_read_in_any_common_encoding(string encoding)
    {
        const string text = "Криолиполиз: противопоказания — беременность. Парковка во дворе.";
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        var bytes = encoding switch
        {
            "windows-1251" => System.Text.Encoding.GetEncoding(1251).GetBytes(text),
            "utf-8" => new System.Text.UTF8Encoding(false).GetBytes(text),
            "utf-8-bom" => [.. new System.Text.UTF8Encoding(true).GetPreamble(), .. System.Text.Encoding.UTF8.GetBytes(text)],
            _ => [.. System.Text.Encoding.Unicode.GetPreamble(), .. System.Text.Encoding.Unicode.GetBytes(text)],
        };
        Assert.Equal(text, new FileParserService().ExtractText(new MemoryStream(bytes), "kb.txt"));
    }
}
