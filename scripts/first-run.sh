#!/usr/bin/env bash
# Everything between a freshly applied stack and a populated review queue.
#
# Safe to re-run: migrations and seeding are both idempotent, and re-uploading a scan reuses the
# order that already exists for it rather than creating a second one.
set -euo pipefail

cd "$(dirname "$0")/.."

echo "==> schema"
dotnet run --project src/ReqLens.Cli -- migrate

echo
echo "==> clinics and catalogue"
dotnet run --project src/ReqLens.Cli -- seed

echo
echo "==> uploading all twenty requisitions, each to the clinic it was generated for"
dotnet run --project src/ReqLens.Cli -- ingest --all

echo
echo "Ingest and Extract run on their own from here. Watch them with:"
echo "  aws logs tail /aws/lambda/reqlens-ingest  --follow --region us-east-2"
echo "  aws logs tail /aws/lambda/reqlens-extract --follow --region us-east-2"
echo
echo "Then:  dotnet run --project src/ReqLens.Cli -- orders"
echo "  or:  ./scripts/run-console.sh"
