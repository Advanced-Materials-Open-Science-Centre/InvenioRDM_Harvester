using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace ConverterPoC;

public class OpenXmlWordSearcher
{
    public static string ReadTextFromDocx(MemoryStream stream)
    {
        var textBuilder = new StringBuilder();

        using (var doc = WordprocessingDocument.Open(stream, false))
        {
            var body = doc.MainDocumentPart.Document.Body;
            foreach (var paragraph in body.Elements<Paragraph>())
            {
                textBuilder.AppendLine(paragraph.InnerText);
            }
        }

        return textBuilder.ToString();
    }
}