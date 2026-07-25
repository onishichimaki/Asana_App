import { useEffect, useState, type ReactNode } from 'react'
import { requestJson } from './api'
import './AuthGate.css'

export type AuthAccount = {
  authenticated: boolean
  mode: 'Development' | 'EmailCode'
  email: string | null
  displayName: string | null
  isAdmin: boolean
  allowedEmailDomains: string[]
  asanaCredentialMode: string
}

type AuthGateProps = {
  children: (account: AuthAccount, logout: () => Promise<void>) => ReactNode
}

type LoginStep = 'email' | 'code'
type RequestCodeResponse = {
  maskedEmail: string
  expiresInMinutes: number
  developmentCode: string | null
}

function AuthGate({ children }: AuthGateProps) {
  const [account, setAccount] = useState<AuthAccount | null>(null)
  const [step, setStep] = useState<LoginStep>('email')
  const [email, setEmail] = useState('')
  const [code, setCode] = useState('')
  const [maskedEmail, setMaskedEmail] = useState('')
  const [developmentCode, setDevelopmentCode] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const [message, setMessage] = useState<string | null>(null)

  const loadAccount = async () => {
    const result = await requestJson<AuthAccount>('/api/auth/me')
    setAccount(result)
    return result
  }

  useEffect(() => {
    loadAccount().catch(() => setMessage('アプリへ接続できません。APIが起動しているか確認してください。'))
  }, [])

  const requestCode = async (event: React.FormEvent) => {
    event.preventDefault()
    setBusy(true)
    setMessage(null)
    try {
      const result = await requestJson<RequestCodeResponse>('/api/auth/request-code', {
        method: 'POST',
        body: JSON.stringify({ email: email.trim() }),
      })
      setMaskedEmail(result.maskedEmail)
      setDevelopmentCode(result.developmentCode)
      setStep('code')
      setCode('')
    } catch (error) {
      setMessage(error instanceof Error ? error.message : '確認コードを送信できませんでした。')
    } finally {
      setBusy(false)
    }
  }

  const verifyCode = async (event: React.FormEvent) => {
    event.preventDefault()
    setBusy(true)
    setMessage(null)
    try {
      await requestJson('/api/auth/verify-code', {
        method: 'POST',
        body: JSON.stringify({ email: email.trim(), code }),
      })
      await loadAccount()
    } catch (error) {
      setMessage(error instanceof Error ? error.message : '確認コードを確認できませんでした。')
    } finally {
      setBusy(false)
    }
  }

  const logout = async () => {
    await requestJson('/api/auth/logout', { method: 'POST' })
    setAccount(current => current ? { ...current, authenticated: false, email: null, displayName: null, isAdmin: false } : current)
    setStep('email')
    setEmail('')
    setCode('')
    setMessage(null)
  }

  if (!account) {
    return <main className="auth-shell"><div className="auth-loading" role="status"><span />アプリを準備しています…</div>{message && <p className="auth-error">{message}</p>}</main>
  }

  if (account.authenticated) {
    return <>{children(account, logout)}</>
  }

  const domainHint = account.allowedEmailDomains.length > 0
    ? account.allowedEmailDomains.map(domain => `@${domain}`).join('、')
    : '会社メール'

  return (
    <main className="auth-shell">
      <section className="auth-card" aria-labelledby="sign-in-title">
        <div className="auth-mark" aria-hidden="true">✓</div>
        <p className="auth-eyebrow">TASK CAPTURE</p>
        <h1 id="sign-in-title">会社メールでログイン</h1>
        <p className="auth-description">
          パスワード登録は不要です。メールに届く6桁の確認コードで安全にログインできます。
        </p>

        {step === 'email'
          ? <form onSubmit={requestCode}>
              <label htmlFor="login-email">会社メールアドレス</label>
              <input
                id="login-email"
                type="email"
                autoComplete="email"
                required
                autoFocus
                value={email}
                placeholder={`name${domainHint.split('、')[0]}`}
                onChange={event => setEmail(event.target.value)}
              />
              <p className="auth-hint">利用できるメール：{domainHint}</p>
              <button type="submit" disabled={busy || !email.trim()}>
                {busy ? '送信しています…' : '確認コードを受け取る'}
              </button>
            </form>
          : <form onSubmit={verifyCode}>
              <div className="auth-delivery">
                <span aria-hidden="true">✉</span>
                <div><strong>{maskedEmail}</strong><small>へ確認コードを送りました</small></div>
              </div>
              <label htmlFor="login-code">6桁の確認コード</label>
              <input
                id="login-code"
                className="auth-code"
                inputMode="numeric"
                pattern="[0-9]{6}"
                autoComplete="one-time-code"
                maxLength={6}
                required
                autoFocus
                value={code}
                placeholder="000000"
                onChange={event => setCode(event.target.value.replace(/\D/g, '').slice(0, 6))}
              />
              {developmentCode && <p className="auth-development-code">開発確認コード：<strong>{developmentCode}</strong></p>}
              <button type="submit" disabled={busy || code.length !== 6}>
                {busy ? '確認しています…' : 'ログイン'}
              </button>
              <button type="button" className="auth-back" onClick={() => { setStep('email'); setCode(''); setMessage(null) }}>
                メールアドレスを変更
              </button>
            </form>}

        {message && <p className="auth-error" role="alert">{message}</p>}
        <p className="auth-note">確認コードは短時間で無効になります。届かない場合は迷惑メールも確認してください。</p>
      </section>
    </main>
  )
}

export default AuthGate
