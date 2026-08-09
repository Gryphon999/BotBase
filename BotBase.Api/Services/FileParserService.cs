using DocumentFormat.OpenXml.Packaging;
using ExcelDataReader;
using System.Text;
using UglyToad.PdfPig;

namespace BotBase.Api.Services;

public class FileParserService
{
    public string ExtractText(Stream stream, string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLower();
        return ext switch
        {
            ".pdf" => ExtractFromPdf(stream),
            ".docx" => ExtractFromWord(stream),
            ".xlsx" or ".xls" => ExtractFromExcel(stream),
            ".txt" => new StreamReader(stream).ReadToEnd(),
            _ => throw new NotSupportedException($"Формат {ext} не поддерживается. Используйте PDF, DOCX, XLSX или TXT.")
        };
    }

    private static string ExtractFromPdf(Stream stream)
    {
        using var pdf = PdfDocument.Open(stream);
        return string.Join("\n", pdf.GetPages().Select(p => p.Text));
    }

    private static string ExtractFromWord(Stream stream)
    {
        using var doc = WordprocessingDocument.Open(stream, false);
        var body = doc.MainDocumentPart?.Document?.Body;
        return body?.InnerText ?? string.Empty;
    }

    private static string ExtractFromExcel(Stream stream)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        using var reader = ExcelReaderFactory.CreateReader(stream);
        var sb = new StringBuilder();
        do
        {
            while (reader.Read())
            {
                for (var i = 0; i < reader.FieldCount; i++)
                    sb.Append(reader.GetValue(i)?.ToString()).Append('\t');
                sb.AppendLine();
            }
        } while (reader.NextResult());
        return sb.ToString();
    }
}
