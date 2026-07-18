import { useEffect, useMemo, useRef, useState } from 'react'
import './App.css'

type InputSource = 'text' | 'paste' | 'voice' | 'clipboard' | 'launcher'
type TaskCandidate = { id: string; taskRequestId: string; title: string; description: string; assignee: string | null; dueDate: string | null; projectGid: string | null; sectionGid: string | null; tags: string[]; customFields: Record<string, string>; priority: string | null }
type CandidateDraft = TaskCandidate & { tagsText: string; customFieldsText: string }
type OrganizeResponse = { taskRequestId: string; status: string; candidate: TaskCandidate }
type RegistrationResponse = { registrationId: string; taskCandidateId: string; succeeded: boolean; alreadyRegistered: boolean; provider: string; externalTaskGid: string | null; externalTaskUrl: string | null; errorMessage: string | null }
type HealthResponse = { status: string; database: string; organizer: string; asana: string }
type SpeechRecognitionEventLike = { results: ArrayLike<{ 0: { transcript: string }; isFinal: boolean }>; resultIndex: number }
type SpeechRecognitionLike = { lang: string; continuous: boolean; interimResults: boolean; start: () => void; stop: () => void; onresult: ((event: SpeechRecognitionEventLike) => void) | null; onend: (() => void) | null; onerror: (() => void) | null }
type SpeechRecognitionConstructor = new () => SpeechRecognitionLike

declare global {
  interface Window {
    SpeechRecognition?: SpeechRecognitionConstructor
    webkitSpeechRecognition?: SpeechRecognitionConstructor
    chrome?: { webview?: { addEventListener: (name: 'message', handler: (event: MessageEvent) => void) => void; removeEventListener: (name: 'message', handler: (event: MessageEvent) => void) => void; postMessage: (message: unknown) => void } }
  }
}

const apiBase = import.meta.env.VITE_API_BASE_URL ?? ''

function getClientKey() {
  const storageKey = 'task-capture-client-key'
  const current = localStorage.getItem(storageKey)
  if (current) return current
  const generated = globalThis.crypto?.randomUUID?.()
    ?? `web-${Date.now()}-${Math.random().toString(16).slice(2)}`
  localStorage.setItem(storageKey, generated)
  return generated
}

