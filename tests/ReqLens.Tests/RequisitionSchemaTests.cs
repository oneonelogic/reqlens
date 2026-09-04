using System.Text.Json.Nodes;
using ReqLens.Ai;
using ReqLens.Domain;

namespace ReqLens.Tests;

public class RequisitionSchemaTests
{
    [Fact]
    public void Schema_asks_for_exactly_the_fields_the_rest_of_the_system_knows_about()
    {
        var schema = JsonNode.Parse(RequisitionSchema.SchemaJson())!.AsObject();

        var properties = schema["properties"]!.AsObject().Select(p => p.Key).ToList();
        var required = schema["required"]!.AsArray().Select(n => n!.GetValue<string>()).ToList();

        Assert.Equal(RequisitionFields.All.Order(), properties.Order());
        Assert.Equal(RequisitionFields.All.Order(), required.Order());
    }

    [Fact]
    public void Every_field_must_carry_a_value_a_confidence_and_a_source()
    {
        var schema = JsonNode.Parse(RequisitionSchema.SchemaJson())!.AsObject();

        foreach (var (name, node) in schema["properties"]!.AsObject())
        {
            var required = node!["required"]!.AsArray().Select(n => n!.GetValue<string>()).ToList();

            Assert.Equal(["value", "confidence", "source_text"], required);
            Assert.False(string.IsNullOrWhiteSpace(node["description"]?.GetValue<string>()), name);
        }
    }

    [Fact]
    public void A_complete_payload_parses_into_fields()
    {
        var result = RequisitionSchema.Parse(Payload());

        Assert.True(result.IsValid, result.Problem);
        Assert.Equal(RequisitionFields.All.Count, result.Fields.Count);

        var npi = result.Fields.Single(f => f.Name == RequisitionFields.ProviderNpi);

        Assert.Equal("1245319599", npi.Value);
        Assert.Equal(0.97, npi.Confidence);
        Assert.Equal("NPI 1245319599", npi.SourceText);
    }

    [Fact]
    public void An_empty_value_is_an_absent_field_not_an_empty_string()
    {
        // The schema uses "" for absent rather than null, so this conversion is the only thing
        // standing between the validators and a field that is technically present and blank.
        var payload = Payload();
        payload[RequisitionFields.DiagnosisCode]!["value"] = "";

        var result = RequisitionSchema.Parse(payload);

        Assert.Null(result.Fields.Single(f => f.Name == RequisitionFields.DiagnosisCode).Value);
    }

    [Fact]
    public void A_missing_property_fails_the_schema_check()
    {
        var payload = Payload();
        payload.Remove(RequisitionFields.PatientMrn);

        var result = RequisitionSchema.Parse(payload);

        Assert.False(result.IsValid);
        Assert.Contains(RequisitionFields.PatientMrn, result.Problem);
    }

    [Fact]
    public void A_confidence_outside_the_range_is_reported_and_clamped()
    {
        // Models do return 1.4 here. Clamping silently would hide it; rejecting the whole
        // document over it would be worse. It is reported, clamped, and the chain escalates.
        var payload = Payload();
        payload[RequisitionFields.PatientSex]!["confidence"] = 1.4;

        var result = RequisitionSchema.Parse(payload);

        Assert.False(result.IsValid);
        Assert.Contains("outside 0..1", result.Problem);
        Assert.Equal(1.0, result.Fields.Single(f => f.Name == RequisitionFields.PatientSex).Confidence);
    }

    [Fact]
    public void A_payload_that_is_not_an_object_fails_rather_than_throws()
        => Assert.False(RequisitionSchema.Parse(JsonNode.Parse("[]")).IsValid);

    private static JsonObject Payload()
    {
        var payload = new JsonObject();

        foreach (var field in RequisitionFields.All)
        {
            payload[field] = new JsonObject
            {
                ["value"] = field == RequisitionFields.ProviderNpi ? "1245319599" : "x",
                ["confidence"] = 0.97,
                ["source_text"] = field == RequisitionFields.ProviderNpi ? "NPI 1245319599" : "x"
            };
        }

        return payload;
    }
}

public class ModelChainOptionsTests
{
    /// <summary>
    /// The exact JSON Terraform puts in MODEL_CHAIN. This test is the contract between
    /// infra/terraform/lambdas.tf and the Lambda that reads it - the two are edited by different
    /// hands at different times, and nothing else would catch a rename.
    /// </summary>
    private const string TerraformShaped = """
        {"models":[
          {"modelId":"us.anthropic.claude-haiku-4-5-20251001-v1:0","role":"Primary","maxCostPerDoc":0.05,
           "inputPricePerMillionTokens":1.00,"outputPricePerMillionTokens":5.00},
          {"modelId":"us.amazon.nova-2-lite-v1:0","role":"Availability","maxCostPerDoc":0.05,
           "inputPricePerMillionTokens":0.06,"outputPricePerMillionTokens":0.24},
          {"modelId":"us.anthropic.claude-sonnet-4-5-20250929-v1:0","role":"Escalation","maxCostPerDoc":0.25,
           "inputPricePerMillionTokens":3.00,"outputPricePerMillionTokens":15.00}]}
        """;

    [Fact]
    public void Parses_the_chain_terraform_writes()
    {
        var chain = ModelChainOptions.FromJson(TerraformShaped);

        Assert.Equal(3, chain.Models.Count);
        Assert.Equal("us.anthropic.claude-haiku-4-5-20251001-v1:0", chain.Primary.ModelId);
        Assert.Equal("us.amazon.nova-2-lite-v1:0", chain.ForRole(ModelRole.Availability)!.ModelId);
        Assert.Equal(0.25m, chain.ForRole(ModelRole.Escalation)!.MaxCostPerDoc);
    }

    [Fact]
    public void Costs_are_computed_per_million_tokens()
    {
        var chain = ModelChainOptions.FromJson(TerraformShaped);

        // 10k in at $1/M, 2k out at $5/M.
        Assert.Equal(0.01m + 0.01m, chain.Primary.EstimateCost(10_000, 2_000));
    }

    [Fact]
    public void A_chain_with_no_primary_is_a_configuration_error_not_a_null()
        => Assert.Throws<InvalidOperationException>(() => ModelChainOptions.FromJson("""{"models":[]}""").Primary);
}
