using System.Text;
using System.Text.RegularExpressions;

namespace SoMan.Services.Text;

/// <summary>
/// Splits a long text blob into Threads-friendly segments (default 500 chars each),
/// preferring paragraph and sentence boundaries before falling back to word
/// boundaries. Never cuts a word in half.
/// </summary>
public static class ThreadTextSplitter
{
    public const int ThreadsMaxCharsPerPost = 500;

    public static List<string> Split(string text, int maxCharsPerSegment = ThreadsMaxCharsPerPost)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new List<string>();

        if (maxCharsPerSegment <= 0)
            maxCharsPerSegment = ThreadsMaxCharsPerPost;

        // Normalise line endings
        text = text.Replace("\r\n", "\n").Replace("\r", "\n").Trim();

        var result = new List<string>();

        // 1) Split by blank-line paragraphs first
        var paragraphs = Regex.Split(text, @"\n\s*\n")
            .Select(p => p.Trim())
            .Where(p => p.Length > 0)
            .ToList();

        var current = new StringBuilder();

        foreach (var paragraph in paragraphs)
        {
            if (paragraph.Length > maxCharsPerSegment)
            {
                // Flush current segment before dealing with this oversized paragraph
                if (current.Length > 0)
                {
                    result.Add(current.ToString().Trim());
                    current.Clear();
                }

                foreach (var chunk in SplitLongParagraph(paragraph, maxCharsPerSegment))
                    result.Add(chunk);
                continue;
            }

            // Try to append this paragraph to the current segment (with \n\n join)
            int joinLen = current.Length == 0 ? 0 : 2; // "\n\n"
            if (current.Length + joinLen + paragraph.Length <= maxCharsPerSegment)
            {
                if (current.Length > 0) current.Append("\n\n");
                current.Append(paragraph);
            }
            else
            {
                if (current.Length > 0)
                {
                    result.Add(current.ToString().Trim());
                    current.Clear();
                }
                current.Append(paragraph);
            }
        }

        if (current.Length > 0)
            result.Add(current.ToString().Trim());

        return result;
    }

    /// <summary>
    /// Splits a single paragraph that is itself larger than maxCharsPerSegment.
    /// Prefers sentence boundaries, then word boundaries, never mid-word.
    /// </summary>
    private static IEnumerable<string> SplitLongParagraph(string paragraph, int maxChars)
    {
        // Break into sentences first — keep delimiter attached to previous sentence.
        var sentences = Regex.Split(paragraph, @"(?<=[\.\!\?…])\s+")
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList();

        var current = new StringBuilder();
        foreach (var sentence in sentences)
        {
            if (sentence.Length > maxChars)
            {
                // Flush current, then word-split the oversized sentence
                if (current.Length > 0)
                {
                    yield return current.ToString().Trim();
                    current.Clear();
                }

                foreach (var piece in SplitByWords(sentence, maxChars))
                    yield return piece;
                continue;
            }

            int joinLen = current.Length == 0 ? 0 : 1;
            if (current.Length + joinLen + sentence.Length <= maxChars)
            {
                if (current.Length > 0) current.Append(' ');
                current.Append(sentence);
            }
            else
            {
                yield return current.ToString().Trim();
                current.Clear();
                current.Append(sentence);
            }
        }

        if (current.Length > 0)
            yield return current.ToString().Trim();
    }

    /// <summary>
    /// Word-boundary split fallback. Still never cuts mid-word unless a single
    /// token itself exceeds the max (in which case hard-cut as last resort).
    /// </summary>
    private static IEnumerable<string> SplitByWords(string text, int maxChars)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var current = new StringBuilder();

        foreach (var word in words)
        {
            if (word.Length > maxChars)
            {
                if (current.Length > 0)
                {
                    yield return current.ToString().Trim();
                    current.Clear();
                }
                // Single token larger than max — hard-cut
                for (int i = 0; i < word.Length; i += maxChars)
                    yield return word.Substring(i, Math.Min(maxChars, word.Length - i));
                continue;
            }

            int joinLen = current.Length == 0 ? 0 : 1;
            if (current.Length + joinLen + word.Length <= maxChars)
            {
                if (current.Length > 0) current.Append(' ');
                current.Append(word);
            }
            else
            {
                yield return current.ToString().Trim();
                current.Clear();
                current.Append(word);
            }
        }

        if (current.Length > 0)
            yield return current.ToString().Trim();
    }
}
