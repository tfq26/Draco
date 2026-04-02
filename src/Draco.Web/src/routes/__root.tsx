import { createRootRoute, Link, Outlet, useLocation, useNavigate } from '@tanstack/react-router'
import { TanStackRouterDevtools } from '@tanstack/router-devtools'
import { Sun, Moon, Settings, LogOut, ChevronDown, RefreshCcw } from 'lucide-react'
import { useState, useEffect } from 'react'
import { useQuery, useQueryClient, useMutation } from '@tanstack/react-query'
import dracoBlack from '../assets/draco-black.svg'
import dracoColored from '../assets/draco-colored.svg'
import { dracoApi } from '../lib/api'
import { NotificationDrawer } from '../components/NotificationDrawer'
import { AssistantWidget } from '../components/AssistantWidget'

export const Route = createRootRoute({
  component: RootComponent,
})

function RootComponent() {
  const location = useLocation()
  const navigate = useNavigate()
  const queryClient = useQueryClient()

  const [isUserMenuOpen, setIsUserMenuOpen] = useState(false)
  const [theme, setTheme] = useState<'light' | 'dark'>(() => {
    const saved = localStorage.getItem('draco-theme')
    if (saved) return saved as 'light' | 'dark'
    return window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light'
  })
  
  const hasToken = Boolean(dracoApi.auth.getToken())
  
  const { data: currentUser } = useQuery({
    queryKey: ['me'],
    queryFn: dracoApi.auth.getMe,
    enabled: hasToken,
    retry: false,
  })

  useEffect(() => {
    document.documentElement.setAttribute('data-theme', theme)
    localStorage.setItem('draco-theme', theme)
  }, [theme])

  useEffect(() => {
    const publicPaths = ['/', '/auth/callback', '/callback', '/aws-onboarding']
    if (!hasToken && !publicPaths.includes(location.pathname)) {
      void navigate({ to: '/' })
    }
  }, [hasToken, location.pathname, navigate])

  const toggleTheme = () => setTheme(prev => prev === 'light' ? 'dark' : 'light')
  const initials = (currentUser?.name || currentUser?.email || 'Draco')
    .split(/\s+/)
    .filter(Boolean)
    .slice(0, 2)
    .map(part => part[0]?.toUpperCase() ?? '')
    .join('') || 'DR'

  const handleSignOut = async () => {
    dracoApi.auth.clearToken()
    dracoApi.auth.clearWorkOsCodeVerifier()
    await queryClient.invalidateQueries({ queryKey: ['me'] })
    window.location.href = '/'
  }

  const handleBeginSignIn = async () => {
    try {
      await dracoApi.auth.beginWorkOsSignIn()
    } catch (error) {
      console.error('Failed to start WorkOS sign-in:', error)
    }
  }

  const syncMutation = useMutation({
    mutationFn: () => dracoApi.cloudConnections.sync(),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['me'] })
      await queryClient.invalidateQueries({ queryKey: ['dashboard-summary'] })
      await queryClient.invalidateQueries({ queryKey: ['resources'] })
    },
  })

  return (
    <>
      <div style={{ minHeight: '100vh', display: 'flex', flexDirection: 'column' }}>
        <div className="page-bg-gradient" />
        <nav style={{
          height: '48px',
          display: 'flex',
          alignItems: 'center',
          padding: '0 1.5rem',
          justifyContent: 'space-between',
          borderBottom: '1px solid var(--border)',
          background: 'var(--background)',
          position: 'sticky',
          top: 0,
          zIndex: 100,
          backdropFilter: 'blur(8px)'
        }}>
          <div style={{ display: 'flex', alignItems: 'center', gap: '2rem' }}>
            <Link to="/" style={{ textDecoration: 'none', color: 'var(--foreground)', display: 'flex', alignItems: 'center', gap: '0.75rem', fontWeight: 800, fontSize: '1rem', letterSpacing: '-0.02em' }}>
              <img 
                src={theme === 'light' ? dracoBlack : dracoColored} 
                alt="Draco" 
                style={{ height: '24px', width: 'auto' }} 
              />
              DRACO
            </Link>
            {hasToken && (
              <div style={{ display: 'flex', gap: '1rem' }}>
                <Link to="/dashboard" className="nav-link" activeProps={{ style: { color: 'var(--primary)', fontWeight: 600 } }}>Dashboard</Link>
                <Link to="/resources" className="nav-link" activeProps={{ style: { color: 'var(--primary)', fontWeight: 600 } }}>Resources</Link>
                <Link to="/governance" className="nav-link" activeProps={{ style: { color: 'var(--primary)', fontWeight: 600 } }}>Governance</Link>
              </div>
            )}
          </div>
          <div style={{ display: 'flex', gap: '1rem', alignItems: 'center' }}>
            {hasToken && (
               <button 
                onClick={() => syncMutation.mutate()}
                disabled={syncMutation.isPending}
                className="btn-secondary" 
                style={{ 
                  width: '32px', 
                  height: '32px', 
                  padding: 0, 
                  display: 'flex', 
                  alignItems: 'center', 
                  justifyContent: 'center',
                  borderRadius: 'var(--radius-md)',
                  color: syncMutation.isPending ? 'var(--primary)' : 'inherit'
                }}
                title="Universal Cloud Sync"
              >
                <RefreshCcw size={16} className={syncMutation.isPending ? 'animate-spin' : ''} />
              </button>
            )}
            {!hasToken && (
              <button 
                onClick={toggleTheme}
                className="btn-secondary" 
                style={{ 
                  width: '32px', 
                  height: '32px', 
                  padding: 0, 
                  display: 'flex', 
                  alignItems: 'center', 
                  justifyContent: 'center',
                  borderRadius: 'var(--radius-md)'
                }}
              >
                {theme === 'light' ? <Moon size={16} /> : <Sun size={16} />}
              </button>
            )}
            {hasToken && <NotificationDrawer />}
            {hasToken ? (
              <div style={{ position: 'relative' }}>
                <button
                  onClick={() => setIsUserMenuOpen(!isUserMenuOpen)}
                  onBlur={() => setTimeout(() => setIsUserMenuOpen(false), 200)}
                  style={{
                    display: 'flex',
                    alignItems: 'center',
                    gap: '0.5rem',
                    background: 'none',
                    border: 'none',
                    padding: '0.25rem',
                    cursor: 'pointer',
                    borderRadius: 'var(--radius-md)',
                    transition: 'background 0.2s',
                  }}
                  className="nav-link"
                >
                  <div style={{
                    width: '32px',
                    height: '32px',
                    borderRadius: 'var(--radius-full)',
                    background: 'var(--secondary)',
                    border: '1px solid var(--border)',
                    display: 'flex',
                    alignItems: 'center',
                    justifyContent: 'center',
                    overflow: 'hidden'
                  }}>
                    {currentUser?.imageUrl ? (
                      <img 
                        src={currentUser.imageUrl} 
                        alt="Avatar" 
                        style={{ width: '100%', height: '100%', objectFit: 'cover' }} 
                      />
                    ) : (
                      <span style={{ 
                        fontSize: '0.75rem', 
                        fontWeight: 600, 
                        color: 'var(--muted-foreground)' 
                      }}>
                        {initials}
                      </span>
                    )}
                  </div>
                  <ChevronDown size={14} color="var(--muted)" style={{ transform: isUserMenuOpen ? 'rotate(180) translateY(-1px)' : 'none', transition: 'transform 0.2s' }} />
                </button>

                {isUserMenuOpen && (
                  <div style={{
                    position: 'absolute',
                    top: 'calc(100% + 8px)',
                    right: 0,
                    width: '180px',
                    background: 'var(--card)',
                    border: '1px solid var(--border)',
                    borderRadius: 'var(--radius-lg)',
                    boxShadow: '0 10px 25px rgba(0,0,0,0.1)',
                    overflow: 'hidden',
                    zIndex: 200,
                    padding: '0.5rem',
                    animation: 'fadeIn 0.2s ease forwards',
                  }}>
                    <Link
                      to="/settings"
                      onClick={() => setIsUserMenuOpen(false)}
                      style={{
                        display: 'flex',
                        alignItems: 'center',
                        gap: '0.75rem',
                        padding: '0.625rem 0.75rem',
                        color: 'var(--foreground)',
                        textDecoration: 'none',
                        fontSize: '0.8125rem',
                        fontWeight: 500,
                        borderRadius: 'var(--radius-md)',
                      }}
                      className="nav-link"
                    >
                      <Settings size={14} /> Settings
                    </Link>
                    <div style={{ height: '1px', background: 'var(--border)', margin: '0.25rem 0.5rem' }} />
                    <button
                      onClick={() => void handleSignOut()}
                      style={{
                        display: 'flex',
                        alignItems: 'center',
                        gap: '0.75rem',
                        padding: '0.625rem 0.75rem',
                        color: 'var(--primary)',
                        background: 'none',
                        border: 'none',
                        width: '100%',
                        textAlign: 'left',
                        fontSize: '0.8125rem',
                        fontWeight: 500,
                        cursor: 'pointer',
                        borderRadius: 'var(--radius-md)',
                      }}
                      className="nav-link"
                    >
                      <LogOut size={14} /> Sign Out
                    </button>
                  </div>
                )}
              </div>
            ) : (
              <button
                onClick={() => void handleBeginSignIn()}
                className="btn-secondary"
                style={{ padding: '0.25rem 0.75rem', height: '32px', fontSize: '0.75rem', display: 'inline-flex', alignItems: 'center' }}
              >
                Sign In
              </button>
            )}
          </div>
        </nav>

        <main style={{ flex: 1, padding: '2rem 3rem', maxWidth: '1400px', margin: '0 auto', width: '100%' }}>
          <Outlet />
        </main>
      </div>
      {hasToken && <AssistantWidget />}
      <TanStackRouterDevtools />
    </>
  )
}
