import { createRoute, Link } from '@tanstack/react-router'
import { useMemo, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { ArrowLeft, CalendarRange, ReceiptText, Wrench } from 'lucide-react'
import { Route as rootRoute } from './__root'
import { formatCurrencyAmount, dracoApi, type ResourceActionDefinition, type ResourceActionExecutionResult } from '../lib/api'
import { Tabs, TabsContent, TabsList, TabsTrigger } from '../components/ui/tabs'
import { Spinner } from '../components/ui/spinner'
import { useIsMobile } from '../hooks/useIsMobile'

export const ResourceDetailRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/resources/$resourceId',
  component: ResourceDetailPage,
})

function ResourceDetailPage() {
  const { resourceId } = ResourceDetailRoute.useParams()
  const queryClient = useQueryClient()
  const isMobile = useIsMobile()
  const [selectedMetricName, setSelectedMetricName] = useState<string>('')
  const [lastActionResult, setLastActionResult] = useState<ResourceActionExecutionResult | null>(null)

  const { data: resourceInsights, isLoading, isFetching } = useQuery({
    queryKey: ['resource-insights', resourceId],
    queryFn: () => dracoApi.resources.getInsights(resourceId),
    enabled: !!resourceId,
    refetchInterval: 30000,
    refetchOnWindowFocus: true,
  })

  const executeActionMutation = useMutation({
    mutationFn: ({ action }: { action: string }) => dracoApi.resources.executeAction(resourceId, action),
    onSuccess: async (result) => {
      setLastActionResult(result)
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ['resource-insights', resourceId] }),
        queryClient.invalidateQueries({ queryKey: ['resource-detail', resourceId] }),
        queryClient.invalidateQueries({ queryKey: ['resources'] }),
        queryClient.invalidateQueries({ queryKey: ['dashboard-summary'] }),
      ])
    },
  })

  const costHistory = resourceInsights?.costHistory ?? []
  const metrics = resourceInsights?.metrics ?? []
  const metricNames = useMemo(
    () => [...new Set(metrics.map((metric) => metric.metricName || metric.name).filter(Boolean) as string[])],
    [metrics],
  )

  const activeMetricName = selectedMetricName || metricNames[0] || ''
  const activeMetricSeries = useMemo(
    () => metrics
      .filter((metric) => (metric.metricName || metric.name) === activeMetricName)
      .slice()
      .sort((left, right) => new Date(left.timestamp).getTime() - new Date(right.timestamp).getTime()),
    [activeMetricName, metrics],
  )

  const averageMonthlyCost = resourceInsights?.costBaseline.averageMonthlyCost ?? null
  const spikingMonths = useMemo(() => {
    if (!averageMonthlyCost || averageMonthlyCost <= 0) {
      return []
    }

    return costHistory.filter((point) => point.amount >= averageMonthlyCost * 1.25)
  }, [averageMonthlyCost, costHistory])

  const resource = resourceInsights?.resource
  const currency = resourceInsights?.cost?.currency || costHistory[0]?.currency || 'USD'

  const handleExecuteAction = (actionDefinition: ResourceActionDefinition) => {
    if (!resource) {
      return
    }

    if (actionDefinition.isDestructive) {
      const confirmed = window.confirm(`Delete ${resource.name}? Draco will generate Terraform files and execute the ${actionDefinition.action} action against Azure.`)
      if (!confirmed) {
        return
      }
    }

    setLastActionResult(null)
    executeActionMutation.mutate({ action: actionDefinition.action })
  }

  if (isLoading) {
    return (
      <div className="animate-fade-in" style={{ padding: '2rem' }}>
        <div className="operational-surface" style={{ padding: '1.25rem', color: 'var(--muted)' }}>
          Loading resource details...
        </div>
      </div>
    )
  }

  if (!resourceInsights || !resource) {
    return (
      <div className="animate-fade-in" style={{ padding: '2rem' }}>
        <div className="operational-surface" style={{ padding: '1.25rem', color: 'var(--muted)' }}>
          Resource details are unavailable right now.
        </div>
      </div>
    )
  }

  return (
    <div className="animate-fade-in resource-detail-page" style={{ display: 'flex', flexDirection: 'column', gap: '1.5rem' }}>
      <div style={{ display: 'flex', flexDirection: isMobile ? 'column' : 'row', justifyContent: 'space-between', gap: '1rem', alignItems: 'flex-start' }}>
        <div style={{ display: 'grid', gap: '0.6rem' }}>
          <Link to="/resources" className="nav-link" style={{ display: 'inline-flex', alignItems: 'center', gap: '0.5rem', width: 'fit-content', fontSize: isMobile ? '0.72rem' : undefined }}>
            <ArrowLeft size={14} />
            Back to resources
          </Link>
          <div>
            <h2 style={{ fontSize: isMobile ? '1.15rem' : '1.9rem', fontWeight: 900, letterSpacing: '-0.03em', margin: 0 }}>{resource.name}</h2>
            <p style={{ color: 'var(--muted)', marginTop: '0.35rem' }}>
              {resource.provider} • {resource.type} • {resource.resourceGroupName || 'Ungrouped'} • {resource.location || 'n/a'}
            </p>
          </div>
        </div>
        <div className="premium-glass" style={{ padding: '1rem 1.15rem', minWidth: isMobile ? '100%' : '220px', width: isMobile ? '100%' : undefined }}>
          <div className="micro-label" style={{ marginBottom: '0.4rem' }}>Current Month</div>
          <div style={{ fontSize: isMobile ? '1rem' : '1.8rem', fontWeight: 900 }}>
            {formatCurrencyAmount(resourceInsights.costBaseline.currentMonthCost, currency)}
          </div>
          <div style={{ color: 'var(--muted)', fontSize: '0.75rem', marginTop: '0.35rem' }}>
            {resourceInsights.cost?.capturedAt
              ? `Updated ${formatTimestamp(resourceInsights.cost.capturedAt)}`
              : 'Latest cost snapshot'}
          </div>
        </div>
      </div>

      <div className="grid grid-cols-4" style={{ gap: '1rem' }}>
        <InsightCard
          label="Monthly Average"
          value={averageMonthlyCost ? formatCurrencyAmount(averageMonthlyCost, currency) : 'Need 3+ months'}
          note={`${resourceInsights.costBaseline.sampleMonthCount} month sample`}
        />
        <InsightCard
          label="Current vs Average"
          value={formatPercent(resourceInsights.costBaseline.currentVsAveragePercentage)}
          note="Percent of normal monthly cost already reached"
        />
        <InsightCard
          label="Projected Month End"
          value={resourceInsights.costBaseline.projectedMonthlyCost ? formatCurrencyAmount(resourceInsights.costBaseline.projectedMonthlyCost, currency) : 'Not available'}
          note="Run-rate projection from the current month"
        />
        <InsightCard
          label="Projected vs Average"
          value={formatPercent(resourceInsights.costBaseline.projectedVsAveragePercentage)}
          note="Expected finish compared to baseline"
        />
      </div>

      <Tabs defaultValue="overview" style={{ display: 'flex', flexDirection: 'column', gap: '1rem' }}>
        <TabsList className="premium-glass resource-tabs-list" style={{ width: isMobile ? '100%' : 'fit-content', padding: '0.3rem', gap: '0.25rem', justifyContent: isMobile ? 'flex-start' : 'center', overflowX: isMobile ? 'auto' : 'visible' }}>
          <TabsTrigger value="overview">Overview</TabsTrigger>
          <TabsTrigger value="costs">Costs</TabsTrigger>
          <TabsTrigger value="usage">Usage</TabsTrigger>
          <TabsTrigger value="terraform">Terraform</TabsTrigger>
        </TabsList>

        <TabsContent value="overview" style={{ display: 'grid', gap: '1rem' }}>
          <div className="grid grid-cols-2" style={{ gap: '1rem' }}>
            <ChartCard
              title="Monthly Cost Trend"
              subtitle="Historical monthly running totals for this resource"
              icon={<CalendarRange size={16} color="var(--muted)" />}
            >
              <CostTrendChart points={costHistory} currency={currency} />
            </ChartCard>
            <div className="operational-surface" style={{ padding: '1rem', display: 'grid', gap: '1rem' }}>
              <SectionHeader title="Resource Context" subtitle="Fast operational context for this asset." />
              <DetailRow label="Subscription" value={resource.subscriptionId} mono />
              <DetailRow label="Resource Group" value={resource.resourceGroupName || 'Ungrouped'} />
              <DetailRow label="Provider Total" value={formatCurrencyAmount(resourceInsights.costContext?.providerTotal ?? 0, currency)} />
              <DetailRow label="Resource Group Total" value={formatCurrencyAmount(resourceInsights.costContext?.resourceGroupTotal ?? 0, currency)} />
              <DetailRow label="Discovered" value={formatTimestamp(resource.discoveredAt)} />
              <DetailRow
                label="Billing Period"
                value={resourceInsights.cost?.periodStart
                  ? `${formatMonthLabel(resourceInsights.cost.periodStart)} to ${formatMonthLabel(resourceInsights.cost.periodEnd || resourceInsights.cost.periodStart)}`
                  : 'Current month allocation'}
              />
            </div>
          </div>

          <div className="grid grid-cols-2" style={{ gap: '1rem' }}>
            <div className="operational-surface" style={{ padding: '1rem', display: 'grid', gap: '0.85rem' }}>
              <SectionHeader title="Savings Signals" subtitle="Highest-value recommendations for this asset." />
              {resourceInsights.recommendations.length > 0 ? (
                resourceInsights.recommendations.slice(0, 4).map((recommendation) => (
                  <div key={recommendation.id} style={{ border: '1px solid var(--border)', borderRadius: 'var(--radius-md)', padding: '0.85rem' }}>
                    <div style={{ display: 'flex', justifyContent: 'space-between', gap: '1rem', alignItems: 'center' }}>
                      <div style={{ fontWeight: 700 }}>{recommendation.recommendationType}</div>
                      <span className="badge">{formatCurrencyAmount(recommendation.potentialSavings, recommendation.currency)}</span>
                    </div>
                    <div style={{ color: 'var(--muted)', fontSize: '0.78rem', marginTop: '0.45rem' }}>{recommendation.description}</div>
                  </div>
                ))
              ) : (
                <EmptyMessage message="No optimization recommendations are available yet." />
              )}
            </div>

            <div className="operational-surface" style={{ padding: '1rem', display: 'grid', gap: '0.85rem' }}>
              <SectionHeader title="Cost Spikes" subtitle="Months that materially exceeded the recent baseline." />
              {spikingMonths.length > 0 ? (
                spikingMonths.map((point) => (
                  <div key={point.id} style={{ display: 'flex', justifyContent: 'space-between', gap: '1rem', padding: '0.8rem 0', borderBottom: '1px solid var(--border)' }}>
                    <div>
                      <div style={{ fontWeight: 700 }}>{formatMonthLabel(point.periodStart)}</div>
                      <div style={{ color: 'var(--muted)', fontSize: '0.75rem' }}>{point.costSource}</div>
                    </div>
                    <div style={{ textAlign: 'right' }}>
                      <div style={{ fontWeight: 800 }}>{formatCurrencyAmount(point.amount, point.currency)}</div>
                      <div style={{ color: 'var(--primary)', fontSize: '0.75rem' }}>
                        {averageMonthlyCost ? `${Math.round((point.amount / averageMonthlyCost) * 100)}% of average` : 'Above baseline'}
                      </div>
                    </div>
                  </div>
                ))
              ) : (
                <EmptyMessage message="No months are currently flagged as baseline spikes." />
              )}
            </div>
          </div>
        </TabsContent>

        <TabsContent value="costs" style={{ display: 'grid', gap: '1rem' }}>
          <div className="grid grid-cols-2" style={{ gap: '1rem' }}>
            <ChartCard
              title="Monthly Cost History"
              subtitle="Average baseline and historical month-by-month totals."
              icon={<ReceiptText size={16} color="var(--muted)" />}
            >
              <CostTrendChart points={costHistory} currency={currency} baseline={averageMonthlyCost ?? undefined} />
            </ChartCard>
            <div className="operational-surface" style={{ padding: '1rem', display: 'grid', gap: '0.75rem' }}>
              <SectionHeader title="Monthly Breakdown" subtitle="Chronological cost history kept for this resource." />
              {costHistory.length > 0 ? (
                costHistory.slice().reverse().map((point) => (
                  <div key={point.id} style={{ display: 'flex', justifyContent: 'space-between', gap: '1rem', padding: '0.75rem 0', borderBottom: '1px solid var(--border)' }}>
                    <div>
                      <div style={{ fontWeight: 700 }}>{formatMonthLabel(point.periodStart)}</div>
                      <div style={{ color: 'var(--muted)', fontSize: '0.75rem' }}>
                        Captured {formatTimestamp(point.capturedAt)}
                      </div>
                    </div>
                    <div style={{ textAlign: 'right' }}>
                      <div style={{ fontWeight: 800 }}>{formatCurrencyAmount(point.amount, point.currency)}</div>
                      <div style={{ color: 'var(--muted)', fontSize: '0.75rem' }}>{point.costSource}</div>
                    </div>
                  </div>
                ))
              ) : (
                <EmptyMessage message="No historical monthly cost records are available yet." />
              )}
            </div>
          </div>
        </TabsContent>

        <TabsContent value="usage" style={{ display: 'grid', gap: '1rem' }}>
          <div className="operational-surface" style={{ padding: '1rem', display: 'grid', gap: '1rem' }}>
            <div style={{ display: 'flex', justifyContent: 'space-between', gap: '1rem', alignItems: 'center' }}>
              <SectionHeader title="Usage Telemetry" subtitle="Recent metric samples that Draco captured for this resource." />
              {metricNames.length > 0 && (
                <select
                  value={activeMetricName}
                  onChange={(event) => setSelectedMetricName(event.target.value)}
                  style={{
                    padding: '0.6rem 0.8rem',
                    borderRadius: 'var(--radius-md)',
                    border: '1px solid var(--border)',
                    background: 'var(--card)',
                    color: 'var(--foreground)',
                    minWidth: '180px',
                  }}
                >
                  {metricNames.map((metricName) => (
                    <option key={metricName} value={metricName}>{metricName}</option>
                  ))}
                </select>
              )}
            </div>

            {activeMetricSeries.length > 0 ? (
              <>
                <MetricTrendChart points={activeMetricSeries} />
                <div className="grid grid-cols-3" style={{ gap: '1rem' }}>
                  <InsightCard
                    label="Latest Sample"
                    value={formatMetricValue(activeMetricSeries[activeMetricSeries.length - 1])}
                    note={formatTimestamp(activeMetricSeries[activeMetricSeries.length - 1].timestamp)}
                  />
                  <InsightCard
                    label="Peak Sample"
                    value={formatMetricValue([...activeMetricSeries].sort((left, right) => (right.value ?? right.metricValue ?? 0) - (left.value ?? left.metricValue ?? 0))[0])}
                    note={activeMetricName}
                  />
                  <InsightCard
                    label="Samples"
                    value={String(activeMetricSeries.length)}
                    note="Captured points for the selected metric"
                  />
                </div>
              </>
            ) : (
              <EmptyMessage message="No usage samples are available yet for this resource." />
            )}
          </div>
        </TabsContent>

        <TabsContent value="terraform" style={{ display: 'grid', gap: '1rem' }}>
          <div className="operational-surface" style={{ padding: '1rem', display: 'grid', gap: '1rem' }}>
            <SectionHeader title="Terraform Actions" subtitle="Run supported actions and review the action history for this resource." />

            {resourceInsights.availableActions.length > 0 ? (
              <div style={{ display: 'flex', flexWrap: 'wrap', gap: '0.75rem' }}>
                {resourceInsights.availableActions.map((actionDefinition) => (
                  <button
                    key={actionDefinition.action}
                    type="button"
                    onClick={() => handleExecuteAction(actionDefinition)}
                    disabled={executeActionMutation.isPending}
                    style={{
                      display: 'inline-flex',
                      alignItems: 'center',
                      gap: '0.45rem',
                      padding: '0.7rem 0.9rem',
                      borderRadius: 'var(--radius-md)',
                      border: `1px solid ${actionDefinition.isDestructive ? 'rgba(248, 113, 113, 0.45)' : 'var(--border)'}`,
                      background: actionDefinition.isDestructive ? 'rgba(248, 113, 113, 0.08)' : 'var(--secondary)',
                      color: actionDefinition.isDestructive ? '#fecaca' : 'inherit',
                      opacity: executeActionMutation.isPending ? 0.7 : 1,
                    }}
                  >
                    {executeActionMutation.isPending && executeActionMutation.variables?.action === actionDefinition.action ? (
                      <Spinner size={14} className="text-current" />
                    ) : (
                      <Wrench size={14} />
                    )}
                    {actionDefinition.label}
                  </button>
                ))}
              </div>
            ) : (
              <EmptyMessage message="No Terraform-backed actions are available for this resource type yet." />
            )}

            {executeActionMutation.isError && (
              <div style={{ color: '#f87171', fontSize: '0.8rem' }}>
                {(executeActionMutation.error as Error).message}
              </div>
            )}

            {(lastActionResult || resourceInsights.actionAudits.length > 0) && (
              <div style={{ display: 'grid', gap: '0.75rem' }}>
                {lastActionResult && (
                  <ActionAuditCard
                    title={lastActionResult.action}
                    status={lastActionResult.status}
                    description={lastActionResult.errorOutput || lastActionResult.output}
                    createdAt={lastActionResult.startedAt}
                    completedAt={lastActionResult.completedAt}
                    responseStatusCode={lastActionResult.responseStatusCode}
                  />
                )}
                {resourceInsights.actionAudits.map((audit) => (
                  <ActionAuditCard
                    key={audit.id}
                    title={audit.actionType}
                    status={audit.status}
                    description={audit.errorMessage || audit.description}
                    createdAt={audit.createdAt}
                    completedAt={audit.completedAt}
                  />
                ))}
              </div>
            )}
          </div>
        </TabsContent>
      </Tabs>

      {isFetching && (
        <div style={{ color: 'var(--muted)', fontSize: '0.75rem' }}>
          Refreshing resource insights...
        </div>
      )}
    </div>
  )
}

