// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using System.Collections.ObjectModel;

namespace MediaExport.Core.Storage;

/// <summary>Something that can be pinned in a <see cref="RecentList{T}"/>.</summary>
public interface IPinnable<T>
{
    bool IsPinned { get; }

    /// <summary>The same thing, pinned or not.</summary>
    T WithPin(bool isPinned);
}

/// <summary>
/// The things last used, newest first. Pinned ones stay at the top, newest pin first, and are kept however many
/// things are used after them.
/// </summary>
/// <param name="capacity">How many unpinned things are kept.</param>
/// <param name="isSame">Whether two things are the same one, used twice.</param>
public sealed class RecentList<T>(int capacity, Func<T, T, bool> isSame) : ObservableCollection<T>
    where T : IPinnable<T>
{
    private int PinnedCount => this.Count(item => item.IsPinned);

    /// <summary>Fills the list, such as with what was saved earlier. Anything listed twice is listed once.</summary>
    public void Load(IEnumerable<T> unpinned, IEnumerable<T> pinned)
    {
        Clear();
        var items = pinned.Select(item => item.WithPin(true)).Concat(unpinned);
        foreach (var item in items)
        {
            var isListed = this.Any(earlier => isSame(earlier, item));
            if (!isListed)
                Add(item);
        }
    }

    /// <summary>
    /// Puts something at the front of the unpinned things, in place of where it was. Something that is pinned
    /// stays where it is.
    /// </summary>
    public void Remember(T item)
    {
        var isPinned = this.Any(listed => listed.IsPinned && isSame(listed, item));
        if (isPinned)
            return;

        var earlier = this.Where(listed => isSame(listed, item)).ToList();
        foreach (var duplicate in earlier)
            Remove(duplicate);

        var pinnedCount = PinnedCount;
        Insert(pinnedCount, item);
        while (Count - pinnedCount > capacity)
            RemoveAt(Count - 1);
    }

    /// <summary>Pins something to the top of the list, or lets it go back among the others.</summary>
    public void TogglePin(T item)
    {
        var isListed = Remove(item);
        if (!isListed)
            return;

        // The newest pin is first, and something that is let go is the first of the others.
        var isPinned = !item.IsPinned;
        var index = isPinned ? 0 : PinnedCount;
        Insert(index, item.WithPin(isPinned));
    }
}
