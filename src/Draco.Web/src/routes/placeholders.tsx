import { createRoute } from '@tanstack/react-router'
import { Route as rootRoute } from './__root'

export const ResourcesRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/resources',
  component: () => <div className="animate-fade-in"><h2>Cloud Resources</h2><p style={{ color: 'var(--muted)' }}>Asset discovery and inventory tracking.</p></div>,
})

export const GovernanceRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/governance',
  component: () => <div className="animate-fade-in"><h2>Governance Policies</h2><p style={{ color: 'var(--muted)' }}>Policy enforcement and drift detection rules.</p></div>,
})

export const SettingsRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/settings',
  component: () => <div className="animate-fade-in"><h2>Settings</h2><p style={{ color: 'var(--muted)' }}>User preferences and API key management.</p></div>,
})