function InsightCard({ label, value, note }: { label: string; value: string; note: string }) {
  const isMobile = useIsMobile()
  return (
    <div className="premium-glass" style={{ padding: '1rem' }}>
      <div className="micro-label" style={{ marginBottom: '0.35rem' }}>{label}</div>
      <div style={{ fontSize: isMobile ? '0.9rem' : '1.35rem', fontWeight: 800 }}>{value}</div>
      <div style={{ color: 'var(--muted)', fontSize: '0.75rem', marginTop: '0.35rem' }}>{note}</div>
    </div>
  )
}

function SectionHeader({ title, subtitle }: { title: string; subtitle: string }) {
  return (
    <div>
      <div style={{ fontWeight: 800, fontSize: '0.95rem' }}>{title}</div>
      <div style={{ color: 'var(--muted)', fontSize: '0.78rem', marginTop: '0.25rem' }}>{subtitle}</div>
    </div>
  )
}

function DetailRow({ label, value, mono = false }: { label: string; value: string; mono?: boolean }) {
  return (
    <div>
      <div style={{ color: 'var(--muted)', fontSize: '0.72rem' }}>{label}</div>
      <div style={{ fontSize: '0.82rem', wordBreak: 'break-word', fontFamily: mono ? 'var(--font-mono)' : 'var(--font-sans)' }}>{value}</div>
    </div>
  )
}

