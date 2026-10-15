// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Converters;

namespace MediaExport.Tests.Converters;

public class FormatTests
{
    [Test]
    public void TimesShowHoursOnlyFromAnHourUp()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Format.Time(TimeSpan.FromSeconds(187)), Is.EqualTo("3:07"));
            Assert.That(Format.Time(TimeSpan.FromSeconds(3787)), Is.EqualTo("1:03:07"));
        });
    }

    [Test]
    public void CountsAreWrittenShort()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Format.Count(812), Is.EqualTo("812"));
            Assert.That(Format.Count(4_200), Is.EqualTo("4.2K"));
            Assert.That(Format.Count(1_900_000), Is.EqualTo("1.9M"));
            Assert.That(Format.Count(2_800_000_000), Is.EqualTo("2.8B"));
        });
    }

    [Test]
    public void AgesUseTheLargestUnitThatFits()
    {
        var now = new DateTimeOffset(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

        Assert.Multiple(() =>
        {
            Assert.That(Format.Age(now, now), Is.EqualTo("today"));
            Assert.That(Format.Age(now.AddDays(-1), now), Is.EqualTo("1 day ago"));
            Assert.That(Format.Age(now.AddDays(-12), now), Is.EqualTo("12 days ago"));
            Assert.That(Format.Age(now.AddDays(-70), now), Is.EqualTo("2 months ago"));
            Assert.That(Format.Age(now.AddDays(-800), now), Is.EqualTo("2 years ago"));
        });
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
