// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;

namespace MediaExport.Export;

/// <summary>One option in a <see cref="ChoiceGroup"/>, shown as a pill or a tile.</summary>
public sealed partial class Choice(string key, string label, string? description = null) : ObservableObject
{
    public string Key { get; } = key;
    public string Label { get; } = label;

    /// <summary>What picking it means, e.g. "Plays everywhere".</summary>
    public string? Description { get; } = description;

    /// <summary>A live detail, e.g. an estimated size.</summary>
    [ObservableProperty]
    public partial string? Detail { get; set; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    [ObservableProperty]
    public partial bool IsEnabled { get; set; } = true;

    // Announced by screen readers.
    public override string ToString() => Detail is null ? Label : $"{Label}, {Detail}";
}

/// <summary>Options where exactly one is picked. The view binds each option's IsSelected; the group keeps it single.</summary>
public sealed partial class ChoiceGroup : ObservableObject
{
    public ChoiceGroup(IEnumerable<Choice> items, string? selectedKey = null)
    {
        Reset(items, selectedKey);
    }

    public ObservableCollection<Choice> Items { get; } = [];

    [ObservableProperty]
    public partial Choice? Selected { get; private set; }

    public string? SelectedKey => Selected?.Key;

    public event EventHandler? Changed;

    /// <summary>Replaces the options, keeping <paramref name="selectedKey"/> picked if it's still there.</summary>
    public void Reset(IEnumerable<Choice> items, string? selectedKey)
    {
        foreach (Choice item in Items)
            item.PropertyChanged -= OnItemChanged;
        Items.Clear();
        foreach (Choice item in items)
        {
            item.PropertyChanged += OnItemChanged;
            Items.Add(item);
        }

        Select(Items.FirstOrDefault(item => item.Key == selectedKey && item.IsEnabled)?.Key ?? Items.FirstOrDefault(item => item.IsEnabled)?.Key);
    }

    public void Select(string? key)
    {
        Choice? next = Items.FirstOrDefault(item => item.Key == key);
        foreach (Choice item in Items)
            item.IsSelected = item == next;
        SetSelected(next);
    }

    private void OnItemChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(Choice.IsSelected) && sender is Choice { IsSelected: true } picked && picked != Selected)
            Select(picked.Key);
    }

    private void SetSelected(Choice? value)
    {
        if (value == Selected)
            return;

        Selected = value;
        OnPropertyChanged(nameof(SelectedKey));
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
