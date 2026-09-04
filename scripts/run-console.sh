#!/usr/bin/env bash
# Runs the API and the Blazor review console locally against the deployed database.
#
# One process serves both: the API project hosts the WASM client, so there is no second server
# to start and no CORS to configure. This is how the demo is driven - the pipeline (S3, Textract,
# Bedrock, Lambda, RDS) runs in AWS, and only the console runs here.
#
# Nothing below ever holds the database password. DB_SECRET_ARN names the secret; the AWS SDK
# resolves it inside the process.
set -euo pipefail

cd "$(dirname "$0")/.."

state=infra/terraform/terraform.tfstate

if [[ ! -f $state ]]; then
  echo "No Terraform state at $state. Apply the stack first." >&2
  exit 1
fi

read_output() {
  python3 -c "import json,sys; print(json.load(open('$state'))['outputs'].get('$1',{}).get('value',''))"
}

export DB_SECRET_ARN="${DB_SECRET_ARN:-$(read_output db_secret_arn)}"
export REQUISITIONS_BUCKET="${REQUISITIONS_BUCKET:-$(read_output bucket_name)}"
export AWS_REGION="${AWS_REGION:-$(read_output region)}"
export AWS_REGION="${AWS_REGION:-us-east-2}"

if [[ -z $DB_SECRET_ARN || -z $REQUISITIONS_BUCKET ]]; then
  echo "Terraform state has no db_secret_arn / bucket_name outputs. Has the stack been applied?" >&2
  exit 1
fi

export ASPNETCORE_URLS="${ASPNETCORE_URLS:-http://localhost:5080}"

# The Blazor client's files are served in place from its build output, and the manifest that
# makes that work is only loaded in the Development environment. Without this the API answers
# fine and every static asset 404s, which presents as a blank page rather than as an error.
# A published deployment copies the client into wwwroot and does not need it.
export ASPNETCORE_ENVIRONMENT="${ASPNETCORE_ENVIRONMENT:-Development}"

echo "Console:  $ASPNETCORE_URLS"
echo "Bucket:   $REQUISITIONS_BUCKET"
echo

# The database is reachable only from admin_cidr. A hang here almost always means the laptop's
# public IP has moved - compare `curl -s https://checkip.amazonaws.com` against admin_cidr in
# infra/terraform/terraform.tfvars.
# --no-launch-profile: launchSettings.json pins its own applicationUrl and would otherwise
# override ASPNETCORE_URLS, leaving the console on a port nobody was told about.
exec dotnet run --project src/ReqLens.Lambdas.Api --configuration Release --no-launch-profile
