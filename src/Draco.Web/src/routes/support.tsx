import { createRoute } from '@tanstack/react-router'
import { useQuery } from '@tanstack/react-query'
import { LifeBuoy, MessageSquareWarning, Wrench } from 'lucide-react'
import { Route as rootRoute } from './__root'
import { dracoApi } from '../lib/api'
import { useIsMobile } from '../hooks/useIsMobile'

export const SupportRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/support',
  component: Support,
})

function Support() {
  const isMobile = useIsMobile()
  const { data, isLoading } = useQuery({
    queryKey: ['support-errors'],
    queryFn: dracoApi.support.getErrors,
  })

  const definitions = data?.definitions ?? []
  const recentLogs = data?.recentLogs ?? []

  return (
    <div className="animate-fade-in settings-page" style={{ maxWidth: '1100px', margin: '0 auto', padding: isMobile ? '0.25rem' : '1rem' }}>
      <div style={{ marginBottom: isMobile ? '1.25rem' : '2rem' }}>
        <div className="micro-label" style={{ marginBottom: '0.5rem' }}>Support</div>
        <h2 style={{ fontSize: isMobile ? '1.5rem' : '2.25rem', marginBottom: '0.5rem', letterSpacing: '-0.03em' }}>Error Logbook</h2>
        <p style={{ color: 'var(--muted)', maxWidth: '720px' }}>
          Support codes explain what failed, what the user sees, and what to check next. Recent logs show the latest messaging and integration issues recorded by Draco.
        </p>
      </div>

      <div className="grid grid-cols-2" style={{ gap: isMobile ? '1rem' : '1.5rem' }}>
        <section className="premium-glass" style={{ padding: '1.25rem' }}>
          <div style={{ display: 'flex', alignItems: 'center', gap: '0.65rem', marginBottom: '1rem' }}>
            <LifeBuoy size={16} />
            <span className="micro-label">Error Codes</span>
          </div>
          <div className="operational-surface" style={{ maxHeight: isMobile ? '420px' : '620px', overflowY: 'auto' }}>
            {definitions.map((item) => (
              <div key={item.code} className="operational-row" style={{ padding: '1rem', display: 'grid', gap: '0.55rem' }}>
                <div style={{ display: 'flex', justifyContent: 'space-between', gap: '1rem', alignItems: 'center' }}>
                  <div style={{ fontWeight: 700 }}>{item.title}</div>
                  <span className="badge" style={{ fontSize: '0.65rem' }}>{item.code}</span>
                </div>
                <div style={{ fontSize: '0.8rem', color: 'var(--muted)' }}>{item.summary}</div>
                <div style={{ fontSize: '0.76rem' }}>
                  <strong>User message:</strong> {item.userMessage}
                </div>
                <div style={{ display: 'grid', gap: '0.35rem' }}>
                  {item.steps.map((step, index) => (
                    <div key={`${item.code}-${index}`} style={{ fontSize: '0.76rem', color: 'var(--muted)' }}>
                      {index + 1}. {step}
                    </div>
                  ))}
                </div>
              </div>
            ))}
          </div>
        </section>

        <section className="premium-glass" style={{ padding: '1.25rem' }}>
          <div style={{ display: 'flex', alignItems: 'center', gap: '0.65rem', marginBottom: '1rem' }}>
            <MessageSquareWarning size={16} />
            <span className="micro-label">Recent Logs</span>
          </div>
          <div className="operational-surface" style={{ maxHeight: isMobile ? '420px' : '620px', overflowY: 'auto' }}>
            {isLoading ? (
              <div style={{ padding: '1rem', color: 'var(--muted)' }}>Loading support logs...</div>
            ) : recentLogs.length === 0 ? (
              <div style={{ padding: '1rem', color: 'var(--muted)' }}>No support errors have been logged recently.</div>
            ) : recentLogs.map((log) => (
              <div key={log.id} className="operational-row" style={{ padding: '1rem', display: 'grid', gap: '0.45rem' }}>
                <div style={{ display: 'flex', justifyContent: 'space-between', gap: '1rem', alignItems: 'center' }}>
                  <div style={{ fontWeight: 700 }}>{log.title}</div>
                  <span className="badge" style={{
                    fontSize: '0.65rem',
                    background: log.severity === 'High' ? 'rgba(255,0,0,0.1)' : 'rgba(255,255,255,0.05)',
                    color: log.severity === 'High' ? 'var(--primary)' : 'var(--muted-foreground)'
                  }}>
                    {log.severity}
                  </span>
                </div>
                <div style={{ fontSize: '0.8rem', color: 'var(--muted)' }}>{log.summary}</div>
                <div style={{ fontSize: '0.74rem', color: 'var(--muted)' }}>
                  Logged {new Date(log.receivedAt).toLocaleString()}
                  {log.correlationId ? ` • Ref ${log.correlationId}` : ''}
                </div>
                {log.processingError ? (
                  <div style={{ fontSize: '0.74rem' }}>
                    <strong>Processing error:</strong> {log.processingError}
                  </div>
                ) : null}
              </div>
            ))}
          </div>
        </section>
      </div>

      <section className="card" style={{ marginTop: '1.5rem', padding: '1.25rem' }}>
        <div style={{ display: 'flex', alignItems: 'center', gap: '0.65rem', marginBottom: '0.75rem' }}>
          <Wrench size={16} />
          <span className="micro-label">Quick Triage</span>
        </div>
        <div style={{ display: 'grid', gap: '0.45rem', fontSize: '0.84rem', color: 'var(--muted)' }}>
          <div>1. If inbound WhatsApp messages are not reaching Draco, verify Twilio points to your stable Railway webhook URL.</div>
          <div>2. If Draco acknowledges but never sends the final answer, check API logs for `DRC-WA-1000` or `DRC-WA-1003`.</div>
          <div>3. If outbound delivery fails, look for `DRC-WA-1004` and confirm the destination number is opted into your WhatsApp sender.</div>
          <div>4. Use the notification drawer and this page together: notifications tell you something failed, and the logbook tells you what to fix.</div>
        </div>
      </section>
    </div>
  )
}
