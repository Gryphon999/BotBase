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
            ".txt" => ExtractFromText(stream),
            _ => throw new NotSupportedException($"Формат {ext} не поддерживается. Используйте PDF, DOCX, XLSX или TXT.")
        };
    }

    private static string ExtractFromPdf(Stream stream)
    {
        using var pdf = PdfDocument.Open(stream);
        return string.Join("\n", pdf.GetPages().Select(p => p.Text));
    }

    // Блокнот Windows часто сохраняет русский текст в Windows-1251. Раньше такой файл читался как UTF-8
    // и превращался в «����» (на проде — 24 тыс. испорченных символов). Порядок: BOM → строгий UTF-8 → Windows-1251.
    private static string ExtractFromText(Stream stream)
    {
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        var bytes = ms.ToArray();
        using (var bomReader = new StreamReader(new MemoryStream(bytes), Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
        {
            bomReader.Peek();
            if (!Equals(bomReader.CurrentEncoding, Encoding.UTF8) || HasUtf8Bom(bytes))
                return bomReader.ReadToEnd();
        }
        try
        {
            return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(1251).GetString(bytes);
        }
    }

    private static bool HasUtf8Bom(byte[] b) => b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF;

    private static string ExtractFromWord(Stream stream)
    {
        using var doc = WordprocessingDocument.Open(stream, false);
        var body = doc.MainDocumentPart?.Document?.Body;
        if (body is null) return string.Empty;

        // Body.InnerText склеивает абзацы и ячейки без пробелов («...во дворе.Телефон», «покрытием60 мин»),
        // и ИИ получает кашу. Собираем текст по блокам: абзац — строка, строка таблицы — ячейки через « | ».
        var lines = new List<string>();
        foreach (var block in body.ChildElements)
        {
            if (block is DocumentFormat.OpenXml.Wordprocessing.Table table)
            {
                foreach (var row in table.Elements<DocumentFormat.OpenXml.Wordprocessing.TableRow>())
                    lines.Add(string.Join(" | ", row.Elements<DocumentFormat.OpenXml.Wordprocessing.TableCell>()
                        .Select(c => string.Join(" ", c.Elements<DocumentFormat.OpenXml.Wordprocessing.Paragraph>().Select(p => p.InnerText)).Trim())));
            }
            else if (block is DocumentFormat.OpenXml.Wordprocessing.Paragraph paragraph)
            {
                lines.Add(paragraph.InnerText);
            }
        }
        return string.Join("\n", lines).Trim();
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
