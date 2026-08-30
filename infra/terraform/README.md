# ReqLens infrastructure

Terraform for the ReqLens stack: VPC, S3, SQS + DLQ, RDS Postgres, Secrets Manager, and one
IAM role per Lambda.

## Running it

```bash
cd infra/terraform
cp example.tfvars terraform.tfvars     # then set admin_cidr to your own address
terraform init
terraform plan  -var-file=terraform.tfvars
terraform apply -var-file=terraform.tfvars
```

`admin_cidr` has no default on purpose. It is the only address outside the VPC allowed to
reach Postgres, and a careless value there is the difference between one laptop and the open
internet. The variable requires a single IPv4 host ending in `/32`; anything broader is
refused, including ranges like `0.0.0.0/1` that quietly cover half the internet.

Find your address with `curl -s https://checkip.amazonaws.com`. It changes when your network
does; re-apply after a change or psql will hang.

## Shape

- **VPC** across two AZs. Public subnets hold the NAT gateway and RDS; private subnets hold the
  Lambdas, which have no inbound route from the internet.
- **One NAT gateway**, not one per AZ. A second would double the largest line on the bill to buy
  availability a demo does not need.
- **S3 gateway endpoint** on the private route table. Scans in and OCR blocks out are the bulk of
  what moves, and a gateway endpoint costs nothing while keeping that traffic off the NAT, where
  it would be billed per gigabyte.
- **RDS is `publicly_accessible`** so psql works from the demo laptop. The security group is what
  keeps it private: ingress is one CIDR plus the Lambda security group, and nothing else.
- **One IAM role per function.** Ingest has no business calling Bedrock and Extract has no
  business calling Textract, so they do not share a role. Bedrock access is scoped to Anthropic
  and Amazon model ARNs, which is what lets the fallback chain reach a second family without
  opening up every model in the catalogue.
- **DLQ after three attempts.** One transient failure is normal; three is a defect worth keeping,
  so the dead-letter queue retains for the full fourteen days.

## What it costs

Roughly **$1.55/day**, dominated by one line:

| Resource | Rate | Per day |
|---|---|---|
| NAT gateway | $0.045/hr + $0.045/GB | $1.08 |
| RDS db.t4g.micro, single-AZ | ~$0.016/hr | $0.39 |
| gp3 storage, 20 GB | $0.115/GB-month | $0.08 |
| Secrets Manager, 1 secret | $0.40/month | $0.01 |
| S3, SQS | per request | pennies |

The NAT is two thirds of it and bills whether or not anything runs. Left up for a month it is
about $32 on its own. **Destroy the stack when you are not using it:**

```bash
terraform destroy -var-file=terraform.tfvars
```

If you want to keep the database between sessions but stop paying for compute, stop the RDS
instance instead - storage still bills, and AWS restarts a stopped instance after seven days.

In production the NAT would be replaced by interface endpoints for Bedrock, Textract, SQS and
Secrets Manager: similar cost at this scale, but the traffic never leaves the AWS network. It is
a NAT here because it is one resource instead of four and the stack is short-lived.

## Notes

- `force_destroy` is set on the bucket and `skip_final_snapshot` on the database. Both are
  deliberate: everything in this stack is synthetic and regenerable, and a destroy that strands
  resources costs money silently.
- Postgres major version is pinned to `17`, which resolves to the latest 17.x minor.
