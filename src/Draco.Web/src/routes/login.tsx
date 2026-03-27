import { createRoute, useNavigate } from '@tanstack/react-router'
import { Route as rootRoute } from './__root'
import { useQuery } from '@tanstack/react-query'
import { useEffect, useState } from 'react'
import { Shield } from 'lucide-react'
import { dracoApi } from '../lib/api'

export const Route = createRoute({
  getParentRoute: () => rootRoute,
  path: '/login',
  component: Login,
})

function Login() {
  const navigate = useNavigate()
  const [isStartingSignIn, setIsStartingSignIn] = useState(false)
  const hasToken = Boolean(dracoApi.auth.getToken())
  const { data: user } = useQuery({
    queryKey: ['me'],
    queryFn: dracoApi.auth.getMe,
    enabled: hasToken,
    retry: false,
  })

  useEffect(() => {
    if (user) {
      void navigate({ to: user.isSetupComplete ? '/dashboard' : '/setup' })
    }
  }, [navigate, user])

  const handleSignIn = async () => {
    setIsStartingSignIn(true)
    try {
      await dracoApi.auth.beginWorkOsSignIn()
    } catch (error) {
      console.error('Failed to start WorkOS sign-in:', error)
      setIsStartingSignIn(false)
    }
  }

  return (
    <div className="animate-fade-in" style={{ maxWidth: '400px', margin: '8rem auto 0' }}>
      <div className="card" style={{ textAlign: 'center', padding: '3rem', position: 'relative', overflow: 'hidden' }}>
        <div style={{ 
          position: 'absolute', 
          top: '-50px', 
          left: '50%', 
          transform: 'translateX(-50%)', 
          width: '200px', 
          height: '100px', 
          background: 'radial-gradient(circle, rgba(255,0,0,0.1) 0%, transparent 70%)',
          pointerEvents: 'none'
        }} />
        
        <div style={{ width: '48px', height: '48px', borderRadius: 'var(--radius-md)', background: 'var(--primary)', display: 'flex', alignItems: 'center', justifyContent: 'center', margin: '0 auto 1.5rem', boxShadow: '0 4px 12px rgba(255, 0, 0, 0.3)' }}>
          <Shield size={24} color="white" />
        </div>
        
        <h2 style={{ fontSize: '1.25rem', marginBottom: '0.5rem', fontWeight: 800, letterSpacing: '-0.02em' }}>SENTINEL ACCESS</h2>
        <p style={{ color: 'var(--muted-foreground)', fontSize: '0.875rem', marginBottom: '2.5rem', lineHeight: '1.5' }}>
          Secure, enterprise-grade authentication powered by WorkOS AuthKit.
        </p>
        
        <button 
          className="btn-primary" 
          onClick={() => void handleSignIn()} 
          style={{ width: '100%', height: '44px' }}
          disabled={isStartingSignIn}
        >
          {isStartingSignIn ? 'Redirecting...' : 'Sign In with AuthKit'}
        </button>
        
        <div style={{ marginTop: '2rem', display: 'flex', alignItems: 'center', justifyContent: 'center', gap: '1rem' }}>
          <span className="micro-label" style={{ fontSize: '0.65rem' }}>Secured</span>
          <div style={{ height: '1px', width: '20px', background: 'var(--border)' }}></div>
          <span className="micro-label" style={{ fontSize: '0.65rem' }}>Draco V1</span>
        </div>
      </div>
    </div>
  )
}
