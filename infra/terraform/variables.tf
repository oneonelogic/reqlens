variable "region" {
  description = "AWS region for the ReqLens stack."
  type        = string
  default     = "us-east-2"
}

variable "project" {
  description = "Name prefix for all resources."
  type        = string
  default     = "reqlens"
}

variable "admin_cidr" {
  description = <<-EOT
    The only CIDR allowed to reach Postgres from outside the VPC, so the database can be
    inspected with psql during the demo. Set this to a single address (x.x.x.x/32).
    Deliberately has no default: a wrong value here is the difference between one laptop
    and the open internet.
  EOT
  type        = string

  validation {
    condition     = can(cidrhost(var.admin_cidr, 0))
    error_message = "admin_cidr must be valid CIDR notation, for example 203.0.113.4/32."
  }

  # Rejecting only 0.0.0.0/0 was not enough: 0.0.0.0/1 and 128.0.0.0/1 between them cover the
  # whole internet and both passed. Requiring a single IPv4 host is the check that matches the
  # intent. IPv4 specifically, because the security group rule consumes this as cidr_ipv4 - a
  # value like 2001:db8::/32 would satisfy a bare /32 suffix check and then fail deep in the
  # apply with a far less helpful message.
  validation {
    condition     = can(regex("^([0-9]{1,3}\\.){3}[0-9]{1,3}/32$", var.admin_cidr))
    error_message = "admin_cidr must be a single IPv4 host ending in /32, for example 203.0.113.4/32. Find yours with: curl -s https://checkip.amazonaws.com"
  }
}

variable "db_instance_class" {
  description = "RDS instance class. The smallest that runs Postgres; this is a demo."
  type        = string
  default     = "db.t4g.micro"
}

variable "db_allocated_storage" {
  description = "GB of gp3 storage for RDS."
  type        = number
  default     = 20
}

variable "vpc_cidr" {
  description = "CIDR for the ReqLens VPC."
  type        = string
  default     = "10.20.0.0/16"
}

variable "enable_public_api" {
  description = <<-EOT
    Whether to put an API Gateway HTTP API in front of the API Lambda.

    Off by default, and deliberately so. The API carries no authentication - the tenant is a
    query parameter - because what this project demonstrates is tenant isolation enforced in the
    data model, not an identity provider. Turning that endpoint on publishes an unauthenticated
    write path to the internet, and the data behind it being synthetic is a reason it is
    acceptable to expose, not a reason to leave it exposed by default.

    The review console runs against the database directly from a laptop with the API hosted
    locally, which is how the demo is driven. Set this to true only when a live URL is wanted.
  EOT

  type    = bool
  default = false
}

variable "ocr_provider" {
  description = <<-EOT
    Which OCR implementation the Ingest Lambda uses.

    "textract" is the intended one and what the architecture is built around. "pdf-text-layer"
    reads the text layer straight out of a born-digital PDF and performs no OCR at all - it works
    on the generated synthetic requisitions because they carry a real text layer, and would
    return nothing for a genuine scan.

    "bedrock-vision" hands the page to a vision model on Bedrock and asks it to transcribe what
    is printed. Unlike "pdf-text-layer" it reads a rendered page, so it works on a genuine scan
    or a photograph as well as a PDF. It reports no per-line confidence and no geometry, and it
    weakens the extractor's grounding check, because the text it produces and the values checked
    against it come from the same model family rather than from an independent reader.

    The two fallbacks exist because this account cannot currently call Textract: Textract,
    Comprehend and Transcribe all return SubscriptionRequiredException, which is an account
    activation matter and not something the code can fix. Either keeps the rest of the pipeline -
    the queue, the model chain, the guardrail, the validators, the review queue - demonstrable
    end to end while that is sorted out.
  EOT

  type    = string
  default = "textract"

  validation {
    condition     = contains(["textract", "pdf-text-layer", "bedrock-vision"], var.ocr_provider)
    error_message = "ocr_provider must be \"textract\", \"pdf-text-layer\" or \"bedrock-vision\"."
  }
}

variable "ocr_model_id" {
  description = <<-EOT
    The Bedrock model that transcribes a page when ocr_provider is "bedrock-vision". Ignored by
    the other providers.

    The cheap model is the right one: transcription is the easiest thing a vision model does, and
    the hard judgement in this pipeline happens in the extraction call, not this one.
  EOT

  type    = string
  default = "us.anthropic.claude-haiku-4-5-20251001-v1:0"
}
