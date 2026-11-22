using System.Globalization;
using Shouldly;
using Xunit;

namespace Vyxum.Platform.Tests.Glossary;

public class GlossaryCatalogTests
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr");
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en");

    private static GlossaryCatalog Catalog() => new(GlossaryStrings.Terms, [English, Turkish]);

    [Fact]
    public void Sections_and_terms_come_in_number_order()
    {
        var sections = Catalog().GetSections(Turkish);

        sections.Select(s => s.Id).ShouldBe(["S0", "S1", "S2", "S3", "S4", "S5", "S6", "S7", "S8"]);
        sections[0].Title.ShouldBe("En çok karıştırılanlar");
        // T1.10 follows T1.09: numbers are compared as numbers.
        sections[1].Terms.Select(t => t.Id).Take(11).ShouldBe(
            ["T1.01", "T1.02", "T1.03", "T1.04", "T1.05", "T1.06", "T1.07", "T1.08", "T1.09", "T1.10", "T1.11"]);
        sections[1].Terms[3].Name.ShouldBe("Dara");
    }

    [Fact]
    public void Every_term_has_a_name_and_text_in_both_languages()
    {
        var catalog = Catalog();
        foreach (var culture in new[] { Turkish, English })
        {
            var terms = catalog.GetSections(culture).SelectMany(s => s.Terms).ToList();
            terms.Count.ShouldBeGreaterThan(140);
            terms.Select(t => t.Id).ShouldBeUnique();
            terms.ShouldAllBe(t => t.Name.Length > 0 && t.Text.Length > 0);
            terms.ShouldAllBe(t => t.InVyxum == null || t.InVyxum.Length > 0);
            terms.ShouldAllBe(t => t.Example == null || t.Example.Length > 0);
        }
    }

    [Fact]
    public void A_term_carries_its_name_in_the_other_language()
    {
        var catalog = Catalog();

        var tare = catalog.GetSections(Turkish)[1].Terms.Single(t => t.Id == "T1.04");
        tare.Name.ShouldBe("Dara");
        tare.InVyxum.ShouldNotBeNull();
        tare.OtherNames.ShouldBe([new GlossaryName("en", "Tare")]);

        var english = catalog.GetSections(English)[1].Terms.Single(t => t.Id == "T1.04");
        english.Name.ShouldBe("Tare");
        english.OtherNames.ShouldBe([new GlossaryName("tr", "Dara")]);
    }

    [Fact]
    public void A_name_that_is_the_same_in_both_languages_is_not_repeated()
    {
        var plu = Catalog().GetSections(Turkish).SelectMany(s => s.Terms).Single(t => t.Name == "PLU");

        plu.OtherNames.ShouldBeEmpty();
    }

    [Fact]
    public void Terms_without_a_vyxum_line_or_example_return_null()
    {
        var pairs = Catalog().GetSections(English)[0].Terms;

        pairs.ShouldAllBe(t => t.InVyxum == null && t.Example == null);
    }

    [Fact]
    public void The_x_height_rule_looks_at_the_package_surface_not_the_label()
    {
        var term = Catalog().GetSections(Turkish).SelectMany(s => s.Terms).Single(t => t.Name == "x yüksekliği");

        term.Text.ShouldContain("80 cm²");
        term.Text.ShouldContain("ambalaj");
    }
}
