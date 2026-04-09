import { createRouter } from '@tanstack/react-router'
import { Route as rootRoute } from './routes/__root'
import { Route as indexRoute } from './routes/index'
import { Route as dashboardRoute } from './routes/dashboard'
import { Route as setupRoute } from './routes/setup'
import { CallbackRoute, LegacyCallbackRoute } from './routes/callback'
import { ResourcesRoute } from './routes/resources'
import { ResourceDetailRoute } from './routes/resource-detail'
import { GovernanceRoute } from './routes/governance'
import { SettingsRoute } from './routes/settings'
import { AwsOnboardingRoute } from './routes/aws-onboarding'

const routeTree = rootRoute.addChildren([
  indexRoute, 
  dashboardRoute, 
  setupRoute, 
  CallbackRoute,
  LegacyCallbackRoute,
  ResourcesRoute,
  ResourceDetailRoute,
  GovernanceRoute,
  SettingsRoute,
  AwsOnboardingRoute
])

export const router = createRouter({ routeTree })

declare module '@tanstack/react-router' {
  interface Register {
    router: typeof router
  }
}
