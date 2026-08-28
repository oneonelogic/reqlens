using ReqLens.Validation;
using Xunit;

namespace ReqLens.Tests;

/// <summary>
/// Every NPI below is synthetic - format-valid, deliberately not issued to a real provider.
/// Skipped until the pairing session implements NpiValidator; remove the Skip then.
/// </summary>
public class NpiValidatorTests
{
    private const string PairingStub = "NpiValidator is a pairing stub - implement it, then unskip.";

    [Theory(Skip = PairingStub)]
    [InlineData("1245319599")]
    [InlineData("1679576722")]
    public void Accepts_valid_check_digit(string npi)
        => Assert.True(new NpiValidator().Validate(npi).IsValid);

    [Theory(Skip = PairingStub)]
    [InlineData("1245319598")]  // last digit wrong
    [InlineData("124531959")]   // too short
    [InlineData("12453195991")] // too long
    [InlineData("12453A9599")]  // not all digits
    [InlineData("")]
    [InlineData(null)]
    public void Rejects_bad_input(string? npi)
        => Assert.False(new NpiValidator().Validate(npi).IsValid);
}
