// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Core.Media.Models;
using MediaExport.Core.Media.Providers;

namespace MediaExport.Tests.Media.Providers;

public sealed class SearchQueryTests
{
    private static readonly MediaSearchResult Studio = new(
        "1", "https://example.test/1", null, "Blue Night Song (hi-fi)", "The Band", TimeSpan.FromMinutes(4), MediaKind.Audio,
        Published: new DateTimeOffset(2019, 6, 1, 0, 0, 0, TimeSpan.Zero));

    private static readonly MediaSearchResult Concert = new(
        "2", "https://example.test/2", null, "Night Song live", "Someone Else", TimeSpan.FromMinutes(90), MediaKind.Video,
        Published: new DateTimeOffset(2023, 2, 1, 0, 0, 0, TimeSpan.Zero));

    private static readonly MediaSearchResult Unknown = new("3", "https://example.test/3", null, "Night Song", null, null);

    [Test]
    public void Sources_are_asked_for_the_words_and_the_other_terms_narrow_what_comes_back()
    {
        SearchQuery query = SearchQuery.Parse("blue \"night song\" -live hi-fi");

        Assert.Multiple(() =>
        {
            Assert.That(query.Words, Is.EqualTo("blue hi-fi night song"));
            Assert.That(query.Keep([Studio, Concert, Unknown]), Is.EqualTo(new[] { Studio, Unknown }), "the phrase, and not the unwanted word");
            Assert.That(SearchQuery.Parse("plain words").Keep([Concert]), Is.EqualTo(new[] { Concert }), "nothing to narrow by");
        });
    }

    [TestCase("song creator:band", "Studio")]
    [TestCase("song creator:\"someone else\"", "Concert")]
    [TestCase("song longer:10m", "Concert")]
    [TestCase("song shorter:1h30m1s longer:0:03:00", "Studio,Concert")]
    [TestCase("song before:2020", "Studio")]
    [TestCase("song after:2019-06 before:2023-02-02", "Studio,Concert")]
    public void Labelled_terms_keep_only_what_is_known_to_fit(string text, string kept)
    {
        var named = new Dictionary<MediaSearchResult, string> { [Studio] = "Studio", [Concert] = "Concert", [Unknown] = "Unknown" };

        Assert.That(string.Join(',', SearchQuery.Parse(text).Keep([Studio, Concert, Unknown]).Select(result => named[result])), Is.EqualTo(kept));
    }

    [Test]
    public void Kind_is_ordinary_search_text_not_a_filter()
    {
        SearchQuery query = SearchQuery.Parse("song kind:video");

        Assert.Multiple(() =>
        {
            Assert.That(query.Words, Is.EqualTo("song kind:video"));
            Assert.That(query.Keep([Studio, Concert, Unknown]), Is.EqualTo(new[] { Studio, Concert, Unknown }));
        });
    }

    [Test]
    public void What_isnt_a_term_is_just_words()
    {
        Assert.Multiple(() =>
        {
            Assert.That(SearchQuery.Parse("re:zero kind:film longer:soon").Words, Is.EqualTo("re:zero kind:film longer:soon"));
            Assert.That(SearchQuery.Parse("song creator:").Words, Is.EqualTo("song"), "a label still waiting for its value");
            Assert.That(SearchQuery.Parse("creator:\"daft punk\"").Words, Is.EqualTo("daft punk"), "with no words, the creator is what's looked for");
        });
    }

    [TestCase("longer:NaN")]
    [TestCase("shorter:1e9999")]
    [TestCase("longer:999999999999999999999999999999999999999999m")]
    public void Invalid_or_overlarge_durations_stay_ordinary_text_without_throwing(string text)
    {
        SearchExtraction extraction = SearchQuery.ExtractTerms(text, includeUnfinished: true, _ => false);

        Assert.Multiple(() =>
        {
            Assert.That(extraction.Text, Is.EqualTo(text));
            Assert.That(extraction.Terms, Is.Empty);
            Assert.That(SearchQuery.Parse(text).Words, Is.EqualTo(text));
        });
    }

    [Test]
    public void Extracting_terms_preserves_unrecognised_punctuation_between_tokens()
    {
        SearchExtraction extraction = SearchQuery.ExtractTerms(
            "rock\"n\"roll source:youtube ", includeUnfinished: true, name => name == "youtube");

        Assert.Multiple(() =>
        {
            Assert.That(extraction.Text, Is.EqualTo("rock\"n\"roll"));
            Assert.That(extraction.Sources, Is.EqualTo(new[] { "youtube" }));
        });
    }

