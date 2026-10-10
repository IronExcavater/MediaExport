// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

namespace MediaExport.Core.Media.Providers;

/// <summary>One term of a search, written as <c>label:value</c>. <c>source:youtube</c> names the provider to search.</summary>
public sealed record SearchTerm(string Label, string Value)
{
    public override string ToString() => $"{Label}:{Value}";
}

/// <summary>
/// A search as it is written in text: its terms, then the words to search for. In
/// <c>source:youtube lofi beats</c> there is one term, and the text is <c>lofi beats</c>.
/// This is the form a search is copied and pasted in.
/// </summary>
/// <param name="Text">The words to search for, without any terms.</param>
public sealed record SearchQuery(string Text, IReadOnlyList<SearchTerm> Terms)
{
    /// <summary>The label of the term that names the provider to search.</summary>
    public const string SourceLabel = "source";

    public static SearchTerm Source(string providerId) => new(SourceLabel, providerId);

    /// <summary>Splits text into its terms and the words left over.</summary>
    /// <param name="isKnown">
    /// Decides whether something written like a term really is one. Anything it turns down stays in the
    /// text as ordinary words, so a link such as <c>https://example.com</c> or a mistyped label is still searched for.
    /// </param>
    public static SearchQuery Parse(string text, Func<SearchTerm, bool> isKnown)
    {
        var terms = new List<SearchTerm>();
        var words = new List<string>();
        foreach (var word in text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            var term = ReadTerm(word);
            if (term is not null && isKnown(term))
                terms.Add(term);
            else
                words.Add(word);
        }
        return new SearchQuery(string.Join(' ', words), terms);
    }

    public override string ToString()
    {
        var parts = Terms.Select(term => term.ToString()).ToList();
        if (Text.Length > 0)
            parts.Add(Text);

        return string.Join(' ', parts);
    }

    // Reads a word of the form label:value, where the label is letters only. A value can't contain spaces yet;
    // quoting it is the way to allow that when a term needs it.
    private static SearchTerm? ReadTerm(string word)
    {
        var separator = word.IndexOf(':');
        var hasLabelAndValue = separator > 0 && separator < word.Length - 1;
        if (!hasLabelAndValue)
            return null;

        var label = word[..separator];
        var value = word[(separator + 1)..];
        return label.All(char.IsAsciiLetter) ? new SearchTerm(label.ToLowerInvariant(), value) : null;
    }
}
