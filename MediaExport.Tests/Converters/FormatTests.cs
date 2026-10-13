// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Converters;

namespace MediaExport.Tests.Converters;

public class FormatTests
{
    [TestCase(187, "3:07")]
    [TestCase(3787, "1:03:07")]
    public void TimesShowHoursOnlyFromAnHourUp(int seconds, string expected)
    {
        Assert.That(Format.Time(TimeSpan.FromSeconds(seconds)), Is.EqualTo(expected));
    }

    [TestCase(812, "812")]
    [TestCase(4_200, "4.2K")]
    [TestCase(1_900_000, "1.9M")]
    [TestCase(2_800_000_000, "2.8B")]
    public void CountsAreWrittenShort(long count, string expected)
    {
        Assert.That(Format.Count(count), Is.EqualTo(expected));
    }

    [TestCase(0, "today")]
    [TestCase(1, "1 day ago")]
    [TestCase(12, "12 days ago")]
    [TestCase(70, "2 months ago")]
    [TestCase(800, "2 years ago")]
    public void AgesUseTheLargestUnitThatFits(int daysAgo, string expected)
    {
        var now = new DateTimeOffset(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

        Assert.That(Format.Age(now.AddDays(-daysAgo), now), Is.EqualTo(expected));
    }

    [Test]
    public void ViewsAndAgeAreEmptyWhenTheyArentKnown()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Format.Views(1), Is.EqualTo("1 view"));
            Assert.That(Format.Views(4_200), Is.EqualTo("4.2K views"));
            Assert.That(Format.Views(null), Is.Empty);
            Assert.That(Format.Age(null), Is.Empty);
        });
    }
}
