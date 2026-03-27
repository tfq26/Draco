import { createRoute, useNavigate } from '@tanstack/react-router'
import { Route as rootRoute } from './__root'
import { useEffect, useState, useMemo } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Cloud, Globe, CheckCircle2, AlertCircle, Loader2, Info, ArrowRight, ShieldCheck } from 'lucide-react'
import { dracoApi, type AzureSubscriptionOption } from '../lib/api'
import { copyToClipboard, getAwsBootstrapErrorMessage } from '../lib/awsOnboarding'
import azureLogo from '../assets/azure-logo.svg'
import awsLogo from '../assets/aws-logo.svg'
import { Drawer, DrawerClose, DrawerContent, DrawerDescription, DrawerFooter, DrawerHeader, DrawerTitle } from '../components/ui/drawer'

export const Route = createRoute({
  getParentRoute: () => rootRoute,
  path: '/setup',
  component: Setup,
})

const PROVIDERS = [
  { id: 'Azure', name: 'Azure', description: 'Enterprise-grade cloud discovery.', logo: azureLogo },
  { id: 'AWS', name: 'AWS', description: 'Scale-driven cost governance.', logo: awsLogo },
]

function Setup() {
  const [step, setStep] = useState(1)
  const [provider, setProvider] = useState<string | null>(null)
  const [subscriptionId, setSubscriptionId] = useState('')
  const [displayName, setDisplayName] = useState('')
  const [awsConnectionMode, setAwsConnectionMode] = useState<'assume-role' | 'access-keys'>('assume-role')
  const [awsRoleArn, setAwsRoleArn] = useState('')
  const [awsAccessKeyId, setAwsAccessKeyId] = useState('')
  const [awsSecretAccessKey, setAwsSecretAccessKey] = useState('')
  const [awsSessionToken, setAwsSessionToken] = useState('')
  const [copiedAwsValue, setCopiedAwsValue] = useState<string | null>(null)
  const [isAwsAccountDrawerOpen, setIsAwsAccountDrawerOpen] = useState(false)
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

  const isAwsAccountIdValid = useMemo(
    () => /^\d{12}$/.test(subscriptionId.trim()),
    [subscriptionId],
  )

  const awsBootstrapQuery = useQuery({
    queryKey: ['aws-bootstrap', subscriptionId.trim()],
    queryFn: () => dracoApi.cloudConnections.getAwsBootstrap(subscriptionId.trim()),
    enabled: provider === 'AWS' && awsConnectionMode === 'assume-role' && isAwsAccountIdValid,
    staleTime: Infinity,
    retry: false,
  })

  const awsBootstrapErrorMessage = useMemo(
    () => getAwsBootstrapErrorMessage(awsBootstrapQuery.error as Error | null),
    [awsBootstrapQuery.error],
  )

  const isConnectionConfigValid = useMemo(() => {
    if (provider === 'Azure') {
      return selectedAzureSubscriptionId.trim().length > 0
    }

    if (provider === 'AWS') {
      if (awsConnectionMode === 'assume-role') {
        return isAwsAccountIdValid &&
          Boolean(awsBootstrapQuery.data) &&
          awsRoleArn.trim().length > 0
      }

      return isAwsAccountIdValid &&
        awsAccessKeyId.trim().length > 0 &&
        awsSecretAccessKey.trim().length > 0
    }

    return false
  }, [
    provider,
    selectedAzureSubscriptionId,
    awsConnectionMode,
    isAwsAccountIdValid,
    awsBootstrapQuery.data,
    awsRoleArn,
    awsAccessKeyId,
    awsSecretAccessKey,
  ])

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
        if (provider !== 'AWS') {
          throw new Error('Unsupported provider.')
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

        connection = await dracoApi.cloudConnections.upsert({
          provider,
          subscriptionId: subscriptionId.trim(),
          displayName: inferredAwsConnectionLabel,
          authType: awsConnectionMode === 'assume-role' ? 'AwsAssumeRole' : 'AwsStaticCredentials',
          externalAccountId: subscriptionId.trim(),
          awsRoleArn: awsConnectionMode === 'assume-role' ? awsRoleArn.trim() : undefined,
          accessToken: awsAccessToken,
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
      if (provider === 'AWS') {
        setIsAwsAccountDrawerOpen(false)
      }
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
                    <label className="micro-label" style={{ display: 'block', marginBottom: '0.5rem' }}>AWS Account ID</label>
                    <div style={{ display: 'flex', gap: '0.75rem', alignItems: 'stretch' }}>
                      <div className="operational-surface" style={{ flex: 1, padding: '0.75rem 1rem', display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: '0.75rem' }}>
                        <span style={{ color: subscriptionId ? 'var(--foreground)' : 'var(--muted)' }}>
                          {subscriptionId || 'Select or paste your 12-digit AWS account ID'}
                        </span>
                        {subscriptionId ? (
                          isAwsAccountIdValid ? <CheckCircle2 size={16} color="#00ff00" /> : <AlertCircle size={16} color="var(--primary)" />
                        ) : null}
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
                    <label className="micro-label" style={{ display: 'block', marginBottom: '0.75rem' }}>AWS Connection Method</label>
                    <div className="grid grid-cols-2">
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
                            Recommended for production. Draco stores only the AWS account metadata and the role ARN you paste back here. We do not keep the setup template after the role is created.
                          </p>
                          {awsBootstrapQuery.isFetching && (
                            <div style={{ fontSize: '0.8125rem', color: 'var(--muted)', display: 'flex', alignItems: 'center', gap: '0.5rem' }}>
                              <Loader2 className="animate-spin" size={14} />
                              Preparing your guided IAM role setup...
                            </div>
                          )}
                        </div>
                      </div>

                      {awsBootstrapQuery.data && (
                        <>
                          <div style={{ background: 'rgba(255,255,255,0.02)', padding: '1rem', borderRadius: 'var(--radius-md)', border: '1px solid var(--border)', display: 'flex', flexDirection: 'column', gap: '0.75rem' }}>
                            <div style={{ fontSize: '0.75rem', fontWeight: 800, textTransform: 'uppercase', letterSpacing: '0.08em', color: 'var(--muted)' }}>Guided AWS Role Setup</div>
                            <div style={{ fontSize: '0.875rem', color: 'var(--foreground)' }}>1. Open the AWS setup guide.</div>
                            <div style={{ fontSize: '0.875rem', color: 'var(--foreground)' }}>2. Create a read-only IAM role in your AWS account using the trust and permissions policies below.</div>
                            <div style={{ fontSize: '0.875rem', color: 'var(--foreground)' }}>3. Paste the created role ARN back into Draco and connect.</div>
                          </div>

                          <div className="grid grid-cols-2">
                            <div>
                              <label className="micro-label" style={{ display: 'block', marginBottom: '0.5rem' }}>Draco Trusted Principal</label>
                              <div style={{ display: 'flex', gap: '0.5rem' }}>
                                <input
                                  value={awsBootstrapQuery.data.trustedPrincipalArn}
                                  readOnly
                                  className="operational-surface"
                                  style={{ width: '100%', padding: '0.75rem 1rem', opacity: 0.8 }}
                                />
                                <button type="button" className="btn-secondary" onClick={() => void handleCopyAwsValue('trusted-principal', awsBootstrapQuery.data.trustedPrincipalArn)}>
                                  {copiedAwsValue === 'trusted-principal' ? 'Copied' : 'Copy'}
                                </button>
                              </div>
                            </div>
                            <div>
                              <label className="micro-label" style={{ display: 'block', marginBottom: '0.5rem' }}>External ID</label>
                              <div style={{ display: 'flex', gap: '0.5rem' }}>
                                <input
                                  value={awsBootstrapQuery.data.externalId}
                                  readOnly
                                  className="operational-surface"
                                  style={{ width: '100%', padding: '0.75rem 1rem', opacity: 0.8 }}
                                />
                                <button type="button" className="btn-secondary" onClick={() => void handleCopyAwsValue('external-id', awsBootstrapQuery.data.externalId)}>
                                  {copiedAwsValue === 'external-id' ? 'Copied' : 'Copy'}
                                </button>
                              </div>
                            </div>
                          </div>

                          <div>
                            <label className="micro-label" style={{ display: 'block', marginBottom: '0.5rem' }}>Trust Policy</label>
                            <textarea
                              value={awsBootstrapQuery.data.trustPolicyJson}
                              readOnly
                              rows={12}
                              className="operational-surface"
                              style={{ width: '100%', padding: '0.75rem 1rem', fontFamily: 'var(--font-mono)', resize: 'vertical' }}
                            />
                            <div style={{ display: 'flex', justifyContent: 'space-between', gap: '1rem', marginTop: '0.5rem' }}>
                              <p style={{ fontSize: '0.75rem', color: 'var(--muted)', margin: 0 }}>
                                Use this as the role trust relationship so your AWS account trusts Draco and requires the external ID above.
                              </p>
                              <button type="button" className="btn-secondary" onClick={() => void handleCopyAwsValue('trust-policy', awsBootstrapQuery.data.trustPolicyJson)}>
                                {copiedAwsValue === 'trust-policy' ? 'Copied' : 'Copy'}
                              </button>
                            </div>
                          </div>

                          <div>
                            <label className="micro-label" style={{ display: 'block', marginBottom: '0.5rem' }}>Read-Only Permissions Policy</label>
                            <textarea
                              value={awsBootstrapQuery.data.permissionsPolicyJson}
                              readOnly
                              rows={14}
                              className="operational-surface"
                              style={{ width: '100%', padding: '0.75rem 1rem', fontFamily: 'var(--font-mono)', resize: 'vertical' }}
                            />
                            <div style={{ display: 'flex', justifyContent: 'space-between', gap: '1rem', marginTop: '0.5rem' }}>
                              <p style={{ fontSize: '0.75rem', color: 'var(--muted)', margin: 0 }}>
                                Attach this policy to the role so Draco can read inventory, budgets, costs, and monitoring data.
                              </p>
                              <button type="button" className="btn-secondary" onClick={() => void handleCopyAwsValue('permissions-policy', awsBootstrapQuery.data.permissionsPolicyJson)}>
                                {copiedAwsValue === 'permissions-policy' ? 'Copied' : 'Copy'}
                              </button>
                            </div>
                          </div>

                          <details style={{ border: '1px solid var(--border)', borderRadius: 'var(--radius-md)', padding: '1rem' }}>
                            <summary style={{ cursor: 'pointer', fontWeight: 700 }}>Advanced: Terraform Template</summary>
                            <div style={{ marginTop: '1rem' }}>
                              <label className="micro-label" style={{ display: 'block', marginBottom: '0.5rem' }}>Terraform Bootstrap File</label>
                              <textarea
                                value={awsBootstrapQuery.data.terraformTemplate}
                                readOnly
                                rows={18}
                                className="operational-surface"
                                style={{ width: '100%', padding: '0.75rem 1rem', fontFamily: 'var(--font-mono)', resize: 'vertical' }}
                              />
                              <div style={{ display: 'flex', justifyContent: 'space-between', gap: '1rem', marginTop: '0.5rem' }}>
                                <p style={{ fontSize: '0.75rem', color: 'var(--muted)', margin: 0 }}>
                                  Advanced fallback for infrastructure teams that prefer Terraform. Run <code>terraform init</code> and <code>terraform apply</code>, then paste the output <code>draco_role_arn</code> below.
                                </p>
                                <button type="button" className="btn-secondary" onClick={() => void handleCopyAwsValue('terraform-template', awsBootstrapQuery.data.terraformTemplate)}>
                                  {copiedAwsValue === 'terraform-template' ? 'Copied' : 'Copy'}
                                </button>
                              </div>
                            </div>
                          </details>

                          <div>
                            <label className="micro-label" style={{ display: 'block', marginBottom: '0.5rem' }}>Provisioned Role ARN</label>
                            <input
                              value={awsRoleArn}
                              onChange={(e) => setAwsRoleArn(e.target.value)}
                              type="text"
                              autoCapitalize="off"
                              autoCorrect="off"
                              spellCheck={false}
                              placeholder={awsBootstrapQuery.data.suggestedRoleArn}
                              className="operational-surface"
                              style={{ width: '100%', padding: '0.75rem 1rem' }}
                            />
                            <p style={{ fontSize: '0.75rem', color: 'var(--muted)', marginTop: '0.5rem' }}>
                              Draco persists this role ARN because it is needed for future syncs. It does not grant access by itself.
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
                          Advanced fallback. Draco will store the credentials you paste here so it can keep syncing this AWS account.
                        </p>
                      </div>
                      <div>
                        <label className="micro-label" style={{ display: 'block', marginBottom: '0.5rem' }}>AWS Access Key ID</label>
                        <input 
                          value={awsAccessKeyId}
                          onChange={(e) => setAwsAccessKeyId(e.target.value)}
                          type="text"
                          autoCapitalize="off"
                          autoCorrect="off"
                          spellCheck={false}
                          placeholder="AKIA..." 
                          className="operational-surface"
                          style={{ width: '100%', padding: '0.75rem 1rem' }} 
                        />
                      </div>

                      <div>
                        <label className="micro-label" style={{ display: 'block', marginBottom: '0.5rem' }}>AWS Secret Access Key</label>
                        <input 
                          value={awsSecretAccessKey}
                          onChange={(e) => setAwsSecretAccessKey(e.target.value)}
                          type="password" 
                          placeholder="Paste read-only IAM secret access key" 
                          className="operational-surface"
                          style={{ width: '100%', padding: '0.75rem 1rem' }} 
                        />
                      </div>

                      <div>
                        <label className="micro-label" style={{ display: 'block', marginBottom: '0.5rem' }}>AWS Session Token (Optional)</label>
                        <input 
                          value={awsSessionToken}
                          onChange={(e) => setAwsSessionToken(e.target.value)}
                          type="password" 
                          placeholder="Paste session token for temporary credentials" 
                          className="operational-surface"
                          style={{ width: '100%', padding: '0.75rem 1rem' }} 
                        />
                      </div>
                    </>
                  )}
                </>
              )}
              
              <div style={{ background: 'rgba(255,255,255,0.02)', padding: '1rem', borderRadius: 'var(--radius-md)', border: '1px solid var(--border)', display: 'flex', gap: '1rem' }}>
                <Info size={20} style={{ flexShrink: 0, marginTop: '2px', color: 'var(--muted)' }} />
                <p style={{ fontSize: '0.8125rem', color: 'var(--muted)', margin: 0 }}>
                  {provider === 'AWS'
                    ? awsConnectionMode === 'assume-role'
                      ? 'The guided path avoids long-lived account secrets. Draco keeps the AWS account ID and role ARN so it can assume the role again during future syncs.'
                      : 'Use Access Keys only when you cannot create an IAM role. Draco will need to retain those credentials so it can keep syncing this account.'
                    : 'Draco operates best with read-only access to your cloud metadata. We only ingest resource telemetry and cost signals to provide autonomous insights.'}
                </p>
              </div>

              {azureAuthError && (
                <div style={{ color: 'var(--primary)', fontSize: '0.875rem', display: 'flex', alignItems: 'center', gap: '0.5rem' }}>
                  <AlertCircle size={16} />
                  {azureAuthError}
                </div>
              )}

              {provider === 'AWS' && awsConnectionMode === 'assume-role' && awsBootstrapQuery.isError && (
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
                  disabled={connectMutation.isPending || !isConnectionConfigValid || (provider === 'Azure' && isAzureExchangePending)}
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
      <Drawer
        open={isAwsAccountDrawerOpen}
        onOpenChange={setIsAwsAccountDrawerOpen}
        shouldScaleBackground={false}
      >
        <DrawerContent>
          <DrawerHeader style={{ borderBottom: '1px solid var(--border)' }}>
            <DrawerTitle>Connect AWS Account</DrawerTitle>
            <DrawerDescription>
              Draco can guide you through a read-only IAM role setup. Paste your AWS account ID, create the role in AWS Console, then paste the role ARN back into Draco.
            </DrawerDescription>
          </DrawerHeader>
          <div style={{ padding: '1.25rem', display: 'flex', flexDirection: 'column', gap: '1rem' }}>
            <div style={{ background: 'rgba(255,255,255,0.02)', padding: '1rem', borderRadius: 'var(--radius-md)', border: '1px solid var(--border)' }}>
              <p style={{ fontSize: '0.875rem', color: 'var(--muted)', margin: 0 }}>
                In AWS Console, click your account name in the top-right corner. The 12-digit Account ID appears in that menu and on the Billing and Cost Management home page. You can also open IAM and check the account details page.
              </p>
            </div>

            <div>
              <label className="micro-label" style={{ display: 'block', marginBottom: '0.5rem' }}>Enter AWS Account ID</label>
              <input
                value={subscriptionId}
                onChange={(e) => setSubscriptionId(e.target.value)}
                type="text"
                placeholder="12-digit AWS account ID"
                className="operational-surface"
                style={{ width: '100%', padding: '0.75rem 1rem' }}
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
                <Loader2 className="animate-spin" size={16} />
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

            {isAwsAccountIdValid && awsBootstrapQuery.data && !connectMutation.isPending && !connectMutation.isError && (
              <div style={{ color: '#00c27a', fontSize: '0.875rem', display: 'flex', alignItems: 'center', gap: '0.5rem' }}>
                <CheckCircle2 size={16} />
                Guided setup is ready for AWS account {subscriptionId}.
              </div>
            )}

            {awsBootstrapQuery.data && (
              <>
                <div style={{ background: 'rgba(255,255,255,0.02)', padding: '1rem', borderRadius: 'var(--radius-md)', border: '1px solid var(--border)', display: 'flex', flexDirection: 'column', gap: '0.75rem' }}>
                  <div style={{ fontSize: '0.75rem', fontWeight: 800, textTransform: 'uppercase', letterSpacing: '0.08em', color: 'var(--muted)' }}>Step 1</div>
                  <div style={{ fontSize: '0.9rem', fontWeight: 700 }}>Create a new IAM role in your AWS account</div>
                  <p style={{ fontSize: '0.8125rem', color: 'var(--muted)', margin: 0 }}>
                    In AWS Console, open IAM, create a role with custom trust policy, and name it something like <code>{awsBootstrapQuery.data.suggestedRoleName}</code>.
                  </p>
                </div>

                <div className="grid grid-cols-2">
                  <div>
                    <label className="micro-label" style={{ display: 'block', marginBottom: '0.5rem' }}>Trusted Principal</label>
                    <div style={{ display: 'flex', gap: '0.5rem' }}>
                      <input value={awsBootstrapQuery.data.trustedPrincipalArn} readOnly className="operational-surface" style={{ width: '100%', padding: '0.75rem 1rem', opacity: 0.8 }} />
                      <button type="button" className="btn-secondary" onClick={() => void handleCopyAwsValue('drawer-trusted-principal', awsBootstrapQuery.data.trustedPrincipalArn)}>
                        {copiedAwsValue === 'drawer-trusted-principal' ? 'Copied' : 'Copy'}
                      </button>
                    </div>
                  </div>
                  <div>
                    <label className="micro-label" style={{ display: 'block', marginBottom: '0.5rem' }}>External ID</label>
                    <div style={{ display: 'flex', gap: '0.5rem' }}>
                      <input value={awsBootstrapQuery.data.externalId} readOnly className="operational-surface" style={{ width: '100%', padding: '0.75rem 1rem', opacity: 0.8 }} />
                      <button type="button" className="btn-secondary" onClick={() => void handleCopyAwsValue('drawer-external-id', awsBootstrapQuery.data.externalId)}>
                        {copiedAwsValue === 'drawer-external-id' ? 'Copied' : 'Copy'}
                      </button>
                    </div>
                  </div>
                </div>

                <div>
                  <label className="micro-label" style={{ display: 'block', marginBottom: '0.5rem' }}>Trust Policy</label>
                  <textarea value={awsBootstrapQuery.data.trustPolicyJson} readOnly rows={12} className="operational-surface" style={{ width: '100%', padding: '0.75rem 1rem', fontFamily: 'var(--font-mono)', resize: 'vertical' }} />
                </div>

                <div>
                  <label className="micro-label" style={{ display: 'block', marginBottom: '0.5rem' }}>Read-Only Permissions Policy</label>
                  <textarea value={awsBootstrapQuery.data.permissionsPolicyJson} readOnly rows={14} className="operational-surface" style={{ width: '100%', padding: '0.75rem 1rem', fontFamily: 'var(--font-mono)', resize: 'vertical' }} />
                </div>

                <details style={{ border: '1px solid var(--border)', borderRadius: 'var(--radius-md)', padding: '1rem' }}>
                  <summary style={{ cursor: 'pointer', fontWeight: 700 }}>Advanced: Terraform Template</summary>
                  <textarea value={awsBootstrapQuery.data.terraformTemplate} readOnly rows={16} className="operational-surface" style={{ width: '100%', padding: '0.75rem 1rem', fontFamily: 'var(--font-mono)', resize: 'vertical', marginTop: '1rem' }} />
                </details>

                <div style={{ background: 'rgba(255,255,255,0.02)', padding: '1rem', borderRadius: 'var(--radius-md)', border: '1px solid var(--border)', display: 'flex', flexDirection: 'column', gap: '0.75rem' }}>
                  <div style={{ fontSize: '0.75rem', fontWeight: 800, textTransform: 'uppercase', letterSpacing: '0.08em', color: 'var(--muted)' }}>Step 2</div>
                  <div style={{ fontSize: '0.9rem', fontWeight: 700 }}>Paste the created role ARN back into Draco</div>
                  <p style={{ fontSize: '0.8125rem', color: 'var(--muted)', margin: 0 }}>
                    Draco stores the role ARN so it can assume the role again during future syncs. The ARN itself is not a secret.
                  </p>
                </div>
              </>
            )}

            {provider === 'AWS' && connectMutation.isPending && (
              <div style={{ color: 'var(--muted)', fontSize: '0.875rem', display: 'flex', alignItems: 'center', gap: '0.5rem' }}>
                <Loader2 className="animate-spin" size={16} />
                Connecting AWS account...
              </div>
            )}

            {provider === 'AWS' && connectMutation.isError && (
              <div style={{ color: 'var(--primary)', fontSize: '0.875rem', display: 'flex', alignItems: 'center', gap: '0.5rem' }}>
                <AlertCircle size={16} />
                {(connectMutation.error as Error).message}
              </div>
            )}
          </div>
          <DrawerFooter style={{ borderTop: '1px solid var(--border)', padding: '1rem 1.25rem' }}>
            <DrawerClose asChild>
              <button className="btn-secondary">Close</button>
            </DrawerClose>
          </DrawerFooter>
        </DrawerContent>
      </Drawer>
    </div>
  )
}
