import { createRoute } from '@tanstack/react-router'
import { Route as rootRoute } from './__root'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { dracoApi, formatCurrencyAmount, getCostSourceColor, getCostSourceLabel, type ResourceActionDefinition, type ResourceActionExecutionResult } from '../lib/api'
import { useEffect, useMemo, useState } from 'react'
import { Activity, ArrowUpDown, Box, ChevronDown, ChevronUp, Database, Pause, Play, ReceiptText, RotateCcw, Search, Server, ShieldCheck, Sparkles, Trash2 } from 'lucide-react'
import {
  Drawer,
  DrawerClose,
  DrawerContent,
  DrawerDescription,
  DrawerFooter,
  DrawerHeader,
  DrawerTitle,
} from '../components/ui/drawer'
import { Spinner } from '../components/ui/spinner'

import { useNavigate } from '@tanstack/react-router'
import { X } from 'lucide-react'

type ResourcesSearch = {
  provider?: string
  resourceGroup?: string
  search?: string
}

export const ResourcesRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/resources',
  validateSearch: (search: Record<string, unknown>): ResourcesSearch => ({
    provider: search.provider as string | undefined,
    resourceGroup: search.resourceGroup as string | undefined,
    search: search.search as string | undefined,
  }),
  component: Resources,
})

