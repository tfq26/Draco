import { createRoute, useNavigate } from '@tanstack/react-router'
import { Route as rootRoute } from './__root'
import { useEffect, useState } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { dracoApi } from '../lib/api'

export const CallbackRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/auth/callback',
  component: CallbackPage,
})

export const LegacyCallbackRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/callback',
  component: CallbackPage,
})

const CALLBACK_RETRY_LIMIT = 8

function isRetriableAuthError(error: unknown) {
  if (error instanceof TypeError) {
    return true
  }

  if (error instanceof Error) {
    return /failed to fetch|networkerror|load failed|connection refused/i.test(error.message)
  }

  return false
}

function getAuthErrorMessage(error: unknown) {
  if (error instanceof Error && error.message) {
    return error.message
  }

  return 'Unable to complete sign-in right now.'
}

function sleep(ms: number) {
  return new Promise(resolve => window.setTimeout(resolve, ms))
}

export function CallbackPage() {
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const [status, setStatus] = useState('Finalizing your WorkOS sign-in with Draco.')
  const [errorMessage, setErrorMessage] = useState<string | null>(null)

  useEffect(() => {
    let cancelled = false

    const finalizeSignIn = async () => {
      const params = new URLSearchParams(window.location.search)
      const code = params.get('code')
      const authError = params.get('error')
      const authErrorDescription = params.get('error_description')
      const codeVerifier = dracoApi.auth.getWorkOsCodeVerifier()

      if (authError) {
        setErrorMessage(authErrorDescription ?? `WorkOS returned: ${authError}`)
        return
      }

      if (!code) {
        setErrorMessage('Missing WorkOS authorization code.')
        return
      }

      if (!codeVerifier) {
        console.error('Missing WorkOS PKCE verifier in session storage.')
        setErrorMessage('Your secure sign-in session expired. Please start the sign-in flow again.')
        return
      }

      let lastError: unknown = null

      for (let attempt = 1; attempt <= CALLBACK_RETRY_LIMIT && !cancelled; attempt += 1) {
        setStatus(
          attempt === 1
            ? 'Finalizing your WorkOS sign-in with Draco.'
            : `Waiting for Draco API to respond... retry ${attempt} of ${CALLBACK_RETRY_LIMIT}.`,
        )

        try {
          const { token, user } = await dracoApi.auth.exchangeWorkOsCode(code, codeVerifier)

          if (cancelled) {
            return
          }

          dracoApi.auth.storeToken(token)
          dracoApi.auth.clearWorkOsCodeVerifier()
          await queryClient.invalidateQueries({ queryKey: ['me'] })
          void navigate({ to: user.isSetupComplete ? '/dashboard' : '/setup' })
          return
        } catch (error) {
          lastError = error

          if (!isRetriableAuthError(error) || attempt === CALLBACK_RETRY_LIMIT) {
            console.error('Failed to exchange WorkOS code:', error)
            setErrorMessage(getAuthErrorMessage(error))
            return
          }

          await sleep(attempt * 1500)
        }
      }

      if (!cancelled) {
        setErrorMessage(getAuthErrorMessage(lastError))
      }
    }

    void finalizeSignIn()

    return () => {
      cancelled = true
    }
  }, [navigate, queryClient])

  return (
    <div className="animate-fade-in" style={{ textAlign: 'center', marginTop: '10rem' }}>
      <div className="premium-glass card" style={{ display: 'inline-block', padding: '3rem' }}>
        <h2 className="monochrome-gradient">Verifying Sentinel...</h2>
        <p style={{ color: 'var(--muted)' }}>{errorMessage ?? status}</p>
        {errorMessage && (
          <div style={{ marginTop: '1.5rem', display: 'flex', gap: '0.75rem', justifyContent: 'center' }}>
            <button className="btn-primary" onClick={() => window.location.reload()}>
              Retry Sign-In
            </button>
            <button
              className="btn-secondary"
              onClick={() => {
                dracoApi.auth.clearWorkOsCodeVerifier()
                void navigate({ to: '/login' })
              }}
            >
              Back to Login
            </button>
          </div>
        )}
      </div>
    </div>
  )
}
