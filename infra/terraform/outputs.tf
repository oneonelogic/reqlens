data "aws_caller_identity" "current" {}

output "bucket_name" {
  description = "Requisition bucket. Scans go under scans/, OCR output under ocr/."
  value       = aws_s3_bucket.requisitions.id
}

output "extract_queue_url" {
  value = aws_sqs_queue.extract.url
}

output "extract_dlq_url" {
  value = aws_sqs_queue.extract_dlq.url
}

output "db_endpoint" {
  value = aws_db_instance.main.endpoint
}

output "db_secret_arn" {
  description = "Secrets Manager ARN holding the Postgres credentials."
  value       = aws_secretsmanager_secret.db.arn
}

output "psql_command" {
  description = "Ready-to-run psql, once the secret is fetched."
  value       = "psql -h ${aws_db_instance.main.address} -p ${aws_db_instance.main.port} -U ${aws_db_instance.main.username} -d ${aws_db_instance.main.db_name}"
}

output "lambda_role_arns" {
  value = {
    ingest  = aws_iam_role.ingest.arn
    extract = aws_iam_role.extract.arn
    api     = aws_iam_role.api.arn
  }
}

output "lambda_vpc_config" {
  description = "Subnets and security group the Lambdas attach to."
  value = {
    subnet_ids         = aws_subnet.private[*].id
    security_group_ids = [aws_security_group.lambda.id]
  }
}

output "guardrail_id" {
  description = "Attach to Converse calls as guardrailIdentifier."
  value       = aws_bedrock_guardrail.main.guardrail_id
}

output "guardrail_version" {
  description = "Pinned version, so editing the guardrail cannot silently change a running pipeline."
  value       = aws_bedrock_guardrail_version.main.version
}
