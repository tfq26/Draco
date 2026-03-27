const API_BASE_URL = import.meta.env.VITE_API_URL || 'http://localhost:5020'
const WORKOS_CLIENT_ID = import.meta.env.VITE_WORKOS_CLIENT_ID
const WORKOS_AUTHORIZE_URL = 'https://api.workos.com/user_management/authorize'
const WORKOS_CODE_VERIFIER_STORAGE_KEY = 'draco:workos:code-verifier'
const AZURE_OAUTH_STATE_STORAGE_KEY = 'draco:azure:oauth-state'

export interface DracoUser {
  id: string
  authId?: string
  name: string
  email?: string
  phone?: string
  imageUrl?: string
  preferredChannel?: string
  isSetupComplete: boolean
  connections: CloudConnection[]
  schedules?: unknown[]
}

export interface CloudConnection {
  id: number
  provider: string
  subscriptionId: string
  displayName?: string
  authType?: string
  externalAccountId?: string
  awsRoleArn?: string
  isActive: boolean
  connectedAt: string
  lastSyncedAt?: string
  syncStatus: string
  syncMessage?: string
}

export interface AzureSubscriptionOption {
  subscriptionId: string
  displayName: string
  state?: string
}

export interface AzureExchangeResult {
  accessToken: string
  refreshToken?: string
  tokenExpiresAt: string
  subscriptions: AzureSubscriptionOption[]
}

export interface AwsBootstrapResult {
  accountId: string
  trustedPrincipalArn: string
  externalId: string
  suggestedRoleName: string
  suggestedRoleArn: string
  trustPolicyJson: string
  permissionsPolicyJson: string
  terraformTemplate: string
}

export interface InsightOverview {
  connectionCount: number
  providerCount: number
  subscriptionCount: number
  resourceCount: number
  recommendationCount: number
  openAlertCount: number
  anomalyCount: number
  currentMonthlyCost: number
  forecastMonthlyCost: number
  potentialMonthlySavings: number
  lastSyncedAt?: string
}

export interface InsightConnectionHealth {
  connectionId: number
  provider: string
  subscriptionId: string
  displayName?: string
  isActive: boolean
  connectedAt: string
  lastSyncedAt?: string
  syncStatus: string
  syncMessage?: string
}

export interface InsightAnomaly {
  id: string
  category: string
  severity: string
  title: string
  summary: string
  provider: string
  subscriptionId?: string
  resourceId?: string
  detectionMethod: string
  currentValue?: number
  baselineValue?: number
  unit?: string
}

export interface InsightWorkflowSuggestion {
  id: string
  trigger: string
  suggestedAction: string
  severity: string
  reason: string
  provider: string
  subscriptionId?: string
  resourceId?: string
  canAutoRun: boolean
}

export interface DashboardSummary {
  userId: string
  userName: string
  email?: string
  generatedAt: string
  overview: InsightOverview
  connections: InsightConnectionHealth[]
  providerBreakdown: Array<{ provider: string; resourceCount: number; subscriptionCount: number }>
  resourceTypeBreakdown: Array<{ type: string; count: number }>
  providerCostBreakdown: Array<{ provider: string; totalAmount: number; currency: string; resourceCount: number }>
  resourceGroupCostBreakdown: Array<{ provider: string; resourceGroupName: string; totalAmount: number; currency: string; resourceCount: number }>
  resourceCostBreakdown: Array<{
    resourceId: string
    resourceName: string
    resourceType: string
    provider: string
    subscriptionId: string
    resourceGroupName: string
    amount: number
    currency: string
    costSource: string
    capturedAt: string
  }>
  costBreakdown: Array<{
    provider: string
    subscriptionId: string
    currency: string
    currentAmount: number
    previousAmount?: number
    deltaAmount?: number
    deltaPercentage?: number
    granularity: string
    periodStart: string
    periodEnd: string
  }>
  budgets: Array<{
    budgetId: string
    name: string
    provider: string
    subscriptionId: string
    limitAmount: number
    currentAmount: number
    remainingAmount: number
    alertThresholdPercentage: number
    consumedPercentage: number
    currency: string
    status: string
  }>
  recommendations: Array<{
    id: string
    provider: string
    subscriptionId: string
    resourceId: string
    resourceName: string
    recommendationType: string
    description: string
    potentialSavings: number
    currency: string
    status: string
    discoveredAt: string
  }>
  anomalies: InsightAnomaly[]
  workflowSuggestions: InsightWorkflowSuggestion[]
}

