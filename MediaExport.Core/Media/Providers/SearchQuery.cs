// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using System.Text.RegularExpressions;

namespace MediaExport.Core.Media.Providers;

/// <summary>
/// One term of a search, written as <c>label:value</c>, or <c>label:"two words"</c> when the value has a
/// space in it. <c>source:youtube</c> names the provider to search.
/// </summary>
public sealed record SearchTerm(string Label, string Value)
{
    public override string ToString()
    {
        var hasSpace = Value.Any(char.IsWhiteSpace);
        return hasSpace ? $"{Label}:\"{Value}\"" : $"{Label}:{Value}";
    }
}

/// <summary>
/// A search as it is written in text: its terms, then the words to search for. In
/// <c>source:youtube exact:"lofi beats" study</c> there are two terms, and the text is <c>study</c>.
/// This is the form a search is copied and pasted in.
/// </summary>
/// <param name="Text">The words to search for, without any terms.</param>
public sealed partial record SearchQuery(string Text, IReadOnlyList<SearchTerm> Terms)
{
    /// <summary>The label of the term that names the provider to search.</summary>
    public const string SourceLabel = "source";

    /// <summary>The label of a term for a word or phrase that results must contain as it is written.</summary>
    public const string ExactLabel = "exact";

    /// <summary>The label of a term for a word or phrase that results must not contain.</summary>
    public const string ExcludeLabel = "exclude";

    /// <summary>The label of a term for who made the media.</summary>
    public const string AuthorLabel = "author";

    public static SearchTerm Source(string providerId) => new(SourceLabel, providerId);

    /// <summary>How many terms with a label one search can have. A search has one source and one author.</summary>
    public static int MaxCount(string label) => label switch
    {
        ExactLabel => 3,
        ExcludeLabel => 5,
        _ => 1
    };

    /// <summary>Splits text into its terms and the words left over.</summary>
    /// <param name="isKnown">
    /// Decides whether something written like a term really is one. Anything it turns down stays in the
    /// text as ordinary words, so a link such as <c>https://example.com</c> or a mistyped label is still searched for.
    /// </param>
    public static SearchQuery Parse(string text, Func<SearchTerm, bool> isKnown)
    {
        var terms = new List<SearchTerm>();
        var words = new List<string>();
        foreach (Match part in TermOrWord().Matches(text))
        {
            var term = ReadTerm(part);
            if (term is not null && isKnown(term))
                terms.Add(term);
            else
                words.Add(part.Value);
        }
        return new SearchQuery(string.Join(' ', words), terms);
    }

    public override string ToString()
    {
        var parts = Terms.Where(term => !string.IsNullOrWhiteSpace(term.Value)).Select(term => term.ToString()).ToList();
        if (Text.Length > 0)
            parts.Add(Text);

        return string.Join(' ', parts);
    }

    /// <summary>
    /// The search as the words a provider is sent, with the terms written the way search engines read them:
    /// an exact phrase in quotation marks, an excluded one after a minus sign. An author has no such form,
    /// so the name is sent as an exact phrase, which favours that author without ruling out others.
    /// </summary>
    public string ToSearchText()
    {
        var parts = new List<string>();
        if (Text.Length > 0)
            parts.Add(Text);

        foreach (var term in Terms)
        {
            // A term that is still being typed has no value yet.
            if (string.IsNullOrWhiteSpace(term.Value))
                continue;

            var quoted = $"\"{term.Value}\"";
            if (term.Label is ExactLabel or AuthorLabel)
                parts.Add(quoted);
            else if (term.Label == ExcludeLabel)
                parts.Add("-" + quoted);
        }
        return string.Join(' ', parts);
    }

    // A quoted term such as exact:"lofi beats" is one part even though it has a space in it; anything else is
    // split at spaces.
    [GeneratedRegex("""(?<label>[A-Za-z]+):"(?<quoted>[^"]+)"|\S+""")]
    private static partial Regex TermOrWord();

    // Reads label:value or label:"value", where the label is letters only.
    private static SearchTerm? ReadTerm(Match part)
    {
        if (part.Groups["quoted"].Success)
            return new SearchTerm(part.Groups["label"].Value.ToLowerInvariant(), part.Groups["quoted"].Value);

        var word = part.Value;
        var separator = word.IndexOf(':');
        var hasLabelAndValue = separator > 0 && separator < word.Length - 1;
        if (!hasLabelAndValue)
            return null;

        var label = word[..separator];
        var value = word[(separator + 1)..];
        return label.All(char.IsAsciiLetter) ? new SearchTerm(label.ToLowerInvariant(), value) : null;
    }
}
