data "aws_iam_policy_document" "lambda_assume" {
  statement {
    actions = ["sts:AssumeRole"]

    principals {
      type        = "Service"
      identifiers = ["lambda.amazonaws.com"]
    }
  }
}

# One role per function rather than a shared one: Ingest has no business calling Bedrock,
# and Extract has no business calling Textract. The split is the point.
resource "aws_iam_role" "ingest" {
  name               = "${var.project}-ingest"
  assume_role_policy = data.aws_iam_policy_document.lambda_assume.json
}

resource "aws_iam_role" "extract" {
  name               = "${var.project}-extract"
  assume_role_policy = data.aws_iam_policy_document.lambda_assume.json
}

resource "aws_iam_role" "api" {
  name               = "${var.project}-api"
  assume_role_policy = data.aws_iam_policy_document.lambda_assume.json
}

# Managed policy for CloudWatch Logs plus the ENI permissions a VPC-attached Lambda needs.
resource "aws_iam_role_policy_attachment" "vpc_access" {
  for_each = {
    ingest  = aws_iam_role.ingest.name
    extract = aws_iam_role.extract.name
    api     = aws_iam_role.api.name
  }

  role       = each.value
  policy_arn = "arn:aws:iam::aws:policy/service-role/AWSLambdaVPCAccessExecutionRole"
}

data "aws_iam_policy_document" "ingest" {
  statement {
    sid       = "ReadScans"
    actions   = ["s3:GetObject"]
    resources = ["${aws_s3_bucket.requisitions.arn}/scans/*"]
  }

  statement {
    sid       = "WriteOcrBlocks"
    actions   = ["s3:PutObject"]
    resources = ["${aws_s3_bucket.requisitions.arn}/ocr/*"]
  }

  statement {
    sid       = "Ocr"
    actions   = ["textract:AnalyzeDocument"]
    resources = ["*"] # Textract has no resource-level permissions for this action
  }

  statement {
    sid       = "EnqueueExtraction"
    actions   = ["sqs:SendMessage"]
    resources = [aws_sqs_queue.extract.arn]
  }

  statement {
    sid       = "ReadDbCredentials"
    actions   = ["secretsmanager:GetSecretValue"]
    resources = [aws_secretsmanager_secret.db.arn]
  }
}

data "aws_iam_policy_document" "extract" {
  statement {
    sid       = "ReadOcrBlocks"
    actions   = ["s3:GetObject"]
    resources = ["${aws_s3_bucket.requisitions.arn}/ocr/*"]
  }

  statement {
    sid       = "ConsumeQueue"
    actions   = ["sqs:ReceiveMessage", "sqs:DeleteMessage", "sqs:GetQueueAttributes"]
    resources = [aws_sqs_queue.extract.arn]
  }

  statement {
    sid = "InvokeModels"
    actions = [
      "bedrock:InvokeModel",
      "bedrock:Converse"
    ]
    # Foundation models and the inference profiles that front them. Scoped to Anthropic and
    # Amazon so the fallback chain can reach a second family without opening up everything.
    resources = [
      "arn:aws:bedrock:*::foundation-model/anthropic.*",
      "arn:aws:bedrock:*::foundation-model/amazon.*",
      "arn:aws:bedrock:${var.region}:${data.aws_caller_identity.current.account_id}:inference-profile/*"
    ]
  }

  statement {
    sid       = "ReadDbCredentials"
    actions   = ["secretsmanager:GetSecretValue"]
    resources = [aws_secretsmanager_secret.db.arn]
  }

  statement {
    sid       = "PublishTelemetry"
    actions   = ["cloudwatch:PutMetricData"]
    resources = ["*"] # PutMetricData is not resource-scopable

    condition {
      test     = "StringEquals"
      variable = "cloudwatch:namespace"
      values   = ["ReqLens"]
    }
  }
}

data "aws_iam_policy_document" "api" {
  statement {
    sid       = "ReadScansForReview"
    actions   = ["s3:GetObject"]
    resources = ["${aws_s3_bucket.requisitions.arn}/scans/*"]
  }

  statement {
    sid       = "PresignUploads"
    actions   = ["s3:PutObject"]
    resources = ["${aws_s3_bucket.requisitions.arn}/scans/*"]
  }

  statement {
    sid       = "ReadDbCredentials"
    actions   = ["secretsmanager:GetSecretValue"]
    resources = [aws_secretsmanager_secret.db.arn]
  }
}

resource "aws_iam_role_policy" "ingest" {
  name   = "${var.project}-ingest"
  role   = aws_iam_role.ingest.id
  policy = data.aws_iam_policy_document.ingest.json
}

resource "aws_iam_role_policy" "extract" {
  name   = "${var.project}-extract"
  role   = aws_iam_role.extract.id
  policy = data.aws_iam_policy_document.extract.json
}

resource "aws_iam_role_policy" "api" {
  name   = "${var.project}-api"
  role   = aws_iam_role.api.id
  policy = data.aws_iam_policy_document.api.json
}
