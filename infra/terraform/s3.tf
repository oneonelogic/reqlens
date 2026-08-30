resource "random_id" "suffix" {
  byte_length = 4
}

# Requisition scans in, Textract block JSON out. One bucket, two prefixes, so the Ingest
# Lambda's S3 trigger can be scoped to scans/ and never fire on its own output.
resource "aws_s3_bucket" "requisitions" {
  bucket        = "${var.project}-requisitions-${random_id.suffix.hex}"
  force_destroy = true # demo stack: destroy should not strand a bucket full of synthetic PDFs
}

resource "aws_s3_bucket_public_access_block" "requisitions" {
  bucket                  = aws_s3_bucket.requisitions.id
  block_public_acls       = true
  block_public_policy     = true
  ignore_public_acls      = true
  restrict_public_buckets = true
}

resource "aws_s3_bucket_server_side_encryption_configuration" "requisitions" {
  bucket = aws_s3_bucket.requisitions.id

  rule {
    apply_server_side_encryption_by_default {
      sse_algorithm = "AES256"
    }
  }
}

resource "aws_s3_bucket_versioning" "requisitions" {
  bucket = aws_s3_bucket.requisitions.id

  versioning_configuration {
    status = "Enabled"
  }
}

# Nothing here is worth storing for long, and versioning would otherwise accumulate silently.
resource "aws_s3_bucket_lifecycle_configuration" "requisitions" {
  bucket     = aws_s3_bucket.requisitions.id
  depends_on = [aws_s3_bucket_versioning.requisitions]

  rule {
    id     = "expire-noncurrent"
    status = "Enabled"

    filter {}

    noncurrent_version_expiration {
      noncurrent_days = 7
    }

    abort_incomplete_multipart_upload {
      days_after_initiation = 1
    }
  }
}
