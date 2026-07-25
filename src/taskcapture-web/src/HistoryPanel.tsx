import { useEffect, useState } from 'react'
import { requestJson } from './api'

type HistoryTask = {
  taskRequestId: string
  rawText: string
  source: string
  status: string
  createdAtUtc: string
  candidate: { title: string; assignee: string | null; dueDate: string | null } | null
  registration: { succeeded: boolean; externalTaskUrl: string | null; provider: string } | null
}

export default function HistoryPanel() {
  const [search, setSearch] = useState('')
  const [status, setStatus] = useState('')
  const [rows, setRows] = useState<HistoryTask[]>([])
  const [busy, setBusy] = useState(false)
  const [message, setMessage] = useState<string | null>(null)

  const load = async (nextSearch = search, nextStatus = status) => {
    setBusy(true)
    setMessage(null)
    try {
      const query = new URLSearchParams({ take: '100' })
      if (nextSearch.trim()) query.set('search', nextSearch.trim())
      if (nextStatus) query.set('status', nextStatus)
      setRows(await requestJson<HistoryTask[]>(`/api/task-requests/recent?${query}`))
    } catch (error) {
      setMessage(error instanceof Error ? error.message : '登録履歴を読み込めませんでした。')
    } finally {
      setBusy(false)
    }
  }

  useEffect(() => {
    let active = true
    setBusy(true)
    requestJson<HistoryTask[]>('/api/task-requests/recent?take=100')
      .then(result => { if (active) setRows(result) })
      .catch(error => {
        if (active) setMessage(error instanceof Error ? error.message : '登録履歴を読み込めませんでした。')
      })
      .finally(() => { if (active) setBusy(false) })
    return () => { active = false }
  }, [])

  const statusLabel = (value: string) => ({
    Received: '受付済み',
    Organized: '確認待ち',
    Registered: '登録済み',
    Failed: '失敗',
  }[value] ?? value)

  return (
    <section className="panel history-page">
      <div className="section-heading"><div><h2>自分の登録履歴</h2><p>タスク名や内容で検索できます。他の利用者の履歴は表示されません。</p></div><span>{rows.length}件</span></div>
      <form className="history-tools" onSubmit={event => { event.preventDefault(); void load() }}>
        <input value={search} maxLength={200} placeholder="タスク名・内容を検索" onChange={event => setSearch(event.target.value)} />
        <select value={status} onChange={event => { setStatus(event.target.value); void load(search, event.target.value) }}>
          <option value="">すべての状態</option>
          <option value="Registered">登録済み</option>
          <option value="Organized">確認待ち</option>
          <option value="Failed">失敗</option>
        </select>
        <button type="submit" className="secondary-button" disabled={busy}>{busy ? '検索中…' : '検索'}</button>
      </form>
      {rows.length === 0 && !busy && <p className="history-empty">条件に合う登録履歴はありません。</p>}
      <div className="history-list">
        {rows.map(row => <article key={row.taskRequestId} className="history-card">
          <div className="history-card-main">
            <strong>{row.candidate?.title || row.rawText.slice(0, 80)}</strong>
            <p>{new Date(row.createdAtUtc).toLocaleString('ja-JP')}{row.candidate?.assignee ? ` ・ 担当 ${row.candidate.assignee}` : ''}{row.candidate?.dueDate ? ` ・ 期限 ${row.candidate.dueDate}` : ''}</p>
          </div>
          <span className={`status-pill ${row.status.toLowerCase()}`}>{statusLabel(row.status)}</span>
          {row.registration?.externalTaskUrl && <a href={row.registration.externalTaskUrl} target="_blank" rel="noreferrer">Asanaで開く ↗</a>}
        </article>)}
      </div>
      {message && <div className="error-message" role="alert">{message}</div>}
    </section>
  )
}
