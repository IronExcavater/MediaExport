// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using CommunityToolkit.Mvvm.ComponentModel;
using MediaExport.Core.Media.Providers;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;

namespace MediaExport.Search;

/// <summary>One structured part of an editable search.</summary>
public sealed partial class SearchTerm(SearchTermDefinition definition, string value) : ObservableObject
{
    public SearchTermDefinition Definition { get; } = definition;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Text), nameof(ValueName))]
    public partial string Value { get; set; } = value;

    public string Text => Value.Trim().Length == 0 ? string.Empty : SearchQuery.Write(Definition.Type, Value.Trim());

    public string DisplayName => Definition.Type switch
    {
        SearchTermType.ExactPhrase => "Exact",
        SearchTermType.ExcludedWord => "Exclude",
        SearchTermType.Creator => "Creator",
        SearchTermType.LongerThan => "Longer",
        SearchTermType.ShorterThan => "Shorter",
        SearchTermType.Before => "Before",
        SearchTermType.After => "After",
        _ => Definition.Label
    };

    public string ValueName => Value.Length == 0 ? "…" : Value;

    public string Glyph => Definition.Type switch
    {
        SearchTermType.ExactPhrase => "\u201C",
        SearchTermType.ExcludedWord => "\u2260",
        SearchTermType.Creator => "\uE77B",
        SearchTermType.LongerThan or SearchTermType.ShorterThan => "\uE916",
        SearchTermType.Before or SearchTermType.After => "\uE787",
        _ => "\uE71C"
    };

    public override string ToString() => $"{DisplayName} {ValueName}";
}

/// <summary>
/// The editable parts of a search, independent of WinUI. It resolves pasted or completed syntax into terms,
/// provides the add menu's remaining choices, and serializes the whole field back to canonical query text.
/// </summary>
public sealed partial class SearchDraft : ObservableObject
{
    private int _batchDepth;
    private bool _batchChanged;

    public SearchDraft() => Terms.CollectionChanged += OnTermsCollectionChanged;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(QueryText))]
    public partial string Text { get; set; } = string.Empty;

    public ObservableCollection<SearchTerm> Terms { get; } = [];

    public string QueryText => string.Join(' ', Terms.Select(term => term.Text)
        .Prepend(Text.Trim())
        .Where(part => part.Length > 0));

    public IReadOnlyList<SearchTermDefinition> AvailableTerms => SearchQuery.Definitions
        .Where(definition => definition.Repeatable || Terms.All(term => term.Definition.Type != definition.Type))
        .ToArray();

    public event EventHandler? Changed;

    internal bool LastChangeWasText { get; private set; }

    public IReadOnlyList<string> Resolve(bool includeUnfinished, Func<string, bool> isSource)
    {
        SearchExtraction extraction = SearchQuery.ExtractTerms(Text, includeUnfinished, isSource);
        if (extraction.Terms.Count == 0 && extraction.Sources.Count == 0)
            return [];

        Batch(() =>
        {
            Text = extraction.Text.Trim();
            foreach (ParsedSearchTerm term in extraction.Terms)
                Add(term.Type, term.Value);
        });
        return extraction.Sources;
    }

    public SearchTerm Add(SearchTermType type, string value = "")
    {
        SearchTermDefinition definition = SearchQuery.Definitions.First(definition => definition.Type == type);
        if (!definition.Repeatable && Terms.FirstOrDefault(term => term.Definition.Type == type) is { } existing)
        {
            existing.Value = value;
            return existing;
        }

        var added = new SearchTerm(definition, value);
        Terms.Add(added);
        return added;
    }

    public void Restore(string text, Func<string, bool> isSource) => Batch(() =>
    {
        Terms.Clear();
        Text = text;
        Resolve(includeUnfinished: true, isSource);
    });

    public string Serialize(IEnumerable<string> sourceIds) => string.Join(' ', sourceIds
        .Select(SearchQuery.WriteSource)
        .Prepend(QueryText)
        .Where(part => part.Length > 0));

    public void Cut() => Batch(() =>
    {
        Terms.Clear();
        Text = string.Empty;
        // Cutting is a whole-draft operation, even when Text happened to be the last property changed.
        LastChangeWasText = false;
    });

    public SearchTerm? Move(SearchTerm? current, int offset)
    {
        if (Terms.Count == 0 || offset == 0)
            return current;

        if (current is null)
            return offset < 0 ? Terms[^1] : Terms[0];

        int next = Terms.IndexOf(current) + offset;
        return next >= 0 && next < Terms.Count ? Terms[next] : null;
    }

    public bool Remove(SearchTerm term) => Terms.Remove(term);

    partial void OnTextChanged(string value)
    {
        LastChangeWasText = true;
        MarkChanged();
    }

    private void OnTermsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        LastChangeWasText = false;
        if (args.OldItems is not null)
            foreach (SearchTerm term in args.OldItems)
                term.PropertyChanged -= OnTermChanged;
        if (args.NewItems is not null)
            foreach (SearchTerm term in args.NewItems)
                term.PropertyChanged += OnTermChanged;
        MarkChanged();
    }

    private void OnTermChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(SearchTerm.Value))
            return;
        LastChangeWasText = false;
        MarkChanged();
    }

    private void Batch(Action action)
    {
        _batchDepth++;
        try
        {
            action();
        }
        finally
        {
            _batchDepth--;
            if (_batchDepth == 0 && _batchChanged)
            {
                _batchChanged = false;
                RaiseChanged();
            }
        }
    }

    private void MarkChanged()
    {
        if (_batchDepth > 0)
        {
            _batchChanged = true;
            return;
        }
        RaiseChanged();
    }

    private void RaiseChanged()
    {
        OnPropertyChanged(nameof(QueryText));
        OnPropertyChanged(nameof(AvailableTerms));
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
