import { createRoute } from '@tanstack/react-router'
import { Route as rootRoute } from './__root'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Gavel, AlertTriangle, FileText, Wallet, Plus, Loader2, Cloud } from 'lucide-react'
import { dracoApi, formatCurrencyAmount, type BudgetRecord } from '../lib/api'
import { useMemo, useState } from 'react'

export const GovernanceRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/governance',
  component: Governance,
})

function Governance() {
  const queryClient = useQueryClient()
  const [name, setName] = useState('')
  const [provider, setProvider] = useState('Azure')
  const [subscriptionId, setSubscriptionId] = useState('')
  const [amount, setAmount] = useState('')
  const [threshold, setThreshold] = useState('80')

  const { data: policies = [] } = useQuery({
    queryKey: ['governance-policies'],
    queryFn: dracoApi.governance.getPolicies,
  })

  const { data: workflows = [] } = useQuery({
    queryKey: ['workflow-suggestions'],
    queryFn: dracoApi.workflows.suggestions,
  })

  const { data: budgets = [] } = useQuery({
    queryKey: ['cost-budgets'],
    queryFn: dracoApi.costs.getBudgets,
  })

  const { data: connections = [] } = useQuery({
    queryKey: ['cloud-connections'],
    queryFn: dracoApi.cloudConnections.list,
  })

  const createBudgetMutation = useMutation({
    mutationFn: () =>
      dracoApi.costs.createBudget({
        name: name.trim(),
        provider,
        subscriptionId: subscriptionId.trim(),
        amount: Number(amount),
        currency: 'USD',
        timeGrain: 'Monthly',
        alertThresholdPercentage: Number(threshold) || 80,
        isActive: true,
      }),
    onSuccess: async () => {
      setName('')
      setAmount('')
      setThreshold('80')
      await queryClient.invalidateQueries({ queryKey: ['cost-budgets'] })
      await queryClient.invalidateQueries({ queryKey: ['governance-policies'] })
      await queryClient.invalidateQueries({ queryKey: ['dashboard-summary'] })
    },
  })

  const providerConnections = useMemo(
    () => connections.filter(connection => connection.provider === provider),
    [connections, provider],
  )

  const manualBudgets = useMemo(
    () => budgets.filter(budget => budget.budgetSource === 'Manual'),
    [budgets],
  )

  const importedBudgets = useMemo(
    () => budgets.filter(budget => budget.budgetSource !== 'Manual'),
    [budgets],
  )

  const isBudgetReady = name.trim() && subscriptionId.trim() && Number(amount) > 0

  return (
    <div className="animate-fade-in">
      <div style={{ marginBottom: '3rem' }}>
        <h2 style={{ fontSize: '2rem', marginBottom: '0.5rem' }}>Governance</h2>
        <p style={{ color: 'var(--muted)' }}>Budgets, policy thresholds, and the notification signals that enforce them.</p>
      </div>

      <div className="grid grid-cols-2" style={{ gap: '2rem', marginBottom: '2rem' }}>
        <div className="premium-glass" style={{ padding: '1.5rem' }}>
          <div style={{ display: 'flex', alignItems: 'center', gap: '0.75rem', marginBottom: '1rem' }}>
            <Wallet className="text-primary" size={20} />
            <h3 style={{ fontSize: '1rem', fontWeight: 800 }}>Manual Budget Guardrails</h3>
          </div>
          <p style={{ color: 'var(--muted)', fontSize: '0.8125rem', marginBottom: '1.25rem' }}>
            Create your own total budget thresholds per subscription. These feed the notification engine alongside imported cloud budgets.
          </p>

          <div style={{ display: 'grid', gap: '1rem' }}>
            <div>
              <label className="micro-label">Budget Name</label>
              <input
                value={name}
                onChange={(event) => setName(event.target.value)}
                placeholder="Monthly production spend"
                className="operational-surface"
                style={{ width: '100%', padding: '0.75rem' }}
              />
            </div>

            <div className="grid grid-cols-2" style={{ gap: '1rem' }}>
              <div>
                <label className="micro-label">Provider</label>
                <select
                  value={provider}
                  onChange={(event) => {
                    setProvider(event.target.value)
                    setSubscriptionId('')
                  }}
                  className="operational-surface"
                  style={{ width: '100%', padding: '0.75rem' }}
                >
                  <option value="Azure">Azure</option>
                  <option value="AWS">AWS</option>
                </select>
              </div>
              <div>
                <label className="micro-label">Threshold %</label>
                <input
                  type="number"
                  min="1"
                  max="100"
                  value={threshold}
                  onChange={(event) => setThreshold(event.target.value)}
                  className="operational-surface"
                  style={{ width: '100%', padding: '0.75rem' }}
                />
              </div>
            </div>

            <div className="grid grid-cols-2" style={{ gap: '1rem' }}>
              <div>
                <label className="micro-label">Subscription / Account</label>
                <select
                  value={subscriptionId}
                  onChange={(event) => setSubscriptionId(event.target.value)}
                  className="operational-surface"
                  style={{ width: '100%', padding: '0.75rem' }}
                >
                  <option value="">Select connection</option>
                  {providerConnections.map(connection => (
                    <option key={connection.id} value={connection.subscriptionId}>
                      {connection.displayName || connection.subscriptionId}
                    </option>
                  ))}
                </select>
              </div>
              <div>
                <label className="micro-label">Monthly Limit</label>
                <input
                  type="number"
                  min="1"
                  step="1"
                  value={amount}
                  onChange={(event) => setAmount(event.target.value)}
                  placeholder="5000"
                  className="operational-surface"
                  style={{ width: '100%', padding: '0.75rem' }}
                />
              </div>
            </div>

            <button
              className="btn-primary"
              onClick={() => createBudgetMutation.mutate()}
              disabled={!isBudgetReady || createBudgetMutation.isPending}
            >
              {createBudgetMutation.isPending ? (
                <>
                  <Loader2 className="animate-spin" size={14} /> Saving...
                </>
              ) : (
                <>
                  <Plus size={14} /> Add Budget
                </>
              )}
            </button>
          </div>
        </div>

        <div className="premium-glass" style={{ padding: '1.5rem' }}>
          <div style={{ display: 'flex', alignItems: 'center', gap: '0.75rem', marginBottom: '1rem' }}>
            <Cloud className="text-primary" size={20} />
            <h3 style={{ fontSize: '1rem', fontWeight: 800 }}>Synced Budget Feeds</h3>
          </div>
          <p style={{ color: 'var(--muted)', fontSize: '0.8125rem', marginBottom: '1.25rem' }}>
            Azure and AWS budgets discovered during sync are imported here and evaluated by the same rule engine as your manual thresholds.
          </p>

          <div className="grid grid-cols-3" style={{ gap: '1rem', marginBottom: '1rem' }}>
            <div className="operational-surface" style={{ padding: '1rem' }}>
              <div className="micro-label" style={{ marginBottom: '0.5rem' }}>Manual</div>
              <div style={{ fontSize: '1.5rem', fontWeight: 800 }}>{manualBudgets.length}</div>
            </div>
            <div className="operational-surface" style={{ padding: '1rem' }}>
              <div className="micro-label" style={{ marginBottom: '0.5rem' }}>Imported</div>
              <div style={{ fontSize: '1.5rem', fontWeight: 800 }}>{importedBudgets.length}</div>
            </div>
            <div className="operational-surface" style={{ padding: '1rem' }}>
              <div className="micro-label" style={{ marginBottom: '0.5rem' }}>Active Policies</div>
              <div style={{ fontSize: '1.5rem', fontWeight: 800 }}>{policies.length}</div>
            </div>
          </div>

          <div style={{ display: 'flex', flexDirection: 'column', gap: '0.75rem' }}>
            {importedBudgets.slice(0, 4).map((budget) => (
              <BudgetRow key={budget.id} budget={budget} />
            ))}
            {importedBudgets.length === 0 && (
              <div className="operational-surface" style={{ padding: '1rem', color: 'var(--muted)' }}>
                Imported budgets will appear here after the next cloud sync.
              </div>
            )}
          </div>
        </div>
      </div>

      <div className="grid grid-cols-3" style={{ marginBottom: '3rem' }}>
        {policies.map((policy: any) => (
          <div key={policy.id} className="card" style={{ padding: '1.25rem' }}>
            <div style={{ display: 'flex', justifyContent: 'space-between', marginBottom: '1rem' }}>
              <Gavel size={16} color={policy.status === 'Exceeded' ? 'var(--primary)' : 'var(--muted-foreground)'} />
              <span className="micro-label" style={{ color: policy.status === 'Exceeded' ? 'var(--primary)' : '#00cc00' }}>{policy.status}</span>
            </div>
            <h3 style={{ fontSize: '1rem', marginBottom: '0.25rem' }}>{policy.name}</h3>
            <div className="mono" style={{ fontSize: '0.75rem', color: 'var(--muted-foreground)', marginBottom: '1.25rem' }}>{policy.provider} • {policy.subscriptionId}</div>

            <div style={{ background: 'var(--border)', height: '3px', borderRadius: 'var(--radius-full)', overflow: 'hidden', marginBottom: '0.5rem' }}>
              <div style={{ width: `${Math.min(Number(policy.threshold ?? 0), 100)}%`, height: '100%', background: policy.status === 'Exceeded' ? 'var(--primary)' : '#00cc00' }}></div>
            </div>
            <div style={{ display: 'flex', justifyContent: 'space-between', fontSize: '0.7rem', fontWeight: 600 }}>
              <span className="micro-label" style={{ color: 'var(--muted-foreground)' }}>Threshold</span>
              <span>{policy.threshold}%</span>
            </div>
          </div>
        ))}
        {policies.length === 0 && <div className="card">No governance policies or budgets configured yet.</div>}
      </div>

      <div className="operational-surface" style={{ marginBottom: '2rem' }}>
        <div style={{ display: 'flex', gap: '0.75rem', alignItems: 'center', padding: '1rem', borderBottom: '1px solid var(--border)' }}>
          <Wallet size={16} />
          <h3 className="micro-label">Budget Catalog</h3>
        </div>

        <div style={{ display: 'flex', flexDirection: 'column' }}>
          {budgets.map((budget) => (
            <div key={budget.id} className="operational-row" style={{ display: 'flex', gap: '1rem', alignItems: 'center' }}>
              <div style={{ flex: 1 }}>
                <div style={{ display: 'flex', gap: '0.5rem', alignItems: 'center', flexWrap: 'wrap' }}>
                  <span style={{ fontWeight: 700, fontSize: '0.85rem' }}>{budget.name}</span>
                  <span className="badge" style={{ fontSize: '0.6rem', background: budget.budgetSource === 'Manual' ? 'rgba(255,255,255,0.05)' : 'rgba(255,0,0,0.08)' }}>
                    {formatBudgetSource(budget.budgetSource)}
                  </span>
                </div>
                <div style={{ fontSize: '0.75rem', color: 'var(--muted-foreground)', marginTop: '0.2rem' }}>
                  {budget.provider} • {budget.subscriptionId}
                </div>
              </div>
              <div style={{ minWidth: '140px', textAlign: 'right' }}>
                <div style={{ fontWeight: 700 }}>{formatCurrencyAmount(budget.amount, budget.currency)}</div>
                <div style={{ fontSize: '0.72rem', color: 'var(--muted)' }}>
                  {budget.currentSpend != null ? `${formatCurrencyAmount(budget.currentSpend, budget.currency)} used` : 'Waiting for spend'}
                </div>
              </div>
              <div style={{ minWidth: '110px', textAlign: 'right', fontSize: '0.75rem', color: 'var(--muted-foreground)' }}>
                {budget.lastSyncedAt ? `Synced ${new Date(budget.lastSyncedAt).toLocaleDateString()}` : 'Manual'}
              </div>
            </div>
          ))}
          {budgets.length === 0 && (
            <div style={{ padding: '1rem', color: 'var(--muted)' }}>No budgets configured yet.</div>
          )}
        </div>
      </div>

      <div className="operational-surface">
        <div style={{ display: 'flex', gap: '0.75rem', alignItems: 'center', padding: '1rem', borderBottom: '1px solid var(--border)' }}>
          <FileText size={16} />
          <h3 className="micro-label">Drift Intelligence Log</h3>
        </div>

        <div style={{ display: 'flex', flexDirection: 'column' }}>
          {workflows.map((workflow) => (
            <div key={workflow.id} className="operational-row" style={{ display: 'flex', gap: '1.5rem', alignItems: 'center' }}>
              <AlertTriangle size={14} color="var(--primary)" />
              <div style={{ flex: 1 }}>
                <div style={{ display: 'flex', justifyContent: 'space-between' }}>
                  <span style={{ fontWeight: 600, fontSize: '0.8125rem' }}>{workflow.suggestedAction}</span>
                  <span className="mono" style={{ fontSize: '0.7rem', color: 'var(--muted-foreground)' }}>{workflow.severity}</span>
                </div>
                <div className="mono" style={{ fontSize: '0.75rem', color: 'var(--muted-foreground)', marginTop: '0.15rem' }}>
                  {workflow.provider || 'global'} • {workflow.reason}
                </div>
              </div>
              <button className="btn-secondary" style={{ padding: '0.35rem 0.65rem', fontSize: '0.7rem', height: '28px' }}>{workflow.canAutoRun ? 'Auto' : 'Review'}</button>
            </div>
          ))}
          {workflows.length === 0 && (
            <div style={{ padding: '1rem', color: 'var(--muted)' }}>No workflow suggestions yet.</div>
          )}
        </div>
      </div>
    </div>
  )
}

