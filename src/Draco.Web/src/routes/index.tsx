import { Link, createRoute } from '@tanstack/react-router'
import { Route as rootRoute } from './__root'
import { useQuery } from '@tanstack/react-query'
import { dracoApi } from '../lib/api'

export const Route = createRoute({
  getParentRoute: () => rootRoute,
  path: '/',
  component: Index,
})

function Index() {
  const hasToken = Boolean(dracoApi.auth.getToken())
  const { data: user } = useQuery({
    queryKey: ['me'],
    queryFn: dracoApi.auth.getMe,
    enabled: hasToken,
  })

  const primaryTarget = hasToken ? (user?.isSetupComplete ? '/dashboard' : '/setup') : null
  const secondaryTarget = hasToken ? '/resources' : null

  const handleBeginSignIn = async () => {
    try {
      await dracoApi.auth.beginWorkOsSignIn()
    } catch (error) {
      console.error('Failed to start WorkOS sign-in:', error)
    }
  }

  return (
    <main className="animate-fade-in">
      <h1 className="monochrome-gradient" style={{ fontSize: '4rem', maxWidth: '800px', lineHeight: 1.1, marginBottom: '1rem' }}>
        Autonomous Cloud Governance that Scales with Confidence.
      </h1>
      <p style={{ fontSize: '1.25rem', color: 'var(--muted)', maxWidth: '600px', marginBottom: '3rem' }}>
        Deterministic remediation, multi-cloud discovery, and AI-powered insights for the modern infrastructure layer.
      </p>
      
      <div style={{ display: 'flex', gap: '1rem' }}>
        {hasToken ? (
          <>
            <Link to={primaryTarget!} className="btn-primary" style={{ textDecoration: 'none' }}>
              {user?.isSetupComplete ? 'Open Dashboard' : 'Continue Setup'}
            </Link>
            <Link to={secondaryTarget!} className="btn-secondary" style={{ textDecoration: 'none' }}>
              Browse Resources
            </Link>
          </>
        ) : (
          <>
            <button className="btn-primary" onClick={() => void handleBeginSignIn()}>
              Initialize Sentinel
            </button>
            <button className="btn-secondary" onClick={() => void handleBeginSignIn()}>
              Sign In
            </button>
          </>
        )}
      </div>

      <section className="grid grid-cols-3" style={{ marginTop: '8rem' }}>
        <div className="premium-glass card">
          <h3 style={{ fontSize: '1.125rem' }}>Smart Remediation</h3>
          <p style={{ color: 'var(--muted)', fontSize: '0.875rem' }}>Automated fixes for security drifts and cost leaks with one-click approval workflows.</p>
        </div>
        <div className="premium-glass card">
          <h3 style={{ fontSize: '1.125rem' }}>Identity Hardening</h3>
          <p style={{ color: 'var(--muted)', fontSize: '0.875rem' }}>Decoupled, immutable identity management ensuring zero-trust access across your cloud.</p>
        </div>
        <div className="premium-glass card">
          <h3 style={{ fontSize: '1.125rem' }}>Infinite Pulse</h3>
          <p style={{ color: 'var(--muted)', fontSize: '0.875rem' }}>Real-time telemetry and periodic reporting delivered directly to your dashboard.</p>
        </div>
      </section>
    </main>
  )
}
