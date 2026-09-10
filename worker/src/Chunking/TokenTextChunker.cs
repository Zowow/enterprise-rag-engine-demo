using System;
using System.Collections.Generic;
using System.Linq;
using EnterpriseRAG.Worker.Parsers;

namespace EnterpriseRAG.Worker.Chunking;

public record DocumentChunk(
    int ChunkIndex,
    int PageNumber,
    string Text,
    int TokenCount);

public interface ITokenTextChunker
{
    IReadOnlyList<DocumentChunk> Chunk(IReadOnlyList<ExtractedPage> pages);
}

public class TokenTextChunker : ITokenTextChunker
{
    private readonly int _targetChunkTokens;
    private readonly int _overlapTokens;

    // A standard rule of thumb: ~0.75 words per token (or ~1.33 words = 1 token approx)
    // 500 tokens ~ 375 words; 50 tokens ~ 38 words
    public TokenTextChunker(int targetChunkTokens = 500, int overlapTokens = 50)
    {
        _targetChunkTokens = targetChunkTokens > 0 ? targetChunkTokens : 500;
        _overlapTokens = overlapTokens >= 0 && overlapTokens < _targetChunkTokens ? overlapTokens : 50;
    }

    public IReadOnlyList<DocumentChunk> Chunk(IReadOnlyList<ExtractedPage> pages)
    {
        if (pages == null || pages.Count == 0)
        {
            return Array.Empty<DocumentChunk>();
        }

        var chunks = new List<DocumentChunk>();
        var globalChunkIndex = 0;

        foreach (var page in pages)
        {
            var text = page.Text?.Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            var words = text.Split(new[] { ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0)
            {
                continue;
            }

            // Approximate tokens as words * 1.33, or chunk by word counts
            var targetWords = (int)Math.Max(10, Math.Round(_targetChunkTokens * 0.75));
            var overlapWords = (int)Math.Max(0, Math.Round(_overlapTokens * 0.75));
            var stepWords = Math.Max(1, targetWords - overlapWords);

            for (var i = 0; i < words.Length; i += stepWords)
            {
                var takeCount = Math.Min(targetWords, words.Length - i);
                var chunkWords = words.Skip(i).Take(takeCount);
                var chunkText = string.Join(" ", chunkWords).Trim();

                if (string.IsNullOrWhiteSpace(chunkText))
                {
                    continue;
                }

                var estimatedTokens = (int)Math.Ceiling(takeCount / 0.75);

                chunks.Add(new DocumentChunk(
                    ChunkIndex: globalChunkIndex++,
                    PageNumber: page.PageNumber,
                    Text: chunkText,
                    TokenCount: estimatedTokens));

                if (i + takeCount >= words.Length)
                {
                    break;
                }
            }
        }

        return chunks;
    }
}
