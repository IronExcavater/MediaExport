// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Core.Media.Providers;
using MediaExport.Search;

namespace MediaExport.Tests.Search;

public sealed class SearchDraftTests
{
    [Test]
    public void Singleton_terms_disappear_from_the_menu_while_repeatable_terms_remain()
    {
        var draft = new SearchDraft();

        draft.Add(SearchTermType.Creator, "The Band");
        draft.Add(SearchTermType.ExactPhrase, "night song");

        Assert.Multiple(() =>
        {
            Assert.That(draft.AvailableTerms.Select(term => term.Type), Does.Not.Contain(SearchTermType.Creator));
            Assert.That(draft.AvailableTerms.Select(term => term.Type), Does.Contain(SearchTermType.ExactPhrase));
            Assert.That(draft.AvailableTerms.Select(term => term.Type), Does.Contain(SearchTermType.ExcludedWord));
        });
    }

    [Test]
    public void Resolving_mixed_text_keeps_words_and_returns_sources()
    {
        var draft = new SearchDraft { Text = "night \"blue moon\" creator:\"The Band\" source:vimeo kind:video " };

        IReadOnlyList<string> sources = draft.Resolve(includeUnfinished: false, name => name == "vimeo");

        Assert.Multiple(() =>
        {
            Assert.That(draft.Text, Is.EqualTo("night kind:video"));
            Assert.That(draft.Terms.Select(term => (term.Definition.Type, term.Value)), Is.EqualTo(new[]
            {
                (SearchTermType.ExactPhrase, "blue moon"),
                (SearchTermType.Creator, "The Band")
            }));
            Assert.That(sources, Is.EqualTo(new[] { "vimeo" }));
        });
    }

    [Test]
    public void Canonical_text_restores_the_same_effective_search()
    {
        var original = new SearchDraft { Text = "night song" };
        original.Add(SearchTermType.ExactPhrase, "blue moon");
        original.Add(SearchTermType.ExactPhrase, "quiet sky");
        original.Add(SearchTermType.ExcludedWord, "live");
        original.Add(SearchTermType.Creator, "The Band");

        string copied = original.Serialize(["youtube", "soundcloud"]);
        var restored = new SearchDraft { Text = copied };
        IReadOnlyList<string> sources = restored.Resolve(includeUnfinished: true, _ => true);

        Assert.Multiple(() =>
        {
            Assert.That(restored.QueryText, Is.EqualTo(original.QueryText));
            Assert.That(sources, Is.EqualTo(new[] { "youtube", "soundcloud" }));
            Assert.That(copied, Is.EqualTo("night song \"blue moon\" \"quiet sky\" -live creator:\"The Band\" source:youtube source:soundcloud"));
        });
    }

    [Test]
    public void Cutting_clears_editable_content_without_owning_provider_state()
    {
        string[] providers = ["youtube"];
        var draft = new SearchDraft { Text = "night" };
        draft.Add(SearchTermType.Before, "2020");

        draft.Cut();

        Assert.Multiple(() =>
        {
            Assert.That(draft.QueryText, Is.Empty);
            Assert.That(draft.Terms, Is.Empty);
            Assert.That(providers, Is.EqualTo(new[] { "youtube" }));
        });
    }

    [Test]
    public void Moving_between_terms_stops_at_the_text_boundary_and_removal_is_explicit()
    {
        var draft = new SearchDraft();
        SearchTerm first = draft.Add(SearchTermType.ExactPhrase, "one");
        SearchTerm second = draft.Add(SearchTermType.ExcludedWord, "two");

        Assert.Multiple(() =>
        {
            Assert.That(draft.Move(null, -1), Is.SameAs(second), "left from the text enters the last term");
            Assert.That(draft.Move(second, -1), Is.SameAs(first));
            Assert.That(draft.Move(first, -1), Is.Null);
            Assert.That(draft.Move(first, 1), Is.SameAs(second));
            Assert.That(draft.Move(second, 1), Is.Null, "right from the last term returns to the text");
            Assert.That(draft.Remove(first), Is.True);
            Assert.That(draft.Remove(first), Is.False, "a term is removed only once");
        });
    }

    [Test]
    public void Editing_a_term_value_raises_one_effective_change()
    {
        var draft = new SearchDraft();
        SearchTerm term = draft.Add(SearchTermType.Creator, "One");
        int changes = 0;
        draft.Changed += (_, _) => changes++;

        term.Value = "Two";

        Assert.That(changes, Is.EqualTo(1));
    }

    [Test]
    public void Phrase_and_exclusion_terms_use_direct_names_and_distinct_icons()
    {
        var draft = new SearchDraft();
        SearchTerm exact = draft.Add(SearchTermType.ExactPhrase, "night song");
        SearchTerm exclude = draft.Add(SearchTermType.ExcludedWord, "live");

        Assert.Multiple(() =>
        {
            Assert.That(exact.DisplayName, Is.EqualTo("Exact"));
            Assert.That(exclude.DisplayName, Is.EqualTo("Exclude"));
            Assert.That(exact.Glyph, Is.Not.EqualTo(exclude.Glyph));
        });
    }
}
