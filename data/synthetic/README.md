# Synthetic data

Everything here is invented. Clinics, providers and patients do not exist. NPIs are
format-valid with correct check digits but are not issued numbers. No PHI has ever existed
in this system.

ICD-10-CM codes are the exception and are genuine: they are a public code system that
identifies nobody, and validation is only meaningful against real codes.

## Files

| File | What |
|---|---|
| `tenants.csv` | Four partner clinics |
| `providers.csv` | Ordering providers, one NPI each, valid check digit |
| `patients.csv` | Invented patients. Sex is independent of first name by design - nothing should infer it from a name |
| `test-catalog.csv` | The panels the lab offers. Two are `active=false` to exercise the retired-panel path |
| `icd10-codes.csv` | Real ICD-10-CM codes, genetic-testing flavoured |
| `invalid-npis.csv` | Negative cases: wrong check digit, too short, too long, non-numeric |
| `requisitions/` | 20 generated requisition PDFs |
| `golden/` | Expected extraction and validation outcome per document |

## The golden set

Each `golden/req-NNN.json` is ground truth **by construction** - the generator writes down what
it drew, rather than someone reading a PDF and transcribing it. That is stronger than
hand-verification and it is why the eval harness can trust it.

`fields` is what a correct extraction returns. A `null` means the value is genuinely absent from
the form, which is different from the model failing to find it - the eval harness has to be able
to tell those two apart.

`expected_validation` is what the deterministic C# layer should conclude, independently of
whatever the model said.

Two things matter about how it grades:

- `test_panel_code` is the code **printed on the form**, not the catalogue entry behind it. On
  `req-005` the form says `GXP-999`, so that is the correct extraction even though no such panel
  exists. Grading against the catalogue would penalise a correct read and hide the out-of-catalog
  path the document exists to exercise.
- `panel_in_catalog` and `panel_active` are `null`, not `false`, when the form names no code at
  all. With nothing to look up the check is unanswerable, which is a different outcome from
  looking it up and finding nothing. `panel_code_present` says which situation you are in.

## Defects

Eleven documents are clean. Nine carry exactly one defect, so every branch of the review gate has
something to catch:

| Defect | Document | What it exercises |
|---|---|---|
| `MissingConsent` | req-001, req-009 | Consent checkbox unticked |
| `AmbiguousPanel` | req-002 | "BRCA panel" - which one? Model should not guess a code |
| `MissingDiagnosis` | req-003 | ICD-10 absent entirely |
| `InvalidNpi` | req-004 | Check digit deliberately wrong |
| `UnknownPanelCode` | req-005 | Well-formed code not in the catalog |
| `InactivePanel` | req-006 | Retired panel still being ordered |
| `HandwrittenNote` | req-007 | Free-text margin note the schema has nowhere to put |
| `SpecimenMismatch` | req-008 | Buccal swab for a blood-only panel |

Layouts rotate across three shapes. A real lab receives forms from many clinics and none of them
agree on where anything goes - that is the reason extraction is not a regex.

Date formats are deliberately inconsistent between fields, as they are on real paperwork.

## Regenerating

```bash
dotnet run --project tools/ReqLens.DataGen -- --count 20 --seed 20260828
```

Deterministic for a given seed. The NPI check digits were computed outside this repo on
purpose, so that implementing the Luhn algorithm in C# stays a real exercise.
