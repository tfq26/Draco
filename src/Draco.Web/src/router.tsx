import { createRouter } from '@tanstack/react-router'
import { Route as rootRoute } from './routes/__root'
import { Route as indexRoute } from './routes/index'
import { Route as dashboardRoute } from './routes/dashboard'
import { Route as setupRoute } from './routes/setup'
import { Route as loginRoute } from './routes/login'
import { CallbackRoute, LegacyCallbackRoute } from './routes/callback'
import { ResourcesRoute } from './routes/resources'
import { GovernanceRoute } from './routes/governance'
import { SettingsRoute } from './routes/settings'

const routeTree = rootRoute.addChildren([
  indexRoute, 
  dashboardRoute, 
  setupRoute, 
  loginRoute,
  CallbackRoute,
  LegacyCallbackRoute,
  ResourcesRoute,
  GovernanceRoute,
  SettingsRoute
])

export const router = createRouter({ routeTree })

declare module '@tanstack/react-router' {
  interface Register {
    router: typeof router
  }
}
