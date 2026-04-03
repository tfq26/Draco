import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { Bell, Trash2, Settings, ExternalLink, Inbox } from 'lucide-react'
import { dracoApi } from '../lib/api'
import { Drawer, DrawerContent, DrawerHeader, DrawerTitle, DrawerTrigger, DrawerFooter, DrawerClose } from './ui/drawer'
import { useNavigate } from '@tanstack/react-router'

type NotificationItem = {
  id: number
  title: string
  message: string
  createdAt: string
  isRead: boolean
  resourceUrl?: string
}

export function NotificationDrawer() {
  const queryClient = useQueryClient()
  const navigate = useNavigate()

  const { data: notifications = [] } = useQuery<NotificationItem[]>({
    queryKey: ['notifications'],
    queryFn: dracoApi.notifications.getAll,
    refetchInterval: 30000, // Sync every 30s
  })

  const markAsRead = useMutation({
    mutationFn: (id: number) => dracoApi.notifications.markAsRead(id),
    onMutate: async (id: number) => {
      await queryClient.cancelQueries({ queryKey: ['notifications'] })
      const previousNotifications = queryClient.getQueryData<NotificationItem[]>(['notifications']) ?? []

      queryClient.setQueryData<NotificationItem[]>(
        ['notifications'],
        previousNotifications.map(notification =>
          notification.id === id
            ? { ...notification, isRead: true }
            : notification,
        ),
      )

      return { previousNotifications }
    },
    onError: (_error, _id, context) => {
      if (context?.previousNotifications) {
        queryClient.setQueryData(['notifications'], context.previousNotifications)
      }
    },
    onSettled: () => queryClient.invalidateQueries({ queryKey: ['notifications'] }),
  })

  const clearAll = useMutation({
    mutationFn: dracoApi.notifications.clearAll,
    onMutate: async () => {
      await queryClient.cancelQueries({ queryKey: ['notifications'] })
      const previousNotifications = queryClient.getQueryData<NotificationItem[]>(['notifications']) ?? []
      queryClient.setQueryData<NotificationItem[]>(['notifications'], [])
      return { previousNotifications }
    },
    onError: (_error, _variables, context) => {
      if (context?.previousNotifications) {
        queryClient.setQueryData(['notifications'], context.previousNotifications)
      }
    },
    onSettled: () => queryClient.invalidateQueries({ queryKey: ['notifications'] }),
  })

  const unreadCount = notifications.filter(n => !n.isRead).length

  return (
    <Drawer shouldScaleBackground={false} direction="right">
      <DrawerTrigger asChild>
        <button
          className="nav-link"
          style={{ 
            position: 'relative', 
            background: 'none', 
            border: 'none', 
            padding: '0.5rem',
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center'
          }}
        >
          <Bell size={18} />
          {unreadCount > 0 && (
            <span style={{
              position: 'absolute',
              top: '2px',
              right: '2px',
              background: 'var(--primary)',
              color: 'white',
              fontSize: '10px',
              height: '14px',
              minWidth: '14px',
              borderRadius: '7px',
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'center',
              fontWeight: 800,
              padding: '0 4px',
              boxShadow: '0 0 0 2px var(--background)'
            }}>
              {unreadCount}
            </span>
          )}
        </button>
      </DrawerTrigger>
      <DrawerContent>
        <DrawerHeader style={{ borderBottom: '1px solid var(--border)' }}>
          <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', width: '100%' }}>
            <DrawerTitle style={{ display: 'flex', alignItems: 'center', gap: '0.75rem' }}>
              <Bell className="text-primary" size={20} /> Notification Center
            </DrawerTitle>
            {notifications.length > 0 && (
              <button 
                onClick={() => clearAll.mutate()}
                style={{ background: 'none', border: 'none', color: 'var(--muted-foreground)', fontSize: '0.75rem', display: 'flex', alignItems: 'center', gap: '0.4rem', cursor: 'pointer' }}
              >
                <Trash2 size={12} /> Clear All
              </button>
            )}
          </div>
        </DrawerHeader>
        
        <div style={{ flex: 1, overflowY: 'auto', padding: '1rem' }}>
          {notifications.length === 0 ? (
            <div style={{ height: '100%', display: 'flex', flexDirection: 'column', alignItems: 'center', justifyContent: 'center', color: 'var(--muted)', gap: '1rem' }}>
              <Inbox size={48} opacity={0.2} />
              <div>No notifications yet.</div>
            </div>
          ) : (
            <div style={{ display: 'flex', flexDirection: 'column', gap: '0.75rem' }}>
              {notifications.map((n) => (
                <div 
                  key={n.id} 
                  onClick={() => {
                    if (!n.isRead) {
                      markAsRead.mutate(n.id)
                    }
                    if (n.resourceUrl) window.location.href = n.resourceUrl
                  }}
                  className="operational-surface"
                  style={{ 
                    padding: '1rem', 
                    cursor: 'pointer',
                    borderLeft: `4px solid ${n.isRead ? 'transparent' : 'var(--primary)'}`,
                    opacity: n.isRead ? 0.7 : 1,
                    transition: 'all 0.2s'
                  }}
                >
                  <div style={{ display: 'flex', justifyContent: 'space-between', marginBottom: '0.25rem' }}>
                    <div style={{ fontWeight: 700, fontSize: '0.8125rem' }}>{n.title}</div>
                    <div style={{ fontSize: '0.7rem', color: 'var(--muted)' }}>
                      {new Date(n.createdAt).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}
                    </div>
                  </div>
                  <div style={{ fontSize: '0.75rem', color: 'var(--muted-foreground)', lineHeight: 1.4 }}>{n.message}</div>
                  {n.resourceUrl && (
                    <div style={{ marginTop: '0.5rem', display: 'flex', alignItems: 'center', gap: '0.25rem', color: 'var(--primary)', fontSize: '0.7rem', fontWeight: 600 }}>
                      View Details <ExternalLink size={10} />
                    </div>
                  )}
                </div>
              ))}
            </div>
          )}
        </div>

        <DrawerFooter style={{ padding: '1.5rem' }}>
          <DrawerClose asChild>
            <button 
              className="btn-primary" 
              style={{ width: '100%', padding: '0.875rem' }}
              onClick={() => navigate({ to: '/settings', search: { tab: 'notifications' } })}
            >
              <Settings size={14} /> Configure Governance
            </button>
          </DrawerClose>
        </DrawerFooter>
      </DrawerContent>
    </Drawer>
  )
}
