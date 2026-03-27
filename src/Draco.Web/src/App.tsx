function App() {
  return (
    <div className="layout-container">
      <header style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '4rem' }}>
        <div style={{ display: 'flex', alignItems: 'center', gap: '0.75rem' }}>
          <div style={{ width: '32px', height: '32px', background: 'white', borderRadius: '4px' }}></div>
          <span style={{ fontWeight: 800, fontSize: '1.25rem', letterSpacing: '-0.05em' }}>DRACO</span>
        </div>
        <nav style={{ display: 'flex', gap: '2rem', fontSize: '0.875rem', fontWeight: 500, color: 'var(--muted)' }}>
          <a href="#" style={{ color: 'var(--foreground)' }}>Dashboard</a>
          <a href="#">Resources</a>
          <a href="#">Governance</a>
          <a href="#">Settings</a>
        </nav>
      </header>
      
      <main className="animate-fade-in">
        <h1 className="monochrome-gradient" style={{ fontSize: '4rem', maxWidth: '800px', lineHeight: 1.1, marginBottom: '2rem' }}>
          Autonomous Cloud Governance that Scales with Confidence.
        </h1>
        <p style={{ fontSize: '1.25rem', color: 'var(--muted)', maxWidth: '600px', marginBottom: '3rem' }}>
          Deterministic remediation, multi-cloud discovery, and AI-powered insights for the modern infrastructure layer.
        </p>
        
        <div style={{ display: 'flex', gap: '1rem' }}>
          <button className="btn-primary">Initialize Sentinel</button>
          <button className="btn-secondary">View Documentation</button>
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
    </div>
  )
}

export default App
