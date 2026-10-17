// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Core.Media.Models;
using MediaExport.Core.Media.Providers;

namespace MediaExport.Tests.Media.Providers;

public class SearchQueryTests
{
    [Test]
    public void Parse_WhenKnownTermsArePresent_SeparatesAndWritesTheQuery()
    {
        // Arrange
        const string input = "exact:\"lofi beats\" study exclude:live source:youtube";

        // Act
        var query = SearchQuery.Parse(input, _ => true);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(query.Text, Is.EqualTo("study"));
            Assert.That(query.Terms, Is.EqualTo(new[]
            {
                new SearchTerm("exact", "lofi beats"),
                new SearchTerm("exclude", "live"),
                new SearchTerm("source", "youtube")
            }));
            Assert.That(query.ToString(), Is.EqualTo("exact:\"lofi beats\" exclude:live source:youtube study"));
            Assert.That(query.ToSearchText(), Is.EqualTo("study \"lofi beats\" -\"live\""));
        });
    }

    [Test]
    public void ReadLink_WhenInputIsNotOnlyAWebAddress_ReturnsNull()
    {
        // Arrange
        var inputs = new[] { "lofi beats", "watch https://youtu.be/abc", "author:someone" };

        // Act
        var valid = SearchQuery.ReadLink(" https://youtu.be/abc ");
        var invalid = inputs.Select(SearchQuery.ReadLink).ToList();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(valid?.Host, Is.EqualTo("youtu.be"));
            Assert.That(invalid, Has.All.Null);
        });
    }

    [Test]
    public void Matches_WhenRangesArePresent_FiltersAndNormalizesResults()
    {
        // Arrange
        var query = new SearchQuery("song", [new("duration", "3m..10m"), new("published", "2024..")]);
        var within = Result(TimeSpan.FromMinutes(5), new DateTimeOffset(2024, 6, 1, 0, 0, 0, TimeSpan.Zero));
        var tooLong = within with { Duration = TimeSpan.FromMinutes(12) };
        var tooOld = within with { Published = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero) };

        // Act
        var normalizedDuration = SearchQuery.NormalizeEnd("duration", "1:30");
        var matches = new[] { query.Matches(within), query.Matches(tooLong), query.Matches(tooOld) };

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(normalizedDuration, Is.EqualTo("1h30m"));
            Assert.That(SearchQuery.IsValid(new SearchTerm("duration", "3m..10m")), Is.True);
            Assert.That(SearchQuery.IsValid(new SearchTerm("duration", "10m..3m")), Is.False);
            Assert.That(matches, Is.EqualTo(new[] { true, false, false }));
            Assert.That(query.ToSearchText(), Is.EqualTo("song"));
        });
    }

    private static MediaSearchResult Result(TimeSpan? duration, DateTimeOffset? published) =>
        new("id", "https://example.com/id", null, "Title", "Creator", duration, Published: published);
}
