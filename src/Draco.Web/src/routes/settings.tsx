import { createRoute, useNavigate } from '@tanstack/react-router'
import { Route as rootRoute } from './__root'
import { useEffect, useMemo, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { User, Bell, Key, Lock, Plus, Loader2, AlertCircle, CheckCircle2, RefreshCcw, Unplug, ShieldCheck, Info, Mail, Trash2 } from 'lucide-react'
import { dracoApi, type AzureSubscriptionOption, type CloudConnection } from '../lib/api'
import { copyToClipboard, getAwsBootstrapErrorMessage } from '../lib/awsOnboarding'
import azureLogo from '../assets/azure-logo.svg'
import awsLogo from '../assets/aws-logo.svg'
import { Drawer, DrawerClose, DrawerContent, DrawerDescription, DrawerFooter, DrawerHeader, DrawerTitle } from '../components/ui/drawer'
import { Spinner } from '../components/ui/spinner'

import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs'

type SettingsSearch = {
  tab?: string
}

export const SettingsRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/settings',
  validateSearch: (search: Record<string, unknown>): SettingsSearch => {
    return {
      tab: search.tab as string | undefined,
    }
  },
  component: Settings,
})

const PROVIDERS = [
  { id: 'Azure', name: 'Azure', description: 'Sign in with Microsoft and pick a subscription.', logo: azureLogo },
  { id: 'AWS', name: 'AWS', description: 'Add another AWS account or environment.', logo: awsLogo },
]

