import { createRoute } from '@tanstack/react-router'
import { Route as rootRoute } from './__root'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { AlertCircle, RefreshCcw, Shield, Zap } from 'lucide-react'
import { dracoApi } from '../lib/api'
import { BarChart } from '../components/charts/DChart'

export const Route = createRoute({
  getParentRoute: () => rootRoute,
  path: '/dashboard',
  component: Dashboard,
})

function Dashboard() {
  const queryClient = useQueryClient()
  
  const { data: summary, isLoading, refetch, isRefetching } = useQuery({
    queryKey: ['dashboard-summary'],
    queryFn: dracoApi.dashboard.getSummary,
  })

  const { data: alerts = [] } = useQuery({
    queryKey: ['monitoring-alerts'],
    queryFn: dracoApi.monitoring.getAlerts,
  })


  const syncMutation = useMutation({
    mutationFn: async () => {
      if (!summary) {
        return
      }

      const connectionIds = summary.connections.map(connection => connection.connectionId)
      if (connectionIds.length === 0) {
        return
      }

      return dracoApi.cloudConnections.sync(connectionIds)
    },
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['dashboard-summary'] })
      await queryClient.invalidateQueries({ queryKey: ['monitoring-alerts'] })
    },
  })

  if (isLoading || !summary) {
    return (
      <div className="layout-container" style={{ display: 'flex', alignItems: 'center', justifyContent: 'center', minHeight: '60vh' }}>
        <div className="animate-pulse" style={{ textAlign: 'center' }}>
          <Shield size={48} className="text-primary" style={{ marginBottom: '1rem', opacity: 0.5 }} />
          <p style={{ color: 'var(--muted)' }}>Loading Sentinel Intelligence...</p>
        </div>
      </div>
    )
  }

  const providerCostBreakdown = summary.providerCostBreakdown ?? []
  const costBreakdown = summary.costBreakdown ?? []
  const resourceGroupCostBreakdown = summary.resourceGroupCostBreakdown ?? []
  const providerBreakdown = summary.providerBreakdown ?? []
  const anomalyItems = alerts.length > 0 ? alerts : (summary.anomalies ?? [])

  const hasCostData = providerCostBreakdown.length > 0 || costBreakdown.length > 0

  const costChartData = providerCostBreakdown.length > 0
    ? providerCostBreakdown.map(item => ({
        x: item.provider,
        y: item.totalAmount,
        metadata: item,
      }))
    : providerBreakdown.map(item => ({
        x: item.provider,
        y: item.resourceCount,
        metadata: item,
      }))

  const resourceGroupChartData = resourceGroupCostBreakdown
    .filter(item => item.totalAmount > 0)
    .map(item => ({
      x: item.resourceGroupName,
      y: item.totalAmount,
      metadata: item,
    }))

  const chartLabel = hasCostData ? 'Spend by Provider' : 'Resources by Provider'
  const showBootstrapNotice =
    summary.connections.length > 0 &&
    !hasCostData &&
    anomalyItems.length === 0

  return (
    <div className="animate-fade-in">
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-end', marginBottom: '3rem' }}>
        <div>
          <h2 className="monochrome-gradient" style={{ fontSize: '2.5rem', marginBottom: '0.5rem', letterSpacing: '-0.03em' }}>
            Command Center
          </h2>
          <p style={{ color: 'var(--muted)', fontSize: '1rem' }}>
            Autonomous cloud governance backed by prepared insight context.
          </p>
        </div>
        <div style={{ display: 'flex', gap: '0.5rem' }}>
          <button 
            className="btn-secondary" 
            onClick={() => {
              if (summary.connections.length > 0) {
                syncMutation.mutate()
                return
              }

              void refetch()
            }} 
            disabled={isRefetching || syncMutation.isPending}
            style={{ display: 'flex', alignItems: 'center', gap: '0.5rem' }}
          >
            <RefreshCcw size={16} className={isRefetching || syncMutation.isPending ? 'animate-spin' : ''} />
            {isRefetching || syncMutation.isPending ? 'Syncing...' : 'Force Sync'}
          </button>
        </div>
      </div>

      {showBootstrapNotice && (
        <div
          className="premium-glass"
          style={{ padding: '1rem 1.25rem', marginBottom: '1.5rem', borderLeft: '4px solid var(--primary)' }}
        >
          <div style={{ fontWeight: 700, marginBottom: '0.35rem' }}>Telemetry is still warming up.</div>
          <div style={{ color: 'var(--muted)', fontSize: '0.8125rem' }}>
            Draco has active cloud connections and synced inventory. Spend, anomaly, and workflow panels will expand as cost snapshots and events arrive.
          </div>
        </div>
      )}

      <div className="grid grid-cols-3" style={{ marginBottom: '3rem' }}>
        <div className="premium-glass" style={{ padding: '1.5rem', borderLeft: '4px solid var(--primary)' }}>
          <div style={{ display: 'flex', justifyContent: 'space-between', marginBottom: '1rem' }}>
            <span className="micro-label">Infrastructure</span>
            <Shield size={16} color="var(--muted)" />
          </div>
          <div style={{ fontSize: '2.5rem', fontWeight: 800, letterSpacing: '-0.02em' }}>{summary.overview.resourceCount}</div>
          <div style={{ fontSize: '0.8125rem', color: 'var(--muted)', marginTop: '0.5rem' }}>
            Across {summary.overview.providerCount} providers and {summary.overview.subscriptionCount} subscriptions
          </div>
        </div>
        
        <div className="premium-glass" style={{ padding: '1.5rem' }}>
          <div style={{ display: 'flex', justifyContent: 'space-between', marginBottom: '1rem' }}>
            <span className="micro-label">{hasCostData ? 'Current Spend' : 'Optimization Leads'}</span>
            <Zap size={16} color="var(--muted)" />
          </div>
          {hasCostData ? (
            <>
              <div style={{ fontSize: '2.5rem', fontWeight: 800, letterSpacing: '-0.02em' }}>${summary.overview.currentMonthlyCost.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 })}</div>
              <div style={{ fontSize: '0.8125rem', color: '#00e600', marginTop: '0.5rem', fontWeight: 600 }}>
                ${summary.overview.potentialMonthlySavings.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 })} monthly potential savings
              </div>
            </>
          ) : (
            <>
              <div style={{ 
                fontSize: '2.5rem', 
                fontWeight: 800, 
                letterSpacing: '-0.02em',
                color: summary.overview.recommendationCount > 5 ? '#00e600' : 
                       summary.overview.recommendationCount > 0 ? '#fbbf24' : '#f87171'
              }}>
                {summary.overview.recommendationCount}
              </div>
              <div style={{ fontSize: '0.8125rem', color: 'var(--muted)', marginTop: '0.5rem' }}>
                {summary.overview.connectionCount} connected environments awaiting cost snapshots
              </div>
            </>
          )}
        </div>

        <div className="premium-glass" style={{ padding: '1.5rem' }}>
          <div style={{ display: 'flex', justifyContent: 'space-between', marginBottom: '1rem' }}>
            <span className="micro-label">Active Signals</span>
            <AlertCircle size={16} color="var(--muted)" />
          </div>
          <div style={{ fontSize: '2.5rem', fontWeight: 800, letterSpacing: '-0.02em' }}>{summary.overview.anomalyCount}</div>
          <div style={{ fontSize: '0.8125rem', color: 'var(--muted)', marginTop: '0.5rem' }}>
            {summary.overview.openAlertCount} pending remediation tasks
          </div>
        </div>
      </div>

      <div className="grid grid-cols-2" style={{ gap: '2rem' }}>
        <div>
          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '1rem' }}>
            <h3 className="micro-label" style={{ color: 'var(--foreground)' }}>{hasCostData ? 'Cost Distribution' : 'Provider Footprint'}</h3>
          </div>
          <div className="card" style={{ padding: '1.5rem', height: '320px', display: 'flex', alignItems: 'center' }}>
            {costChartData.length > 0 ? (
              <BarChart data={costChartData} height={250} color="var(--primary)" label={chartLabel} />
            ) : (
              <div style={{ color: 'var(--muted)', textAlign: 'center', width: '100%' }}>
                {summary.connections.length > 0 ? 'Waiting for the first cloud sync result.' : 'Connect a provider to populate the Command Center.'}
              </div>
            )}
          </div>
        </div>

        <div>
          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '1rem' }}>
            <h3 className="micro-label" style={{ color: 'var(--foreground)' }}>Anomaly Stream</h3>
            <button className="nav-link" style={{ fontSize: '0.7rem' }}>View All</button>
          </div>
          <div className="operational-surface" style={{ maxHeight: '320px', overflowY: 'auto' }}>
            {anomalyItems.length > 0 ? anomalyItems.slice(0, 6).map((item) => (
              <div key={item.id} className="operational-row" style={{ display: 'flex', alignItems: 'center', gap: '1rem', padding: '0.75rem 1rem' }}>
                <span style={{ color: item.severity === 'Critical' ? 'var(--primary)' : 'var(--muted)' }}>
                  <AlertCircle size={14} />
                </span>
                <div style={{ flex: 1 }}>
                  <div style={{ fontWeight: 600, fontSize: '0.8125rem' }}>{item.title}</div>
                  <div style={{ fontSize: '0.75rem', color: 'var(--muted)', whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis', maxWidth: '300px' }}>{item.summary}</div>
                </div>
                <span className="badge" style={{ 
                  fontSize: '0.6rem', 
                  background: item.severity === 'Critical' ? 'rgba(255,0,0,0.1)' : 'rgba(255,255,255,0.05)', 
                  color: item.severity === 'Critical' ? 'var(--primary)' : 'var(--muted-foreground)',
                  padding: '2px 6px'
                }}>{item.severity}</span>
              </div>
            )) : (
              <div style={{ padding: '2rem', textAlign: 'center', color: 'var(--muted)' }}>No active anomalies detected.</div>
            )}
          </div>
        </div>
      </div>

      <div style={{ marginTop: '2rem' }}>
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '1rem' }}>
          <h3 className="micro-label" style={{ color: 'var(--foreground)' }}>Resource Group Cost</h3>
        </div>
        <div className="card" style={{ padding: '1.5rem', height: '360px', display: 'flex', alignItems: 'center' }}>
          {resourceGroupChartData.length > 0 ? (
            <BarChart data={resourceGroupChartData.slice(0, 20)} height={300} color="var(--primary)" label="Spend by Resource Group" />
          ) : (
            <div style={{ color: 'var(--muted)', textAlign: 'center', width: '100%' }}>
              {summary.connections.length > 0 ? 'Waiting for resource group cost allocations.' : 'Connect a provider to generate cost rollups.'}
            </div>
          )}
        </div>
      </div>

    </div>
  )
}
