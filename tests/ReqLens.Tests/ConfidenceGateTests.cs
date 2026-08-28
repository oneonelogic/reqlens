using ReqLens.Validation;
using Xunit;

namespace ReqLens.Tests;

public class ConfidenceGateTests
{
    private readonly ConfidenceGate _gate = new();

    [Fact]
    public void High_confidence_and_valid_flows_through()
        => Assert.False(_gate.RequiresReview(0.97, validationPassed: true));

    [Fact]
    public void Low_confidence_queues_for_review()
        => Assert.True(_gate.RequiresReview(0.55, validationPassed: true));

    [Fact]
    public void Failed_validation_queues_however_confident_the_model_was()
        => Assert.True(_gate.RequiresReview(0.99, validationPassed: false));
}