function EmptyMessage({ message }: { message: string }) {
  return (
    <div style={{ color: 'var(--muted)', fontSize: '0.82rem', padding: '0.6rem 0' }}>
      {message}
    </div>
  )
}

function ChartCard({
  title,
  subtitle,
  icon,
  children,
}: {
  title: string
  subtitle: string
  icon: React.ReactNode
  children: React.ReactNode
}) {
  return (
    <div className="operational-surface" style={{ padding: '1rem', display: 'grid', gap: '1rem' }}>
      <div style={{ display: 'flex', alignItems: 'center', gap: '0.6rem' }}>
        {icon}
        <div>
          <div style={{ fontWeight: 800 }}>{title}</div>
          <div style={{ color: 'var(--muted)', fontSize: '0.78rem' }}>{subtitle}</div>
        </div>
      </div>
      {children}
    </div>
  )
}

function CostTrendChart({
  points,
  currency,
  baseline,
}: {
  points: Array<{ id: string; amount: number; periodStart: string }>
  currency: string
  baseline?: number
}) {
  if (points.length === 0) {
    return <EmptyMessage message="No monthly history is available yet." />
  }

  const width = 560
  const height = 220
  const padding = 24
  const values = points.map((point) => point.amount)
  const maxValue = Math.max(...values, baseline ?? 0, 1)
  const xStep = points.length > 1 ? (width - padding * 2) / (points.length - 1) : 0
  const polylinePoints = points
    .map((point, index) => {
      const x = padding + index * xStep
      const y = height - padding - (point.amount / maxValue) * (height - padding * 2)
      return `${x},${y}`
    })
    .join(' ')

  const baselineY = baseline
    ? height - padding - (baseline / maxValue) * (height - padding * 2)
    : null

  return (
    <div style={{ display: 'grid', gap: '0.8rem' }}>
      <svg viewBox={`0 0 ${width} ${height}`} style={{ width: '100%', height: '220px', overflow: 'visible' }}>
        <line x1={padding} y1={height - padding} x2={width - padding} y2={height - padding} stroke="var(--border)" strokeWidth="1" />
        <polyline
          fill="none"
          stroke="var(--primary)"
          strokeWidth="3"
          points={polylinePoints}
          strokeLinejoin="round"
          strokeLinecap="round"
        />
        {baselineY !== null && (
          <line
            x1={padding}
            y1={baselineY}
            x2={width - padding}
            y2={baselineY}
            stroke="var(--muted-foreground)"
            strokeDasharray="6 6"
            strokeWidth="1.5"
          />
        )}
        {points.map((point, index) => {
          const x = padding + index * xStep
          const y = height - padding - (point.amount / maxValue) * (height - padding * 2)
          return (
            <g key={point.id}>
              <circle cx={x} cy={y} r="4" fill="var(--primary)" />
              <text x={x} y={height - 6} textAnchor="middle" fontSize="11" fill="var(--muted-foreground)">
                {formatMonthLabel(point.periodStart)}
              </text>
            </g>
          )
        })}
      </svg>
      <div style={{ display: 'flex', justifyContent: 'space-between', gap: '1rem', fontSize: '0.75rem', color: 'var(--muted)' }}>
        <span>Peak {formatCurrencyAmount(maxValue, currency)}</span>
        {baseline ? <span>Baseline {formatCurrencyAmount(baseline, currency)}</span> : <span>Need 3 months for a baseline</span>}
      </div>
    </div>
  )
}

