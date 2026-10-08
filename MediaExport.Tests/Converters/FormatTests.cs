// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Converters;

namespace MediaExport.Tests.Converters;

[SetCulture("en-AU")]
public sealed class FormatTests
{
    [TestCase(1, "1 view")]
    [TestCase(2, "2 views")]
    [TestCase(4_200, "4.2K views")]
    [TestCase(1_900_000, "1.9M views")]
    public void Compact_metadata_uses_view_wording(long views, string expected) =>
        Assert.That(Format.Meta(views, null), Is.EqualTo(expected));

    [TestCase(1, "1 view")]
    [TestCase(21_315_080, "21,315,080 views")]
    public void Detailed_metadata_uses_view_wording(long views, string expected) =>
        Assert.That(Format.MetaDetail(views, null, false), Is.EqualTo(expected));

    [Test]
    public void Metadata_keeps_dates_without_counts_and_counts_without_dates()
    {
        DateTimeOffset today = DateTimeOffset.Now;

        Assert.Multiple(() =>
        {
            Assert.That(Format.Meta(null, today), Is.EqualTo("today"));
            Assert.That(Format.Meta(1, null), Is.EqualTo("1 view"));
            Assert.That(Format.MetaDetail(null, new DateTimeOffset(2013, 4, 17, 0, 0, 0, TimeSpan.Zero), false), Is.EqualTo("17 April 2013"));
        });
    }
}
