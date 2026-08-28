# ReqLens infrastructure

Terraform for the whole stack: S3 (requisitions + static site), CloudFront, API Gateway,
three Lambdas with their IAM roles, SQS + DLQ, RDS Postgres, Secrets Manager, CloudWatch
log groups, dashboard and alarms.

Only the provider pinning is here so far; the resources land with the Terraform pass.

```bash
terraform init
terraform plan
terraform apply
terraform destroy   # the RDS instance is the only thing that costs real money when idle
```
