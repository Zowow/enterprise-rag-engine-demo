using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using UglyToad.PdfPig;

namespace EnterpriseRAG.Worker.Parsers;

public record ExtractedPage(int PageNumber, string Text);

public interface IDocumentTextExtractor
{
    Task<IReadOnlyList<ExtractedPage>> ExtractTextAsync(
        Stream contentStream,
        string fileExtension,
        CancellationToken cancellationToken = default);

    bool IsScannedOrEmpty(IReadOnlyList<ExtractedPage> pages);
}

public class DocumentTextExtractor : IDocumentTextExtractor
{
    private const int MinCharacterThreshold = 50;

    public async Task<IReadOnlyList<ExtractedPage>> ExtractTextAsync(
        Stream contentStream,
        string fileExtension,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(contentStream);

        if (contentStream.CanSeek)
        {
            contentStream.Position = 0;
        }

        var normalizedExt = (fileExtension ?? string.Empty).Trim().ToLowerInvariant();

        return normalizedExt switch
        {
            ".pdf" => ExtractFromPdf(contentStream),
            ".docx" => ExtractFromDocx(contentStream),
            ".md" or ".txt" or "" => await ExtractFromMarkdownOrTextAsync(contentStream, cancellationToken),
            _ => await ExtractFromMarkdownOrTextAsync(contentStream, cancellationToken)
        };
    }

    public bool IsScannedOrEmpty(IReadOnlyList<ExtractedPage> pages)
    {
        if (pages == null || pages.Count == 0)
        {
            return true;
        }

        var totalChars = pages.Sum(p => (p.Text ?? string.Empty).Trim().Length);
        return totalChars < MinCharacterThreshold;
    }

    private static IReadOnlyList<ExtractedPage> ExtractFromPdf(Stream stream)
    {
        var result = new List<ExtractedPage>();

        using var pdfDocument = PdfDocument.Open(stream);
        foreach (var page in pdfDocument.GetPages())
        {
            var pageText = page.Text ?? string.Empty;
            result.Add(new ExtractedPage(page.Number, pageText));
        }

        return result;
    }

    private static IReadOnlyList<ExtractedPage> ExtractFromDocx(Stream stream)
    {
        var result = new List<ExtractedPage>();

        using var wordDoc = WordprocessingDocument.Open(stream, false);
        var body = wordDoc.MainDocumentPart?.Document.Body;
        if (body == null)
        {
            return result;
        }

        var paragraphs = body.Descendants<Paragraph>()
            .Select(p => p.InnerText)
            .Where(t => !string.IsNullOrWhiteSpace(t));

        var fullText = string.Join("\n\n", paragraphs);
        result.Add(new ExtractedPage(1, fullText));

        return result;
    }

    private static async Task<IReadOnlyList<ExtractedPage>> ExtractFromMarkdownOrTextAsync(
        Stream stream,
        CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        var content = await reader.ReadToEndAsync(cancellationToken);
        return new List<ExtractedPage>
        {
            new ExtractedPage(1, content ?? string.Empty)
        };
    }
}
