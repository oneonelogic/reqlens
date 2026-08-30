resource "aws_db_subnet_group" "main" {
  name       = "${var.project}-db"
  subnet_ids = aws_subnet.public[*].id

  tags = { Name = "${var.project}-db" }
}

resource "aws_db_instance" "main" {
  identifier     = "${var.project}-db"
  engine         = "postgres"
  engine_version = "17"
  instance_class = var.db_instance_class

  db_name  = "reqlens"
  username = "reqlens_admin"
  password = random_password.db.result

  allocated_storage = var.db_allocated_storage
  storage_type      = "gp3"
  storage_encrypted = true

  db_subnet_group_name   = aws_db_subnet_group.main.name
  vpc_security_group_ids = [aws_security_group.rds.id]

  # Reachable from the demo laptop for psql. The security group, not this flag, is what keeps
  # it private: ingress is one CIDR plus the Lambda security group and nothing else.
  publicly_accessible = true

  multi_az                = false
  backup_retention_period = 0 # demo data is regenerable; backups would only cost money
  skip_final_snapshot     = true
  deletion_protection     = false # this stack is meant to be destroyed

  performance_insights_enabled = false
  auto_minor_version_upgrade   = true
  apply_immediately            = true

  tags = { Name = "${var.project}-db" }
}
