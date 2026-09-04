# Copy to terraform.tfvars and set admin_cidr to your own address.
# Find it with: curl -s https://checkip.amazonaws.com
admin_cidr = "203.0.113.4/32"

# region            = "us-east-2"
# db_instance_class = "db.t4g.micro"

# "textract" is the intended provider. Set "pdf-text-layer" only to keep the pipeline runnable
# when Textract is unavailable; it performs no OCR and works solely on born-digital PDFs.
# "textract" | "pdf-text-layer" | "bedrock-vision". See the OCR section of the top-level README.
ocr_provider = "textract"

# Only read when ocr_provider is "bedrock-vision".
ocr_model_id = "us.anthropic.claude-haiku-4-5-20251001-v1:0"

# Publishes the API and console on a public, unauthenticated URL. Off unless a live URL is wanted.
enable_public_api = false
