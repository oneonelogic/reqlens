locals {
  artifacts = "${path.module}/../../artifacts"

  # The fallback chain lives in configuration, not in code, so swapping a model is an
  # environment change that can be demonstrated live rather than a rebuild.
  # Roles: primary is the cheap default; availability is a DIFFERENT family, so a single
  # vendor's outage cannot stop intake; escalation is a stronger sibling for output that
  # fails schema or confidence, one hop only, before the human queue takes over.
  model_chain = jsonencode({
    models = [
      {
        modelId                     = "us.anthropic.claude-haiku-4-5-20251001-v1:0"
        role                        = "Primary"
        maxCostPerDoc               = 0.05
        inputPricePerMillionTokens  = 1.00
        outputPricePerMillionTokens = 5.00
      },
      {
        modelId                     = "us.amazon.nova-2-lite-v1:0"
        role                        = "Availability"
        maxCostPerDoc               = 0.05
        inputPricePerMillionTokens  = 0.06
        outputPricePerMillionTokens = 0.24
      },
      {
        modelId                     = "us.anthropic.claude-sonnet-4-5-20250929-v1:0"
        role                        = "Escalation"
        maxCostPerDoc               = 0.25
        inputPricePerMillionTokens  = 3.00
        outputPricePerMillionTokens = 15.00
      }
    ]
  })

  lambda_common_env = {
    REQUISITIONS_BUCKET = aws_s3_bucket.requisitions.id
    DB_SECRET_ARN       = aws_secretsmanager_secret.db.arn
  }
}

# Explicit log groups so retention is set. A group Lambda creates for itself never expires,
# which is a slow leak on a demo account.
resource "aws_cloudwatch_log_group" "lambda" {
  for_each = toset(["ingest", "extract", "api"])

  name              = "/aws/lambda/${var.project}-${each.key}"
  retention_in_days = 7
}

resource "aws_lambda_function" "ingest" {
  function_name = "${var.project}-ingest"
  role          = aws_iam_role.ingest.arn
  handler       = "ReqLens.Lambdas.Ingest::ReqLens.Lambdas.Ingest.IngestFunction::FunctionHandler"
  runtime       = "dotnet10"
  architectures = ["arm64"]

  filename         = "${local.artifacts}/ingest.zip"
  source_code_hash = filebase64sha256("${local.artifacts}/ingest.zip")

  # Textract on a multi-page scan is the slow part, and .NET cold starts are not free.
  memory_size = 512
  timeout     = 60

  vpc_config {
    subnet_ids         = aws_subnet.private[*].id
    security_group_ids = [aws_security_group.lambda.id]
  }

  environment {
    variables = merge(local.lambda_common_env, {
      EXTRACT_QUEUE_URL = aws_sqs_queue.extract.url
    })
  }

  depends_on = [aws_cloudwatch_log_group.lambda]
}

resource "aws_lambda_function" "extract" {
  function_name = "${var.project}-extract"
  role          = aws_iam_role.extract.arn
  handler       = "ReqLens.Lambdas.Extract::ReqLens.Lambdas.Extract.ExtractFunction::FunctionHandler"
  runtime       = "dotnet10"
  architectures = ["arm64"]

  filename         = "${local.artifacts}/extract.zip"
  source_code_hash = filebase64sha256("${local.artifacts}/extract.zip")

  # Must stay comfortably under the queue's 180s visibility timeout, or a slow Bedrock call
  # gets redelivered while the first attempt is still working and the document is extracted twice.
  memory_size = 1024
  timeout     = 120

  vpc_config {
    subnet_ids         = aws_subnet.private[*].id
    security_group_ids = [aws_security_group.lambda.id]
  }

  environment {
    variables = merge(local.lambda_common_env, {
      MODEL_CHAIN       = local.model_chain
      GUARDRAIL_ID      = aws_bedrock_guardrail.main.guardrail_id
      GUARDRAIL_VERSION = aws_bedrock_guardrail_version.main.version
    })
  }

  depends_on = [aws_cloudwatch_log_group.lambda]
}

resource "aws_lambda_function" "api" {
  function_name = "${var.project}-api"
  role          = aws_iam_role.api.arn
  # An ASP.NET Core Lambda started by AddAWSLambdaHosting is addressed by assembly name alone.
  handler       = "ReqLens.Lambdas.Api"
  runtime       = "dotnet10"
  architectures = ["arm64"]

  filename         = "${local.artifacts}/api.zip"
  source_code_hash = filebase64sha256("${local.artifacts}/api.zip")

  memory_size = 512
  timeout     = 30

  vpc_config {
    subnet_ids         = aws_subnet.private[*].id
    security_group_ids = [aws_security_group.lambda.id]
  }

  environment {
    variables = local.lambda_common_env
  }

  depends_on = [aws_cloudwatch_log_group.lambda]
}

# ---- event wiring -----------------------------------------------------------

resource "aws_lambda_permission" "s3_invoke_ingest" {
  statement_id   = "AllowExecutionFromS3"
  action         = "lambda:InvokeFunction"
  function_name  = aws_lambda_function.ingest.function_name
  principal      = "s3.amazonaws.com"
  source_arn     = aws_s3_bucket.requisitions.arn
  source_account = data.aws_caller_identity.current.account_id
}

# Scoped to scans/ so the notification cannot fire on the Ingest Lambda's own OCR output and
# drive the pipeline in a loop.
resource "aws_s3_bucket_notification" "requisitions" {
  bucket = aws_s3_bucket.requisitions.id

  lambda_function {
    lambda_function_arn = aws_lambda_function.ingest.arn
    events              = ["s3:ObjectCreated:*"]
    filter_prefix       = "scans/"
    filter_suffix       = ".pdf"
  }

  depends_on = [aws_lambda_permission.s3_invoke_ingest]
}

resource "aws_lambda_event_source_mapping" "extract" {
  event_source_arn = aws_sqs_queue.extract.arn
  function_name    = aws_lambda_function.extract.arn

  # One document per invocation. Batching would make a single poisoned scan fail its
  # whole batch, and the per-document telemetry is the point of this pipeline.
  batch_size = 1

  function_response_types = ["ReportBatchItemFailures"]
}
