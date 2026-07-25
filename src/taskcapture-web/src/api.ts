export const apiBase = import.meta.env.VITE_API_BASE_URL ?? ''

export async function requestJson<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(`${apiBase}${path}`, {
    ...init,
    credentials: 'include',
    headers: { 'Content-Type': 'application/json', ...init?.headers },
  })
  const data = await response.json().catch(() => null)
  if (!response.ok) {
    const validation = data?.errors ? Object.values(data.errors).flat().join(' ') : ''
    throw new Error(validation || data?.detail || data?.errorMessage || '処理に失敗しました。もう一度お試しください。')
  }
  return data as T
}
