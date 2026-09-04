using ReqLens.Validation;

namespace ReqLens.Tests;

public class Icd10ValidatorTests
{
    private readonly Icd10Validator _validator = new();

    [Theory]
    [InlineData("Z15.01")]
    [InlineData("E11.9")]
    [InlineData("C18.9")]
    [InlineData("Z00.00")]
    [InlineData("z31.440")] // case-insensitive
    public void Accepts_codes_in_the_list(string code)
        => Assert.True(_validator.Validate(code).IsValid, code);

    [Theory]
    [InlineData("11.9")]     // no letter
    [InlineData("E1")]       // too short
    [InlineData("U07.1")]    // U is reserved
    [InlineData("E11.99999")] // too many characters after the dot
    [InlineData("")]
    [InlineData(null)]
    public void Rejects_malformed_codes(string? code)
        => Assert.False(_validator.Validate(code).IsValid);

    [Fact]
    public void Rejects_a_well_formed_code_that_is_not_real()
    {
        // The failure mode that matters: a model asked for an ICD-10 code will happily invent
        // one that passes any regex you write. Only the list catches it.
        var result = _validator.Validate("Q99.9");

        Assert.False(result.IsValid);
        Assert.Contains("not a known", result.Message);
    }

    [Fact]
    public void Embedded_code_list_loaded()
        => Assert.True(Icd10CodeSet.Default.Count >= 40, $"loaded {Icd10CodeSet.Default.Count} codes");
}
