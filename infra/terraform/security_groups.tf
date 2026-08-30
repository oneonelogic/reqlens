resource "aws_security_group" "lambda" {
  name        = "${var.project}-lambda"
  description = "ReqLens Lambdas. Egress only."
  vpc_id      = aws_vpc.main.id

  tags = { Name = "${var.project}-lambda" }
}

resource "aws_vpc_security_group_egress_rule" "lambda_all" {
  security_group_id = aws_security_group.lambda.id
  description       = "Outbound to AWS services via NAT and the S3 endpoint."
  ip_protocol       = "-1"
  cidr_ipv4         = "0.0.0.0/0"
}

resource "aws_security_group" "rds" {
  name        = "${var.project}-rds"
  description = "Postgres. Reachable from the Lambdas and from one admin address."
  vpc_id      = aws_vpc.main.id

  tags = { Name = "${var.project}-rds" }
}

resource "aws_vpc_security_group_ingress_rule" "rds_from_lambda" {
  security_group_id            = aws_security_group.rds.id
  description                  = "Postgres from the ReqLens Lambdas."
  referenced_security_group_id = aws_security_group.lambda.id
  from_port                    = 5432
  to_port                      = 5432
  ip_protocol                  = "tcp"
}

resource "aws_vpc_security_group_ingress_rule" "rds_from_admin" {
  security_group_id = aws_security_group.rds.id
  description       = "Postgres from the demo laptop only, for psql."
  cidr_ipv4         = var.admin_cidr
  from_port         = 5432
  to_port           = 5432
  ip_protocol       = "tcp"
}
