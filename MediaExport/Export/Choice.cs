// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace MediaExport.Export;

/// <summary>One option in a <see cref="ChoiceGroup"/>, shown as a combo box item.</summary>
/// <param name="Detail">Shown beside the label, e.g. an estimated size or what the format is good for.</param>
public sealed record Choice(string Key, string Label, string? Detail = null)
{
    // Announced by screen readers, and shown by the closed combo box.
    public override string ToString() => Label;
}

/// <summary>Options where exactly one is picked, e.g. a quality or a format.</summary>
public sealed partial class ChoiceGroup : ObservableObject
{
    public ChoiceGroup(IEnumerable<Choice> items, string? selectedKey = null)
    {
        Reset(items, selectedKey);
    }

    public ObservableCollection<Choice> Items { get; } = [];

    /// <summary>The picked option; the view binds it two-way. Setting it to <c>null</c> (a list being rebuilt) is ignored.</summary>
    public Choice? Selected
    {
        get;
        set
        {
            if (value is null || value == field || !Items.Contains(value))
                return;

            field = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedKey));
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public string? SelectedKey => Selected?.Key;

    public event EventHandler? Changed;

    /// <summary>Replaces the options, keeping <paramref name="selectedKey"/> picked if it's still there.</summary>
    public void Reset(IEnumerable<Choice> items, string? selectedKey)
    {
        Choice[] next = items.ToArray();
        if (next.SequenceEqual(Items))
        {
            Select(selectedKey);
            return;
        }

        Items.Clear();
        foreach (Choice item in next)
            Items.Add(item);
        Select(selectedKey ?? Items.FirstOrDefault()?.Key);
    }

    public void Select(string? key) =>
        Selected = Items.FirstOrDefault(item => item.Key == key) ?? Items.FirstOrDefault(item => item.Key == Selected?.Key) ?? Items.FirstOrDefault();
}
