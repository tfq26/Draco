import { createRoute, useNavigate } from '@tanstack/react-router'
import { ArrowLeft, ExternalLink, ShieldCheck } from 'lucide-react'
import { Route as rootRoute } from './__root'
import awsOnboardingGuide from '../content/aws-onboarding-guide.md?raw'
import { MarkdownPage } from '../lib/markdown'

export const AwsOnboardingRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/aws-onboarding',
  component: AwsOnboardingGuidePage,
})

function AwsOnboardingGuidePage() {
  const navigate = useNavigate()

  return (
    <div style={{ maxWidth: '920px', margin: '0 auto', padding: '2rem 1rem 4rem' }}>
      <div style={{ display: 'flex', justifyContent: 'space-between', gap: '1rem', alignItems: 'center', marginBottom: '1.5rem', flexWrap: 'wrap' }}>
        <button className="btn-secondary" onClick={() => window.history.length > 1 ? window.history.back() : void navigate({ to: '/setup' })}>
          <ArrowLeft size={16} /> Back
        </button>
        <div style={{ display: 'flex', gap: '0.75rem', flexWrap: 'wrap' }}>
          <button className="btn-secondary" onClick={() => void navigate({ to: '/setup' })}>Open Setup</button>
          <button className="btn-secondary" onClick={() => void navigate({ to: '/settings', search: { tab: 'connections' } })}>Open Settings</button>
        </div>
      </div>

      <div className="card" style={{ padding: '2rem', display: 'flex', flexDirection: 'column', gap: '1.5rem' }}>
        <div style={{ display: 'flex', alignItems: 'flex-start', gap: '1rem' }}>
          <div style={{ width: '48px', height: '48px', borderRadius: '14px', display: 'flex', alignItems: 'center', justifyContent: 'center', background: 'rgba(255, 0, 0, 0.08)', border: '1px solid var(--border)' }}>
            <ShieldCheck size={22} className="text-primary" />
          </div>
          <div>
            <div style={{ fontSize: '0.75rem', fontWeight: 800, textTransform: 'uppercase', letterSpacing: '0.08em', color: 'var(--primary)', marginBottom: '0.35rem' }}>
              AWS Setup Guide
            </div>
            <div style={{ color: 'var(--muted-foreground)', lineHeight: 1.6 }}>
              Use this guide when Draco asks you to finish AWS onboarding or when your workspace admin needs to configure the trusted AWS principal for guided setup.
            </div>
          </div>
        </div>

        <MarkdownPage markdown={awsOnboardingGuide} />

        <div style={{ display: 'flex', justifyContent: 'space-between', gap: '1rem', alignItems: 'center', flexWrap: 'wrap', paddingTop: '0.5rem', borderTop: '1px solid var(--border)' }}>
          <div style={{ color: 'var(--muted-foreground)', fontSize: '0.875rem' }}>
            If the trusted-principal error appears again, send users here first.
          </div>
          <a
            href="https://console.aws.amazon.com/iamv2/home#/home"
            target="_blank"
            rel="noreferrer"
            className="btn-secondary"
            style={{ textDecoration: 'none' }}
          >
            AWS IAM <ExternalLink size={14} />
          </a>
        </div>
      </div>
    </div>
  )
}
