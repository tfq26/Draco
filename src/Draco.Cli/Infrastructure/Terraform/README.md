## Draco Terraform

This folder contains two kinds of infrastructure:

- baseline Draco hosting resources in Azure
- opt-in example templates for customer-side eventing

### Current fallback behavior

If event-driven monitoring is not configured, Draco falls back to periodic discovery.

- default interval: `20` minutes
- override with: `DRACO_DISCOVERY_INTERVAL_MINUTES`

Example:

```bash
export DRACO_DISCOVERY_INTERVAL_MINUTES=10
```

### Event-driven examples

The `examples/` folder contains starter Terraform templates for:

- AWS EventBridge -> Draco webhook
- Azure Activity Log Alert -> Draco webhook

These are intentionally provided as examples rather than included in the main plan because:

- customer clouds need explicit sign-off
- webhook URLs and secrets are environment-specific
- most teams will want to review those resources before applying them

### Existing Draco ingestion endpoint

The current protected event endpoint is:

- `POST /api/events/ingest`

For deterministic AWS routing, the EventBridge forwarder should include the owning Draco email in the event payload. The AWS example template now does that automatically using:

- `draco_api_events_ingest_url`
- `draco_event_ingestion_secret`
- `draco_user_email`

If the email is missing, Draco will still fall back to unique AWS connection ownership by account/subscription ID.

For Azure Monitor Activity Log alerts, Draco now also exposes:

- `POST /api/events/azure/activity-log?code=<DRACO_EVENT_INGESTION_SECRET>`

For deterministic per-customer routing, prefer:

- `POST /api/events/azure/activity-log?code=<DRACO_EVENT_INGESTION_SECRET>&userEmail=<customer-email>`

The Azure example template now builds that URL automatically from:

- `draco_activity_webhook_url`
- `draco_event_ingestion_secret`
- `draco_user_email`

It expects Draco's signed workflow-event payload shape, so the example files are best treated as starting points for a reviewed rollout rather than plug-and-play production automation.
