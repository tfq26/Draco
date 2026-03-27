import { createRoute } from '@tanstack/react-router'
import { Route as rootRoute } from './__root'
import { useEffect, useMemo, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { User, Bell, Key, Lock, Plus, Loader2, AlertCircle, CheckCircle2, RefreshCcw, Unplug, ShieldCheck } from 'lucide-react'
import { dracoApi, type AzureSubscriptionOption, type CloudConnection } from '../lib/api'
import azureLogo from '../assets/azure-logo.svg'
import awsLogo from '../assets/aws-logo.svg'
import gcpLogo from '../assets/gcp-logo.svg'

import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs'

export const SettingsRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/settings',
  component: Settings,
})

const PROVIDERS = [
  { id: 'Azure', name: 'Azure', description: 'Sign in with Microsoft and pick a subscription.', logo: azureLogo },
  { id: 'AWS', name: 'AWS', description: 'Add another AWS account or environment.', logo: awsLogo },
  { id: 'GCP', name: 'GCP', description: 'Add another Google Cloud project connection.', logo: gcpLogo },
]

function Settings() {
  const queryClient = useQueryClient()
  const [activeTab, setActiveTab] = useState('profile')
  const [provider, setProvider] = useState<string>('Azure')
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

  const { data: user, isLoading } = useQuery({
    queryKey: ['me'],
    queryFn: dracoApi.auth.getMe,
  })

  const selectedAzureSubscription = useMemo(
    () => azureSubscriptions.find(subscription => subscription.subscriptionId === selectedAzureSubscriptionId) ?? null,
    [azureSubscriptions, selectedAzureSubscriptionId],
  )

  const isConnectionReady = useMemo(() => {
    if (provider === 'Azure') {
      return Boolean(selectedAzureSubscriptionId && azureTokenBundle)
    }
    return subscriptionId.trim().length > 0
  }, [azureTokenBundle, provider, selectedAzureSubscriptionId, subscriptionId])

  const refreshViews = async () => {
    await queryClient.invalidateQueries({ queryKey: ['me'] })
    await queryClient.invalidateQueries({ queryKey: ['dashboard-summary'] })
    await queryClient.invalidateQueries({ queryKey: ['resources'] })
  }

  const addConnectionMutation = useMutation({
    mutationFn: async () => {
      if (provider === 'Azure') {
        if (!selectedAzureSubscription || !azureTokenBundle) {
          throw new Error('Sign in with Microsoft and choose a subscription first.')
        }

        return dracoApi.cloudConnections.upsert({
          provider: 'Azure',
          subscriptionId: selectedAzureSubscription.subscriptionId,
          displayName: displayName.trim() || selectedAzureSubscription.displayName,
          accessToken: azureTokenBundle.accessToken,
          refreshToken: azureTokenBundle.refreshToken,
          tokenExpiresAt: azureTokenBundle.tokenExpiresAt,
        })
      }

      if (!subscriptionId.trim()) {
        throw new Error('Subscription or project ID is required.')
      }

      return dracoApi.cloudConnections.upsert({
        provider,
        subscriptionId: subscriptionId.trim(),
        displayName: displayName.trim() || undefined,
        accessToken: accessToken.trim() || undefined,
      })
    },
    onSuccess: async () => {
      await refreshViews()
      if (provider !== 'Azure') {
        setSubscriptionId('')
        setDisplayName('')
        setAccessToken('')
      }
    },
  })

  const syncMutation = useMutation({
    mutationFn: (connectionId: number) => dracoApi.cloudConnections.sync([connectionId]),
    onSuccess: refreshViews,
  })

  const disconnectMutation = useMutation({
    mutationFn: (connectionId: number) => dracoApi.cloudConnections.remove(connectionId),
    onSuccess: refreshViews,
  })

  useEffect(() => {
    const params = new URLSearchParams(window.location.search)
    const code = params.get('code')
    const state = params.get('state')

    if (!code) return

    const storedState = dracoApi.auth.getAzureOauthState()
    dracoApi.auth.clearAzureOauthState()
    setProvider('Azure')
    setActiveTab('connections')

    if (!storedState || storedState !== state) {
      setAzureAuthError('Microsoft sign-in validation failed. Please try again.')
      window.history.replaceState({}, document.title, window.location.pathname)
      return
    }

    const completeAzureSignIn = async () => {
      try {
        setAzureAuthError(null)
        setIsAzureExchangePending(true)
        const result = await dracoApi.cloudConnections.exchangeAzureCode({
          code,
          redirectUri: `${window.location.origin}/settings`,
        })
        setAzureTokenBundle({
          accessToken: result.accessToken,
          refreshToken: result.refreshToken,
          tokenExpiresAt: result.tokenExpiresAt,
        })
        setAzureSubscriptions(result.subscriptions)
        setSelectedAzureSubscriptionId(result.subscriptions[0]?.subscriptionId || '')
      } catch (error) {
        setAzureAuthError((error as Error).message)
      } finally {
        setIsAzureExchangePending(false)
        window.history.replaceState({}, document.title, window.location.pathname)
      }
    }

    void completeAzureSignIn()
  }, [])

  const handleAzureSignIn = async () => {
    try {
      setAzureAuthError(null)
      const state = crypto.randomUUID()
      sessionStorage.setItem('draco:azure:oauth-state', state)
      const response = await fetch(`${import.meta.env.VITE_API_URL || 'http://localhost:5020'}/api/cloud-connections/azure/authorize-url?redirectUri=${encodeURIComponent(`${window.location.origin}/settings`)}&state=${encodeURIComponent(state)}`, {
        headers: { Authorization: `Bearer ${dracoApi.auth.getToken()}` },
      })
      if (!response.ok) throw new Error('Microsoft sign-in could not be started.')
      const result = await response.json() as { authorizeUrl: string }
      window.location.assign(result.authorizeUrl)
    } catch (error) {
      setAzureAuthError((error as Error).message)
    }
  }

  if (isLoading || !user) {
    return (
      <div className="flex items-center justify-center min-h-[400px]">
        <div className="flex flex-col items-center gap-4">
          <Loader2 className="animate-spin text-primary" size={32} />
          <p className="text-muted-foreground font-medium">Synchronizing Draco Account...</p>
        </div>
      </div>
    )
  }

  return (
    <div className="animate-fade-in" style={{ maxWidth: '1200px', margin: '0 auto', padding: '1rem' }}>
      <Tabs defaultValue="profile" value={activeTab} onValueChange={setActiveTab} className="w-full">
        <div style={{ display: 'grid', gridTemplateColumns: '280px 1fr', gap: '4rem', alignItems: 'start' }}>
          {/* Left Side Sidebar Navigation */}
          <div style={{ position: 'sticky', top: '2rem' }}>
            <div className="micro-label" style={{ marginBottom: '1.5rem', opacity: 0.5 }}>Configuration</div>
            <TabsList style={{ 
              display: 'flex', 
              flexDirection: 'column', 
              background: 'var(--card)', 
              border: '1px solid var(--border)', 
              padding: '8px', 
              height: 'auto',
              borderRadius: 'var(--radius-xl)',
              boxShadow: '0 15px 50px rgba(0,0,0,0.06)',
              gap: '0.5rem',
              width: '100%'
            }}>
              <TabsTrigger value="profile" className="w-full" style={{ justifyContent: 'flex-start', padding: '1rem 1.25rem', borderRadius: 'var(--radius-lg)', fontWeight: 700 }}>
                <User size={18} style={{ marginRight: '1rem' }} /> Profile
              </TabsTrigger>
              <TabsTrigger value="connections" className="w-full" style={{ justifyContent: 'flex-start', padding: '1rem 1.25rem', borderRadius: 'var(--radius-lg)', fontWeight: 700 }}>
                <RefreshCcw size={18} style={{ marginRight: '1rem' }} /> Connections
              </TabsTrigger>
              <TabsTrigger value="security" className="w-full" style={{ justifyContent: 'flex-start', padding: '1rem 1.25rem', borderRadius: 'var(--radius-lg)', fontWeight: 700 }}>
                <Lock size={18} style={{ marginRight: '1rem' }} /> Security
              </TabsTrigger>
              <TabsTrigger value="notifications" className="w-full" style={{ justifyContent: 'flex-start', padding: '1rem 1.25rem', borderRadius: 'var(--radius-lg)', fontWeight: 700 }}>
                <Bell size={18} style={{ marginRight: '1rem' }} /> Notifications
              </TabsTrigger>
            </TabsList>
            
            <div className="premium-glass" style={{ marginTop: '2.5rem', padding: '1.5rem', borderRadius: 'var(--radius-xl)' }}>
              <div style={{ fontSize: '0.65rem', fontWeight: 900, textTransform: 'uppercase', letterSpacing: '0.1em', color: 'var(--primary)', marginBottom: '0.5rem' }}>System Status</div>
              <div style={{ fontSize: '0.85rem', fontWeight: 700 }}>Draco Sentinel v2.4</div>
              <div style={{ display: 'flex', alignItems: 'center', gap: '0.5rem', marginTop: '0.5rem', color: '#00ff00', fontSize: '0.75rem' }}>
                <div style={{ width: '6px', height: '6px', borderRadius: '50%', background: '#00ff00' }} />
                Online & Synchronized
              </div>
            </div>
          </div>

          {/* Right Side: Content area */}
          <div style={{ flex: 1 }}>
            <div style={{ marginBottom: '3rem' }}>
              <h2 style={{ fontSize: '2.5rem', marginBottom: '0.75rem', fontWeight: 900, letterSpacing: '-0.04em' }}>Settings</h2>
              <p style={{ color: 'var(--muted-foreground)', fontSize: '1rem', maxWidth: '600px' }}>
                Manage your profile, cloud governance connections, and security preferences.
              </p>
            </div>

            <TabsContent value="profile" className="animate-fade-in" style={{ marginTop: 0 }}>
              <div className="card" style={{ padding: '2.5rem', boxShadow: '0 10px 40px rgba(0,0,0,0.03)' }}>
                <div style={{ display: 'flex', alignItems: 'center', gap: '0.75rem', marginBottom: '2.5rem' }}>
                  <User className="text-primary" size={24} />
                  <h3 style={{ fontSize: '1.25rem', fontWeight: 800 }}>User Profile</h3>
                </div>
                <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '2.5rem' }}>
                  <div>
                    <label className="micro-label" style={{ display: 'block', marginBottom: '0.75rem' }}>Full Name</label>
                    <div className="operational-surface" style={{ padding: '1rem 1.25rem', fontSize: '1rem' }}>{user.name}</div>
                  </div>
                  <div>
                    <label className="micro-label" style={{ display: 'block', marginBottom: '0.75rem' }}>Email Address</label>
                    <div className="operational-surface" style={{ padding: '1rem 1.25rem', fontSize: '1rem' }}>{user.email || 'Not set'}</div>
                  </div>
                </div>
              </div>
            </TabsContent>

            <TabsContent value="connections" className="animate-fade-in" style={{ marginTop: 0 }}>
              <div style={{ display: 'flex', flexDirection: 'column', gap: '2rem' }}>
                <div className="card" style={{ padding: '1.25rem 2rem' }}>
                  <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between' }}>
                    <div style={{ display: 'flex', alignItems: 'center', gap: '2rem' }}>
                      <div className="micro-label">Fleet Health</div>
                      <div style={{ display: 'flex', gap: '1.5rem' }}>
                        {PROVIDERS.map(p => {
                          const count = user.connections.filter(c => c.provider === p.id).length
                          const hasError = user.connections.some(c => c.provider === p.id && c.syncStatus === 'Failed')
                          return (
                            <div key={p.id} style={{ display: 'flex', alignItems: 'center', gap: '0.6rem', opacity: count > 0 ? 1 : 0.4 }}>
                              <img src={p.logo} alt={p.name} style={{ height: '16px' }} />
                              <span style={{ fontWeight: 800 }}>{count}</span>
                              {count > 0 && (hasError ? <AlertCircle size={14} className="text-primary" /> : <ShieldCheck size={14} style={{ color: '#00ff00' }} />)}
                            </div>
                          )
                        })}
                      </div>
                    </div>
                    <button className="btn-secondary" style={{ padding: '0.5rem 1rem', fontSize: '0.75rem' }} onClick={() => queryClient.invalidateQueries({ queryKey: ['me'] })}>
                      <RefreshCcw size={12} />
                    </button>
                  </div>
                </div>

                <div style={{ display: 'grid', gridTemplateColumns: '1.3fr 1fr', gap: '2rem' }}>
                  <div className="card" style={{ padding: '2rem' }}>
                    <h3 style={{ marginBottom: '2rem', fontSize: '1.125rem', fontWeight: 800 }}>Active Fleet Connections</h3>
                    <div style={{ display: 'flex', flexDirection: 'column', gap: '1.25rem' }}>
                      {user.connections.length === 0 && <div style={{ color: 'var(--muted)', fontSize: '0.875rem', textAlign: 'center', padding: '3rem' }}>No cloud accounts connected.</div>}
                      {user.connections.map(c => (
                        <ConnectionRow
                          key={c.id}
                          connection={c}
                          isSyncing={syncMutation.isPending && syncMutation.variables === c.id}
                          isDisconnecting={disconnectMutation.isPending && disconnectMutation.variables === c.id}
                          onSync={() => syncMutation.mutate(c.id)}
                          onDisconnect={() => disconnectMutation.mutate(c.id)}
                        />
                      ))}
                    </div>
                  </div>

                  <div className="card" style={{ padding: '2rem' }}>
                    <h3 style={{ marginBottom: '1.5rem', fontSize: '1.125rem', fontWeight: 800 }}>Add Provider</h3>
                    <div style={{ display: 'flex', gap: '0.75rem', marginBottom: '2rem' }}>
                      {PROVIDERS.map(option => (
                        <button
                          key={option.id}
                          className="btn-secondary"
                          onClick={() => setProvider(option.id)}
                          style={{
                            padding: '0.6rem', width: '100%',
                            borderColor: provider === option.id ? 'var(--primary)' : 'var(--border)',
                            background: provider === option.id ? 'rgba(255, 0, 0, 0.04)' : 'transparent',
                          }}
                        >
                          <img src={option.logo} alt={option.name} style={{ height: '20px' }} />
                        </button>
                      ))}
                    </div>

                    <div style={{ display: 'flex', flexDirection: 'column', gap: '1.25rem' }}>
                      {provider === 'Azure' ? (
                        <>
                          {!azureTokenBundle ? (
                            <button className="btn-primary" onClick={() => void handleAzureSignIn()} disabled={isAzureExchangePending} style={{ width: '100%' }}>
                              {isAzureExchangePending ? <Loader2 className="animate-spin" size={16} /> : 'Authorize Microsoft'}
                            </button>
                          ) : (
                            <>
                              <div>
                                <label className="micro-label">Subscription</label>
                                <select value={selectedAzureSubscriptionId} onChange={(e) => setSelectedAzureSubscriptionId(e.target.value)} className="operational-surface" style={{ width: '100%', padding: '0.75rem' }}>
                                  {azureSubscriptions.map(s => <option key={s.subscriptionId} value={s.subscriptionId}>{s.displayName}</option>)}
                                </select>
                              </div>
                              <div>
                                <label className="micro-label">Label</label>
                                <input value={displayName} onChange={(e) => setDisplayName(e.target.value)} className="operational-surface" style={{ width: '100%', padding: '0.75rem' }} placeholder="e.g. Sales Prod" />
                              </div>
                            </>
                          )}
                        </>
                      ) : (
                        <>
                          <div>
                            <label className="micro-label">{provider === 'GCP' ? 'Project ID' : 'Account/Sub ID'}</label>
                            <input value={subscriptionId} onChange={(e) => setSubscriptionId(e.target.value)} className="operational-surface" style={{ width: '100%', padding: '0.75rem' }} />
                          </div>
                          <div>
                            <label className="micro-label">Label</label>
                            <input value={displayName} onChange={(e) => setDisplayName(e.target.value)} className="operational-surface" style={{ width: '100%', padding: '0.75rem' }} />
                          </div>
                        </>
                      )}
                      {azureAuthError && <div style={{ color: 'var(--primary)', fontSize: '0.75rem' }}>{azureAuthError}</div>}
                      <button className="btn-primary" onClick={() => addConnectionMutation.mutate()} disabled={!isConnectionReady || addConnectionMutation.isPending} style={{ marginTop: '0.5rem', width: '100%' }}>
                        {addConnectionMutation.isPending ? <Loader2 className="animate-spin" /> : <><Plus size={16} /> Link Account</>}
                      </button>
                    </div>
                  </div>
                </div>
              </div>
            </TabsContent>

            <TabsContent value="security" className="animate-fade-in" style={{ marginTop: 0 }}>
              <div className="card" style={{ padding: '2.5rem' }}>
                <div style={{ display: 'flex', alignItems: 'center', gap: '0.75rem', marginBottom: '1.5rem' }}>
                  <Lock className="text-primary" size={24} />
                  <h3 style={{ fontSize: '1.25rem', fontWeight: 800 }}>Security & Access</h3>
                </div>
                <p style={{ color: 'var(--muted-foreground)', marginBottom: '2.5rem' }}>Manage account security and telemetry encryption keys.</p>
                <div className="operational-surface" style={{ padding: '1.5rem', borderLeft: '4px solid var(--primary)' }}>
                  <div style={{ display: 'flex', gap: '1.25rem', alignItems: 'center' }}>
                    <Key size={24} className="text-primary" />
                    <div>
                      <div style={{ fontWeight: 800, fontSize: '0.9rem' }}>Webhook Ingestion Secret</div>
                      <div style={{ fontSize: '0.8rem', color: 'var(--muted-foreground)', marginTop: '0.25rem' }}>Unique key for authenticated telemetry signals.</div>
                    </div>
                  </div>
                </div>
              </div>
            </TabsContent>

            <TabsContent value="notifications" className="animate-fade-in" style={{ marginTop: 0 }}>
              <div className="card" style={{ padding: '2.5rem' }}>
                <div style={{ display: 'flex', alignItems: 'center', gap: '0.75rem', marginBottom: '1.5rem' }}>
                  <Bell className="text-primary" size={24} />
                  <h3 style={{ fontSize: '1.25rem', fontWeight: 800 }}>Notification Center</h3>
                </div>
                <p style={{ color: 'var(--muted-foreground)', marginBottom: '2.5rem' }}>Configure real-time alerts and autonomous governance signals.</p>
                
                <div style={{ display: 'flex', flexDirection: 'column', gap: '1.5rem' }}>
                  <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', padding: '1.25rem', border: '1px solid var(--border)', borderRadius: 'var(--radius-lg)' }}>
                    <div>
                      <div style={{ fontWeight: 700 }}>Real-time Browser Alerts</div>
                      <div style={{ fontSize: '0.8rem', color: 'var(--muted-foreground)' }}>Immediate popups for high-priority fleet events.</div>
                    </div>
                    <div style={{ width: '44px', height: '24px', background: 'var(--primary)', borderRadius: '24px', position: 'relative', cursor: 'pointer' }}>
                       <div style={{ position: 'absolute', right: '4px', top: '4px', width: '16px', height: '16px', borderRadius: '50%', background: 'white' }} />
                    </div>
                  </div>
                  
                  <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', padding: '1.25rem', border: '1px solid var(--border)', borderRadius: 'var(--radius-lg)', opacity: 0.6 }}>
                    <div>
                      <div style={{ fontWeight: 700 }}>Telegram Ingestion (Beta)</div>
                      <div style={{ fontSize: '0.8rem', color: 'var(--muted-foreground)' }}>Stream governance signals directly to your mobile device.</div>
                    </div>
                    <button className="btn-secondary" style={{ fontSize: '0.75rem' }}>Connect</button>
                  </div>
                </div>
              </div>
            </TabsContent>
          </div>
        </div>
      </Tabs>
    </div>
  )
}

