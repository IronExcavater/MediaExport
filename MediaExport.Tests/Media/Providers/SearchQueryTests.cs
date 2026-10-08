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
    [TestCase("song kind:video", "Concert,Unknown")]
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
    public void What_isnt_a_term_is_just_words()
    {
        Assert.Multiple(() =>
        {
            Assert.That(SearchQuery.Parse("re:zero kind:film longer:soon").Words, Is.EqualTo("re:zero kind:film longer:soon"));
            Assert.That(SearchQuery.Parse("song creator:").Words, Is.EqualTo("song"), "a label still waiting for its value");
            Assert.That(SearchQuery.Parse("creator:\"daft punk\"").Words, Is.EqualTo("daft punk"), "with no words, the creator is what's looked for");
        });
    }

    [Test]
    public void Source_terms_are_taken_out_once_they_are_finished_and_name_a_real_source()
    {
        static bool IsSource(string name) => name is "vimeo" or "YouTube Music";

        Assert.Multiple(() =>
        {
            Assert.That(SearchQuery.TakeSources("source:vimeo night song", false, IsSource), Is.EqualTo(("night song", new[] { "vimeo" })));
            Assert.That(SearchQuery.TakeSources("night song source:vim", false, IsSource).Sources, Is.Empty, "half a name, still being typed");
            Assert.That(SearchQuery.TakeSources("night song source:vimeo", false, IsSource).Sources, Is.Empty, "may yet be more to it");
            Assert.That(SearchQuery.TakeSources("night song source:vimeo", true, IsSource), Is.EqualTo(("night song ", new[] { "vimeo" })));
            Assert.That(SearchQuery.TakeSources("source:\"YouTube Music\" source:vimeo song", false, IsSource).Sources, Is.EqualTo(new[] { "YouTube Music", "vimeo" }));
            Assert.That(SearchQuery.TakeSources("source:nowhere song", true, IsSource), Is.EqualTo(("source:nowhere song", Array.Empty<string>())));
        });
    }
}
