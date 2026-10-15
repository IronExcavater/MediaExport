// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Core.Media.Providers;

namespace MediaExport.Tests.Media.Providers;

public class SearchQueryTests
{
    private static bool IsSource(SearchTerm term) => term.Label == SearchQuery.SourceLabel;

    [Test]
    public void KnownTermsAreTakenOutOfTheText()
    {
        var search = SearchQuery.Parse("  lofi Source:youtube   beats ", IsSource);

        Assert.Multiple(() =>
        {
            Assert.That(search.Text, Is.EqualTo("lofi beats"));
            Assert.That(search.Terms, Is.EqualTo(new[] { new SearchTerm("source", "youtube") }), "labels are read without regard to case");
        });
    }

    [Test]
    public void AnythingElseStaysInTheText()
    {
        foreach (var text in new[] { "https://example.com/watch", "creator:someone", "source:", "12:30" })
        {
            var search = SearchQuery.Parse(text, IsSource);
            Assert.That(search.Text, Is.EqualTo(text));
            Assert.That(search.Terms, Is.Empty);
        }
    }

    [Test]
    public void OnlyAWebAddressByItselfIsALink()
    {
        Assert.Multiple(() =>
        {
            Assert.That(SearchQuery.ReadLink(" https://youtu.be/abc ")?.Host, Is.EqualTo("youtu.be"));
            Assert.That(SearchQuery.ReadLink("lofi beats"), Is.Null);
            Assert.That(SearchQuery.ReadLink("watch https://youtu.be/abc"), Is.Null, "a link among words is searched for");
            Assert.That(SearchQuery.ReadLink("author:someone"), Is.Null, "a term looks like an address with a scheme");
        });
    }

    [Test]
    public void CreatorIsReadAsAuthor()
    {
        var search = SearchQuery.Parse("Creator:someone piano", term => term.Label == SearchQuery.AuthorLabel);

        Assert.That(search.Terms, Is.EqualTo(new[] { new SearchTerm("author", "someone") }));
    }

    [Test]
    public void ASearchIsWrittenWithItsTermsFirst()
    {
        var search = new SearchQuery("lofi beats", [SearchQuery.Source(["youtube"])]);

        Assert.Multiple(() =>
        {
            Assert.That(search.ToString(), Is.EqualTo("source:youtube lofi beats"));
            Assert.That(SearchQuery.Parse(search.ToString(), IsSource).ToString(), Is.EqualTo(search.ToString()), "what is written reads back the same");
        });
    }

    [Test]
    public void AQuotedValueMayHaveSpacesAndReadsBackTheSame()
    {
        var search = SearchQuery.Parse("exact:\"lofi beats\" study exclude:live", _ => true);

        Assert.Multiple(() =>
        {
            Assert.That(search.Text, Is.EqualTo("study"));
            Assert.That(search.Terms, Is.EqualTo(new[] { new SearchTerm("exact", "lofi beats"), new SearchTerm("exclude", "live") }));
            Assert.That(search.ToString(), Is.EqualTo("exact:\"lofi beats\" exclude:live study"));
        });
    }

    [Test]
    public void TermsAreSentToAProviderAsSearchOperators()
    {
        var search = new SearchQuery("study", [new("exact", "lofi beats"), new("exclude", "live"), new("author", "Chillhop"), SearchQuery.Source(["youtube"])]);

        Assert.That(search.ToSearchText(), Is.EqualTo("study \"lofi beats\" -\"live\" \"Chillhop\""));
    }

    [Test]
    public void ALengthIsReadInHoursMinutesAndSeconds()
    {
        Assert.Multiple(() =>
        {
            Assert.That(SearchQuery.ReadLength("90s"), Is.EqualTo(TimeSpan.FromSeconds(90)));
            Assert.That(SearchQuery.ReadLength("3m"), Is.EqualTo(TimeSpan.FromMinutes(3)));
            Assert.That(SearchQuery.ReadLength("1h30m"), Is.EqualTo(TimeSpan.FromMinutes(90)));
            Assert.That(SearchQuery.ReadLength("4"), Is.EqualTo(TimeSpan.FromMinutes(4)));
            Assert.That(SearchQuery.ReadLength("1:30"), Is.EqualTo(TimeSpan.FromMinutes(90)));
            Assert.That(SearchQuery.ReadLength("1h 30m"), Is.EqualTo(TimeSpan.FromMinutes(90)));
        });
    }

