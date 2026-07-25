import { useCallback, useEffect, useState } from 'react'
import type { AuthAccount } from './AuthGate'
import { requestJson } from './api'

type AsanaStatus = {
  credentialMode: string
  connected: boolean
  asanaUserName: string | null
  asanaUserEmail: string | null
  workspaceName: string | null
  connectedAtUtc: string | null
  message: string | null
}

type AllowedProject = { projectGid: string; projectName: string }
type AdminUser = {
  id: string
  email: string | null
  displayName: string
  isActive: boolean
  isAdmin: boolean
  restrictProjects: boolean
  lastLoginAtUtc: string | null
  taskCount: number
  asanaConnected: boolean
  allowedProjects: AllowedProject[]
}

type UserDraft = AdminUser & { projectsText: string }

export default function SettingsPanel({ account, onConnectionChanged }: { account: AuthAccount; onConnectionChanged: (connected: boolean) => void }) {
  const [asana, setAsana] = useState<AsanaStatus | null>(null)
  const [users, setUsers] = useState<UserDraft[]>([])
  const [busy, setBusy] = useState<string | null>(null)
  const [message, setMessage] = useState<string | null>(null)
  const [newEmail, setNewEmail] = useState('')
  const [newDisplayName, setNewDisplayName] = useState('')
  const [newIsAdmin, setNewIsAdmin] = useState(false)

  const load = useCallback(async () => {
    const status = await requestJson<AsanaStatus>('/api/asana/connection')
    setAsana(status)
    onConnectionChanged(status.connected)
    if (account.isAdmin) {
      const result = await requestJson<AdminUser[]>('/api/admin/users')
      setUsers(result.map(user => ({
        ...user,
        projectsText: user.allowedProjects
          .map(project => `${project.projectGid} | ${project.projectName}`)
          .join('\n'),
      })))
    }
  }, [account.isAdmin, onConnectionChanged])

  useEffect(() => {
    load().catch(error => setMessage(error instanceof Error ? error.message : '設定を読み込めませんでした。'))
  }, [load])

  const connectAsana = async () => {
    setBusy('asana')
    setMessage(null)
    try {
      const result = await requestJson<{ authorizationUrl: string }>('/api/asana/connection/start', { method: 'POST' })
      window.location.assign(result.authorizationUrl)
    } catch (error) {
      setMessage(error instanceof Error ? error.message : 'Asana接続を開始できませんでした。')
      setBusy(null)
    }
  }

  const disconnectAsana = async () => {
    if (!window.confirm('Asanaとの接続を解除しますか？登録履歴は残ります。')) return
    setBusy('asana')
    setMessage(null)
    try {
      await requestJson('/api/asana/connection', { method: 'DELETE' })
      await load()
    } catch (error) {
      setMessage(error instanceof Error ? error.message : 'Asana接続を解除できませんでした。')
    } finally {
      setBusy(null)
    }
  }

  const updateDraft = (id: string, patch: Partial<UserDraft>) =>
    setUsers(current => current.map(user => user.id === id ? { ...user, ...patch } : user))

  const saveUser = async (user: UserDraft) => {
    if (!user.isActive && !window.confirm(`${user.displayName}さんを利用停止しますか？\n現在のログインとAsana接続も解除されます。`)) {
      await load()
      return
    }
    setBusy(user.id)
    setMessage(null)
    try {
      const allowedProjects = user.projectsText
        .split('\n')
        .map(line => line.trim())
        .filter(Boolean)
        .map(line => {
          const [projectGid, ...nameParts] = line.split('|')
          return { projectGid: projectGid.trim(), projectName: nameParts.join('|').trim() || projectGid.trim() }
        })
      await requestJson(`/api/admin/users/${user.id}/access`, {
        method: 'PUT',
        body: JSON.stringify({
          isActive: user.isActive,
          isAdmin: user.isAdmin,
          restrictProjects: user.restrictProjects,
          allowedProjects,
        }),
      })
      setMessage(`${user.displayName}さんの利用設定を保存しました。`)
      await load()
    } catch (error) {
      setMessage(error instanceof Error ? error.message : '利用設定を保存できませんでした。')
    } finally {
      setBusy(null)
    }
  }

  const preRegisterUser = async (event: React.FormEvent) => {
    event.preventDefault()
    setBusy('new-user')
    setMessage(null)
    try {
      await requestJson('/api/admin/users', {
        method: 'POST',
        body: JSON.stringify({ email: newEmail.trim(), displayName: newDisplayName.trim() || null, isAdmin: newIsAdmin }),
      })
      setMessage(`${newEmail.trim()} を利用登録しました。`)
      setNewEmail('')
      setNewDisplayName('')
      setNewIsAdmin(false)
      await load()
    } catch (error) {
      setMessage(error instanceof Error ? error.message : '利用者を登録できませんでした。')
    } finally {
      setBusy(null)
    }
  }

  return (
    <div className="settings-page">
      <section className="panel settings-card">
        <div className="section-heading">
          <div><h2>Asanaとの接続</h2><p>会社メールと同じAsanaアカウントを接続します。登録先は、そのアカウントで見られるプロジェクトから選べます。</p></div>
          <span className={`settings-status ${asana?.connected ? 'connected' : ''}`}>{asana?.connected ? '接続済み' : '未接続'}</span>
        </div>
        {!asana
          ? <p className="settings-muted">接続状態を確認しています…</p>
          : <>
              {asana.asanaUserName && <dl className="settings-detail">
                <div><dt>Asana利用者</dt><dd>{asana.asanaUserName}</dd></div>
                {asana.asanaUserEmail && <div><dt>Asanaメール</dt><dd>{asana.asanaUserEmail}</dd></div>}
                <div><dt>ワークスペース</dt><dd>{asana.workspaceName || '自動選択'}</dd></div>
              </dl>}
              {asana.message && <p className="settings-muted">{asana.message}</p>}
              {asana.credentialMode === 'PerUserOAuth' && (asana.connected
                ? <button type="button" className="secondary-button" disabled={busy === 'asana'} onClick={() => void disconnectAsana()}>Asana接続を解除</button>
                : <button type="button" className="primary-button" disabled={busy === 'asana'} onClick={() => void connectAsana()}>{busy === 'asana' ? '準備しています…' : '同じメールのAsanaを接続'}</button>)}
            </>}
      </section>

      <section className="panel settings-card">
        <h2>ログイン中のアカウント</h2>
        <dl className="settings-detail">
          <div><dt>会社メール</dt><dd>{account.email}</dd></div>
          <div><dt>権限</dt><dd>{account.isAdmin ? '管理者' : '利用者'}</dd></div>
        </dl>
      </section>

      {account.isAdmin && <section className="panel settings-card admin-users">
        <div className="section-heading"><div><h2>利用者の管理</h2><p>利用停止、管理者、登録できるプロジェクトを利用者ごとに設定できます。</p></div><span>{users.length}人</span></div>
        <form className="admin-register-form" onSubmit={preRegisterUser}>
          <div><strong>利用者を事前登録</strong><small>ここで登録した会社メールだけログインできます。</small></div>
          <div className="admin-register-fields">
            <label>会社メール<input type="email" required value={newEmail} placeholder="name@example.co.jp" onChange={event => setNewEmail(event.target.value)} /></label>
            <label>表示名<input value={newDisplayName} maxLength={200} placeholder="例：田中 太郎" onChange={event => setNewDisplayName(event.target.value)} /></label>
          </div>
          <label className="admin-register-check"><input type="checkbox" checked={newIsAdmin} onChange={event => setNewIsAdmin(event.target.checked)} />管理者として登録</label>
          <button type="submit" className="secondary-button" disabled={busy === 'new-user'}>{busy === 'new-user' ? '登録中…' : 'このメールを利用登録'}</button>
        </form>
        {users.map(user => <article key={user.id} className="admin-user-card">
          <div className="admin-user-heading">
            <div><strong>{user.displayName}</strong><small>{user.email || 'メール未設定'} ・ タスク{user.taskCount}件</small></div>
            <span className={user.asanaConnected ? 'connected' : ''}>{user.asanaConnected ? 'Asana接続済み' : 'Asana未接続'}</span>
          </div>
          <div className="admin-checks">
            <label><input type="checkbox" checked={user.isActive} onChange={event => updateDraft(user.id, { isActive: event.target.checked })} />このアプリを利用できる</label>
            <label><input type="checkbox" checked={user.isAdmin} onChange={event => updateDraft(user.id, { isAdmin: event.target.checked })} />管理者にする</label>
            <label><input type="checkbox" checked={user.restrictProjects} onChange={event => updateDraft(user.id, { restrictProjects: event.target.checked })} />登録先プロジェクトを限定する</label>
          </div>
          {user.restrictProjects && <label className="admin-projects">
            利用できるプロジェクト
            <textarea rows={3} value={user.projectsText} placeholder={'プロジェクト番号 | プロジェクト名\n例：123456789 | 営業部'} onChange={event => updateDraft(user.id, { projectsText: event.target.value })} />
            <small>1行に1件、「番号 | わかりやすい名前」で入力します。</small>
          </label>}
          <button type="button" className="secondary-button" disabled={busy === user.id} onClick={() => void saveUser(user)}>{busy === user.id ? '保存中…' : 'この利用者の設定を保存'}</button>
        </article>)}
      </section>}
      {message && <div className={message.includes('保存しました') || message.includes('利用登録しました') ? 'wbs-message' : 'error-message'} role="status">{message}</div>}
    </div>
  )
}