function normalizeDashboardSummary(summary: DashboardSummary): DashboardSummary {
  return {
    ...summary,
    connections: summary.connections ?? [],
    providerBreakdown: summary.providerBreakdown ?? [],
    resourceTypeBreakdown: summary.resourceTypeBreakdown ?? [],
    providerCostBreakdown: summary.providerCostBreakdown ?? [],
    resourceGroupCostBreakdown: summary.resourceGroupCostBreakdown ?? [],
    resourceCostBreakdown: summary.resourceCostBreakdown ?? [],
    costBreakdown: summary.costBreakdown ?? [],
    budgets: summary.budgets ?? [],
    recommendations: summary.recommendations ?? [],
    anomalies: summary.anomalies ?? [],
    workflowSuggestions: summary.workflowSuggestions ?? [],
  }
}

export interface ResourceRecord {
  id: string
  name: string
  type: string
  provider: string
  location: string
  subscriptionId: string
  resourceGroupName: string
  tags: Record<string, string>
  discoveredAt: string
  monthlyCost: number
  currency: string
  costSource: string
  costCapturedAt?: string
}

export interface ResourceActionDefinition {
  action: string
  label: string
  description: string
  isDestructive: boolean
  provider: string
  resourceType: string
  executionMode: string
}

export interface ResourceActionAudit {
  id: string
  actionType: string
  status: string
  description?: string
  errorMessage?: string
  createdAt: string
  completedAt?: string
}

export interface ResourceActionExecutionResult {
  auditId: string
  action: string
  status: string
  workspacePath: string
  terraformConfiguration: string
  output?: string
  errorOutput?: string
  responseBody?: string
  responseStatusCode?: number
  startedAt: string
  completedAt?: string
  errorMessage?: string
}

export interface ResourceDetail {
  resource: {
    id: string
    name: string
    type: string
    provider: string
    location: string
    subscriptionId: string
    resourceGroupName: string
    tags: Record<string, string>
    discoveredAt: string
  }
  cost?: {
    amount: number
    currency: string
    costSource: string
    capturedAt?: string
    periodStart?: string
    periodEnd?: string
    provider: string
    subscriptionId: string
    resourceGroupName: string
  } | null
  costContext?: {
    providerTotal: number
    resourceGroupTotal: number
  } | null
  availableActions: ResourceActionDefinition[]
  actionAudits: ResourceActionAudit[]
  recommendations: Array<{
    id: string
    recommendationType: string
    description: string
    potentialSavings: number
    currency: string
    status: string
  }>
  metrics: Array<{
    id: string
    name?: string
    metricName?: string
    value?: number
    metricValue?: number
    unit?: string
    timestamp: string
  }>
}

export interface BudgetRecord {
  id: string
  name: string
  provider: string
  subscriptionId: string
  budgetSource: string
  externalBudgetId?: string
  scope: string
  scopeType?: string
  scopeDisplayName?: string
  amount: number
  currentSpend?: number
  forecastSpend?: number
  currency: string
  timeGrain: string
  alertThresholdPercentage: number
  notificationSettingsJson?: string
  createdAt: string
  lastSyncedAt?: string
  isActive: boolean
}

export interface CreateBudgetInput {
  name: string
  provider: string
  subscriptionId: string
  amount: number
  currency?: string
  timeGrain?: string
  alertThresholdPercentage: number
  isActive: boolean
}

export interface WorkflowRun {
  id: string
  workflowType: string
  trigger: string
  suggestedAction: string
  severity: string
  provider: string
  subscriptionId: string
  resourceId?: string
  status: string
  canAutoRun: boolean
  reason: string
  recommendation?: string
  createdAt: string
  updatedAt?: string
  completedAt?: string
}

export interface GovernancePolicy {
  id: string
  name: string
  provider: string
  subscriptionId: string
  type: string
  threshold: number
  limit: number
  current: number
  currency: string
  status: string
}

const costSourceLabels: Record<string, string> = {
  AzureActual: 'Actual (Azure)',
  AwsActual: 'Actual (AWS EC2)',
  Estimated: 'Estimated Fallback',
  Unavailable: 'Unavailable',
}

export function getCostSourceLabel(costSource?: string): string {
  if (!costSource) {
    return 'Unavailable'
  }

  return costSourceLabels[costSource] ?? costSource
}

export function getCostSourceColor(costSource?: string): string {
  switch (costSource) {
    case 'AzureActual':
    case 'AwsActual':
      return '#1dd1a1'
    case 'Estimated':
      return '#ffaa33'
    default:
      return 'var(--muted)'
  }
}

export function formatCurrencyAmount(amount: number, currency = 'USD') {
  try {
    return new Intl.NumberFormat(undefined, {
      style: 'currency',
      currency,
      minimumFractionDigits: 2,
      maximumFractionDigits: 2,
    }).format(amount)
  } catch {
    return `$${amount.toFixed(2)}`
  }
}