    [Test]
    public void ARangeIsValidWhenItsEndsCanBeReadAndLeaveRoom()
    {
        Assert.Multiple(() =>
        {
            Assert.That(SearchQuery.IsValid(new SearchTerm("duration", "3m..10m")), Is.True);
            Assert.That(SearchQuery.IsValid(new SearchTerm("duration", "3m..")), Is.True);
            Assert.That(SearchQuery.IsValid(new SearchTerm("duration", "..10m")), Is.True);
            Assert.That(SearchQuery.IsValid(new SearchTerm("duration", "..")), Is.False);
            Assert.That(SearchQuery.IsValid(new SearchTerm("duration", "soon..10m")), Is.False);
            Assert.That(SearchQuery.IsValid(new SearchTerm("duration", "10m..3m")), Is.False);
            Assert.That(SearchQuery.IsValid(new SearchTerm("published", "2024..2025-06")), Is.True);
            Assert.That(SearchQuery.IsValid(new SearchTerm("published", "June..")), Is.False);
        });
    }

    [Test]
    public void AnEndIsWrittenTheStandardWayHoweverItWasTyped()
    {
        Assert.Multiple(() =>
        {
            Assert.That(SearchQuery.NormalizeEnd("duration", "1:30"), Is.EqualTo("1h30m"));
            Assert.That(SearchQuery.NormalizeEnd("duration", "90"), Is.EqualTo("1h30m"));
            Assert.That(SearchQuery.NormalizeEnd("duration", "90s"), Is.EqualTo("1m30s"));
            Assert.That(SearchQuery.NormalizeEnd("duration", "soon"), Is.EqualTo("soon"));
            Assert.That(SearchQuery.NormalizeEnd("published", "2024"), Is.EqualTo("2024"));
            Assert.That(SearchQuery.NormalizeEnd("published", "1 June 2024"), Is.EqualTo("2024-06-01"));
            Assert.That(SearchQuery.NormalizeEnd("published", ""), Is.Empty);
        });
    }

    [Test]
    public void ResultsOutsideARangeOrWithoutTheValueItIsOnDontMatch()
    {
        var search = new SearchQuery("song", [new("duration", "3m..10m"), new("published", "2024..")]);
        var within = Result(TimeSpan.FromMinutes(5), new DateTimeOffset(2024, 6, 1, 0, 0, 0, TimeSpan.Zero));
        var tooLong = within with { Duration = TimeSpan.FromMinutes(12) };
        var tooOld = within with { Published = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero) };
        var live = within with { Duration = null };

        Assert.Multiple(() =>
        {
            Assert.That(search.Matches(within), Is.True);
            Assert.That(search.Matches(tooLong), Is.False);
            Assert.That(search.Matches(tooOld), Is.False);
            Assert.That(search.Matches(live), Is.False, "a result with no duration is outside a range of durations");
            Assert.That(search.ToSearchText(), Is.EqualTo("song"), "ranges aren't sent to the provider");
            Assert.That(search.ToString(), Is.EqualTo("duration:3m..10m published:2024.. song"));
        });
    }

    [Test]
    public void AnEndWrittenByItselfJoinsTheRangeAndTakesTheOtherEndAwayIfTheyClash()
    {
        var longer = SearchQuery.WithEnd(null, new SearchTerm("longer", "3m"));
        var both = SearchQuery.WithEnd(longer, new SearchTerm("shorter", "10m"));
        var clashing = SearchQuery.WithEnd(both, new SearchTerm("longer", "15m"));

        Assert.Multiple(() =>
        {
            Assert.That(longer, Is.EqualTo(new SearchTerm("duration", "3m..")));
            Assert.That(both, Is.EqualTo(new SearchTerm("duration", "3m..10m")));
            Assert.That(clashing, Is.EqualTo(new SearchTerm("duration", "15m..")), "the newer end is the one kept");
        });
    }

    private static MediaExport.Core.Media.Models.MediaSearchResult Result(TimeSpan? duration, DateTimeOffset? published) =>
        new("id", "https://example.com/id", null, "Title", "Creator", duration, Published: published);
}
