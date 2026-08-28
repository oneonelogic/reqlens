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
