#!/usr/bin/env bash
# Packages the three C# Lambdas into artifacts/ as zips Terraform can upload.
#
# Run this before `terraform apply` whenever Lambda code changes. Terraform hashes the zips,
# so a stale artifact silently deploys stale code - the failure mode is a function that runs
# yesterday's logic with today's configuration.
set -euo pipefail

cd "$(dirname "$0")/.."
ROOT=$(pwd)
OUT="$ROOT/artifacts"
mkdir -p "$OUT"

export PATH="$PATH:$HOME/.dotnet/tools"

if ! command -v dotnet-lambda >/dev/null 2>&1; then
  echo "dotnet-lambda not found. Install it with:" >&2
  echo "  dotnet tool install --global Amazon.Lambda.Tools" >&2
  exit 1
fi

# arm64 throughout: Graviton is cheaper per millisecond and every dependency here is portable.
package() {
  local project=$1 zip=$2
  echo "packaging $project -> artifacts/$zip"
  (
    cd "$ROOT/src/$project"
    dotnet lambda package \
      --configuration Release \
      --framework net10.0 \
      --function-architecture arm64 \
      --output-package "$OUT/$zip" \
      >/dev/null
  )
}

package ReqLens.Lambdas.Ingest  ingest.zip
package ReqLens.Lambdas.Extract extract.zip
package ReqLens.Lambdas.Api     api.zip

echo
ls -lh "$OUT"/*.zip | awk '{printf "  %-14s %s\n", $9, $5}'