function MetricTrendChart({
  points,
}: {
  points: Array<{ id: string; timestamp: string; unit?: string; value?: number; metricValue?: number }>
}) {
  if (points.length === 0) {
    return <EmptyMessage message="No metric samples are available yet." />
  }

  const width = 560
  const height = 220
  const padding = 24
  const values = points.map((point) => point.value ?? point.metricValue ?? 0)
  const maxValue = Math.max(...values, 1)
  const xStep = points.length > 1 ? (width - padding * 2) / (points.length - 1) : 0
  const polylinePoints = points
    .map((point, index) => {
      const value = point.value ?? point.metricValue ?? 0
      const x = padding + index * xStep
      const y = height - padding - (value / maxValue) * (height - padding * 2)
      return `${x},${y}`
    })
    .join(' ')

  return (
    <svg viewBox={`0 0 ${width} ${height}`} style={{ width: '100%', height: '220px', overflow: 'visible' }}>
      <line x1={padding} y1={height - padding} x2={width - padding} y2={height - padding} stroke="var(--border)" strokeWidth="1" />
      <polyline
        fill="none"
        stroke="#ff8a00"
        strokeWidth="3"
        points={polylinePoints}
        strokeLinejoin="round"
        strokeLinecap="round"
      />
      {points.map((point, index) => {
        const value = point.value ?? point.metricValue ?? 0
        const x = padding + index * xStep
        const y = height - padding - (value / maxValue) * (height - padding * 2)
        return <circle key={point.id} cx={x} cy={y} r="3" fill="#ff8a00" />
      })}
    </svg>
  )
}