async function requestJson<T>(path: string, init?: RequestInit): Promise<T> {
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

function toDraft(candidate: TaskCandidate): CandidateDraft {
  return { ...candidate, tagsText: candidate.tags.join(', '), customFieldsText: Object.keys(candidate.customFields).length ? JSON.stringify(candidate.customFields, null, 2) : '' }
}

function candidatePayload(candidate: CandidateDraft) {
  let customFields: Record<string, string> = {}
  if (candidate.customFieldsText.trim()) {
    const parsed: unknown = JSON.parse(candidate.customFieldsText)
    if (!parsed || Array.isArray(parsed) || typeof parsed !== 'object') throw new Error('カスタムフィールドは {"GID":"値"} 形式で入力してください。')
    customFields = Object.fromEntries(Object.entries(parsed).map(([key, value]) => [key, String(value)]))
  }
  return {
    title: candidate.title.trim(), description: candidate.description.trim(), assignee: candidate.assignee?.trim() || null,
    dueDate: candidate.dueDate || null, projectGid: candidate.projectGid?.trim() || null, sectionGid: candidate.sectionGid?.trim() || null,
    tags: candidate.tagsText.split(',').map((tag) => tag.trim()).filter(Boolean), customFields, priority: candidate.priority || null,
  }
}

function App() {
  const [rawText, setRawText] = useState('')
  const [source, setSource] = useState<InputSource>('text')
  const [candidate, setCandidate] = useState<CandidateDraft | null>(null)
  const [registration, setRegistration] = useState<RegistrationResponse | null>(null)
  const [health, setHealth] = useState<HealthResponse | null>(null)
  const [busy, setBusy] = useState<'organize' | 'register' | null>(null)
  const [message, setMessage] = useState<string | null>(null)
  const [listening, setListening] = useState(false)
  const inputRef = useRef<HTMLTextAreaElement>(null)
  const recognitionRef = useRef<SpeechRecognitionLike | null>(null)
  const speechSupported = useMemo(() => Boolean(window.SpeechRecognition ?? window.webkitSpeechRecognition), [])

  useEffect(() => {
    requestJson<HealthResponse>('/api/health').then(setHealth).catch(() => setHealth(null))
    inputRef.current?.focus()
  }, [])

  useEffect(() => {
    const webview = window.chrome?.webview
    if (!webview) return
    const receiveLauncherMessage = (event: MessageEvent) => {
      const data = event.data as { type?: string; text?: string }
      if (data?.type === 'clipboard' && data.text) {
        setRawText(data.text.slice(0, 10_000)); setSource('launcher'); setCandidate(null); setRegistration(null)
        window.setTimeout(() => inputRef.current?.focus(), 0)
      }
    }
    webview.addEventListener('message', receiveLauncherMessage)
    webview.postMessage({ type: 'web-ready' })
    return () => webview.removeEventListener('message', receiveLauncherMessage)
  }, [])

  useEffect(() => () => recognitionRef.current?.stop(), [])

  const setDraftField = <K extends keyof CandidateDraft>(field: K, value: CandidateDraft[K]) => {
    setCandidate((current) => current ? { ...current, [field]: value } : current)
    setRegistration(null)
  }

  const readClipboard = async () => {
    setMessage(null)
    try {
      const text = await navigator.clipboard.readText()
      if (!text.trim()) throw new Error('クリップボードにテキストがありません。')
      setRawText(text.slice(0, 10_000)); setSource('clipboard'); setCandidate(null); setRegistration(null); inputRef.current?.focus()
    } catch (error) { setMessage(error instanceof Error ? error.message : 'クリップボードを読み取れませんでした。') }
  }

  const toggleSpeech = () => {
    setMessage(null)
    if (listening) { recognitionRef.current?.stop(); return }
    const Recognition = window.SpeechRecognition ?? window.webkitSpeechRecognition
    if (!Recognition) { setMessage('このブラウザーは音声入力に対応していません。キーボード入力をご利用ください。'); return }
    const recognition = new Recognition()
    recognition.lang = 'ja-JP'; recognition.continuous = true; recognition.interimResults = false
    recognition.onresult = (event) => {
      let transcript = ''
      for (let index = event.resultIndex; index < event.results.length; index += 1) if (event.results[index].isFinal) transcript += event.results[index][0].transcript
      if (transcript) {
        setRawText((current) => `${current}${current.trim() ? '\n' : ''}${transcript}`.slice(0, 10_000))
        setSource('voice'); setCandidate(null); setRegistration(null)
      }
    }
    recognition.onerror = () => setMessage('音声を認識できませんでした。マイク権限とブラウザー設定をご確認ください。')
    recognition.onend = () => setListening(false)
    recognitionRef.current = recognition; recognition.start(); setListening(true)
  }

  const organize = async () => {
    if (!rawText.trim()) { setMessage('登録したい内容を入力してください。'); inputRef.current?.focus(); return }
    setBusy('organize'); setMessage(null); setRegistration(null)
    try {
      const result = await requestJson<OrganizeResponse>('/api/task-requests/organize', { method: 'POST', body: JSON.stringify({ rawText, source }) })
      setCandidate(toDraft(result.candidate)); window.setTimeout(() => document.getElementById('candidate-title')?.focus(), 0)
    } catch (error) { setMessage(error instanceof Error ? error.message : 'AI整理に失敗しました。') }
    finally { setBusy(null) }
  }

  const register = async () => {
    if (!candidate) return
    if (!candidate.title.trim()) { setMessage('タスクタイトルを入力してください。'); document.getElementById('candidate-title')?.focus(); return }
    setBusy('register'); setMessage(null)
    try {
      const result = await requestJson<RegistrationResponse>(`/api/task-candidates/${candidate.id}/register`, { method: 'POST', body: JSON.stringify(candidatePayload(candidate)) })
      setRegistration(result); window.chrome?.webview?.postMessage({ type: 'registration-complete', succeeded: true })
    } catch (error) { setMessage(error instanceof Error ? error.message : 'Asana登録に失敗しました。') }
    finally { setBusy(null) }
  }

  const reset = () => {
    setRawText(''); setSource('text'); setCandidate(null); setRegistration(null); setMessage(null)
    window.setTimeout(() => inputRef.current?.focus(), 0)
  }

  const handleShortcut = (event: React.KeyboardEvent) => {
    if ((event.ctrlKey || event.metaKey) && event.key === 'Enter') { event.preventDefault(); if (candidate) void register(); else void organize() }
  }

  return (
    <main className="app-shell" onKeyDown={handleShortcut}>
      <header className="topbar">
        <div><p className="eyebrow">TASK CAPTURE</p><h1>思いついたら、すぐ Asana へ。</h1></div>
        <div className={`connection ${health ? 'online' : 'offline'}`} title={health ? `DB: ${health.database} / Asana: ${health.asana}` : 'APIへ接続できません'}><span aria-hidden="true" />{health ? health.asana : 'offline'}</div>
      </header>

      <ol className="steps" aria-label="登録手順">
        <li className="active"><span>1</span>入力</li><li className={candidate ? 'active' : ''}><span>2</span>確認</li><li className={registration ? 'active' : ''}><span>3</span>登録</li>
      </ol>

      <section className="panel input-panel" aria-labelledby="input-heading">
        <div className="section-heading"><div><p className="step-label">STEP 1</p><h2 id="input-heading">何をしておきますか？</h2></div><span className="char-count">{rawText.length.toLocaleString()} / 10,000</span></div>
        <textarea ref={inputRef} value={rawText} maxLength={10_000} rows={6} placeholder={'例：明日までに見積書を確認する\n担当：田中さん'}
          onChange={(event) => { setRawText(event.target.value); setSource('text'); setCandidate(null); setRegistration(null) }} onPaste={() => setSource('paste')} aria-label="タスクにしたい内容" />
        <div className="input-actions">
          <button type="button" className="tool-button" onClick={readClipboard}><span aria-hidden="true">▣</span> クリップボード</button>
          <button type="button" className={`tool-button ${listening ? 'recording' : ''}`} onClick={toggleSpeech} aria-pressed={listening}><span aria-hidden="true">●</span> {listening ? '音声を停止' : '音声で入力'}</button>
          {!speechSupported && <span className="support-note">音声非対応</span>}
        </div>
        <button type="button" className="primary-button" onClick={organize} disabled={busy !== null || !rawText.trim()}>
          {busy === 'organize' ? <span className="spinner" aria-hidden="true" /> : <span aria-hidden="true">✦</span>}{busy === 'organize' ? '整理しています…' : 'AIで整理'}<kbd>Ctrl ↵</kbd>
        </button>
      </section>

      {candidate && <section className="panel candidate-panel" aria-labelledby="candidate-heading">
        <div className="section-heading"><div><p className="step-label">STEP 2</p><h2 id="candidate-heading">この内容で登録しますか？</h2></div><span className="ready-badge">編集できます</span></div>
        <div className="field full-field"><label htmlFor="candidate-title">タスクタイトル <strong>必須</strong></label><input id="candidate-title" value={candidate.title} maxLength={200} onChange={(event) => setDraftField('title', event.target.value)} /></div>
        <div className="field full-field"><label htmlFor="candidate-description">タスク内容</label><textarea id="candidate-description" value={candidate.description} maxLength={10_000} rows={5} onChange={(event) => setDraftField('description', event.target.value)} /></div>
        <div className="field-grid">
          <div className="field"><label htmlFor="candidate-assignee">担当者</label><input id="candidate-assignee" value={candidate.assignee ?? ''} maxLength={200} placeholder="名前 / me / Asana GID" onChange={(event) => setDraftField('assignee', event.target.value)} /><small>名前はメモとして保持。me または GID は Asana に設定。</small></div>
          <div className="field"><label htmlFor="candidate-due">期限</label><input id="candidate-due" type="date" value={candidate.dueDate ?? ''} onChange={(event) => setDraftField('dueDate', event.target.value || null)} /></div>
        </div>
        <details className="advanced"><summary>詳細設定 <span>必要なときだけ</span></summary><div className="advanced-grid">
          <div className="field"><label htmlFor="project-gid">プロジェクト GID</label><input id="project-gid" inputMode="numeric" value={candidate.projectGid ?? ''} onChange={(event) => setDraftField('projectGid', event.target.value)} /></div>
          <div className="field"><label htmlFor="section-gid">セクション GID</label><input id="section-gid" inputMode="numeric" value={candidate.sectionGid ?? ''} onChange={(event) => setDraftField('sectionGid', event.target.value)} /></div>
          <div className="field"><label htmlFor="tags">タグ GID</label><input id="tags" value={candidate.tagsText} placeholder="123, 456" onChange={(event) => setDraftField('tagsText', event.target.value)} /></div>
          <div className="field"><label htmlFor="priority">優先度</label><select id="priority" value={candidate.priority ?? ''} onChange={(event) => setDraftField('priority', event.target.value || null)}><option value="">未設定</option><option value="low">低</option><option value="normal">通常</option><option value="high">高</option><option value="urgent">緊急</option></select></div>
          <div className="field full-field"><label htmlFor="custom-fields">カスタムフィールド</label><textarea id="custom-fields" rows={3} value={candidate.customFieldsText} placeholder={'{"フィールドGID":"値"}'} onChange={(event) => setDraftField('customFieldsText', event.target.value)} /></div>
        </div></details>
        <button type="button" className="asana-button" onClick={register} disabled={busy !== null || Boolean(registration)}>{busy === 'register' ? <span className="spinner" aria-hidden="true" /> : <span className="check" aria-hidden="true">✓</span>}{busy === 'register' ? '登録しています…' : registration ? '登録済み' : 'Asanaへ登録'}</button>
      </section>}

      {registration && <section className="success-card" aria-live="polite"><div className="success-icon" aria-hidden="true">✓</div><div><strong>{registration.provider === 'Mock' ? 'モックへ登録しました' : 'Asanaへ登録しました'}</strong><p>{registration.externalTaskGid ? `Task ID: ${registration.externalTaskGid}` : '登録履歴を保存しました。'}</p>{registration.externalTaskUrl && <a href={registration.externalTaskUrl} target="_blank" rel="noreferrer">Asanaで開く ↗</a>}</div><button type="button" onClick={reset}>続けて登録</button></section>}
      {message && <div className="error-message" role="alert"><span aria-hidden="true">!</span>{message}</div>}
      <footer><span>入力内容は登録履歴としてサーバーに保存されます。</span><button type="button" onClick={reset} disabled={!rawText && !candidate}>入力をクリア</button></footer>
    </main>
  )
}

export default App
