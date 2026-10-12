// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Core.Media.Providers;
using System.Globalization;

namespace MediaExport.Search;

/// <summary>Says a search term in words, for where it is shown as text and not as its label and value.</summary>
public static class SearchTermText
{
    // The value at rest. A range reads as both ends around a dash, or one end after a sign for which end it is.
    public static string Describe(SearchTerm? term)
    {
        if (term is null || !SearchQuery.IsRange(term.Label))
            return term?.Value ?? string.Empty;

        var range = SearchRange.Parse(term.Value);
        var lower = DescribeEnd(range.Lower);
        var upper = DescribeEnd(range.Upper);
        if (lower.Length > 0 && upper.Length > 0)
            return $"{lower} \u2013 {upper}";

        return lower.Length > 0 ? $"> {lower}" : $"< {upper}";
    }

    // What the term asks of a result, as a sentence. A term with no value yet says nothing.
    public static string Explain(SearchTerm? term)
    {
        if (term is null || term.Value.Length == 0)
            return string.Empty;

        if (!SearchQuery.IsRange(term.Label))
        {
            return term.Label switch
            {
                SearchQuery.ExactLabel => $"Has the exact words \"{term.Value}\"",
                SearchQuery.ExcludeLabel => $"Doesn't have \"{term.Value}\"",
                SearchQuery.AuthorLabel => $"By {term.Value}",
                _ => term.ToString()
            };
        }

        var range = SearchRange.Parse(term.Value);
        var lower = DescribeEnd(range.Lower);
        var upper = DescribeEnd(range.Upper);
        var isDuration = term.Label == SearchQuery.DurationLabel;
        if (lower.Length > 0 && upper.Length > 0)
            return isDuration ? $"Between {lower} and {upper} long" : $"Published between {lower} and {upper}";

        if (lower.Length > 0)
            return isDuration ? $"Longer than {lower}" : $"Published after {lower}";

        return isDuration ? $"Shorter than {upper}" : $"Published before {upper}";
    }

    // A date reads as day, month and year, a year alone as its first of January. Anything else reads as written.
    public static string DescribeEnd(string end)
    {
        var date = SearchQuery.ReadDate(end);
        return date is null ? end : date.Value.ToString("d MMM yyyy", CultureInfo.CurrentCulture);
    }
}
