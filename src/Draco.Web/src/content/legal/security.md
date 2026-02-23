# Security Protocols for Draco

**Last Updated: February 23, 2026**

The security of your cloud infrastructure and the credentials used to manage it is the core foundation of **Draco Sentinel**. This document outlines the protocols, architectural decisions, and code-level protections in place to ensure your data remains secure and private.

## 1. Zero-Trust Access Model
Draco operates on a "Least Privilege" principle. Every interaction is designed to require the minimum level of access necessary to perform its function.

### Cloud Permission Scopes
By default, the Draco OAuth connection requests **Read-Only** permissions. 
- **Azure**: Utilizes the `Reader` role or specific `Management.Read` scopes.
- **AWS**: Interacts with resources using IAM roles or user policies strictly following `ReadOnlyAccess` standards.

This ensures that even if Draco is compromised, the sentinel cannot delete or modify your production infrastructure without explicit, secondary approval through a separate GitOps workflow.

## 2. Handshake Security (Onboarding)
To prevent unauthorized users from linking cloud accounts to your identity, we implement a multi-layered verification handshake during setup:

### Proof-of-Ownership (PoO)
1. **Physical Verification**: Before any cloud connection is initiated, the user must verify their identity via a 2-factor "Pulse" sent to their physical device (SMS/WhatsApp).
2. **Session Binding**: Upon successful verification, the Draco API issues a unique, cryptographically secure **Session Token**. 
3. **Strict Binding**: The OAuth `state` parameter is bound to this Session Token. If a third party attempts to intercept the OAuth redirect or initiate a link for your phone number without the token, the API will reject the connection immediately.

## 3. Credential Protection & Management
### Encryption at Rest
Credentials such as OAuth Refresh Tokens and API keys are stored in your private PostgreSQL database. We strongly recommend using **Transparent Data Encryption (TDE)** or equivalent database-level encryption provided by your host (e.g., Neon or Azure SQL).

### Token Isolation
Draco does not use a single "Master Key" for all users. Each user's cloud connection is isolated within the database and identified by their unique, verified phone number. Access tokens are never shared between sessions or users.

### Environment Security
Master API keys (Twilio, SendGrid, Gemini) are managed exclusively through **Environment Variables** (`.env`). They are never hardcoded and are not accessible via the Draco web interface.

## 4. Secure Communication
### HTTPS/TLS
All data in transit between the Draco Web interface, the Draco API, and your cloud providers is encrypted using **TLS 1.2+**. 

### CORS Protection
The Draco API implements strict **Cross-Origin Resource Sharing (CORS)** policies, ensuring that only your trusted frontend domain can communicate with the backend services.

## 5. Third-Party Data Redaction
Before sending metadata to AI models (like Google Gemini) for analysis, Draco is designed to redact or obfuscate sensitive identifiers where possible, ensuring that your raw cloud security profile isn't fully exposed to external LLM providers.

## 6. Self-Hosted Privacy
Draco is a **Self-Hosted** platform. We (the Draco maintainers) never have access to your database, your cloud credentials, or your infrastructure. The "keys to the kingdom" stay entirely within the infrastructure you control.