function Settings() {
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const search = SettingsRoute.useSearch()
  const [activeTab, setActiveTab] = useState(search.tab || 'profile')

  useEffect(() => {
    if (search.tab) {
      setActiveTab(search.tab)
    }
  }, [search.tab])

  const [provider, setProvider] = useState<string>('Azure')
  const [subscriptionId, setSubscriptionId] = useState('')
  const [displayName, setDisplayName] = useState('')
  const [awsConnectionMode, setAwsConnectionMode] = useState<'assume-role' | 'access-keys'>('assume-role')
  const [awsRoleArn, setAwsRoleArn] = useState('')
  const [awsAccessKeyId, setAwsAccessKeyId] = useState('')
  const [awsSecretAccessKey, setAwsSecretAccessKey] = useState('')
  const [awsSessionToken, setAwsSessionToken] = useState('')
  const [copiedAwsValue, setCopiedAwsValue] = useState<string | null>(null)
  const [isAwsAccountDrawerOpen, setIsAwsAccountDrawerOpen] = useState(false)
  const [notificationEmails, setNotificationEmails] = useState<string[]>([])
  const [newEmail, setNewEmail] = useState('')
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

  const inferredAwsConnectionLabel = useMemo(() => {
    if (!subscriptionId.trim()) {
      return undefined
    }

    if (awsConnectionMode === 'assume-role') {
      const roleName = awsRoleArn.trim().split('/').pop()
      return roleName ? `AWS ${subscriptionId.trim()} • ${roleName}` : `AWS ${subscriptionId.trim()}`
    }

    return `AWS ${subscriptionId.trim()}`
  }, [subscriptionId, awsConnectionMode, awsRoleArn])

  const isAwsAccountIdValid = useMemo(
    () => /^\d{12}$/.test(subscriptionId.trim()),
    [subscriptionId],
  )

  const awsBootstrapQuery = useQuery({
    queryKey: ['aws-bootstrap-settings', subscriptionId.trim()],
    queryFn: () => dracoApi.cloudConnections.getAwsBootstrap(subscriptionId.trim()),
    enabled: provider === 'AWS' && awsConnectionMode === 'assume-role' && isAwsAccountIdValid,
    staleTime: Infinity,
    retry: false,
  })

  const awsBootstrapErrorMessage = useMemo(
    () => getAwsBootstrapErrorMessage(awsBootstrapQuery.error as Error | null),
    [awsBootstrapQuery.error],
  )

  const isConnectionReady = useMemo(() => {
    if (provider === 'Azure') {
      return Boolean(selectedAzureSubscriptionId && azureTokenBundle)
    }
    if (awsConnectionMode === 'assume-role') {
      return isAwsAccountIdValid && Boolean(awsBootstrapQuery.data) && awsRoleArn.trim().length > 0
    }
    return isAwsAccountIdValid && awsAccessKeyId.trim().length > 0 && awsSecretAccessKey.trim().length > 0
  }, [
    azureTokenBundle,
    provider,
    selectedAzureSubscriptionId,
    awsConnectionMode,
    isAwsAccountIdValid,
    awsBootstrapQuery.data,
    awsRoleArn,
    awsAccessKeyId,
    awsSecretAccessKey,
  ])

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
        throw new Error('AWS account ID is required.')
      }

      const awsAccessToken = awsConnectionMode === 'access-keys'
        ? JSON.stringify({
            kind: 'AwsStaticCredentials',
            accessKeyId: awsAccessKeyId.trim(),
            secretAccessKey: awsSecretAccessKey.trim(),
            sessionToken: awsSessionToken.trim() || undefined,
          })
        : undefined

      if (awsConnectionMode === 'assume-role' && !awsBootstrapQuery.data) {
        throw new Error('Open the AWS setup guide, create the read-only IAM role, and then paste the role ARN to connect this account.')
      }

      return dracoApi.cloudConnections.upsert({
        provider,
        subscriptionId: subscriptionId.trim(),
        displayName: inferredAwsConnectionLabel,
        authType: awsConnectionMode === 'assume-role' ? 'AwsAssumeRole' : 'AwsStaticCredentials',
        externalAccountId: subscriptionId.trim(),
        awsRoleArn: awsConnectionMode === 'assume-role' ? awsRoleArn.trim() : undefined,
        accessToken: awsAccessToken,
      })
    },
    onSuccess: async () => {
      if (provider === 'AWS') {
        setIsAwsAccountDrawerOpen(false)
      }
      await refreshViews()
      if (provider !== 'Azure') {
        setSubscriptionId('')
        setDisplayName('')
        setAwsRoleArn('')
        setAwsAccessKeyId('')
        setAwsSecretAccessKey('')
        setAwsSessionToken('')
      }
    },
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

  useEffect(() => {
    if (user?.email && notificationEmails.length === 0) {
      setNotificationEmails([user.email])
    }
  }, [user?.email])

  useEffect(() => {
    setAwsRoleArn('')
  }, [subscriptionId, awsConnectionMode])

  const handleCopyAwsValue = async (key: string, value: string) => {
    try {
      await copyToClipboard(value)
      setCopiedAwsValue(key)
      window.setTimeout(() => {
        setCopiedAwsValue((current) => current === key ? null : current)
      }, 1500)
    } catch {
      setCopiedAwsValue(null)
    }
  }

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
          <Spinner className="text-primary" size={32} />
          <p className="text-muted-foreground font-medium">Synchronizing Draco Account...</p>
        </div>
      </div>
    )
  }

  return (
    <div className="animate-fade-in" style={{ maxWidth: '1000px', margin: '0 auto', padding: '1rem' }}>
      <Tabs defaultValue="profile" value={activeTab} onValueChange={setActiveTab} className="w-full">
        <div style={{ display: 'flex', flexDirection: 'column', gap: '2.5rem' }}>
          {/* Centered Top Navigation */}
          <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', textAlign: 'center' }}>
            <h2 style={{ fontSize: '2.5rem', marginBottom: '0.75rem', fontWeight: 900, letterSpacing: '-0.04em' }}>Settings</h2>
            <p style={{ color: 'var(--muted-foreground)', fontSize: '1rem', maxWidth: '600px', marginBottom: '2.5rem' }}>
              Manage your Draco account infrastructure and active cloud connection nodes.
            </p>

            <TabsList style={{ 
              display: 'flex', 
              flexDirection: 'row', 
              padding: 0, 
              height: 'auto',
              background: 'transparent',
              border: 'none',
              borderBottom: '1px solid var(--border)',
              gap: '1.5rem',
              width: '100%',
              justifyContent: 'center'
            }}>
              <TabsTrigger 
                value="profile" 
                className="flex items-center gap-2" 
                style={{ 
                  background: 'transparent',
                  border: 'none',
                  borderBottom: activeTab === 'profile' ? '2px solid var(--primary)' : '2px solid transparent',
                  padding: '1rem 0.5rem', 
                  borderRadius: 0, 
                  fontWeight: activeTab === 'profile' ? 700 : 500,
                  fontSize: '0.875rem',
                  color: activeTab === 'profile' ? 'var(--foreground)' : 'var(--muted-foreground)',
                  marginBottom: '-1px', // Align with the borderBottom of the TabsList
                  transition: 'all 0.15s ease'
                }}
              >
                <User size={16} /> Profile
              </TabsTrigger>
              <TabsTrigger 
                value="connections" 
                className="flex items-center gap-2" 
                style={{ 
                  background: 'transparent',
                  border: 'none',
                  borderBottom: activeTab === 'connections' ? '2px solid var(--primary)' : '2px solid transparent',
                  padding: '1rem 0.5rem', 
                  borderRadius: 0, 
                  fontWeight: activeTab === 'connections' ? 700 : 500,
                  fontSize: '0.875rem',
                  color: activeTab === 'connections' ? 'var(--foreground)' : 'var(--muted-foreground)',
                  marginBottom: '-1px',
                  transition: 'all 0.15s ease'
                }}
              >
                <RefreshCcw size={16} /> Connections
              </TabsTrigger>
              <TabsTrigger 
                value="security" 
                className="flex items-center gap-2" 
                style={{ 
                  background: 'transparent',
                  border: 'none',
                  borderBottom: activeTab === 'security' ? '2px solid var(--primary)' : '2px solid transparent',
                  padding: '1rem 0.5rem', 
                  borderRadius: 0, 
                  fontWeight: activeTab === 'security' ? 700 : 500,
                  fontSize: '0.875rem',
                  color: activeTab === 'security' ? 'var(--foreground)' : 'var(--muted-foreground)',
                  marginBottom: '-1px',
                  transition: 'all 0.15s ease'
                }}
              >
                <Lock size={16} /> Security
              </TabsTrigger>
              <TabsTrigger 
                value="notifications" 
                className="flex items-center gap-2" 
                style={{ 
                  background: 'transparent',
                  border: 'none',
                  borderBottom: activeTab === 'notifications' ? '2px solid var(--primary)' : '2px solid transparent',
                  padding: '1rem 0.5rem', 
                  borderRadius: 0, 
                  fontWeight: activeTab === 'notifications' ? 700 : 500,
                  fontSize: '0.875rem',
                  color: activeTab === 'notifications' ? 'var(--foreground)' : 'var(--muted-foreground)',
                  marginBottom: '-1px',
                  transition: 'all 0.15s ease'
                }}
              >
                <Bell size={16} /> Notifications
              </TabsTrigger>
            </TabsList>
          </div>

          {/* Centered Content area */}
          <div style={{ flex: 1, paddingTop: '1rem', width: '100%' }}>

            <TabsContent value="profile" className="animate-fade-in" style={{ marginTop: 0 }}>
              <div style={{ display: 'flex', flexDirection: 'column', gap: '2rem' }}>
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

                <div className="card" style={{ padding: '2.5rem' }}>
                  <div style={{ display: 'flex', alignItems: 'center', gap: '0.75rem', marginBottom: '1.5rem' }}>
                    <Mail className="text-primary" size={20} />
                    <h3 style={{ fontSize: '1.125rem', fontWeight: 800 }}>Notification Recipients</h3>
                  </div>
                  <p style={{ color: 'var(--muted-foreground)', fontSize: '0.875rem', marginBottom: '2rem' }}>
                    Manage the target infrastructure fleet recipients for real-time drift alerts and executive summaries.
                  </p>

                  <div style={{ display: 'flex', flexDirection: 'column', gap: '0.75rem', marginBottom: '2rem' }}>
                    {notificationEmails.map((email, idx) => (
                      <div key={idx} className="operational-surface" style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', padding: '0.75rem 1.25rem' }}>
                        <div style={{ display: 'flex', alignItems: 'center', gap: '0.75rem' }}>
                          <div style={{ width: '8px', height: '8px', borderRadius: '50%', background: idx === 0 ? '#00ff00' : 'var(--muted)', opacity: 0.6 }} />
                          <span style={{ fontSize: '0.9rem', fontWeight: 600 }}>{email}</span>
                          {idx === 0 && <span className="micro-label" style={{ opacity: 0.5, fontSize: '0.6rem' }}>PRIMARY</span>}
                        </div>
                        {idx > 0 && (
                          <button 
                            onClick={() => setNotificationEmails(prev => prev.filter((_, i) => i !== idx))}
                            style={{ color: 'var(--primary)', opacity: 0.6, cursor: 'pointer', transition: 'opacity 0.2s' }}
                            onMouseOver={(e) => e.currentTarget.style.opacity = '1'}
                            onMouseOut={(e) => e.currentTarget.style.opacity = '0.6'}
                          >
                            <Trash2 size={16} />
                          </button>
                        )}
                      </div>
                    ))}
                  </div>

                  <div style={{ display: 'flex', gap: '0.75rem' }}>
                    <input 
                      type="email" 
                      placeholder="Add recipient address..."
                      className="operational-surface"
                      style={{ flex: 1, padding: '0.75rem 1rem' }}
                      value={newEmail}
                      onChange={(e) => setNewEmail(e.target.value)}
                      onKeyDown={(e) => {
                        if (e.key === 'Enter' && newEmail.includes('@')) {
                          setNotificationEmails(prev => [...prev, newEmail])
                          setNewEmail('')
                        }
                      }}
                    />
                    <button 
                      className="btn-primary" 
                      onClick={() => {
                        if (newEmail.includes('@')) {
                          setNotificationEmails(prev => [...prev, newEmail])
                          setNewEmail('')
                        }
                      }}
                      disabled={!newEmail.includes('@')}
                      style={{ padding: '0 1.5rem' }}
                    >
                      <Plus size={16} /> Add
                    </button>
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
                          isDisconnecting={disconnectMutation.isPending && disconnectMutation.variables === c.id}
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
                              {isAzureExchangePending ? <Spinner size={16} /> : 'Login with Microsoft'}
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
                            <label className="micro-label">AWS Account ID</label>
                            <div style={{ display: 'flex', gap: '0.75rem', alignItems: 'stretch' }}>
                              <div style={{ position: 'relative', flex: 1 }}>
                                <input
                                  value={subscriptionId}
                                  onChange={(e) => setSubscriptionId(e.target.value)}
                                  className="operational-surface"
                                  style={{ width: '100%', padding: '0.75rem 2.5rem 0.75rem 1rem' }}
                                  placeholder="12-digit AWS account ID"
                                />
                                <div style={{ position: 'absolute', right: '0.75rem', top: '50%', transform: 'translateY(-50%)', display: 'flex' }}>
                                  {subscriptionId ? (
                                    isAwsAccountIdValid ? <CheckCircle2 size={16} color="#00ff00" /> : <AlertCircle size={16} color="var(--primary)" />
                                  ) : null}
                                </div>
                              </div>
                              <button
                                type="button"
                                className="btn-secondary"
                                onClick={() => setIsAwsAccountDrawerOpen(true)}
                                style={{ padding: '0.75rem' }}
                                aria-label="Open AWS account ID instructions"
                              >
                                <Info size={16} />
                              </button>
                            </div>
                          </div>
                          <div>
                            <label className="micro-label">AWS Connection Method</label>
                            <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '0.75rem' }}>
                              <button
                                type="button"
                                className={awsConnectionMode === 'assume-role' ? 'btn-primary' : 'btn-secondary'}
                                onClick={() => setAwsConnectionMode('assume-role')}
                                style={{ justifyContent: 'center' }}
                              >
                                Assume Role
                              </button>
                              <button
                                type="button"
                                className={awsConnectionMode === 'access-keys' ? 'btn-primary' : 'btn-secondary'}
                                onClick={() => setAwsConnectionMode('access-keys')}
                                style={{ justifyContent: 'center' }}
                              >
                                Access Keys
                              </button>
                            </div>
                          </div>
                          {awsConnectionMode === 'assume-role' ? (
                            <>
                              <div style={{ background: 'rgba(255,255,255,0.02)', padding: '1rem', borderRadius: 'var(--radius-md)', border: '1px solid var(--border)', display: 'flex', gap: '1rem' }}>
                                <ShieldCheck size={20} style={{ flexShrink: 0, marginTop: '2px', color: 'var(--muted)' }} />
                                <div style={{ margin: 0, display: 'flex', flexDirection: 'column', gap: '0.5rem' }}>
                                  <p style={{ fontSize: '0.8125rem', color: 'var(--muted)', margin: 0 }}>
                                    Recommended for ongoing use. Draco stores only the AWS account metadata and the role ARN you paste back after the role is created.
                                  </p>
                                  {awsBootstrapQuery.isFetching && (
                                    <div style={{ fontSize: '0.8125rem', color: 'var(--muted)', display: 'flex', alignItems: 'center', gap: '0.5rem' }}>
                                      <Spinner size={14} />
                                      Preparing your guided IAM role setup...
                                    </div>
                                  )}
                                </div>
                              </div>

                              {awsBootstrapQuery.data && (
                                <>
                                  <div style={{ background: 'rgba(255,255,255,0.02)', padding: '1rem', borderRadius: 'var(--radius-md)', border: '1px solid var(--border)', display: 'flex', flexDirection: 'column', gap: '0.75rem' }}>
                                    <div style={{ fontSize: '0.75rem', fontWeight: 800, textTransform: 'uppercase', letterSpacing: '0.08em', color: 'var(--muted)' }}>Guided AWS Role Setup</div>
                                    <div style={{ fontSize: '0.875rem', color: 'var(--foreground)' }}>1. Open the setup guide and create a read-only IAM role in AWS.</div>
                                    <div style={{ fontSize: '0.875rem', color: 'var(--foreground)' }}>2. Use Draco&apos;s trust policy and read-only permissions policy.</div>
                                    <div style={{ fontSize: '0.875rem', color: 'var(--foreground)' }}>3. Paste the new role ARN here and link the account.</div>
                                  </div>

                                  <div className="grid grid-cols-2">
                                    <div>
                                      <label className="micro-label">Draco Trusted Principal</label>
                                      <div style={{ display: 'flex', gap: '0.5rem' }}>
                                        <input
                                          value={awsBootstrapQuery.data.trustedPrincipalArn}
                                          readOnly
                                          className="operational-surface"
                                          style={{ width: '100%', padding: '0.75rem' }}
                                        />
                                        <button type="button" className="btn-secondary" onClick={() => void handleCopyAwsValue('settings-trusted-principal', awsBootstrapQuery.data.trustedPrincipalArn)}>
                                          {copiedAwsValue === 'settings-trusted-principal' ? 'Copied' : 'Copy'}
                                        </button>
                                      </div>
                                    </div>
                                    <div>
                                      <label className="micro-label">External ID</label>
                                      <div style={{ display: 'flex', gap: '0.5rem' }}>
                                        <input
                                          value={awsBootstrapQuery.data.externalId}
                                          readOnly
                                          className="operational-surface"
                                          style={{ width: '100%', padding: '0.75rem' }}
                                        />
                                        <button type="button" className="btn-secondary" onClick={() => void handleCopyAwsValue('settings-external-id', awsBootstrapQuery.data.externalId)}>
                                          {copiedAwsValue === 'settings-external-id' ? 'Copied' : 'Copy'}
                                        </button>
                                      </div>
                                    </div>
                                  </div>

                                  <div>
                                    <label className="micro-label">Trust Policy</label>
                                    <textarea
                                      value={awsBootstrapQuery.data.trustPolicyJson}
                                      readOnly
                                      rows={10}
                                      className="operational-surface"
                                      style={{ width: '100%', padding: '0.75rem', fontFamily: 'var(--font-mono)', resize: 'vertical' }}
                                    />
                                  </div>

                                  <div>
                                    <label className="micro-label">Read-Only Permissions Policy</label>
                                    <textarea
                                      value={awsBootstrapQuery.data.permissionsPolicyJson}
                                      readOnly
                                      rows={12}
                                      className="operational-surface"
                                      style={{ width: '100%', padding: '0.75rem', fontFamily: 'var(--font-mono)', resize: 'vertical' }}
                                    />
                                  </div>

                                  <details style={{ border: '1px solid var(--border)', borderRadius: 'var(--radius-md)', padding: '1rem' }}>
                                    <summary style={{ cursor: 'pointer', fontWeight: 700 }}>Advanced: Terraform Template</summary>
                                    <textarea
                                      value={awsBootstrapQuery.data.terraformTemplate}
                                      readOnly
                                      rows={12}
                                      className="operational-surface"
                                      style={{ width: '100%', padding: '0.75rem', fontFamily: 'var(--font-mono)', resize: 'vertical', marginTop: '1rem' }}
                                    />
                                  </details>

                                  <div>
                                    <label className="micro-label">Provisioned Role ARN</label>
                                    <input
                                      value={awsRoleArn}
                                      onChange={(e) => setAwsRoleArn(e.target.value)}
                                      className="operational-surface"
                                      style={{ width: '100%', padding: '0.75rem' }}
                                      placeholder={awsBootstrapQuery.data.suggestedRoleArn}
                                    />
                                    <p style={{ fontSize: '0.75rem', color: 'var(--muted)', marginTop: '0.5rem' }}>
                                      Draco persists the role ARN because it is needed for future syncs. The ARN itself is not a secret.
                                    </p>
                                  </div>
                                </>
                              )}
                            </>
                          ) : (
                            <>
                              <div style={{ background: 'rgba(255,255,255,0.02)', padding: '1rem', borderRadius: 'var(--radius-md)', border: '1px solid var(--border)', display: 'flex', gap: '1rem' }}>
                                <Info size={20} style={{ flexShrink: 0, marginTop: '2px', color: 'var(--muted)' }} />
                                <p style={{ fontSize: '0.8125rem', color: 'var(--muted)', margin: 0 }}>
                                  Advanced fallback. Draco must retain these credentials so it can keep syncing this AWS account.
                                </p>
                              </div>
                              <div>
                                <label className="micro-label">AWS Access Key ID</label>
                                <input value={awsAccessKeyId} onChange={(e) => setAwsAccessKeyId(e.target.value)} className="operational-surface" style={{ width: '100%', padding: '0.75rem' }} />
                              </div>
                              <div>
                                <label className="micro-label">AWS Secret Access Key</label>
                                <input value={awsSecretAccessKey} onChange={(e) => setAwsSecretAccessKey(e.target.value)} type="password" className="operational-surface" style={{ width: '100%', padding: '0.75rem' }} />
                              </div>
                              <div>
                                <label className="micro-label">AWS Session Token (Optional)</label>
                                <input value={awsSessionToken} onChange={(e) => setAwsSessionToken(e.target.value)} type="password" className="operational-surface" style={{ width: '100%', padding: '0.75rem' }} />
                              </div>
                            </>
                          )}
                        </>
                      )}
                      {azureAuthError && <div style={{ color: 'var(--primary)', fontSize: '0.75rem' }}>{azureAuthError}</div>}
                      {provider === 'AWS' && awsConnectionMode === 'assume-role' && awsBootstrapQuery.isError && (
                        <div style={{ color: 'var(--primary)', fontSize: '0.75rem', display: 'flex', flexDirection: 'column', alignItems: 'flex-start', gap: '0.75rem' }}>
                          <div>{awsBootstrapErrorMessage}</div>
                          <button className="btn-secondary" type="button" onClick={() => void navigate({ to: '/aws-onboarding' })}>
                            Open AWS Setup Guide
                          </button>
                        </div>
                      )}
                      <button className="btn-primary" onClick={() => addConnectionMutation.mutate()} disabled={!isConnectionReady || addConnectionMutation.isPending} style={{ marginTop: '0.5rem', width: '100%' }}>
                        {addConnectionMutation.isPending ? <Spinner size={16} /> : <><Plus size={16} /> Link Account</>}
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
      <Drawer
        open={isAwsAccountDrawerOpen}
        onOpenChange={setIsAwsAccountDrawerOpen}
        shouldScaleBackground={false}
      >
        <DrawerContent>
          <DrawerHeader style={{ border: 'none', paddingBottom: '0.5rem' }}>
            <DrawerTitle>Connect AWS Account</DrawerTitle>
            <DrawerDescription>
              Draco can walk you through a read-only IAM role setup. Paste your AWS account ID, create the role in AWS Console, then paste the role ARN back into Draco.
            </DrawerDescription>
          </DrawerHeader>
          <div style={{ padding: '1.25rem', display: 'flex', flexDirection: 'column', gap: '1rem' }}>
            <div style={{ background: 'var(--secondary)', padding: '1.25rem', borderRadius: 'var(--radius-lg)' }}>
              <p style={{ fontSize: '0.875rem', color: 'var(--foreground)', margin: 0, lineHeight: 1.6 }}>
                In <a href="https://console.aws.amazon.com/" target="_blank" rel="noopener noreferrer" style={{ color: 'var(--primary)', textDecoration: 'underline', fontWeight: 600 }}>AWS Console</a>, 
                click your account name in the top-right corner. The 12-digit Account ID appears in that menu and on the 
                <a href="https://console.aws.amazon.com/billing/home" target="_blank" rel="noopener noreferrer" style={{ color: 'var(--primary)', textDecoration: 'underline', fontWeight: 600, marginLeft: '0.35rem' }}>Billing and Cost Management</a> home page. 
                You can also open the <a href="https://console.aws.amazon.com/iamv2/home#/home" target="_blank" rel="noopener noreferrer" style={{ color: 'var(--primary)', textDecoration: 'underline', fontWeight: 600 }}>IAM Console</a> to check the account details page.
              </p>
            </div>

            <div>
              <label className="micro-label" style={{ display: 'block', marginBottom: '0.5rem' }}>Enter AWS Account ID</label>
              <input
                value={subscriptionId}
                onChange={(e) => setSubscriptionId(e.target.value)}
                className="operational-surface"
                style={{ width: '100%', padding: '0.75rem 1rem' }}
                placeholder="12-digit AWS account ID"
              />
            </div>

            {subscriptionId && !isAwsAccountIdValid && (
              <div style={{ color: 'var(--primary)', fontSize: '0.875rem', display: 'flex', alignItems: 'center', gap: '0.5rem' }}>
                <AlertCircle size={16} />
                Enter the full 12-digit AWS account ID.
              </div>
            )}

            {isAwsAccountIdValid && awsBootstrapQuery.isFetching && (
              <div style={{ color: 'var(--muted)', fontSize: '0.875rem', display: 'flex', alignItems: 'center', gap: '0.5rem' }}>
                <Spinner size={16} />
                Preparing your guided IAM role setup for account {subscriptionId}...
              </div>
            )}

            {isAwsAccountIdValid && awsBootstrapQuery.isError && (
              <div style={{ color: 'var(--primary)', fontSize: '0.875rem', display: 'flex', flexDirection: 'column', alignItems: 'flex-start', gap: '0.75rem' }}>
                <div style={{ display: 'flex', alignItems: 'center', gap: '0.5rem' }}>
                  <AlertCircle size={16} />
                  {awsBootstrapErrorMessage}
                </div>
                <button className="btn-secondary" type="button" onClick={() => void navigate({ to: '/aws-onboarding' })}>
                  Open AWS Setup Guide
                </button>
              </div>
            )}

            {isAwsAccountIdValid && awsBootstrapQuery.data && !addConnectionMutation.isPending && !addConnectionMutation.isError && (
              <div style={{ color: '#00c27a', fontSize: '0.875rem', display: 'flex', alignItems: 'center', gap: '0.5rem' }}>
                <CheckCircle2 size={16} />
                Guided setup is ready for AWS account {subscriptionId}.
              </div>
            )}

            {awsBootstrapQuery.data && (
              <>
                <div style={{ background: 'rgba(255,255,255,0.02)', padding: '1rem', borderRadius: 'var(--radius-md)', border: '1px solid var(--border)', display: 'flex', flexDirection: 'column', gap: '0.75rem' }}>
                  <div style={{ fontSize: '0.75rem', fontWeight: 800, textTransform: 'uppercase', letterSpacing: '0.08em', color: 'var(--muted)' }}>Step 1</div>
                  <div style={{ fontSize: '0.9rem', fontWeight: 700 }}>Create a read-only IAM role in AWS</div>
                  <p style={{ fontSize: '0.8125rem', color: 'var(--muted)', margin: 0 }}>
                    Use the trust policy and permissions policy below, or use the Terraform template under Advanced if your team prefers infrastructure-as-code.
                  </p>
                </div>

                <div className="grid grid-cols-2">
                  <div>
                    <label className="micro-label" style={{ display: 'block', marginBottom: '0.5rem' }}>Trusted Principal</label>
                    <div style={{ display: 'flex', gap: '0.5rem' }}>
                      <input value={awsBootstrapQuery.data.trustedPrincipalArn} readOnly className="operational-surface" style={{ width: '100%', padding: '0.75rem 1rem' }} />
                      <button type="button" className="btn-secondary" onClick={() => void handleCopyAwsValue('settings-drawer-trusted-principal', awsBootstrapQuery.data.trustedPrincipalArn)}>
                        {copiedAwsValue === 'settings-drawer-trusted-principal' ? 'Copied' : 'Copy'}
                      </button>
                    </div>
                  </div>
                  <div>
                    <label className="micro-label" style={{ display: 'block', marginBottom: '0.5rem' }}>External ID</label>
                    <div style={{ display: 'flex', gap: '0.5rem' }}>
                      <input value={awsBootstrapQuery.data.externalId} readOnly className="operational-surface" style={{ width: '100%', padding: '0.75rem 1rem' }} />
                      <button type="button" className="btn-secondary" onClick={() => void handleCopyAwsValue('settings-drawer-external-id', awsBootstrapQuery.data.externalId)}>
                        {copiedAwsValue === 'settings-drawer-external-id' ? 'Copied' : 'Copy'}
                      </button>
                    </div>
                  </div>
                </div>

                <div>
                  <label className="micro-label" style={{ display: 'block', marginBottom: '0.5rem' }}>Trust Policy</label>
                  <textarea value={awsBootstrapQuery.data.trustPolicyJson} readOnly rows={10} className="operational-surface" style={{ width: '100%', padding: '0.75rem 1rem', fontFamily: 'var(--font-mono)', resize: 'vertical' }} />
                </div>

                <div>
                  <label className="micro-label" style={{ display: 'block', marginBottom: '0.5rem' }}>Read-Only Permissions Policy</label>
                  <textarea value={awsBootstrapQuery.data.permissionsPolicyJson} readOnly rows={12} className="operational-surface" style={{ width: '100%', padding: '0.75rem 1rem', fontFamily: 'var(--font-mono)', resize: 'vertical' }} />
                </div>

                <details style={{ border: '1px solid var(--border)', borderRadius: 'var(--radius-md)', padding: '1rem' }}>
                  <summary style={{ cursor: 'pointer', fontWeight: 700 }}>Advanced: Terraform Template</summary>
                  <textarea value={awsBootstrapQuery.data.terraformTemplate} readOnly rows={16} className="operational-surface" style={{ width: '100%', padding: '0.75rem 1rem', fontFamily: 'var(--font-mono)', resize: 'vertical', marginTop: '1rem' }} />
                </details>

                <div style={{ background: 'rgba(255,255,255,0.02)', padding: '1rem', borderRadius: 'var(--radius-md)', border: '1px solid var(--border)', display: 'flex', flexDirection: 'column', gap: '0.75rem' }}>
                  <div style={{ fontSize: '0.75rem', fontWeight: 800, textTransform: 'uppercase', letterSpacing: '0.08em', color: 'var(--muted)' }}>Step 2</div>
                  <div style={{ fontSize: '0.9rem', fontWeight: 700 }}>Paste the created role ARN back into Draco</div>
                  <p style={{ fontSize: '0.8125rem', color: 'var(--muted)', margin: 0 }}>
                    Draco persists the role ARN because it needs it for future syncs. The ARN itself is not a secret.
                  </p>
                </div>
              </>
            )}            {provider === 'AWS' && addConnectionMutation.isPending && (
              <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', gap: '1.25rem', padding: '3rem 0', animation: 'fade-in 0.3s forwards' }}>
                <Spinner size={32} className="text-primary" />
                <div style={{ textAlign: 'center' }}>
                  <div style={{ fontWeight: 800, fontSize: '1.1rem', marginBottom: '0.35rem' }}>Bootstrap in Progress</div>
                  <div style={{ color: 'var(--muted-foreground)', fontSize: '0.875rem' }}>Deploying initial identity context for the {subscriptionId.trim()} fleet.</div>
                </div>
              </div>
            )}

            {provider === 'AWS' && addConnectionMutation.isError && (
              <div style={{ color: 'var(--primary)', fontSize: '0.875rem', display: 'flex', alignItems: 'center', gap: '0.5rem' }}>
                <AlertCircle size={16} />
                {(addConnectionMutation.error as Error).message}
              </div>
            )}
          </div>
          <DrawerFooter style={{ border: 'none', paddingTop: '0.5rem' }}>
            <DrawerClose asChild>
              <button className="btn-secondary">Close</button>
            </DrawerClose>
          </DrawerFooter>
        </DrawerContent>
      </Drawer>
    </div>
  )
}

