# Working agreement

## Branches
`master` is always buildable. Nothing lands on it directly.

Every change goes on a branch named for its issue:

```
feature/<issue>-<slug>     new capability      feature/14-npi-luhn-validator
fix/<issue>-<slug>         defect
chore/<slug>               tooling, docs, CI
```

Open a PR back to `master`, link the issue with `Closes #n`, squash on merge.

## Stories and tasks
A **story** is a vertical slice someone can watch working. A **task** is one unit of
work under a story. Stories carry a `slice/N` label matching the build plan.

A story is done when its acceptance criteria are demonstrable end to end — not when
the code exists.

## Definition of done
- Builds with no new warnings, tests green.
- Synthetic data only. No real patient, provider, or clinic identifiers, ever.
- Any AWS resource added is declared in Terraform and its running cost is known.
- Anything left running is recorded, with its cost, before the session ends.

## Pairing
Some tasks are marked as hand-written on purpose — the check-digit validation, the
Bedrock call, and one Lambda. Those are not generated; they get written and walked
through by hand so they can be explained line by line.