function getStoredToken(): string | null {
  return localStorage.getItem('draco_token')
}

function storeToken(token: string) {
  localStorage.setItem('draco_token', token)
}

function clearToken() {
  localStorage.removeItem('draco_token')
}

function getWorkOsCodeVerifier() {
  return sessionStorage.getItem(WORKOS_CODE_VERIFIER_STORAGE_KEY)
}

function clearWorkOsCodeVerifier() {
  sessionStorage.removeItem(WORKOS_CODE_VERIFIER_STORAGE_KEY)
}

function getAzureOauthState() {
  return sessionStorage.getItem(AZURE_OAUTH_STATE_STORAGE_KEY)
}

function clearAzureOauthState() {
  sessionStorage.removeItem(AZURE_OAUTH_STATE_STORAGE_KEY)
}

function base64UrlEncode(bytes: Uint8Array): string {
  const binary = Array.from(bytes, byte => String.fromCharCode(byte)).join('')
  return btoa(binary).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/g, '')
}

function createCodeVerifier(): string {
  const bytes = crypto.getRandomValues(new Uint8Array(96))
  return base64UrlEncode(bytes)
}

async function createCodeChallenge(codeVerifier: string): Promise<string> {
  const data = new TextEncoder().encode(codeVerifier)
  const digest = await crypto.subtle.digest('SHA-256', data)
  return base64UrlEncode(new Uint8Array(digest))
}

async function beginWorkOsSignIn() {
  if (!WORKOS_CLIENT_ID) {
    throw new Error('VITE_WORKOS_CLIENT_ID is not configured.')
  }

  const codeVerifier = createCodeVerifier()
  const codeChallenge = await createCodeChallenge(codeVerifier)
  const redirectUri = `${window.location.origin}/auth/callback`

  sessionStorage.setItem(WORKOS_CODE_VERIFIER_STORAGE_KEY, codeVerifier)

  const url = new URL(WORKOS_AUTHORIZE_URL)
  url.searchParams.set('provider', 'authkit')
  url.searchParams.set('client_id', WORKOS_CLIENT_ID)
  url.searchParams.set('redirect_uri', redirectUri)
  url.searchParams.set('response_type', 'code')
  url.searchParams.set('screen_hint', 'sign-in')
  url.searchParams.set('code_challenge', codeChallenge)
  url.searchParams.set('code_challenge_method', 'S256')

  window.location.assign(url.toString())
}

async function beginAzureSignIn() {
  const state = createCodeVerifier()
  const redirectUri = `${window.location.origin}/setup`

  sessionStorage.setItem(AZURE_OAUTH_STATE_STORAGE_KEY, state)

  const result = await fetchWithAuth<{ authorizeUrl: string }>(
    `/api/cloud-connections/azure/authorize-url?redirectUri=${encodeURIComponent(redirectUri)}&state=${encodeURIComponent(state)}`,
  )

  window.location.assign(result.authorizeUrl)
}

async function fetchWithAuth<T>(endpoint: string, options: RequestInit = {}): Promise<T> {
  const token = getStoredToken()
  const headers = new Headers(options.headers)

  if (!headers.has('Content-Type') && options.body) {
    headers.set('Content-Type', 'application/json')
  }

  if (token) {
    headers.set('Authorization', `Bearer ${token}`)
  }

  const response = await fetch(`${API_BASE_URL}${endpoint}`, {
    ...options,
    headers,
  })

  if (response.status === 401) {
    clearToken()
    window.location.href = '/login'
    throw new Error('Unauthorized')
  }

  if (!response.ok) {
    const error = await response
      .json()
      .catch(async () => ({ message: (await response.text().catch(() => '')).trim() || 'API request failed' }))
    const message =
      error.message ||
      error.detail ||
      error.title ||
      (typeof error.errors === 'object' && error.errors !== null
        ? Object.values(error.errors)
          .flatMap((value) => Array.isArray(value) ? value : [String(value)])
          .join(' ')
        : undefined) ||
      'API request failed'
    throw new Error(message)
  }

  if (response.status === 204) {
    return undefined as T
  }

  return response.json() as Promise<T>
}