function ActionAuditCard({
  title,
  status,
  description,
  createdAt,
  completedAt,
  responseStatusCode,
}: {
  title: string
  status: string
  description?: string
  createdAt: string
  completedAt?: string
  responseStatusCode?: number
}) {
  return (
    <div style={{ border: '1px solid var(--border)', borderRadius: 'var(--radius-md)', padding: '0.9rem', display: 'grid', gap: '0.45rem' }}>
      <div style={{ display: 'flex', justifyContent: 'space-between', gap: '1rem', alignItems: 'center' }}>
        <div style={{ fontWeight: 800 }}>{title}</div>
        <span className="badge" style={{ borderColor: getActionStatusColor(status), color: getActionStatusColor(status) }}>{status}</span>
      </div>
      {description && (
        <div style={{ color: 'var(--muted)', fontSize: '0.76rem', whiteSpace: 'pre-wrap' }}>{description}</div>
      )}
      <div style={{ color: 'var(--muted)', fontSize: '0.74rem' }}>
        Started {formatTimestamp(createdAt)}
        {completedAt ? ` • Completed ${formatTimestamp(completedAt)}` : ''}
        {typeof responseStatusCode === 'number' ? ` • HTTP ${responseStatusCode}` : ''}
      </div>
    </div>
  )
}

function formatTimestamp(value?: string) {
  if (!value) {
    return 'Not available'
  }

  return new Date(value).toLocaleString([], {
    month: 'short',
    day: 'numeric',
    hour: 'numeric',
    minute: '2-digit',
  })
}

function formatMonthLabel(value?: string) {
  if (!value) {
    return 'Unknown'
  }

  return new Date(value).toLocaleDateString([], {
    month: 'short',
    year: '2-digit',
  })
}

function formatPercent(value?: number | null) {
  return typeof value === 'number' ? `${value.toFixed(1)}%` : 'Not available'
}

function formatMetricValue(point?: { unit?: string; value?: number; metricValue?: number }) {
  const value = point?.value ?? point?.metricValue
  if (typeof value !== 'number') {
    return 'No data'
  }

  return `${value.toFixed(2)}${point?.unit ? ` ${point.unit}` : ''}`
}

function getActionStatusColor(status?: string) {
  switch ((status ?? '').toLowerCase()) {
    case 'succeeded':
      return '#34d399'
    case 'inprogress':
    case 'pending':
      return '#fbbf24'
    case 'failed':
      return '#f87171'
    default:
      return 'var(--muted-foreground)'
  }
}