    [Test]
    public void Every_editable_term_and_known_source_is_extracted_without_losing_other_text()
    {
        SearchExtraction extraction = SearchQuery.ExtractTerms(
            "night \"blue moon\" -live creator:\"The Band\" longer:10m shorter:2h before:2024 after:2020-06 source:vimeo kind:video later:",
            includeUnfinished: true,
            name => name == "vimeo");

        Assert.Multiple(() =>
        {
            Assert.That(extraction.Text.Trim(), Is.EqualTo("night kind:video later:"));
            Assert.That(extraction.Terms, Is.EqualTo(new[]
            {
                new ParsedSearchTerm(SearchTermType.ExactPhrase, "blue moon"),
                new ParsedSearchTerm(SearchTermType.ExcludedWord, "live"),
                new ParsedSearchTerm(SearchTermType.Creator, "The Band"),
                new ParsedSearchTerm(SearchTermType.LongerThan, "10m"),
                new ParsedSearchTerm(SearchTermType.ShorterThan, "2h"),
                new ParsedSearchTerm(SearchTermType.Before, "2024"),
                new ParsedSearchTerm(SearchTermType.After, "2020-06")
            }));
            Assert.That(extraction.Sources, Is.EqualTo(new[] { "vimeo" }));
        });
    }

    [Test]
    public void Editable_terms_and_sources_have_one_canonical_text_form()
    {
        Assert.Multiple(() =>
        {
            Assert.That(SearchQuery.Write(SearchTermType.ExactPhrase, "blue moon"), Is.EqualTo("\"blue moon\""));
            Assert.That(SearchQuery.Write(SearchTermType.ExcludedWord, "live"), Is.EqualTo("-live"));
            Assert.That(SearchQuery.Write(SearchTermType.Creator, "The Band"), Is.EqualTo("creator:\"The Band\""));
            Assert.That(SearchQuery.Write(SearchTermType.LongerThan, "10m"), Is.EqualTo("longer:10m"));
            Assert.That(SearchQuery.WriteSource("YouTube Music"), Is.EqualTo("source:\"YouTube Music\""));
        });
    }

    [Test]
    public void Extracted_terms_round_trip_through_the_query_parser()
    {
        string text = string.Join(' ', new[]
        {
            "song",
            SearchQuery.Write(SearchTermType.ExactPhrase, "night song"),
            SearchQuery.Write(SearchTermType.ExcludedWord, "live"),
            SearchQuery.Write(SearchTermType.Creator, "The Band"),
            SearchQuery.Write(SearchTermType.LongerThan, "3m"),
            SearchQuery.Write(SearchTermType.Before, "2020")
        });

        Assert.That(SearchQuery.Parse(text).Keep([Studio, Concert, Unknown]), Is.EqualTo(new[] { Studio }));
    }

    [Test]
    public void Quoted_term_values_round_trip_without_losing_quotes_or_backslashes()
    {
        const string creator = "Dwayne \"The Rock\" Johnson";
        const string phrase = "C:\\Music \"Live\"";
        string text = $"{SearchQuery.Write(SearchTermType.Creator, creator)} {SearchQuery.Write(SearchTermType.ExactPhrase, phrase)}";

        SearchExtraction extraction = SearchQuery.ExtractTerms(text, includeUnfinished: true, _ => false);

        Assert.Multiple(() =>
        {
            Assert.That(SearchQuery.Write(SearchTermType.Creator, creator), Is.EqualTo("creator:\"Dwayne \\\"The Rock\\\" Johnson\""));
            Assert.That(SearchQuery.Write(SearchTermType.ExactPhrase, phrase), Is.EqualTo("\"C:\\\\Music \\\"Live\\\"\""));
            Assert.That(extraction.Text, Is.Empty);
            Assert.That(extraction.Terms, Is.EqualTo(new[]
            {
                new ParsedSearchTerm(SearchTermType.Creator, creator),
                new ParsedSearchTerm(SearchTermType.ExactPhrase, phrase)
            }));
        });
    }

    [Test]
    public void Excluded_values_that_look_like_labelled_terms_stay_exclusions()
    {
        const string value = "source:youtube";
        string written = SearchQuery.Write(SearchTermType.ExcludedWord, value);

        SearchExtraction extraction = SearchQuery.ExtractTerms(written, includeUnfinished: true, _ => true);

        Assert.Multiple(() =>
        {
            Assert.That(written, Is.EqualTo("-\"source:youtube\""));
            Assert.That(extraction.Sources, Is.Empty);
            Assert.That(extraction.Terms, Is.EqualTo(new[] { new ParsedSearchTerm(SearchTermType.ExcludedWord, value) }));
        });
    }
}
