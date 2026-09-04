using ReqLens.Validation;

namespace ReqLens.Tests;

public class TestCatalogValidatorTests
{
    private readonly TestCatalogValidator _validator = new();
    private readonly List<Domain.TestCatalogEntry> _catalog = RepoData.Catalog();

    [Fact]
    public void Resolves_an_active_panel_with_the_right_specimen()
    {
        var check = _validator.Check("GXP-100", "Whole Blood EDTA", _catalog);

        Assert.True(check.PanelCodePresent);
        Assert.True(check.PanelInCatalog);
        Assert.True(check.PanelActive);
        Assert.True(check.SpecimenMatches);
    }

    [Fact]
    public void No_code_leaves_every_row_property_unanswered()
    {
        var check = _validator.Check(null, "Whole Blood EDTA", _catalog);

        Assert.False(check.PanelCodePresent);
        Assert.Null(check.PanelInCatalog);
        Assert.Null(check.PanelActive);
        Assert.Null(check.SpecimenMatches);
    }

    [Fact]
    public void An_unknown_code_is_found_absent_but_says_nothing_about_the_row()
    {
        // The distinction the golden set insists on: looked it up and it is not there (false)
        // is different from there being no row to ask about (null).
        var check = _validator.Check("GXP-999", "Saliva", _catalog);

        Assert.True(check.PanelCodePresent);
        Assert.False(check.PanelInCatalog);
        Assert.Null(check.PanelActive);
        Assert.Null(check.SpecimenMatches);
    }

    [Fact]
    public void Specimen_comparison_does_not_accept_a_near_miss()
    {
        // A buccal swab for a blood-only panel is a re-draw on a real patient. Fuzzy matching
        // here would swallow exactly the case this check exists for.
        var check = _validator.Check("GXP-121", "Buccal Swab", _catalog);

        Assert.True(check.PanelInCatalog);
        Assert.False(check.SpecimenMatches);
    }
}
