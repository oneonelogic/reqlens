# Extraction is the expensive, failure-prone half of the pipeline, so it sits behind a queue:
# Textract finishing and Bedrock succeeding are separate concerns with separate retry stories.
resource "aws_sqs_queue" "extract_dlq" {
  name                      = "${var.project}-extract-dlq"
  message_retention_seconds = 1209600 # 14 days, the maximum - a poisoned message is evidence
}

resource "aws_sqs_queue" "extract" {
  name = "${var.project}-extract"

  # Comfortably above the Extract Lambda timeout so a slow Bedrock call cannot cause a
  # second delivery while the first is still working.
  visibility_timeout_seconds = 180
  message_retention_seconds  = 345600 # 4 days

  redrive_policy = jsonencode({
    deadLetterTargetArn = aws_sqs_queue.extract_dlq.arn
    # Three attempts: one transient failure is normal, three is a real defect.
    maxReceiveCount = 3
  })
}
