export const apiBase = import.meta.env.VITE_API_BASE_URL ?? ''

export function getClientKey() {
  const storageKey = 'task-capture-client-key'
  const current = localStorage.getItem(storageKey)
  if (current) return current
  const generated = globalThis.crypto?.randomUUID?.()
    ?? `web-${Date.now()}-${Math.random().toString(16).slice(2)}`
  localStorage.setItem(storageKey, generated)
  return generated
}

export async function requestJson<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(`${apiBase}${path}`, {
    ...init,
    headers: { 'Content-Type': 'application/json', 'X-TaskCapture-Client': getClientKey(), ...init?.headers },
  })
  const data = await response.json().catch(() => null)
  if (!response.ok) {
    const validation = data?.errors ? Object.values(data.errors).flat().join(' ') : ''
    throw new Error(validation || data?.detail || data?.errorMessage || '処理に失敗しました。もう一度お試しください。')
  }
  return data as T
}