function ConnectionRow({
  connection,
  isSyncing,
  isDisconnecting,
  onSync,
  onDisconnect,
}: {
  connection: CloudConnection
  isSyncing: boolean
  isDisconnecting: boolean
  onSync: () => void
  onDisconnect: () => void
}) {
  return (
    <div className="operational-surface" style={{ padding: '1rem', display: 'flex', justifyContent: 'space-between', gap: '1rem', alignItems: 'center' }}>
      <div>
        <div style={{ fontWeight: 600 }}>{connection.displayName || connection.provider}</div>
        <div style={{ fontSize: '0.8125rem', color: 'var(--muted-foreground)' }}>{connection.subscriptionId}</div>
        <div style={{ fontSize: '0.75rem', color: 'var(--muted)', display: 'flex', alignItems: 'center', gap: '0.35rem', marginTop: '0.25rem' }}>
          <CheckCircle2 size={14} />
          {connection.syncStatus} • {connection.syncMessage || 'Waiting for sync'}
        </div>
      </div>

      <div style={{ display: 'flex', gap: '0.5rem', flexWrap: 'wrap', justifyContent: 'flex-end' }}>
        <button className="btn-secondary" onClick={onSync} disabled={isSyncing || isDisconnecting}>
          {isSyncing ? (
            <>
              <Loader2 className="animate-spin" size={14} /> Syncing...
            </>
          ) : (
            <>
              <RefreshCcw size={14} />
            </>
          )}
        </button>
        <button className="btn-secondary" onClick={onDisconnect} disabled={isSyncing || isDisconnecting}>
          {isDisconnecting ? (
            <>
              <Loader2 className="animate-spin" size={14} /> Disconnecting...
            </>
          ) : (
            <>
              <Unplug size={14} />
              
            </>
          )}
        </button>
      </div>
    </div>
  )
}