function BudgetRow({ budget }: { budget: BudgetRecord }) {
  const importedNotificationCount = useMemo(() => {
    if (!budget.notificationSettingsJson) {
      return 0
    }

    try {
      const parsed = JSON.parse(budget.notificationSettingsJson)
      return Array.isArray(parsed) ? parsed.length : 0
    } catch {
      return 0
    }
  }, [budget.notificationSettingsJson])

  return (
    <div className="operational-surface" style={{ padding: '1rem' }}>
      <div style={{ display: 'flex', justifyContent: 'space-between', gap: '1rem', alignItems: 'center' }}>
        <div>
          <div style={{ fontWeight: 700, fontSize: '0.85rem' }}>{budget.name}</div>
          <div style={{ fontSize: '0.75rem', color: 'var(--muted-foreground)' }}>
            {budget.provider} • {budget.scopeDisplayName || budget.subscriptionId}
          </div>
        </div>
        <span className="badge" style={{ fontSize: '0.6rem', background: 'rgba(255,0,0,0.08)' }}>
          {formatBudgetSource(budget.budgetSource)}
        </span>
      </div>
      <div style={{ marginTop: '0.75rem', display: 'flex', justifyContent: 'space-between', gap: '1rem', fontSize: '0.75rem' }}>
        <span style={{ color: 'var(--muted)' }}>
          {budget.currentSpend != null ? `${formatCurrencyAmount(budget.currentSpend, budget.currency)} used` : 'Waiting for spend'}
        </span>
        <span style={{ color: 'var(--muted-foreground)' }}>
          {budget.lastSyncedAt ? `Synced ${new Date(budget.lastSyncedAt).toLocaleDateString()}` : 'Imported'}
        </span>
      </div>
      <div style={{ marginTop: '0.5rem', display: 'flex', justifyContent: 'space-between', gap: '1rem', fontSize: '0.72rem' }}>
        <span style={{ color: 'var(--muted)' }}>
          {budget.scopeType || 'Subscription'} scope
        </span>
        <span style={{ color: 'var(--muted-foreground)' }}>
          {importedNotificationCount > 0 ? `${importedNotificationCount} imported reminders` : `${budget.alertThresholdPercentage}% threshold`}
        </span>
      </div>
    </div>
  )
}

function formatBudgetSource(source: string) {
  switch (source) {
    case 'AzureImported':
      return 'Azure Import'
    case 'AwsImported':
      return 'AWS Import'
    default:
      return 'Manual'
  }
}
