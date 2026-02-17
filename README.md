# 🐉 Draco: Autonomous Cloud Governance Sentinel

**Draco** is an autonomous governance and observability platform designed to watch over your cloud infrastructure like a sentinel. Powered by **Google Gemini AI**, it identifies anomalies, generates remediations, and integrates directly into your GitOps workflow.

![Draco Logo](src/Draco.Web/public/draco-colored.svg)

---

## 🔥 Key Features

- **🤖 AI-Powered Analysis**: Leverages Google Gemini to analyze resource snapshots and provide natural language reasoning for infrastructure changes.
- **🛰️ Autonomous Observation**: Continuous discovery and ingestion of cloud resources across multi-provider environments.
- **🛠️ GitOps Remediation**: Automatically generates Terraform/HCL fixes and opens Pull Requests for human review.
- **📱 Multi-Channel Alerts**: Real-time notifications via Twilio (SMS) and SendGrid (Email) with interactive approval flows.
- **🔒 Privacy-First**: Zero-data-collection architecture. All sensitive data is processed within your controlled environment.
- **🎨 Premium Governance Portal**: A high-performance Documentation Hub and Changelog system built with Astro and styled with the *Dragon Emperor* (L-Drago) aesthetic.

## 🛠️ Technology Stack

- **Core**: .NET 8 CLI
- **AI**: Google Gemini Pro
- **Frontend**: Astro (SSR Mode)
- **Database**: Neon (Serverless PostgreSQL)
- **Messaging**: Twilio (SMS), SendGrid (Email)
- **Infrastructure**: Terraform / GitOps

## 🚀 Getting Started

### 1. Configuration
Initialize your environment variables by copying the examples:
```bash
cp src/Draco.Cli/.env.example src/Draco.Cli/.env
cp src/Draco.Web/.env.example src/Draco.Web/.env
```

### 2. Database Setup
Ensure you have a **Neon PostgreSQL** instance available. Update the `NEON_DATABASE_URL` in your `.env` files.

### 3. Launch the Hub
```bash
cd src/Draco.Web
npm install
npm run dev
```

## ⚖️ License
Built for secure, autonomous clouds. Refer to the [Terms of Service](/docs/terms) and [Privacy Policy](/docs/privacy) for more details.

---
*Developed for those who demand ultimate control over their cloud destiny.*