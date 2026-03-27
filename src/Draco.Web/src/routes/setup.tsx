import { createRoute, useNavigate } from '@tanstack/react-router'
import { Route as rootRoute } from './__root'
import { useEffect, useState, useMemo } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Cloud, Globe, CheckCircle2, AlertCircle, Loader2, Info, ArrowRight, ShieldCheck } from 'lucide-react'
import { dracoApi, type AzureSubscriptionOption } from '../lib/api'
import azureLogo from '../assets/azure-logo.svg'
import awsLogo from '../assets/aws-logo.svg'
import gcpLogo from '../assets/gcp-logo.svg'

export const Route = createRoute({
  getParentRoute: () => rootRoute,
  path: '/setup',
  component: Setup,
})

const PROVIDERS = [
  { id: 'Azure', name: 'Azure', description: 'Enterprise-grade cloud discovery.', logo: azureLogo },
  { id: 'AWS', name: 'AWS', description: 'Scale-driven cost governance.', logo: awsLogo },
  { id: 'GCP', name: 'GCP', description: 'Advanced AI-first infrastructure.', logo: gcpLogo },
]

function Setup() {
  const [step, setStep] = useState(1)
  const [provider, setProvider] = useState<string | null>(null)
  const [subscriptionId, setSubscriptionId] = useState('')
  const [displayName, setDisplayName] = useState('')
  const [accessToken, setAccessToken] = useState('')
  const [azureSubscriptions, setAzureSubscriptions] = useState<AzureSubscriptionOption[]>([])
  const [selectedAzureSubscriptionId, setSelectedAzureSubscriptionId] = useState('')
  const [azureTokenBundle, setAzureTokenBundle] = useState<{
    accessToken: string
    refreshToken?: string
    tokenExpiresAt: string
  } | null>(null)
  const [azureAuthError, setAzureAuthError] = useState<string | null>(null)
  const [isAzureExchangePending, setIsAzureExchangePending] = useState(false)
  const [syncPhase, setSyncPhase] = useState(0) // 0: Idle, 1: Inventory, 2: Cost Analysis, 3: AI Insights, 4: Done
  
  const navigate = useNavigate()
  const queryClient = useQueryClient()

  const { data: user } = useQuery({
    queryKey: ['me'],
    queryFn: dracoApi.auth.getMe,
  })

  const isSubscriptionIdValid = useMemo(() => {
    if (provider === 'Azure') {
      return selectedAzureSubscriptionId.trim().length > 0
    }

    if (!subscriptionId.trim()) return false
    return subscriptionId.trim().length > 5
  }, [provider, selectedAzureSubscriptionId, subscriptionId])

  const selectedAzureSubscription = useMemo(
    () => azureSubscriptions.find(subscription => subscription.subscriptionId === selectedAzureSubscriptionId) ?? null,
    [azureSubscriptions, selectedAzureSubscriptionId],
  )

  const connectMutation = useMutation({
    mutationFn: async () => {
      if (!provider) {
        throw new Error('Provider is required.')
      }

      let connection

      if (provider === 'Azure') {
        if (!selectedAzureSubscription || !azureTokenBundle) {
          throw new Error('Sign in with Microsoft and choose a subscription first.')
        }

        connection = await dracoApi.cloudConnections.upsert({
          provider,
          subscriptionId: selectedAzureSubscription.subscriptionId,
          displayName: displayName.trim() || selectedAzureSubscription.displayName,
          accessToken: azureTokenBundle.accessToken,
          refreshToken: azureTokenBundle.refreshToken,
          tokenExpiresAt: azureTokenBundle.tokenExpiresAt,
        })
      } else {
        if (!subscriptionId.trim()) {
          throw new Error('Provider and Subscription ID are required.')
        }

        connection = await dracoApi.cloudConnections.upsert({
          provider,
          subscriptionId: subscriptionId.trim(),
          displayName: displayName.trim() || undefined,
          accessToken: accessToken.trim() || undefined,
        })
      }

      // Simulate a multi-phase sync for better UX
      setStep(3)
      setSyncPhase(1)
      await dracoApi.cloudConnections.sync([connection.id])
      
      setSyncPhase(2)
      await new Promise(r => setTimeout(r, 800))
      
      setSyncPhase(3)
      await new Promise(r => setTimeout(r, 1000))
      
      setSyncPhase(4)
      return connection
    },
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['me'] })
      await queryClient.invalidateQueries({ queryKey: ['dashboard-summary'] })
      await queryClient.invalidateQueries({ queryKey: ['resources'] })
    },
  })

  useEffect(() => {
    const params = new URLSearchParams(window.location.search)
    const code = params.get('code')
    const state = params.get('state')

    if (!code) {
      return
    }

    const storedState = dracoApi.auth.getAzureOauthState()
    dracoApi.auth.clearAzureOauthState()
    setProvider('Azure')
    setStep(2)

    if (!storedState || storedState !== state) {
      setAzureAuthError('Microsoft sign-in validation failed. Please try again.')
      window.history.replaceState({}, document.title, window.location.pathname)
      return
    }

    let isCancelled = false

    const completeAzureSignIn = async () => {
      try {
        setAzureAuthError(null)
        setIsAzureExchangePending(true)

        const result = await dracoApi.cloudConnections.exchangeAzureCode({
          code,
          redirectUri: `${window.location.origin}/setup`,
        })

        if (isCancelled) {
          return
        }

        setAzureTokenBundle({
          accessToken: result.accessToken,
          refreshToken: result.refreshToken,
          tokenExpiresAt: result.tokenExpiresAt,
        })
        setAzureSubscriptions(result.subscriptions)
        setSelectedAzureSubscriptionId(current =>
          current || result.subscriptions[0]?.subscriptionId || '',
        )
      } catch (error) {
        if (!isCancelled) {
          setAzureAuthError((error as Error).message)
        }
      } finally {
        if (!isCancelled) {
          setIsAzureExchangePending(false)
        }
        window.history.replaceState({}, document.title, window.location.pathname)
      }
    }

    void completeAzureSignIn()

    return () => {
      isCancelled = true
    }
  }, [])

  useEffect(() => {
    if (user?.isSetupComplete && step === 1) {
      void navigate({ to: '/dashboard' })
    }
  }, [navigate, step, user?.isSetupComplete])

  const handleAzureSignIn = async () => {
    try {
      setAzureAuthError(null)
      await dracoApi.auth.beginAzureSignIn()
    } catch (error) {
      setAzureAuthError((error as Error).message)
    }
  }
  
  return (
    <div className="layout-container" style={{ maxWidth: '800px', paddingTop: '4rem' }}>
      <div className="animate-fade-in" style={{ textAlign: 'center', marginBottom: '4rem' }}>
        <h2 className="monochrome-gradient" style={{ fontSize: '3rem', marginBottom: '1rem', letterSpacing: '-0.04em' }}>
          Initialize Draco
        </h2>
        <p style={{ color: 'var(--muted)', fontSize: '1.125rem' }}>
          Connect your infrastructure to start autonomous cloud governance.
        </p>
      </div>

      <div className="premium-glass" style={{ padding: '2rem', border: '1px solid var(--border)' }}>
        {/* Progress Stepper */}
        <div style={{ display: 'flex', justifyContent: 'space-between', marginBottom: '3rem', position: 'relative' }}>
          <div style={{ position: 'absolute', top: '15px', left: '0', right: '0', height: '2px', background: 'var(--border)', zIndex: 0 }} />
          {[1, 2, 3].map(s => (
            <div key={s} style={{ 
              position: 'relative', 
              zIndex: 1, 
              background: step >= s ? 'var(--primary)' : 'var(--card)',
              color: step >= s ? 'white' : 'var(--muted)',
              width: '32px', height: '32px', 
              borderRadius: '50%', 
              display: 'flex', alignItems: 'center', justifyContent: 'center',
              fontWeight: 700,
              border: '2px solid',
              borderColor: step >= s ? 'var(--primary)' : 'var(--border)',
              transition: 'all 0.3s cubic-bezier(0.4, 0, 0.2, 1)'
            }}>
              {step > s ? <CheckCircle2 size={18} /> : s}
            </div>
          ))}
        </div>

        {step === 1 && (
          <div className="animate-fade-in">
            <h3 style={{ marginBottom: '2rem', display: 'flex', alignItems: 'center', gap: '0.75rem' }}>
              <Cloud size={20} className="text-primary" />
              Select Cloud Provider
            </h3>
            <div className="grid grid-cols-2">
              {PROVIDERS.map(p => (
                <div 
                  key={p.id} 
                  className={`card ${provider === p.id ? 'active' : ''}`}
                  style={{ 
                    cursor: 'pointer', 
                    padding: '1.5rem',
                    transition: 'all 0.2s',
                    border: provider === p.id ? '2px solid var(--primary)' : '1px solid var(--border)',
                    background: provider === p.id ? 'rgba(255, 0, 0, 0.03)' : 'transparent',
                    transform: provider === p.id ? 'scale(1.02)' : 'none'
                  }}
                  onClick={() => setProvider(p.id)}
                >
                  <div style={{ height: '32px', marginBottom: '1rem', display: 'flex', alignItems: 'center' }}>
                    <img
                      src={p.logo}
                      alt={`${p.name} logo`}
                      style={{
                        height: p.id === 'AWS' ? '26px' : '28px',
                        width: 'auto',
                        objectFit: 'contain',
                        filter: provider === p.id ? 'none' : 'grayscale(0.05)',
                      }}
                    />
                  </div>
                  <div style={{ fontWeight: 700, marginBottom: '0.25rem' }}>{p.name}</div>
                  <div style={{ fontSize: '0.75rem', color: 'var(--muted)' }}>{p.description}</div>
                </div>
              ))}
            </div>
            
            <div style={{ marginTop: '3rem', display: 'flex', justifyContent: 'flex-end' }}>
              <button 
                className="btn-primary" 
                disabled={!provider} 
                onClick={() => setStep(2)}
              >
                Continue Setup <ArrowRight size={16} />
              </button>
            </div>
          </div>
        )}

        {step === 2 && (
          <div className="animate-fade-in">
            <h3 style={{ marginBottom: '2rem', display: 'flex', alignItems: 'center', gap: '0.75rem' }}>
              <ShieldCheck size={20} className="text-primary" />
              Authorize {provider}
            </h3>
            
            <div style={{ display: 'flex', flexDirection: 'column', gap: '1.5rem' }}>
              {provider === 'Azure' ? (
                <>
                  <div style={{ background: 'rgba(255,255,255,0.02)', padding: '1rem', borderRadius: 'var(--radius-md)', border: '1px solid var(--border)', display: 'flex', gap: '1rem' }}>
                    <Info size={20} style={{ flexShrink: 0, marginTop: '2px', color: 'var(--muted)' }} />
                    <p style={{ fontSize: '0.8125rem', color: 'var(--muted)', margin: 0 }}>
                      Sign in with your Microsoft account, let Draco discover the Azure subscriptions you can access, then choose the one you want to connect.
                    </p>
                  </div>

                  {!azureTokenBundle && (
                    <button
                      className="btn-primary"
                      onClick={() => void handleAzureSignIn()}
                      disabled={isAzureExchangePending}
                      style={{ alignSelf: 'flex-start' }}
                    >
                      {isAzureExchangePending ? (
                        <>
                          <Loader2 className="animate-spin" size={16} /> Completing Microsoft Sign-In...
                        </>
                      ) : (
                        'Sign In with Microsoft'
                      )}
                    </button>
                  )}

                  {azureTokenBundle && (
                    <>
                      <div>
                        <div style={{ display: 'flex', alignItems: 'center', gap: '0.5rem', marginBottom: '0.5rem' }}>
                          <label className="micro-label" style={{ marginBottom: 0 }}>Azure Subscription</label>
                          {selectedAzureSubscriptionId && (
                            <div 
                              title={selectedAzureSubscriptionId}
                              style={{ cursor: 'help', display: 'flex', alignItems: 'center', color: 'var(--muted)', marginTop: '-1px' }}
                            >
                              <Info size={14} />
                            </div>
                          )}
                        </div>
                        <select
                          value={selectedAzureSubscriptionId}
                          onChange={(e) => setSelectedAzureSubscriptionId(e.target.value)}
                          className="operational-surface"
                          style={{ width: '100%', padding: '0.75rem 1rem' }}
                        >
                          <option value="">Select a subscription</option>
                          {azureSubscriptions.map(subscription => (
                            <option key={subscription.subscriptionId} value={subscription.subscriptionId}>
                              {subscription.displayName}
                            </option>
                          ))}
                        </select>
                        {azureSubscriptions.length === 0 && (
                          <p style={{ fontSize: '0.75rem', color: 'var(--primary)', marginTop: '0.5rem' }}>
                            No Azure subscriptions were found for this Microsoft account.
                          </p>
                        )}
                      </div>

                      <div>
                        <label className="micro-label" style={{ display: 'block', marginBottom: '0.5rem' }}>Connection Label</label>
                        <input
                          value={displayName}
                          onChange={(e) => setDisplayName(e.target.value)}
                          type="text"
                          placeholder={selectedAzureSubscription?.displayName || 'e.g., Production Fleet'}
                          className="operational-surface"
                          style={{ width: '100%', padding: '0.75rem 1rem' }}
                        />
                      </div>
                    </>
                  )}
                </>
              ) : (
                <>
                  <div>
                    <label className="micro-label" style={{ display: 'block', marginBottom: '0.5rem' }}>Subscription ID</label>
                    <div style={{ position: 'relative' }}>
                      <input 
                        value={subscriptionId} 
                        onChange={(e) => setSubscriptionId(e.target.value)} 
                        type="text" 
                        placeholder="Enter your subscription or project identifier" 
                        className="operational-surface"
                        style={{ width: '100%', padding: '0.75rem 2.5rem 0.75rem 1rem' }} 
                      />
                      {subscriptionId && (
                        <div style={{ position: 'absolute', right: '12px', top: '12px' }}>
                          {isSubscriptionIdValid ? <CheckCircle2 size={16} color="#00ff00" /> : <AlertCircle size={16} color="var(--primary)" />}
                        </div>
                      )}
                    </div>
                  </div>

                  <div>
                    <label className="micro-label" style={{ display: 'block', marginBottom: '0.5rem' }}>Connection Label</label>
                    <input 
                      value={displayName} 
                      onChange={(e) => setDisplayName(e.target.value)} 
                      type="text" 
                      placeholder="e.g., Production Fleet" 
                      className="operational-surface"
                      style={{ width: '100%', padding: '0.75rem 1rem' }} 
                    />
                  </div>

                  <div>
                    <label className="micro-label" style={{ display: 'block', marginBottom: '0.5rem' }}>Read-Only Access Token (Optional)</label>
                    <input 
                      value={accessToken} 
                      onChange={(e) => setAccessToken(e.target.value)} 
                      type="password" 
                      placeholder="Paste manual provider token" 
                      className="operational-surface"
                      style={{ width: '100%', padding: '0.75rem 1rem' }} 
                    />
                  </div>
                </>
              )}
              
              <div style={{ background: 'rgba(255,255,255,0.02)', padding: '1rem', borderRadius: 'var(--radius-md)', border: '1px solid var(--border)', display: 'flex', gap: '1rem' }}>
                <Info size={20} style={{ flexShrink: 0, marginTop: '2px', color: 'var(--muted)' }} />
                <p style={{ fontSize: '0.8125rem', color: 'var(--muted)', margin: 0 }}>
                  Draco operates best with read-only access to your cloud metadata. We only ingest resource telemetry and cost signals to provide autonomous insights.
                </p>
              </div>

              {azureAuthError && (
                <div style={{ color: 'var(--primary)', fontSize: '0.875rem', display: 'flex', alignItems: 'center', gap: '0.5rem' }}>
                  <AlertCircle size={16} />
                  {azureAuthError}
                </div>
              )}

              {connectMutation.isError && (
                <div style={{ color: 'var(--primary)', fontSize: '0.875rem', display: 'flex', alignItems: 'center', gap: '0.5rem' }}>
                  <AlertCircle size={16} />
                  {(connectMutation.error as Error).message}
                </div>
              )}

              <div style={{ display: 'flex', justifyContent: 'space-between', marginTop: '1rem' }}>
                <button className="btn-secondary" onClick={() => setStep(1)}>Back</button>
                <button 
                  className="btn-primary" 
                  onClick={() => connectMutation.mutate()} 
                  disabled={connectMutation.isPending || !isSubscriptionIdValid || (provider === 'Azure' && isAzureExchangePending)}
                >
                  {connectMutation.isPending ? (
                    <>
                      <Loader2 className="animate-spin" size={16} /> Connecting...
                    </>
                  ) : (
                    'Connect Infrastructure'
                  )}
                </button>
              </div>
            </div>
          </div>
        )}

        {step === 3 && (
          <div className="animate-fade-in" style={{ textAlign: 'center', padding: '2rem 0' }}>
            {syncPhase < 4 ? (
              <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', gap: '2rem' }}>
                <div style={{ position: 'relative', width: '100px', height: '100px' }}>
                  <Loader2 size={100} className="animate-spin text-primary" style={{ opacity: 0.2 }} />
                  <div style={{ position: 'absolute', inset: 0, display: 'flex', alignItems: 'center', justifyContent: 'center' }}>
                    <Globe size={40} className="animate-pulse" />
                  </div>
                </div>
                
                <div style={{ width: '100%', maxWidth: '300px' }}>
                  <h4 style={{ marginBottom: '1.5rem', fontSize: '1.125rem' }}>Synchronizing Sentinel</h4>
                  
                  <div style={{ textAlign: 'left', display: 'flex', flexDirection: 'column', gap: '0.75rem' }}>
                    <div style={{ display: 'flex', alignItems: 'center', gap: '0.75rem', opacity: syncPhase >= 1 ? 1 : 0.3 }}>
                      {syncPhase > 1 ? <CheckCircle2 size={16} color="#00ff00" /> : <Loader2 className="animate-spin" size={16} />}
                      <span>Resource Inventory Discovery</span>
                    </div>
                    <div style={{ display: 'flex', alignItems: 'center', gap: '0.75rem', opacity: syncPhase >= 2 ? 1 : 0.3 }}>
                      {syncPhase > 2 ? <CheckCircle2 size={16} color="#00ff00" /> : syncPhase === 2 ? <Loader2 className="animate-spin" size={16} /> : <div style={{ width: 16 }} />}
                      <span>Cost Metric Aggregation</span>
                    </div>
                    <div style={{ display: 'flex', alignItems: 'center', gap: '0.75rem', opacity: syncPhase >= 3 ? 1 : 0.3 }}>
                      {syncPhase > 3 ? <CheckCircle2 size={16} color="#00ff00" /> : syncPhase === 3 ? <Loader2 className="animate-spin" size={16} /> : <div style={{ width: 16 }} />}
                      <span>LLM Context Analysis</span>
                    </div>
                  </div>
                </div>
              </div>
            ) : (
              <div className="animate-fade-in">
                <div style={{ 
                  width: '80px', height: '80px', borderRadius: '50%', background: 'var(--primary)', 
                  display: 'flex', alignItems: 'center', justifyContent: 'center', margin: '0 auto 2rem',
                  boxShadow: '0 0 30px rgba(255, 0, 0, 0.4)'
                }}>
                  <CheckCircle2 size={40} color="white" />
                </div>
                <h3 style={{ fontSize: '2rem', marginBottom: '1rem', letterSpacing: '-0.02em' }}>Sentinel Initialized</h3>
                <p style={{ color: 'var(--muted)', marginBottom: '3rem', maxWidth: '400px', margin: '0 auto 3rem' }}>
                  Your {provider} connection is verified and your first insights are ready for review.
                </p>
                <button className="btn-primary" onClick={() => navigate({ to: '/dashboard' })}>
                  Enter Command Center <ArrowRight size={16} />
                </button>
              </div>
            )}
          </div>
        )}
      </div>
    </div>
  )
}
