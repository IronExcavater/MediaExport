// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Converters;

namespace MediaExport.Tests.Converters;

public class FormatTests
{
    [Test]
    public void Format_WhenValuesAreKnown_ReturnsReadableText()
    {
        // Arrange
        var now = new DateTimeOffset(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

        // Act
        var shortTime = Format.Time(TimeSpan.FromSeconds(187));
        var longTime = Format.Time(TimeSpan.FromSeconds(3787));
        var count = Format.Count(1_900_000);
        var age = Format.Age(now.AddDays(-70), now);
        var views = Format.Views(4_200);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(shortTime, Is.EqualTo("3:07"));
            Assert.That(longTime, Is.EqualTo("1:03:07"));
            Assert.That(count, Is.EqualTo("1.9M"));
            Assert.That(age, Is.EqualTo("2 months ago"));
            Assert.That(views, Is.EqualTo("4.2K views"));
            Assert.That(Format.Views(null), Is.Empty);
        });
    }
}
