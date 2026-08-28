# ReqLens

A B2B test-requisition intake pipeline for a genetic-testing lab, built on AWS in C#.

Partner clinics order lab tests by sending requisition forms, which arrive as messy scans and
faxes and get re-keyed by hand. ReqLens is the intake system: a clinic uploads the scanned PDF,
and the pipeline OCRs it, runs schema-constrained LLM extraction, validates hard rules in
deterministic code, gates on confidence, routes shaky fields to a human review queue with a full
audit trail, and turns the mess into a structured order the clinic can track.

> **All data in this repository is synthetic.** Every clinic, patient, provider and identifier is
> invented. NPIs and ICD-10 codes are format-valid and deliberately not real. No PHI has ever
> existed in this system, by design.

## Architecture

```
Blazor WASM (S3 + CloudFront) - clinic portal | lab review console
        | presigned upload / REST
API Gateway (HTTP API)
        |
C# Lambda: Api  (ASP.NET Core minimal API)
        |
S3 (requisitions) --event--> C# Lambda: Ingest --> Textract (AnalyzeDocument)
        |                                              | blocks JSON -> S3
        |                                         SQS (+ DLQ)
        |                                              |
        |                          C# Lambda: Extract --> Bedrock Converse API
        |                                              |   (Claude Haiku, JSON-schema tool,
        |                                              |    Guardrails attached)
        |                          deterministic validation:
        |                            NPI Luhn check digit
        |                            ICD-10 format + code list
        |                            test-panel catalog membership
        |                            specimen / consent / date sanity
        |                                              | confidence gate
        +------------ RDS Postgres (EF Core) <---------+
             tenants - orders - fields - reviews(audit) - test_catalog
```

Everything is provisioned with Terraform. Every row carries a `tenant_id` and the API layer
scopes to it, so one clinic can never see another's orders.

## Treating the model as a production dependency

The model is measured, not trusted:

- **Per-call telemetry** on every Bedrock call - model id, latency, input/output tokens, computed
  cost, guardrail intervention, schema-validation outcome. Emitted from the first call rather than
  retrofitted. Logs carry S3 pointers, never extracted values.
- **Two guardrail layers** - Bedrock Guardrails on the call, plus a deterministic C# validation
  layer that re-checks everything the model returns.
- **Config-driven fallback chain** - retry with backoff, fail over to a different model family on
  throttle or 5xx, escalate to a stronger sibling when output fails schema or confidence checks.
  The human review queue is the terminal fallback.
- **Golden-set eval harness** - hand-verified synthetic requisitions run as a batch, reporting
  field-level precision and recall per model in the chain.
- **Overturn rate as a drift signal** - every human correction in the review queue is a scored
  model miss. A rising per-field overturn rate is drift you can see on a dashboard.

## Layout

| Path | What |
|---|---|
| `src/ReqLens.Domain` | Entities: tenants, orders, fields, review audit, test catalog |
| `src/ReqLens.Validation` | Deterministic field validators and the confidence gate |
| `src/ReqLens.Ai` | Bedrock extraction, model chain config, per-call telemetry |
| `src/ReqLens.Data` | EF Core `DbContext` over Postgres |
| `src/ReqLens.Lambdas.Ingest` | S3-triggered: Textract, order shell, enqueue |
| `src/ReqLens.Lambdas.Extract` | SQS-triggered: extract, validate, gate, persist |
| `src/ReqLens.Lambdas.Api` | Minimal API behind API Gateway |
| `src/ReqLens.Web` | Blazor WASM front end |
| `src/ReqLens.Cli` | Pipeline driver and eval harness |
| `infra/terraform` | The stack |
| `tests/ReqLens.Tests` | Unit tests |

## Build

Requires the .NET 10 SDK.

```bash
dotnet build ReqLens.slnx
dotnet test  ReqLens.slnx
```

Lambdas target the `dotnet10` managed runtime on Amazon Linux 2023.

## Status

Early. The project skeleton, domain model, telemetry contract and confidence gate are in place.
The Bedrock call, the NPI and ICD-10 validators, and the ingest Lambda are marked as stubs and are
the next things to land.
