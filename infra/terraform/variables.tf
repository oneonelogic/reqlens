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

  validation {
    condition     = var.admin_cidr != "0.0.0.0/0"
    error_message = "admin_cidr must not be 0.0.0.0/0. Use your own address."
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
