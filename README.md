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
Blazor WASM review console
        | same origin, no CORS
C# Lambda: Api  (ASP.NET Core minimal API - serves the console and the JSON)
        | optionally behind API Gateway (HTTP API); off by default
        |
S3 (requisitions) --event--> C# Lambda: Ingest --> OCR provider
        |                                              |   Textract (AnalyzeDocument), or the
        |                                              |   PDF text layer - see "OCR" below
        |                                              | OCR projection -> S3
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
| `src/ReqLens.Domain` | Entities: tenants, orders, fields, review audit, test catalog, call telemetry |
| `src/ReqLens.Validation` | Deterministic field validators, the catalogue check and the confidence gate |
| `src/ReqLens.Ocr` | OCR providers behind one interface: Textract, and the PDF text layer |
| `src/ReqLens.Ai` | Bedrock extraction, the JSON-schema tool, the model chain, per-call telemetry |
| `src/ReqLens.Pipeline` | The two pipeline steps, independent of how they are triggered |
| `src/ReqLens.Data` | EF Core `DbContext` over Postgres, and credential resolution |
| `src/ReqLens.Contracts` | Wire types shared by the API and the browser |
| `src/ReqLens.Lambdas.Ingest` | S3-triggered adapter: read, park OCR, open order, enqueue |
| `src/ReqLens.Lambdas.Extract` | SQS-triggered adapter: extract, validate, gate, persist, publish metrics |
| `src/ReqLens.Lambdas.Api` | Minimal API, and the host that serves the console |
| `src/ReqLens.Web` | Blazor WASM review console |
| `src/ReqLens.Cli` | Migrations, seeding, ingest, local pipeline runs, eval harness |
| `infra/terraform` | The stack |
| `tests/ReqLens.Tests` | Unit tests, including the golden set graded offline |

The Lambda handlers are deliberately thin. Each one turns an AWS event into a call on a step in
`ReqLens.Pipeline` and turns the result back into an AWS thing - so the pipeline can be run from
the CLI against the same database, and tested without constructing an `S3Event`.

## Build

Requires the .NET 10 SDK.

```bash
dotnet build ReqLens.slnx
dotnet test  ReqLens.slnx
```

Lambdas target the `dotnet10` managed runtime on Amazon Linux 2023, on arm64.

## Running it

```bash
./scripts/package-lambdas.sh                                       # before every apply
(cd infra/terraform && terraform apply -var-file=terraform.tfvars)  # provision

./scripts/first-run.sh        # migrate, seed the clinics and catalogue, upload all 20 scans
./scripts/run-console.sh      # API + review console on http://localhost:5080
```

The CLI is the driver:

```bash
dotnet run --project src/ReqLens.Cli -- migrate      # schema, without a password on a command line
dotnet run --project src/ReqLens.Cli -- seed         # clinics and test catalogue
dotnet run --project src/ReqLens.Cli -- ingest --all # upload; the deployed Lambdas take it from there
dotnet run --project src/ReqLens.Cli -- process --all  # or run the same pipeline locally
dotnet run --project src/ReqLens.Cli -- orders       # what came out
dotnet run --project src/ReqLens.Cli -- eval         # grade the golden set
```

Bucket, guardrail and secret ARN are read from Terraform state, so nothing has to be exported by
hand. The database password is never one of them: `DB_SECRET_ARN` names the secret and the AWS SDK
resolves it inside the process.

## OCR

OCR sits behind `IOcrProvider` with three implementations, chosen by the `ocr_provider` Terraform
variable.

`textract` is the intended one and what the architecture is built around.

`pdf-text-layer` reads the text layer straight out of a born-digital PDF and performs **no OCR at
all** - it works on these generated requisitions because they carry a real text layer, and would
return nothing for a genuine scan. It is offline and free, which is what makes the eval harness
runnable on every model change.

`bedrock-vision` hands the page to a vision model and asks it to transcribe what is printed. A PDF
goes as a Converse document block, so there is no rasteriser and no native dependency to package;
a scan or a fax goes as an image block. Unlike the text-layer reader it works on a real scan.

The two fallbacks exist because the AWS account this was built on cannot currently call Textract:
Textract, Comprehend and Transcribe all return `SubscriptionRequiredException`, which is an
account activation matter and not something the code can fix. Rather than let that block the
queue, the model chain, the guardrail, the validators and the review console from being
demonstrable, OCR became an interface. **Read the eval numbers below with that in mind: they
measure extraction from clean text, not extraction from OCR output, and real Textract results
will be lower.**

### Two guardrails, and why

`bedrock-vision` cannot use the extraction guardrail, for a structural reason rather than a
tunable one. That guardrail carries a contextual grounding policy, and Converse rejects any
request applying it without a grounding source: *"Grounding source, query and content to guard are
required."* At OCR time there is no source text to ground against, because producing it is the
whole point of the call.

So there is a second, smaller guardrail for transcription. It checks the one thing that can be
judged at that point - identifiers with no business on a requisition, such as an SSN or a card
number, which mean the wrong document was uploaded. It deliberately carries no prompt-attack
filter: the untrusted part of an OCR call is the page, and a page is an image, not text a
classifier can read. The only text in the request is this pipeline's own instruction, and at
`HIGH` strength the filter blocks precisely that, scoring *"transcribe this form, do not
interpret"* as an injection attempt. Prompt-attack screening happens one step later, on the
transcript, where the extraction guardrail marks it `guard_content` and there is finally text to
judge. **A page cannot be screened before it has been read.**

## Eval

`reqlens eval` runs the golden set through the same extractor, the same schema and the same
validators the Lambda uses, and grades against ground truth the generator wrote down at the time
it drew each form.

Twenty documents, Claude Haiku 4.5, PDF text layer:

| | |
|---|---|
| Field exact accuracy | 100.0 % (240 field reads) |
| Validation agreement | 100.0 % |
| Review decision accuracy | 100.0 % |
| Missed reviews | 0 |
| Unnecessary reviews | 0 |
| Cost | $0.0068 per document |

A perfect score on twenty documents is a statement about the difficulty of the input, not a claim
about the model. The synthetic forms are clean and the text layer is exact. What the harness is
for is noticing the day that stops being true - when a model is swapped, a prompt is edited, or
real scans arrive.

## What is deliberately not here

- **No authentication.** The API takes the tenant as a query parameter and trusts it. What is
  being demonstrated is tenant isolation enforced in the data model - orders keyed
  `(tenant_id, id)`, children carrying their own `tenant_id` under a composite foreign key, no
  query that reaches a row without naming its tenant. Putting Cognito in front of it changes who
  supplies the slug and nothing else. The public API Gateway is off by default for this reason.
- **No multi-AZ, no NAT per AZ, no backups.** This stack is short-lived and its data is
  regenerable. The cheaper single-instance choice is the correct one here and is argued for in
  `infra/terraform/README.md`.
- **Slice 2, the general order console.** Cut on purpose to finish the review queue, which is the
  screen that carries the argument.

## Status

Working end to end. Twenty synthetic requisitions go in and come out as orders: eleven clean and
accepted, nine routed to review, each naming its own reason - an unticked consent box, a bad NPI
check digit, a retired panel, a specimen that does not match the panel, a code in no catalogue, a
panel named only in prose, a missing diagnosis, and a handwritten margin note the schema has
nowhere to put.

The review console reads the queue, shows each field beside the snippet it was copied from and
the original scan, records corrections as an append-only audit trail, and derives the overturn
rate from it.
