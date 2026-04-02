# Draco: Autonomous Cloud Governance Sentinel

**Draco** is an autonomous governance and observability platform designed to watch over your cloud infrastructure like a sentinel. Powered by **Google Gemini AI**, it identifies anomalies, generates remediations, and integrates directly into your GitOps workflow.

![Draco Logo](src/Draco.Web/public/draco-colored.svg)

---

## 🔥 Key Features

- **🤖 AI-Powered Analysis**: Leverages Google Gemini to analyze resource snapshots and provide natural language reasoning for infrastructure changes.
- **🛰️ Autonomous Observation**: Continuous discovery and ingestion of cloud resources across multi-provider environments.
- **🛠️ GitOps Remediation**: Automatically generates Terraform/HCL fixes and opens Pull Requests for human review.
- **📱 Multi-Channel Alerts**: Messaging hooks exist, but the current deployment path is focused on core cloud visibility, workflows, and AI-assisted operations first.
- **🔒 Privacy-First**: Zero-data-collection architecture. All sensitive data is processed within your controlled environment.
- **🖥️ Governance Portal**: A React dashboard for onboarding cloud accounts, reviewing inventory, costs, workflows, and AI insights.

## 🛠️ Technology Stack

- **Core**: .NET 9 API + CLI
- **AI**: Google Gemini API
- **Frontend**: React + Vite
- **Database**: Neon (Serverless PostgreSQL)
- **Auth**: WorkOS
- **Messaging**: Deferred for the main deployment path
- **Infrastructure**: Terraform / GitOps

## 🚀 Getting Started

### 1. Configuration
Initialize your environment variables by configuring the active app paths:
```bash
cp src/Draco.Api/.env src/Draco.Api/.env.local 2>/dev/null || true
```

### 2. Database Setup
Ensure you have a **Neon PostgreSQL** instance available and set:

- `DRACO_DB_MAIN_CONNECTION` for application data
- `DRACO_DB_RELEASE_CONNECTION` for release/public content if you use it

The active app does not require Neon Auth.

For Twilio-backed outbound messaging, also set:

- `TWILIO_ACCOUNT_SID`
- `TWILIO_AUTH_TOKEN`
- `TWILIO_SMS_FROM_NUMBER`
- `TWILIO_WHATSAPP_FROM_NUMBER` (optional)
- `SENDGRID_API_KEY`
- `SENDGRID_FROM_EMAIL`

### 3. Launch the App
```bash
bun start-sentinel.js
```

## ⚖️ License
Built for secure, autonomous clouds. Refer to the [Terms of Service](/docs/terms) and [Privacy Policy](/docs/privacy) for more details.

---
*Developed for those who demand ultimate control over their cloud destiny.*
