# Privacy Policy for Draco

**Last Updated: February 17, 2026**

At **Draco**, your privacy and the security of your cloud metadata are our top priorities. This policy explains how information is handled within the platform.

## 1. Zero-Collection Policy
Draco is built on a "Privacy by Default" architecture. **We (the developers/maintainers) never collect, store, or have access to your data in any way.** The software is entirely self-hosted within your own infrastructure.

## 2. Information Handled During Interactions
When you interact with Draco via chat or messaging (e.g., SMS approvals), you are providing information directly to the instance of the software **you control**. This information is processed in real-time to facilitate your specific requests (such as approving a remediation). 
- **User Consent**: By using these features, you consent to the processing of this information within your own environment.
- **No Malicious Use**: You agree that you will provide information only for legitimate governance purposes and not for any malicious intent.

## 2. Use of Data
Data collected by Draco is used exclusively for:
- Detecting cost and security anomalies in your cloud infrastructure.
- Generating natural language alerts and remediation scripts.
- Orchestrating multi-channel notifications.

## 3. Third-Party Services
Draco integrates with the following third-party services. Usage of these services is governed by their respective privacy policies:
- **Google Gemini**: For AI analysis and remediation generation.
- **GitHub**: For GitOps workflows and Pull Request management.
- **Twilio**: For SMS-based alerting and approvals.
- **SendGrid**: For email notifications.

**Note**: Draco is designed to redact or minimize PII (Personally Identifiable Information) before sending data to AI providers.

## 4. Data Security
As a self-hosted tool, the security of the data stored in the Draco database (PostgreSQL) is your responsibility. We recommend:
- Using encrypted connection strings.
- Restricting network access to the database.
- Managing API keys securely via environment variables or secret managers.

## 5. Data Retention
Draco stores historical scan data in your local or hosted database according to your own retention policies. Draco does not phone home to any centralized telemetry server.

## 6. Your Rights
Since Draco is self-hosted, you have full control over your data. You may delete or modify any scan history or configuration at any time by accessing your database or configuration files.

## 7. Contact
For questions regarding this policy or the software, please refer to the project's repository.