function Resources() {
  const sentinelSearch = ResourcesRoute.useSearch()
  const navigate = useNavigate()
  const AUTO_SYNC_INTERVAL_MS = 120000
  const [search, setSearch] = useState(sentinelSearch.search || '')
  const [selectedResourceId, setSelectedResourceId] = useState<string | null>(null)
  const [selectedGroupKey, setSelectedGroupKey] = useState<string | null>(sentinelSearch.resourceGroup ? `*::*::${sentinelSearch.resourceGroup}` : null)
  const [isExcludedSectionVisible, setIsExcludedSectionVisible] = useState(false)
  const [currentPage, setCurrentPage] = useState(1)
  const ITEMS_PER_PAGE = 50
  const [lastActionResult, setLastActionResult] = useState<ResourceActionExecutionResult | null>(null)
  const [sortConfig, setSortConfig] = useState<{
    field: 'monthlyCost' | 'location' | 'provider' | null;
    direction: 'asc' | 'desc' | null;
  }>({ field: null, direction: null })
  const [hasInitializedAutoSync, setHasInitializedAutoSync] = useState(false)
  const queryClient = useQueryClient()
  const { data: resources } = useQuery({
    queryKey: ['resources'],
    queryFn: dracoApi.resources.list,
    refetchInterval: 30000,
    refetchOnWindowFocus: true,
  })
  const { data: connections = [] } = useQuery({
    queryKey: ['cloud-connections'],
    queryFn: dracoApi.cloudConnections.list,
    refetchInterval: 30000,
    refetchOnWindowFocus: true,
  })
  const { data: resourceDetail, isFetching: isFetchingResourceDetail } = useQuery({
    queryKey: ['resource-detail', selectedResourceId],
    queryFn: () => dracoApi.resources.getById(selectedResourceId!),
    enabled: !!selectedResourceId,
    refetchInterval: selectedResourceId ? 15000 : false,
    refetchOnWindowFocus: true,
  })
  const syncMutation = useMutation({
    mutationFn: async (mode: 'manual' | 'auto') => {
      const connectionIds = connections
        .filter((connection) => connection.isActive)
        .map((connection) => connection.id)

      if (connectionIds.length === 0) {
        return null
      }

      return {
        mode,
        result: await dracoApi.cloudConnections.sync(connectionIds),
      }
    },
    onSuccess: async () => {
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ['resources'] }),
        queryClient.invalidateQueries({ queryKey: ['resource-detail'] }),
        queryClient.invalidateQueries({ queryKey: ['dashboard-summary'] }),
        queryClient.invalidateQueries({ queryKey: ['cloud-connections'] }),
        queryClient.invalidateQueries({ queryKey: ['cost-budgets'] }),
      ])

      await Promise.all([
        queryClient.refetchQueries({ queryKey: ['resources'], type: 'active' }),
        queryClient.refetchQueries({ queryKey: ['resource-detail'], type: 'active' }),
        queryClient.refetchQueries({ queryKey: ['dashboard-summary'], type: 'active' }),
        queryClient.refetchQueries({ queryKey: ['cloud-connections'], type: 'active' }),
        queryClient.refetchQueries({ queryKey: ['cost-budgets'], type: 'active' }),
      ])
    },
  })
  const executeActionMutation = useMutation({
    mutationFn: ({ resourceId, action }: { resourceId: string; action: string }) =>
      dracoApi.resources.executeAction(resourceId, action),
    onSuccess: async (result, variables) => {
      setLastActionResult(result)
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ['resources'] }),
        queryClient.invalidateQueries({ queryKey: ['resource-detail', variables.resourceId] }),
        queryClient.invalidateQueries({ queryKey: ['dashboard-summary'] }),
      ])
    },
  })

  const filteredResources = useMemo(() => {
    let items = resources || []
    
    // Apply URL-based filters
    if (sentinelSearch.provider) {
      items = items.filter(r => r.provider.toLowerCase() === sentinelSearch.provider?.toLowerCase())
    }
    if (sentinelSearch.resourceGroup) {
      items = items.filter(r => (r.resourceGroupName || 'Ungrouped').toLowerCase() === sentinelSearch.resourceGroup?.toLowerCase())
    }

    if (!search.trim()) return items
    const query = search.trim().toLowerCase()
    return items.filter((resource) =>
      resource.name.toLowerCase().includes(query) ||
      resource.type.toLowerCase().includes(query) ||
      resource.provider.toLowerCase().includes(query) ||
      resource.location.toLowerCase().includes(query)
    )
  }, [resources, search, sentinelSearch])

  const isActualCostSource = (costSource?: string) =>
    costSource === 'AzureActual' || costSource === 'AwsActual'

  const preferredRollupResources = useMemo(() => {
    const groupedByProviderSubscription = new Map<string, typeof filteredResources>()

    for (const resource of filteredResources) {
      const key = `${resource.provider}::${resource.subscriptionId}`
      const existing = groupedByProviderSubscription.get(key)

      if (existing) {
        existing.push(resource)
        continue
      }

      groupedByProviderSubscription.set(key, [resource])
    }

    return [...groupedByProviderSubscription.values()].flatMap((group) => {
      const actualResources = group.filter((resource) => isActualCostSource(resource.costSource))
      return actualResources.length > 0 ? actualResources : group
    })
  }, [filteredResources])

  const preferredRollupIds = useMemo(
    () => new Set(preferredRollupResources.map((resource) => resource.id)),
    [preferredRollupResources],
  )

  const groupedResources = useMemo(() => {
    const normalizedResources = [...filteredResources].sort((left, right) => {
      if (sortConfig.field) {
        const dir = sortConfig.direction === 'asc' ? 1 : -1
        if (sortConfig.field === 'monthlyCost') {
          const costA = preferredRollupIds.has(left.id) ? left.monthlyCost : -1
          const costB = preferredRollupIds.has(right.id) ? right.monthlyCost : -1
          return (costA - costB) * dir
        }
        if (sortConfig.field === 'location') {
          return left.location.localeCompare(right.location) * dir
        }
        if (sortConfig.field === 'provider') {
          return left.provider.localeCompare(right.provider) * dir
        }
      }

      const providerComparison = left.provider.localeCompare(right.provider)
      if (providerComparison !== 0) return providerComparison

      const subscriptionComparison = left.subscriptionId.localeCompare(right.subscriptionId)
      if (subscriptionComparison !== 0) return subscriptionComparison

      const resourceGroupComparison = (left.resourceGroupName || 'Ungrouped').localeCompare(right.resourceGroupName || 'Ungrouped')
      if (resourceGroupComparison !== 0) return resourceGroupComparison

      const costA = preferredRollupIds.has(left.id) ? left.monthlyCost : -1
      const costB = preferredRollupIds.has(right.id) ? right.monthlyCost : -1
      if (costA !== costB) return costB - costA

      return left.name.localeCompare(right.name)
    })

    const groups = new Map<string, {
      key: string
      provider: string
      subscriptionId: string
      resourceGroupName: string
      currency: string
      totalMonthlyCost: number
      excludedEstimatedCost: number
      resources: typeof filteredResources
    }>()

    for (const resource of normalizedResources) {
      const resourceGroupName = resource.resourceGroupName || 'Ungrouped'
      const groupKey = `${resource.provider}::${resource.subscriptionId}::${resourceGroupName}`
      const existingGroup = groups.get(groupKey)
      const contributesToRollup = preferredRollupIds.has(resource.id)

      if (existingGroup) {
        existingGroup.resources.push(resource)
        if (contributesToRollup) {
          existingGroup.totalMonthlyCost += resource.monthlyCost
        } else {
          existingGroup.excludedEstimatedCost += resource.monthlyCost
        }
        continue
      }

      groups.set(groupKey, {
        key: groupKey,
        provider: resource.provider,
        subscriptionId: resource.subscriptionId,
        resourceGroupName,
        currency: resource.currency || 'USD',
        totalMonthlyCost: contributesToRollup ? resource.monthlyCost : 0,
        excludedEstimatedCost: contributesToRollup ? 0 : resource.monthlyCost,
        resources: [resource],
      })
    }

    const result = [...groups.values()]
    if (sortConfig.field) {
      const dir = sortConfig.direction === 'asc' ? 1 : -1
      result.sort((a, b) => {
        if (sortConfig.field === 'monthlyCost') {
          const costA = a.totalMonthlyCost > 0 || a.resources.some(r => preferredRollupIds.has(r.id)) ? a.totalMonthlyCost : -1
          const costB = b.totalMonthlyCost > 0 || b.resources.some(r => preferredRollupIds.has(r.id)) ? b.totalMonthlyCost : -1
          return (costA - costB) * dir
        }
        if (sortConfig.field === 'location') {
          const locA = a.resources[0]?.location || ''
          const locB = b.resources[0]?.location || ''
          return locA.localeCompare(locB) * dir
        }
        if (sortConfig.field === 'provider') {
          return a.provider.localeCompare(b.provider) * dir
        }
        return 0
      })
    } else {
      result.sort((a, b) => b.totalMonthlyCost - a.totalMonthlyCost)
    }

    return result
  }, [filteredResources, preferredRollupIds, sortConfig])

  const totalMonthlyCost = useMemo(
    () => preferredRollupResources.reduce((sum, resource) => sum + resource.monthlyCost, 0),
    [preferredRollupResources],
  )

  const providerCount = useMemo(
    () => new Set(filteredResources.map(resource => resource.provider)).size,
    [filteredResources],
  )


  const toggleSort = (field: 'monthlyCost' | 'location' | 'provider') => {
    if (sortConfig.field !== field) {
      setSortConfig({ field, direction: 'asc' })
    } else if (sortConfig.direction === 'asc') {
      setSortConfig({ field, direction: 'desc' })
    } else {
      setSortConfig({ field: null, direction: null })
    }
  }

  const renderSortIcon = (field: 'monthlyCost' | 'location' | 'provider') => {
    if (sortConfig.field !== field) return <ArrowUpDown size={12} style={{ opacity: 0.2, marginLeft: '0.4rem' }} />
    return sortConfig.direction === 'asc'
      ? <ChevronUp size={12} style={{ marginLeft: '0.4rem', color: 'var(--primary)' }} />
      : <ChevronDown size={12} style={{ marginLeft: '0.4rem', color: 'var(--primary)' }} />
  }

  const getIcon = (type: string) => {
    const normalized = type.toLowerCase()
    if (normalized.includes('database') || normalized.includes('postgres')) return <Database size={18} />
    if (normalized.includes('storage') || normalized.includes('blob') || normalized.includes('bucket')) return <Box size={18} />
    if (normalized.includes('compute') || normalized.includes('instance') || normalized.includes('server') || normalized.includes('vm')) return <Server size={18} />
    return <ShieldCheck size={18} />
  }

  const selectedListResource = useMemo(
    () => resources?.find(resource => resource.id === selectedResourceId) ?? null,
    [resources, selectedResourceId],
  )

  useEffect(() => {
    setLastActionResult(null)
  }, [selectedResourceId])

  useEffect(() => {
    const activeConnectionIds = connections
      .filter((connection) => connection.isActive)
      .map((connection) => connection.id)

    if (activeConnectionIds.length === 0 || hasInitializedAutoSync || syncMutation.isPending) {
      return
    }

    setHasInitializedAutoSync(true)
    syncMutation.mutate('auto')
  }, [connections, hasInitializedAutoSync, syncMutation])

  useEffect(() => {
    const activeConnectionIds = connections.filter((connection) => connection.isActive)
    if (activeConnectionIds.length === 0) {
      return
    }

    const intervalId = window.setInterval(() => {
      if (!syncMutation.isPending) {
        syncMutation.mutate('auto')
      }
    }, AUTO_SYNC_INTERVAL_MS)

    return () => window.clearInterval(intervalId)
  }, [connections, syncMutation, AUTO_SYNC_INTERVAL_MS])

  // Sync sidebar selection with URL filter
  useEffect(() => {
    if (sentinelSearch.resourceGroup) {
      const match = groupedResources.find(g => g.resourceGroupName.toLowerCase() === sentinelSearch.resourceGroup?.toLowerCase())
      if (match) {
        setSelectedGroupKey(match.key)
      }
    } else {
      setSelectedGroupKey(null)
    }
  }, [sentinelSearch.resourceGroup, groupedResources])

  const activeResource = resourceDetail?.resource ?? selectedListResource
  const activeCost = resourceDetail?.cost
  const activeCurrency = activeCost?.currency ?? selectedListResource?.currency ?? 'USD'
  const activeCostSource = activeCost?.costSource ?? selectedListResource?.costSource
  const hasLiveDetail = !!resourceDetail

  const formatTimestamp = (value?: string) => {
    if (!value) {
      return 'Not available yet'
    }

    return new Date(value).toLocaleString([], {
      month: 'short',
      day: 'numeric',
      hour: 'numeric',
      minute: '2-digit',
    })
  }

  const formatPeriodDate = (value?: string) => {
    if (!value) {
      return 'Current month'
    }

    return new Date(value).toLocaleDateString([], {
      month: 'short',
      day: 'numeric',
    })
  }

  const formatResourceAmount = (amount: number, currency = 'USD') => {
    try {
      const formattedAmount = new Intl.NumberFormat(undefined, {
        style: 'currency',
        currency,
        minimumFractionDigits: 2,
        maximumFractionDigits: 2,
      }).format(amount)

      return `${formattedAmount} ${currency}`
    } catch {
      return `$${amount.toFixed(2)} ${currency}`
    }
  }

  const getActionIcon = (action: string) => {
    switch (action.toLowerCase()) {
      case 'pause':
        return <Pause size={14} />
      case 'resume':
        return <Play size={14} />
      case 'restart':
        return <RotateCcw size={14} />
      case 'delete':
        return <Trash2 size={14} />
      default:
        return <Sparkles size={14} />
    }
  }

  const getActionStatusColor = (status?: string) => {
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

  const handleExecuteAction = (actionDefinition: ResourceActionDefinition) => {
    if (!selectedResourceId || !activeResource) {
      return
    }

    if (actionDefinition.isDestructive) {
      const confirmed = window.confirm(`Delete ${activeResource.name}? Draco will generate Terraform files and execute the ${actionDefinition.action} action against Azure.`)
      if (!confirmed) {
        return
      }
    }

    setLastActionResult(null)
    executeActionMutation.mutate({ resourceId: selectedResourceId, action: actionDefinition.action })
  }

  const sortedResources = useMemo(() => {
    let items = [...filteredResources]

    // If a group is selected, filter to only those resources
    if (selectedGroupKey) {
      items = items.filter(r => {
        const groupKey = `${r.provider}::${r.subscriptionId}::${r.resourceGroupName || 'Ungrouped'}`
        return groupKey === selectedGroupKey
      })
    }

    // Apply sorting
    items.sort((left, right) => {
      if (sortConfig.field) {
        const dir = sortConfig.direction === 'asc' ? 1 : -1
        if (sortConfig.field === 'monthlyCost') {
          const costA = preferredRollupIds.has(left.id) ? left.monthlyCost : -1
          const costB = preferredRollupIds.has(right.id) ? right.monthlyCost : -1
          return (costA - costB) * dir
        }
        if (sortConfig.field === 'location') {
          return left.location.localeCompare(right.location) * dir
        }
        if (sortConfig.field === 'provider') {
          return left.provider.localeCompare(right.provider) * dir
        }
      }

      // Default sort order (Provider -> Subscription -> Group -> Cost Desc)
      const providerComparison = left.provider.localeCompare(right.provider)
      if (providerComparison !== 0) return providerComparison

      const subscriptionComparison = left.subscriptionId.localeCompare(right.subscriptionId)
      if (subscriptionComparison !== 0) return subscriptionComparison

      const resourceGroupNameA = left.resourceGroupName || 'Ungrouped'
      const resourceGroupNameB = right.resourceGroupName || 'Ungrouped'
      const resourceGroupComparison = resourceGroupNameA.localeCompare(resourceGroupNameB)
      if (resourceGroupComparison !== 0) return resourceGroupComparison

      const costA = preferredRollupIds.has(left.id) ? left.monthlyCost : -1
      const costB = preferredRollupIds.has(right.id) ? right.monthlyCost : -1
      if (costA !== costB) return costB - costA

      return left.name.localeCompare(right.name)
    })

    return items
  }, [filteredResources, selectedGroupKey, sortConfig, preferredRollupIds])

  const actualResources = useMemo(() => sortedResources.filter(r => preferredRollupIds.has(r.id)), [sortedResources, preferredRollupIds])
  const excludedResources = useMemo(() => sortedResources.filter(r => !preferredRollupIds.has(r.id)), [sortedResources, preferredRollupIds])

  const paginatedActualResources = useMemo(() => {
    const start = (currentPage - 1) * ITEMS_PER_PAGE
    return actualResources.slice(start, start + ITEMS_PER_PAGE)
  }, [actualResources, currentPage])

  const totalPages = Math.max(1, Math.ceil(actualResources.length / ITEMS_PER_PAGE))

  useEffect(() => {
    setCurrentPage(1)
  }, [selectedGroupKey, search, sentinelSearch.provider, sentinelSearch.resourceGroup])

  return (
    <div className="animate-fade-in" style={{ fontFamily: 'Roboto, sans-serif' }}>
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-end', marginBottom: '2rem' }}>
        <div>
          <div style={{ display: 'flex', alignItems: 'center', gap: '1.5rem' }}>
            <h2 style={{ fontSize: '1.75rem', fontWeight: 900, letterSpacing: '-0.03em', margin: 0 }}>Cloud Fleet</h2>
            {(sentinelSearch.provider || sentinelSearch.resourceGroup) && (
              <div style={{ display: 'flex', gap: '0.5rem' }}>
                {sentinelSearch.provider && (
                  <span className="badge" style={{ background: 'var(--secondary)', color: 'var(--foreground)', border: '1px solid var(--border)', display: 'flex', alignItems: 'center', gap: '0.5rem', textTransform: 'none' }}>
                    {sentinelSearch.provider}
                    <X size={12} style={{ cursor: 'pointer' }} onClick={() => navigate({ to: '/resources', search: (prev: any) => ({ ...prev, provider: undefined }) })} />
                  </span>
                )}
                {sentinelSearch.resourceGroup && (
                  <span className="badge" style={{ background: 'var(--secondary)', color: 'var(--foreground)', border: '1px solid var(--border)', display: 'flex', alignItems: 'center', gap: '0.5rem', textTransform: 'none' }}>
                    {sentinelSearch.resourceGroup}
                    <X size={12} style={{ cursor: 'pointer' }} onClick={() => navigate({ to: '/resources', search: (prev: any) => ({ ...prev, resourceGroup: undefined }) })} />
                  </span>
                )}
              </div>
            )}
          </div>
          <p style={{ color: 'var(--muted)', fontSize: '0.8125rem', marginTop: '0.25rem' }}>Asset inventory and live billing telemetry.</p>
        </div>
        <div style={{ display: 'flex', alignItems: 'center', gap: '0.75rem' }}>
          <div className="premium-glass" style={{ display: 'flex', alignItems: 'center', padding: '0.4rem 0.75rem', gap: '0.5rem', width: '280px', borderRadius: 'var(--radius-md)' }}>
            <Search size={14} color="var(--muted-foreground)" />
            <input
              type="text"
              value={search}
              onChange={(event) => setSearch(event.target.value)}
              placeholder="Search assets..."
              style={{ background: 'transparent', border: 'none', color: 'white', outline: 'none', fontSize: '0.8125rem', width: '100%' }}
            />
          </div>
        </div>
      </div>

      {(syncMutation.isError || syncMutation.isSuccess) && (
        <div
          className="premium-glass"
          style={{
            padding: '0.85rem 1rem',
            marginBottom: '1rem',
            borderLeft: `4px solid ${syncMutation.isError ? '#f87171' : '#34d399'}`,
          }}
        >
          <div style={{ fontSize: '0.78rem', color: syncMutation.isError ? '#fecaca' : 'var(--muted-foreground)' }}>
            {syncMutation.isError
              ? (syncMutation.error as Error).message
              : syncMutation.data?.mode === 'manual'
                ? 'Resource inventory and billing telemetry refreshed from the cloud provider. Draco will keep auto-refreshing this page while it stays open.'
                : 'Draco automatically refreshed resource inventory and billing telemetry from the cloud provider.'}
          </div>
        </div>
      )}

      <div style={{ display: 'grid', gridTemplateColumns: '280px 1fr', gap: '2rem', alignItems: 'start' }}>
        {/* Left Sidebar: Group Browser */}
        <div style={{ position: 'sticky', top: '2rem', display: 'flex', flexDirection: 'column', gap: '1.5rem', maxHeight: 'calc(100vh - 4rem)' }}>
          <div style={{ display: 'flex', flexDirection: 'column', flex: 1, overflow: 'hidden' }}>
            <div className="micro-label" style={{ marginBottom: '1rem', opacity: 0.5 }}>Account Groups</div>
            <div style={{
              display: 'flex',
              flexDirection: 'column',
              gap: '0.4rem',
              overflowY: 'auto',
              paddingRight: '0.5rem',
              scrollbarWidth: 'thin',
              scrollbarColor: 'var(--border) transparent'
            }}>
              <button
                onClick={() => {
                  setSelectedGroupKey(null)
                  navigate({ to: '/resources', search: (prev: any) => ({ ...prev, resourceGroup: undefined }) })
                }}
                style={{
                  display: 'flex',
                  justifyContent: 'space-between',
                  alignItems: 'center',
                  padding: '0.75rem 1rem',
                  borderRadius: 'var(--radius-lg)',
                  background: selectedGroupKey === null ? 'var(--primary)' : 'rgba(255,255,255,0.03)',
                  color: selectedGroupKey === null ? '#ffffff' : 'inherit',
                  border: 'none',
                  cursor: 'pointer',
                  textAlign: 'left',
                  fontWeight: 700,
                  fontSize: '0.8125rem',
                  transition: 'all 0.2s cubic-bezier(0.4, 0, 0.2, 1)',
                  flexShrink: 0
                }}
              >
                <span>Full Environment</span>
                <span style={{ opacity: 0.7, fontSize: '0.7rem' }}>{filteredResources.length}</span>
              </button>

              {groupedResources.map(group => (
                <button
                  key={group.key}
                  onClick={() => {
                    setSelectedGroupKey(group.key)
                    navigate({ to: '/resources', search: (prev: any) => ({ ...prev, resourceGroup: group.resourceGroupName }) })
                  }}
                  style={{
                    display: 'flex',
                    flexDirection: 'column',
                    gap: '0.25rem',
                    padding: '0.75rem 1rem',
                    borderRadius: 'var(--radius-lg)',
                    background: selectedGroupKey === group.key ? 'rgba(255, 0, 0, 0.1)' : 'transparent',
                    border: `1px solid ${selectedGroupKey === group.key ? 'var(--primary)' : 'transparent'}`,
                    cursor: 'pointer',
                    textAlign: 'left',
                    transition: 'all 0.2s cubic-bezier(0.4, 0, 0.2, 1)',
                    flexShrink: 0
                  }}
                >
                  <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                    <span style={{ fontWeight: 800, fontSize: '0.75rem', color: selectedGroupKey === group.key ? 'var(--primary)' : 'inherit' }}>
                      {group.resourceGroupName.toUpperCase()}
                    </span>
                    <span className="badge" style={{ fontSize: '0.6rem', padding: '0 0.3rem', background: 'rgba(255,255,255,0.05)' }}>{group.provider}</span>
                  </div>
                  <div style={{ fontSize: '0.65rem', color: 'var(--muted)', display: 'flex', justifyContent: 'space-between' }}>
                    <span>{group.resources.length} assets</span>
                    <span style={{ fontWeight: 600 }}>{formatCurrencyAmount(group.totalMonthlyCost, group.currency)}</span>
                  </div>
                </button>
              ))}
            </div>
          </div>
        </div>

        {/* Right Area: Asset Table */}
        <div style={{ display: 'flex', flexDirection: 'column', gap: '1.5rem' }}>
          <div style={{ display: 'flex', gap: '1rem' }}>
            <div className="premium-glass" style={{ flex: 1, padding: '1rem' }}>
              <div className="micro-label" style={{ fontSize: '0.65rem', marginBottom: '0.4rem' }}>Monthly Burn</div>
              <div style={{ fontSize: '1.5rem', fontWeight: 900 }}>{formatCurrencyAmount(totalMonthlyCost)}</div>
            </div>
            <div className="premium-glass" style={{ flex: 1, padding: '1rem' }}>
              <div className="micro-label" style={{ fontSize: '0.65rem', marginBottom: '0.4rem' }}>Active Assets</div>
              <div style={{ fontSize: '1.5rem', fontWeight: 900 }}>{filteredResources.length}</div>
            </div>
            <div className="premium-glass" style={{ flex: 1, padding: '1rem' }}>
              <div className="micro-label" style={{ fontSize: '0.65rem', marginBottom: '0.4rem' }}>Environments</div>
              <div style={{ fontSize: '1.5rem', fontWeight: 900 }}>{providerCount}</div>
            </div>
          </div>

          <div className="operational-surface" style={{ overflow: 'hidden', border: '1px solid var(--border)', borderRadius: 'var(--radius-lg)' }}>
            <table style={{ width: '100%', borderCollapse: 'collapse', textAlign: 'left', fontSize: '0.8125rem' }}>
              <thead>
                <tr style={{ background: 'rgba(255,255,255,0.02)', borderBottom: '1px solid var(--border)' }}>
                  <th className="micro-label" style={{ padding: '0.6rem 0.75rem' }}>Asset & Type</th>
                  <th
                    className="micro-label"
                    style={{ padding: '0.6rem 0.75rem', cursor: 'pointer', userSelect: 'none' }}
                    onClick={() => toggleSort('provider')}
                  >
                    <div style={{ display: 'flex', alignItems: 'center' }}>
                      Provider {renderSortIcon('provider')}
                    </div>
                  </th>
                  <th
                    className="micro-label"
                    style={{ padding: '0.6rem 0.75rem', cursor: 'pointer', userSelect: 'none' }}
                    onClick={() => toggleSort('location')}
                  >
                    <div style={{ display: 'flex', alignItems: 'center' }}>
                      Region {renderSortIcon('location')}
                    </div>
                  </th>
                  <th
                    className="micro-label"
                    style={{ padding: '0.6rem 0.75rem', textAlign: 'right', cursor: 'pointer', userSelect: 'none' }}
                    onClick={() => toggleSort('monthlyCost')}
                  >
                    <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'flex-end' }}>
                      Monthly Cost {renderSortIcon('monthlyCost')}
                    </div>
                  </th>
                </tr>
              </thead>
              <tbody>
                {paginatedActualResources.map((resource) => (
                  <tr key={resource.id} className="operational-row" onClick={() => setSelectedResourceId(resource.id)} style={{ cursor: 'pointer' }}>
                    <td style={{ padding: '0.6rem 0.75rem' }}>
                      <div style={{ display: 'flex', alignItems: 'center', gap: '0.75rem' }}>
                        <div style={{ color: 'var(--muted-foreground)', flexShrink: 0 }}>{getIcon(resource.type)}</div>
                        <div style={{ display: 'flex', flexDirection: 'column' }}>
                          <span style={{ fontWeight: 700 }}>{resource.name}</span>
                          <span style={{ fontSize: '0.7rem', color: 'var(--muted)' }}>{resource.type}</span>
                        </div>
                      </div>
                    </td>
                    <td style={{ padding: '0.6rem 0.75rem' }}>
                      <span className="badge" style={{ background: 'rgba(255,255,255,0.05)', fontSize: '0.6rem', padding: '0.1rem 0.4rem' }}>{resource.provider}</span>
                    </td>
                    <td style={{ padding: '0.6rem 0.75rem', color: 'var(--muted-foreground)' }}>{resource.location}</td>
                    <td style={{ padding: '0.6rem 0.75rem', textAlign: 'right' }}>
                      <div style={{ fontWeight: 800 }}>
                        {formatResourceAmount(resource.monthlyCost, resource.currency)}
                      </div>
                    </td>
                  </tr>
                ))}
                {paginatedActualResources.length === 0 && (
                  <tr>
                    <td colSpan={4} style={{ padding: '3rem', textAlign: 'center', color: 'var(--muted)' }}>No resources found with identified cost values.</td>
                  </tr>
                )}
              </tbody>
            </table>
          </div>

          {totalPages > 1 && (
            <div style={{ display: 'flex', justifyContent: 'center', alignItems: 'center', gap: '1rem', marginTop: '0.5rem' }}>
              <button 
                className="btn-secondary" 
                disabled={currentPage === 1} 
                onClick={() => setCurrentPage(p => p - 1)}
                style={{ fontSize: '0.75rem', padding: '0.35rem 0.75rem' }}
              >
                Previous
              </button>
              <span className="micro-label" style={{ opacity: 0.6 }}>Page {currentPage} of {totalPages}</span>
              <button 
                className="btn-secondary" 
                disabled={currentPage === totalPages} 
                onClick={() => setCurrentPage(p => p + 1)}
                style={{ fontSize: '0.75rem', padding: '0.35rem 0.75rem' }}
              >
                Next
              </button>
            </div>
          )}

          {excludedResources.length > 0 && (
            <div style={{ marginTop: '1rem' }}>
              <button 
                onClick={() => setIsExcludedSectionVisible(!isExcludedSectionVisible)}
                style={{ 
                  background: 'transparent', 
                  border: 'none', 
                  color: 'var(--muted)', 
                  display: 'flex', 
                  alignItems: 'center', 
                  gap: '0.5rem', 
                  cursor: 'pointer',
                  padding: 0,
                  fontSize: '0.75rem',
                  fontWeight: 600
                }}
              >
                {isExcludedSectionVisible ? <ChevronUp size={14} /> : <ChevronDown size={14} />}
                {isExcludedSectionVisible ? 'Hide' : 'Show'} {excludedResources.length} non-billable or estimate-only resources
              </button>

              {isExcludedSectionVisible && (
                <div className="operational-surface" style={{ marginTop: '1rem', overflow: 'hidden', border: '1px solid var(--border)', borderRadius: 'var(--radius-lg)', opacity: 0.8 }}>
                  <table style={{ width: '100%', borderCollapse: 'collapse', textAlign: 'left', fontSize: '0.8125rem' }}>
                    <thead>
                      <tr style={{ background: 'rgba(255,255,255,0.01)', borderBottom: '1px solid var(--border)' }}>
                        <th className="micro-label" style={{ padding: '0.5rem 0.75rem', opacity: 0.4 }}>Asset & Type</th>
                        <th className="micro-label" style={{ padding: '0.5rem 0.75rem', opacity: 0.4 }}>Provider</th>
                        <th className="micro-label" style={{ padding: '0.5rem 0.75rem', opacity: 0.4 }}>Region</th>
                        <th className="micro-label" style={{ padding: '0.5rem 0.75rem', textAlign: 'right', opacity: 0.4 }}>Estimated Cost</th>
                      </tr>
                    </thead>
                    <tbody>
                      {excludedResources.map((resource) => (
                        <tr key={resource.id} className="operational-row" onClick={() => setSelectedResourceId(resource.id)} style={{ cursor: 'pointer' }}>
                          <td style={{ padding: '0.5rem 0.75rem', opacity: 0.7 }}>
                            <div style={{ display: 'flex', alignItems: 'center', gap: '0.75rem' }}>
                              <div style={{ color: 'var(--muted-foreground)', flexShrink: 0 }}>{getIcon(resource.type)}</div>
                              <div style={{ display: 'flex', flexDirection: 'column' }}>
                                <span style={{ fontWeight: 700 }}>{resource.name}</span>
                                <span style={{ fontSize: '0.65rem', color: 'var(--muted)' }}>{resource.type}</span>
                              </div>
                            </div>
                          </td>
                          <td style={{ padding: '0.5rem 0.75rem' }}>
                            <span className="badge" style={{ background: 'rgba(255,255,255,0.03)', fontSize: '0.6rem', padding: '0.1rem 0.4rem', opacity: 0.6 }}>{resource.provider}</span>
                          </td>
                          <td style={{ padding: '0.5rem 0.75rem', color: 'var(--muted-foreground)', opacity: 0.6 }}>{resource.location}</td>
                          <td style={{ padding: '0.5rem 0.75rem', textAlign: 'right', opacity: 0.6 }}>
                            <div style={{ fontSize: '0.75rem' }}>{formatResourceAmount(resource.monthlyCost, resource.currency)}</div>
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              )}
            </div>
          )}
        </div>
      </div>

      <Drawer
        shouldScaleBackground={false}
        direction="right"
        open={selectedResourceId !== null}
        onOpenChange={(open) => {
          if (!open) {
            setSelectedResourceId(null)
          }
        }}
      >
        <DrawerContent>
          <DrawerHeader style={{ borderBottom: '1px solid var(--border)' }}>
            <div style={{ display: 'flex', justifyContent: 'space-between', gap: '1rem', width: '100%' }}>
              <div>
                <DrawerTitle>{activeResource?.name || 'Resource details'}</DrawerTitle>
                <DrawerDescription style={{ marginTop: '0.35rem' }}>
                  {activeResource
                    ? `${activeResource.provider} • ${activeResource.resourceGroupName || 'Ungrouped'} • ${activeResource.location || 'n/a'}`
                    : 'Loading live cost context...'}
                </DrawerDescription>
              </div>
              {activeResource && (
                <span
                  className="badge"
                  style={{ background: 'rgba(255,255,255,0.05)', fontSize: '0.65rem', height: 'fit-content' }}
                >
                  {activeResource.provider}
                </span>
              )}
            </div>
          </DrawerHeader>

          <div style={{ flex: 1, overflowY: 'auto', padding: '1rem', display: 'flex', flexDirection: 'column', gap: '1rem' }}>
            {selectedResourceId && isFetchingResourceDetail && !resourceDetail ? (
              <div className="operational-surface" style={{ padding: '1.25rem', color: 'var(--muted)' }}>
                Loading billing context...
              </div>
            ) : activeResource ? (
              <>
                <div className="premium-glass" style={{ padding: '1.25rem' }}>
                  <div className="micro-label" style={{ marginBottom: '0.75rem' }}>Monthly Resource Cost</div>
                  <div style={{ fontSize: '2rem', fontWeight: 800 }}>
                    {formatResourceAmount(activeCost?.amount ?? selectedListResource?.monthlyCost ?? 0, activeCurrency)}
                  </div>
                  <div style={{ display: 'flex', alignItems: 'center', gap: '0.5rem', marginTop: '0.75rem', color: getCostSourceColor(activeCostSource), fontSize: '0.8125rem' }}>
                    <div style={{ width: '6px', height: '6px', borderRadius: '50%', background: 'currentColor' }}></div>
                    {getCostSourceLabel(activeCostSource)}
                  </div>
                  <div style={{ color: 'var(--muted)', fontSize: '0.75rem', marginTop: '0.6rem' }}>
                    Last billing sync: {formatTimestamp(activeCost?.capturedAt ?? selectedListResource?.costCapturedAt)}
                  </div>
                </div>

                <div className="grid grid-cols-2" style={{ gap: '1rem' }}>
                  <div className="operational-surface" style={{ padding: '1rem' }}>
                    <div style={{ display: 'flex', alignItems: 'center', gap: '0.5rem', marginBottom: '0.5rem' }}>
                      <ReceiptText size={16} color="var(--muted)" />
                      <span className="micro-label">Provider Total</span>
                    </div>
                    <div style={{ fontSize: '1.4rem', fontWeight: 700 }}>
                      {hasLiveDetail
                        ? formatCurrencyAmount(resourceDetail?.costContext?.providerTotal ?? 0, activeCurrency)
                        : 'Live detail unavailable'}
                    </div>
                    <div style={{ color: 'var(--muted)', fontSize: '0.75rem', marginTop: '0.35rem' }}>
                      {hasLiveDetail
                        ? `Current billing period for ${activeResource.provider}`
                        : 'Refresh the app after restarting the latest API/frontend build'}
                    </div>
                  </div>

                  <div className="operational-surface" style={{ padding: '1rem' }}>
                    <div style={{ display: 'flex', alignItems: 'center', gap: '0.5rem', marginBottom: '0.5rem' }}>
                      <Activity size={16} color="var(--muted)" />
                      <span className="micro-label">Resource Group Total</span>
                    </div>
                    <div style={{ fontSize: '1.4rem', fontWeight: 700 }}>
                      {hasLiveDetail
                        ? formatCurrencyAmount(resourceDetail?.costContext?.resourceGroupTotal ?? 0, activeCurrency)
                        : 'Live detail unavailable'}
                    </div>
                    <div style={{ color: 'var(--muted)', fontSize: '0.75rem', marginTop: '0.35rem' }}>
                      {hasLiveDetail
                        ? `${activeResource.resourceGroupName || 'Ungrouped'} rollup`
                        : 'The row-level cost loaded, but the detail query did not'}
                    </div>
                  </div>
                </div>

                <div className="operational-surface" style={{ padding: '1rem', display: 'flex', flexDirection: 'column', gap: '0.75rem' }}>
                  <div>
                    <div className="micro-label" style={{ marginBottom: '0.35rem' }}>Resource Context</div>
                    <div style={{ fontSize: '0.8125rem', color: 'var(--muted-foreground)' }}>
                      {activeResource.type} in {activeResource.location || 'n/a'}
                    </div>
                  </div>
                  <div style={{ display: 'grid', gap: '0.75rem' }}>
                    <div>
                      <div style={{ color: 'var(--muted)', fontSize: '0.72rem' }}>Subscription</div>
                      <div style={{ fontSize: '0.8125rem', wordBreak: 'break-all' }}>{activeResource.subscriptionId}</div>
                    </div>
                    <div>
                      <div style={{ color: 'var(--muted)', fontSize: '0.72rem' }}>Discovered</div>
                      <div style={{ fontSize: '0.8125rem' }}>{formatTimestamp(activeResource.discoveredAt)}</div>
                    </div>
                    <div>
                      <div style={{ color: 'var(--muted)', fontSize: '0.72rem' }}>Billing Period</div>
                      <div style={{ fontSize: '0.8125rem' }}>
                        {activeCost?.periodStart
                          ? `${formatPeriodDate(activeCost.periodStart)} to ${formatPeriodDate(activeCost.periodEnd)}`
                          : 'Current month allocation'}
                      </div>
                    </div>
                  </div>
                </div>

                <div className="operational-surface" style={{ padding: '1rem', display: 'flex', flexDirection: 'column', gap: '0.9rem' }}>
                  <div>
                    <div className="micro-label" style={{ marginBottom: '0.35rem' }}>Terraform Actions</div>
                    <div style={{ fontSize: '0.78rem', color: 'var(--muted)' }}>
                      Generate Terraform-backed action files to operate on this resource.
                    </div>
                  </div>

                  {hasLiveDetail && resourceDetail?.availableActions.length ? (
                    <div style={{ display: 'flex', flexWrap: 'wrap', gap: '0.6rem' }}>
                      {resourceDetail.availableActions.map((actionDefinition) => (
                        <button
                          key={actionDefinition.action}
                          type="button"
                          onClick={() => handleExecuteAction(actionDefinition)}
                          disabled={executeActionMutation.isPending}
                          style={{
                            display: 'inline-flex',
                            alignItems: 'center',
                            gap: '0.45rem',
                            padding: '0.65rem 0.8rem',
                            borderRadius: 'var(--radius-md)',
                            border: `1px solid ${actionDefinition.isDestructive ? 'rgba(248, 113, 113, 0.45)' : 'var(--border)'}`,
                            background: actionDefinition.isDestructive ? 'rgba(248, 113, 113, 0.08)' : 'rgba(255,255,255,0.03)',
                            color: actionDefinition.isDestructive ? '#fecaca' : 'inherit',
                            cursor: executeActionMutation.isPending ? 'wait' : 'pointer',
                            opacity: executeActionMutation.isPending ? 0.7 : 1,
                            fontWeight: 700,
                            fontSize: '0.78rem',
                          }}
                        >
                          {executeActionMutation.isPending && executeActionMutation.variables?.action === actionDefinition.action ? (
                            <Spinner size={14} className="text-current" />
                          ) : (
                            getActionIcon(actionDefinition.action)
                          )}
                          {actionDefinition.label}
                        </button>
                      ))}
                    </div>
                  ) : (
                    <div style={{ color: 'var(--muted)', fontSize: '0.78rem' }}>
                      {hasLiveDetail
                        ? 'No Terraform-backed actions are available for this resource type yet.'
                        : 'Load live detail to discover supported actions.'}
                    </div>
                  )}

                  {executeActionMutation.isPending && (
                    <div style={{ color: '#fbbf24', fontSize: '0.78rem' }}>
                      Running {executeActionMutation.variables?.action ?? 'action'} for {activeResource.name}...
                    </div>
                  )}

                  {executeActionMutation.isError && (
                    <div style={{ color: '#f87171', fontSize: '0.78rem' }}>
                      {(executeActionMutation.error as Error).message}
                    </div>
                  )}
                </div>

                {(lastActionResult || (hasLiveDetail && resourceDetail?.actionAudits.length)) && (
                  <div className="operational-surface" style={{ padding: '1rem', display: 'flex', flexDirection: 'column', gap: '0.9rem' }}>
                    <div>
                      <div className="micro-label" style={{ marginBottom: '0.35rem' }}>Recent Action Runs</div>
                      <div style={{ fontSize: '0.78rem', color: 'var(--muted)' }}>
                        Terraform execution history for this resource.
                      </div>
                    </div>

                    {lastActionResult && (
                      <div style={{ border: '1px solid rgba(255,255,255,0.06)', borderRadius: 'var(--radius-md)', padding: '0.85rem', display: 'grid', gap: '0.55rem' }}>
                        <div style={{ display: 'flex', justifyContent: 'space-between', gap: '1rem', alignItems: 'center' }}>
                          <div style={{ fontWeight: 700, textTransform: 'capitalize' }}>{lastActionResult.action}</div>
                          <span
                            className="badge"
                            style={{
                              background: 'rgba(255,255,255,0.04)',
                              color: getActionStatusColor(lastActionResult.status),
                              border: `1px solid ${getActionStatusColor(lastActionResult.status)}`,
                            }}
                          >
                            {lastActionResult.status}
                          </span>
                        </div>
                        <div style={{ display: 'grid', gap: '0.35rem', fontSize: '0.75rem', color: 'var(--muted-foreground)' }}>
                          <div>Terraform workspace: {lastActionResult.workspacePath}</div>
                          <div>Started: {formatTimestamp(lastActionResult.startedAt)}</div>
                          <div>Completed: {formatTimestamp(lastActionResult.completedAt)}</div>
                          {typeof lastActionResult.responseStatusCode === 'number' && (
                            <div>Azure response: HTTP {lastActionResult.responseStatusCode}</div>
                          )}
                        </div>
                        {(lastActionResult.output || lastActionResult.errorOutput) && (
                          <pre style={{ margin: 0, padding: '0.75rem', background: 'rgba(0,0,0,0.2)', borderRadius: 'var(--radius-sm)', overflowX: 'auto', fontSize: '0.68rem', whiteSpace: 'pre-wrap', color: 'var(--muted-foreground)' }}>
                            {(lastActionResult.errorOutput || lastActionResult.output || '').trim()}
                          </pre>
                        )}
                      </div>
                    )}

                    {hasLiveDetail && !!resourceDetail?.actionAudits.length && (
                      <div style={{ display: 'grid', gap: '0.6rem' }}>
                        {resourceDetail.actionAudits.map((audit) => (
                          <div key={audit.id} style={{ border: '1px solid rgba(255,255,255,0.04)', borderRadius: 'var(--radius-md)', padding: '0.8rem', display: 'grid', gap: '0.35rem' }}>
                            <div style={{ display: 'flex', justifyContent: 'space-between', gap: '1rem', alignItems: 'center' }}>
                              <div style={{ fontWeight: 700 }}>{audit.actionType}</div>
                              <span
                                className="badge"
                                style={{
                                  background: 'rgba(255,255,255,0.04)',
                                  color: getActionStatusColor(audit.status),
                                  border: `1px solid ${getActionStatusColor(audit.status)}`,
                                }}
                              >
                                {audit.status}
                              </span>
                            </div>
                            {audit.description && (
                              <div style={{ fontSize: '0.75rem', color: 'var(--muted)' }}>{audit.description}</div>
                            )}
                            <div style={{ fontSize: '0.72rem', color: 'var(--muted-foreground)' }}>
                              Started {formatTimestamp(audit.createdAt)}
                              {audit.completedAt ? ` • Completed ${formatTimestamp(audit.completedAt)}` : ''}
                            </div>
                            {audit.errorMessage && (
                              <div style={{ fontSize: '0.72rem', color: '#f87171' }}>{audit.errorMessage}</div>
                            )}
                          </div>
                        ))}
                      </div>
                    )}
                  </div>
                )}

                <div className="grid grid-cols-2" style={{ gap: '1rem' }}>
                  <div className="operational-surface" style={{ padding: '1rem' }}>
                    <div style={{ display: 'flex', alignItems: 'center', gap: '0.5rem', marginBottom: '0.5rem' }}>
                      <Sparkles size={16} color="var(--muted)" />
                      <span className="micro-label">Savings Signals</span>
                    </div>
                    <div style={{ fontSize: '1.4rem', fontWeight: 700 }}>
                      {hasLiveDetail ? (resourceDetail?.recommendations.length ?? 0) : 'Live detail unavailable'}
                    </div>
                    <div style={{ color: 'var(--muted)', fontSize: '0.75rem', marginTop: '0.35rem' }}>
                      {hasLiveDetail && resourceDetail?.recommendations[0]
                        ? `Top opportunity: ${formatCurrencyAmount(resourceDetail.recommendations[0].potentialSavings, resourceDetail.recommendations[0].currency)}`
                        : hasLiveDetail
                          ? 'No optimization recommendations yet'
                          : 'No live recommendation data was returned'}
                    </div>
                  </div>

                  <div className="operational-surface" style={{ padding: '1rem' }}>
                    <div style={{ display: 'flex', alignItems: 'center', gap: '0.5rem', marginBottom: '0.5rem' }}>
                      <Activity size={16} color="var(--muted)" />
                      <span className="micro-label">Recent Metrics</span>
                    </div>
                    <div style={{ fontSize: '1.4rem', fontWeight: 700 }}>
                      {hasLiveDetail ? (resourceDetail?.metrics.length ?? 0) : 'Live detail unavailable'}
                    </div>
                    <div style={{ color: 'var(--muted)', fontSize: '0.75rem', marginTop: '0.35rem' }}>
                      {hasLiveDetail && resourceDetail?.metrics[0]
                        ? `Latest sample: ${formatTimestamp(resourceDetail.metrics[0].timestamp)}`
                        : hasLiveDetail
                          ? 'No metric samples captured yet'
                          : 'No live metric samples were returned'}
                    </div>
                  </div>
                </div>
              </>
            ) : (
              <div className="operational-surface" style={{ padding: '1.25rem', color: 'var(--muted)' }}>
                Select a resource to inspect its live billing context.
              </div>
            )}
          </div>

          <DrawerFooter style={{ borderTop: '1px solid var(--border)', padding: '1.25rem' }}>
            <DrawerClose asChild>
              <button className="btn-secondary" style={{ width: '100%' }}>
                Close Resource Details
              </button>
            </DrawerClose>
          </DrawerFooter>
        </DrawerContent>
      </Drawer>
    </div>
  )
}
