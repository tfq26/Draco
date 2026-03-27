# AWS Onboarding Guide

This page explains how to connect an AWS account to Draco using the recommended read-only IAM role flow.

## For End Users

If Draco is already configured by your workspace admin, you only need to do these steps:

1. Open AWS Console: [https://console.aws.amazon.com/](https://console.aws.amazon.com/)
2. Find your 12-digit AWS account ID from the account menu or Billing page.
3. In Draco, open **Setup** or **Settings** and choose **AWS**.
4. Paste the AWS account ID.
5. Copy the **Trusted Principal**, **External ID**, **Trust Policy**, and **Read-Only Permissions Policy** shown by Draco.
6. In AWS IAM, create a new role using Draco's trust policy.
7. Attach the read-only permissions policy Draco provides.
8. Copy the created role ARN from AWS.
9. Paste the role ARN back into Draco.
10. Connect the account.

### IAM Console Links

- AWS IAM Console: [https://console.aws.amazon.com/iamv2/home#/home](https://console.aws.amazon.com/iamv2/home#/home)
- AWS Billing Console: [https://console.aws.amazon.com/billing/home](https://console.aws.amazon.com/billing/home)

### What Draco Stores

- Draco stores the AWS account ID.
- Draco stores the IAM role ARN.
- Draco does not need to keep the bootstrap template after setup.
- The role ARN is not a secret by itself.

## For Workspace Admins

If users see this message:

`Guided AWS onboarding is not configured for this Draco workspace yet. Ask your workspace admin to finish the AWS trust setup, or use Access Keys under Advanced.`

then Draco itself is missing the AWS-side trust configuration required to generate onboarding details.

### Admin Steps

1. Decide which AWS principal Draco will use to assume customer roles.
2. Give the Draco API valid AWS runtime credentials for that principal, or set `AWS_ASSUME_ROLE_PRINCIPAL_ARN` explicitly.
3. Restart the Draco API.
4. Open the AWS onboarding flow again and confirm Draco now returns:
   - Trusted Principal
   - External ID
   - Trust Policy
   - Read-Only Permissions Policy

### Required API Configuration

You can configure Draco in either of these ways:

1. Provide AWS runtime credentials to the API process:
   - `AWS_ACCESS_KEY_ID`
   - `AWS_SECRET_ACCESS_KEY`
   - `AWS_SESSION_TOKEN` if using temporary credentials
2. Or set this explicit value on the API:
   - `AWS_ASSUME_ROLE_PRINCIPAL_ARN`

### Example

```env
AWS_ASSUME_ROLE_PRINCIPAL_ARN=arn:aws:iam::123456789012:role/draco-control-plane
```

### Notes

- Draco still needs working AWS credentials at runtime even if `AWS_ASSUME_ROLE_PRINCIPAL_ARN` is set.
- The environment variable only helps Draco tell users which principal to trust.
- The runtime credentials are what let Draco actually call `sts:AssumeRole` later during sync.

## Troubleshooting

### Draco says the trusted principal is not configured

Ask your workspace admin to complete the admin steps above.

### Draco says the role ARN is missing

Finish the IAM role creation in AWS and paste the resulting role ARN back into Draco.

### Users cannot use Assume Role yet

Use **Access Keys** only as a temporary advanced fallback. Draco must retain those credentials to keep syncing the account.