export const dracoApi = {
  auth: {
    getToken: getStoredToken,
    storeToken,
    clearToken,
    beginWorkOsSignIn,
    getWorkOsCodeVerifier,
    clearWorkOsCodeVerifier,
    getAzureOauthState,
    clearAzureOauthState,
    beginAzureSignIn,
    syncWorkOs: (data: { workOsUserId: string; email?: string; name?: string; imageUrl?: string }) =>
      fetchWithAuth<{ token: string; user: DracoUser }>('/api/auth/workos/sync', {
        method: 'POST',
        body: JSON.stringify(data),
      }),
    exchangeWorkOsCode: (code: string, codeVerifier: string) =>
      fetchWithAuth<{ token: string; user: DracoUser }>('/api/auth/workos/exchange', {
        method: 'POST',
        body: JSON.stringify({ code, codeVerifier }),
      }),
    getMe: () => fetchWithAuth<DracoUser>('/api/auth/me'),
  },
  cloudConnections: {
    list: () => fetchWithAuth<CloudConnection[]>('/api/cloud-connections'),
    getAwsBootstrap: (accountId: string, roleName?: string) =>
      fetchWithAuth<AwsBootstrapResult>(
        `/api/cloud-connections/aws/bootstrap?accountId=${encodeURIComponent(accountId)}${roleName ? `&roleName=${encodeURIComponent(roleName)}` : ''}`,
      ),
    exchangeAzureCode: (data: { code: string; redirectUri: string }) =>
      fetchWithAuth<AzureExchangeResult>('/api/cloud-connections/azure/exchange', {
        method: 'POST',
        body: JSON.stringify(data),
      }),
    upsert: (data: {
      provider: string
      subscriptionId: string
      displayName?: string
      authType?: string
      externalAccountId?: string
      awsRoleArn?: string
      accessToken?: string
      refreshToken?: string
      tokenExpiresAt?: string
    }) =>
      fetchWithAuth<CloudConnection>('/api/cloud-connections', {
        method: 'POST',
        body: JSON.stringify(data),
      }),
    remove: (id: number) =>
      fetchWithAuth<{ message: string }>(`/api/cloud-connections/${id}`, {
        method: 'DELETE',
      }),
    sync: (connectionIds?: number[]) =>
      fetchWithAuth<{ connections: number; results: Array<Record<string, unknown>> }>('/api/cloud-connections/sync', {
        method: 'POST',
        body: JSON.stringify({ connectionIds }),
      }),
  },
  notifications: {
    getAll: () => fetchWithAuth<any[]>('/api/notifications'),
    markAsRead: (id: number) => fetchWithAuth(`/api/notifications/${id}/read`, { method: 'PATCH' }),
    clearAll: () => fetchWithAuth('/api/notifications/clear-all', { method: 'POST' }),
    createTest: () => fetchWithAuth('/api/notifications/test', { method: 'POST' }),
  },
  dashboard: {
    getSummary: async (provider?: string, resourceGroup?: string) => {
      let url = '/api/dashboard/summary'
      const params = new URLSearchParams()
      if (provider) params.append('provider', provider)
      if (resourceGroup) params.append('resourceGroup', resourceGroup)
      const qs = params.toString()
      if (qs) url += `?${qs}`
      return normalizeDashboardSummary(await fetchWithAuth<DashboardSummary>(url))
    },
    getAiContext: () => fetchWithAuth<{ context: DashboardSummary; modelContext: string }>('/api/ai/context'),
  },
  monitoring: {
    getStats: () => fetchWithAuth<Record<string, number | string | null>>('/api/monitoring/stats'),
    getAlerts: () => fetchWithAuth<InsightAnomaly[]>('/api/monitoring/alerts'),
  },
  resources: {
    list: () => fetchWithAuth<ResourceRecord[]>('/api/resources/list'),
    getById: (id: string) => fetchWithAuth<ResourceDetail>(`/api/resources/detail?id=${encodeURIComponent(id)}`),
    executeAction: (resourceId: string, action: string) =>
      fetchWithAuth<ResourceActionExecutionResult>('/api/resources/actions/execute', {
        method: 'POST',
        body: JSON.stringify({ resourceId, action }),
      }),
  },
  governance: {
    getPolicies: () => fetchWithAuth<GovernancePolicy[]>('/api/governance/policies'),
  },
  costs: {
    getOverview: () => fetchWithAuth<Record<string, unknown>>('/api/costs/overview'),
    getBudgets: () => fetchWithAuth<BudgetRecord[]>('/api/costs/budgets'),
    createBudget: (data: CreateBudgetInput) =>
      fetchWithAuth<BudgetRecord>('/api/costs/budgets', {
        method: 'POST',
        body: JSON.stringify(data),
      }),
  },
  workflows: {
    suggestions: () => fetchWithAuth<InsightWorkflowSuggestion[]>('/api/workflows/suggestions'),
    list: () => fetchWithAuth<WorkflowRun[]>('/api/workflows/runs'),
    updateStatus: (id: string, status: string) =>
      fetchWithAuth<WorkflowRun>(`/api/workflows/runs/${id}`, {
        method: 'PATCH',
        body: JSON.stringify({ status }),
      }),
  },
  events: {
    list: () => fetchWithAuth<Array<Record<string, unknown>>>('/api/events'),
  },
}
