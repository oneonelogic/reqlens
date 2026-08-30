resource "random_password" "db" {
  length = 32
  # RDS rejects several punctuation characters in master passwords; this set is safe.
  override_special = "!#$%&*()-_=+[]{}<>:?"
}

resource "aws_secretsmanager_secret" "db" {
  name                    = "${var.project}/db/master-${random_id.suffix.hex}"
  description             = "ReqLens Postgres master credentials."
  recovery_window_in_days = 0 # demo stack: allow immediate re-create after destroy
}

resource "aws_secretsmanager_secret_version" "db" {
  secret_id = aws_secretsmanager_secret.db.id

  secret_string = jsonencode({
    username = aws_db_instance.main.username
    password = random_password.db.result
    host     = aws_db_instance.main.address
    port     = aws_db_instance.main.port
    dbname   = aws_db_instance.main.db_name
  })
}
