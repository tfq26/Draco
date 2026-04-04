const CLOUD_SYNC_RATE_LIMIT_KEY = 'draco:last-cloud-sync-at'

export const CLOUD_SYNC_COOLDOWN_MS = 5 * 60 * 1000

function readStoredTimestamp() {
  if (typeof window === 'undefined') {
    return null
  }

  const rawValue = window.localStorage.getItem(CLOUD_SYNC_RATE_LIMIT_KEY)
  if (!rawValue) {
    return null
  }

  const parsedValue = Number(rawValue)
  return Number.isFinite(parsedValue) ? parsedValue : null
}

export function isCloudSyncAllowed(cooldownMs = CLOUD_SYNC_COOLDOWN_MS) {
  const lastSyncAt = readStoredTimestamp()
  if (!lastSyncAt) {
    return true
  }

  return Date.now() - lastSyncAt >= cooldownMs
}

export function recordCloudSyncAttempt(timestamp = Date.now()) {
  if (typeof window === 'undefined') {
    return
  }

  window.localStorage.setItem(CLOUD_SYNC_RATE_LIMIT_KEY, String(timestamp))
}