function ConnectionRow({
  connection,
  isDisconnecting,
  onDisconnect,
}: {
  connection: CloudConnection
  isDisconnecting: boolean
  onDisconnect: () => void
}) {
  const connectionModeLabel =
    connection.provider === 'AWS'
      ? connection.authType === 'AwsAssumeRole'
        ? 'Assume Role'
        : connection.authType === 'AwsStaticCredentials'
          ? 'Access Keys'
          : null
      : null

  return (
    <div className="operational-surface" style={{ padding: '1rem', display: 'flex', justifyContent: 'space-between', gap: '1rem', alignItems: 'center' }}>
      <div>
        <div style={{ fontWeight: 600 }}>{connection.displayName || connection.provider}</div>
        <div style={{ fontSize: '0.8125rem', color: 'var(--muted-foreground)' }}>{connection.subscriptionId}</div>
        {connectionModeLabel && (
          <div style={{ fontSize: '0.75rem', color: 'var(--muted)', marginTop: '0.25rem' }}>
            {connectionModeLabel}
          </div>
        )}
        <div style={{ fontSize: '0.75rem', color: 'var(--muted)', display: 'flex', alignItems: 'center', gap: '0.35rem', marginTop: '0.25rem' }}>
          <CheckCircle2 size={14} />
          {connection.syncStatus} • {connection.syncMessage || 'Waiting for sync'}
        </div>
      </div>

      <div style={{ display: 'flex', gap: '0.5rem', flexWrap: 'wrap', justifyContent: 'flex-end' }}>
        <button className="btn-secondary" onClick={onDisconnect} disabled={isDisconnecting} title="Disconnect">
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
