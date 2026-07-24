import { useEffect, useMemo, useRef, useState } from 'react'
import { requestJson } from './api'
import WbsImport from './WbsImport'
import './App.css'

type InputSource = 'text' | 'paste' | 'voice' | 'clipboard' | 'launcher' | 'image' | 'minutes'
type TaskCandidate = { id: string; taskRequestId: string; title: string; description: string; assignee: string | null; dueDate: string | null; subtasks: string[]; projectGid: string | null; sectionGid: string | null; tags: string[]; customFields: Record<string, string>; priority: string | null }
type CandidateDraft = TaskCandidate & { subtasksText: string; tagsText: string; customFieldsText: string }
type OrganizeResponse = { taskRequestId: string; status: string; candidate: TaskCandidate }
type SubtaskRegistrationResponse = { taskCandidateSubtaskId: string; title: string; succeeded: boolean; provider: string; externalTaskGid: string | null; externalTaskUrl: string | null; errorMessage: string | null }
type RegistrationResponse = { registrationId: string; taskCandidateId: string; succeeded: boolean; alreadyRegistered: boolean; provider: string; externalTaskGid: string | null; externalTaskUrl: string | null; errorMessage: string | null; assigneeResolutionStatus: string | null; resolvedAssigneeGid: string | null; resolvedAssigneeName: string | null; warningMessage: string | null; subtasks: SubtaskRegistrationResponse[] }
type HealthResponse = { status: string; database: string; organizer: string; asana: string }
type ImportStatus = { kind: 'clipboard' | 'image' | 'minutes' | 'voice'; text: string; progress?: number }
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

const maxInputLength = 10_000
const maxImageBytes = 10 * 1024 * 1024
const maxMinutesBytes = 2 * 1024 * 1024
const acceptedMinutesExtensions = ['.txt', '.md', '.csv']
const acceptedImageTypes = ['image/jpeg', 'image/png', 'image/webp']
const isLauncher = new URLSearchParams(window.location.search).get('launcher') === '1'

function toDraft(candidate: TaskCandidate): CandidateDraft {
  return { ...candidate, subtasksText: candidate.subtasks.join('\n'), tagsText: candidate.tags.join(', '), customFieldsText: Object.keys(candidate.customFields).length ? JSON.stringify(candidate.customFields, null, 2) : '' }
}

function parseSubtasks(value: string) {
  return Array.from(new Set(value.split('\n').map((subtask) => subtask.trim()).filter(Boolean)))
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
    subtasks: parseSubtasks(candidate.subtasksText),
    dueDate: candidate.dueDate || null, projectGid: candidate.projectGid?.trim() || null, sectionGid: candidate.sectionGid?.trim() || null,
    tags: candidate.tagsText.split(',').map((tag) => tag.trim()).filter(Boolean), customFields, priority: candidate.priority || null,
  }
}

function readableSource(source: InputSource) {
  const labels: Record<InputSource, string> = {
    text: 'テキスト', paste: '貼り付け', voice: '音声', clipboard: 'クリップボード', launcher: 'ランチャー', image: '画像OCR', minutes: '議事録',
  }
  return labels[source]
}

async function readMinutesText(file: File) {
  const buffer = await file.arrayBuffer()
  try {
    return new TextDecoder('utf-8', { fatal: true }).decode(buffer)
  } catch {
    return new TextDecoder('shift_jis').decode(buffer)
  }
}

function App() {
  const [mode, setMode] = useState<'capture' | 'wbs'>('capture')
  const [rawText, setRawText] = useState('')
  const [source, setSource] = useState<InputSource>('text')
  const [candidate, setCandidate] = useState<CandidateDraft | null>(null)
  const [registration, setRegistration] = useState<RegistrationResponse | null>(null)
  const [health, setHealth] = useState<HealthResponse | null>(null)
  const [busy, setBusy] = useState<'organize' | 'register' | null>(null)
  const [mediaBusy, setMediaBusy] = useState<'image' | 'minutes' | null>(null)
  const [message, setMessage] = useState<string | null>(null)
  const [importStatus, setImportStatus] = useState<ImportStatus | null>(null)
  const [imagePreviewUrl, setImagePreviewUrl] = useState<string | null>(null)
  const [listening, setListening] = useState(false)
  const inputRef = useRef<HTMLTextAreaElement>(null)
  const imageInputRef = useRef<HTMLInputElement>(null)
  const minutesInputRef = useRef<HTMLInputElement>(null)
  const recognitionRef = useRef<SpeechRecognitionLike | null>(null)
  const speechSupported = useMemo(() => Boolean(window.SpeechRecognition ?? window.webkitSpeechRecognition), [])
  const isWorking = busy !== null || mediaBusy !== null || listening

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
        setRawText(data.text.slice(0, maxInputLength)); setSource('launcher'); setCandidate(null); setRegistration(null)
        setImportStatus({ kind: 'clipboard', text: 'ランチャーからクリップボードの内容を取り込みました。' })
        window.setTimeout(() => inputRef.current?.focus(), 0)
      }
    }
    webview.addEventListener('message', receiveLauncherMessage)
    webview.postMessage({ type: 'web-ready' })
    return () => webview.removeEventListener('message', receiveLauncherMessage)
  }, [])

  useEffect(() => () => recognitionRef.current?.stop(), [])
  useEffect(() => () => { if (imagePreviewUrl) URL.revokeObjectURL(imagePreviewUrl) }, [imagePreviewUrl])

  const clearDerivedResults = () => {
    setCandidate(null)
    setRegistration(null)
  }

  const appendExtractedText = (text: string, nextSource: InputSource) => {
    const normalized = text.replace(/\r\n/g, '\n').replace(/[ \t]+\n/g, '\n').trim()
    setRawText((current) => {
      const separator = current.trim() ? '\n\n' : ''
      return `${current}${separator}${normalized}`.slice(0, maxInputLength)
    })
    setSource(nextSource)
    if (nextSource !== 'image') setImagePreviewUrl(null)
    clearDerivedResults()
    window.setTimeout(() => inputRef.current?.focus(), 0)
  }

  const setDraftField = <K extends keyof CandidateDraft>(field: K, value: CandidateDraft[K]) => {
    setCandidate((current) => current ? { ...current, [field]: value } : current)
    setRegistration(null)
  }

  const readClipboard = async () => {
    setMessage(null)
    try {
      const text = await navigator.clipboard.readText()
      if (!text.trim()) throw new Error('クリップボードにテキストがありません。')
      appendExtractedText(text, 'clipboard')
      setImportStatus({ kind: 'clipboard', text: 'クリップボードの内容を取り込みました。' })
    } catch (error) {
      setMessage(error instanceof Error ? error.message : 'クリップボードを読み取れませんでした。')
    }
  }

  const importMinutes = async (file: File) => {
    setMessage(null)
    const extension = file.name.slice(file.name.lastIndexOf('.')).toLowerCase()
    if (!acceptedMinutesExtensions.includes(extension)) {
      setMessage('議事録ファイルは .txt、.md、.csv に対応しています。')
      return
    }
    if (file.size > maxMinutesBytes) {
      setMessage('議事録ファイルは2MB以下にしてください。')
      return
    }
    setMediaBusy('minutes')
    setImportStatus({ kind: 'minutes', text: `${file.name} を読み込んでいます…` })
    try {
      const text = await readMinutesText(file)
      if (!text.trim()) throw new Error('議事録ファイルに文字がありません。')
      appendExtractedText(text, 'minutes')
      const truncated = text.trim().length > maxInputLength
      setImportStatus({ kind: 'minutes', text: `${file.name} から${Math.min(text.trim().length, maxInputLength).toLocaleString()}文字を取り込みました${truncated ? '（上限まで）' : ''}。` })
    } catch (error) {
      setImportStatus(null)
      setMessage(error instanceof Error ? error.message : '議事録ファイルを読み込めませんでした。')
    } finally {
      setMediaBusy(null)
    }
  }

  const importImage = async (file: File) => {
    setMessage(null)
    if (!acceptedImageTypes.includes(file.type)) {
      setMessage('画像はJPEG、PNG、WebPに対応しています。iPhoneでは「写真を撮る」も利用できます。')
      return
    }
    if (file.size > maxImageBytes) {
      setMessage('画像は10MB以下にしてください。')
      return
    }

    setImagePreviewUrl(URL.createObjectURL(file))
    setMediaBusy('image')
    setImportStatus({ kind: 'image', text: '日本語OCRを準備しています…', progress: 0 })
    try {
      const { createWorker } = await import('tesseract.js')
      const worker = await createWorker('jpn', undefined, {
        logger: ({ status, progress }) => {
          if (status === 'recognizing text') {
            const percentage = Math.max(0, Math.min(100, Math.round(progress * 100)))
            setImportStatus({ kind: 'image', text: `画像の文字を読み取っています… ${percentage}%`, progress: percentage })
          }
        },
      })
      try {
        const result = await worker.recognize(file)
        const text = result.data.text.trim()
        if (!text) throw new Error('画像から文字を読み取れませんでした。文字が大きく写った画像をお試しください。')
        appendExtractedText(text, 'image')
        const truncated = text.length > maxInputLength
        setImportStatus({ kind: 'image', text: `${file.name} から${Math.min(text.length, maxInputLength).toLocaleString()}文字を抽出しました${truncated ? '（上限まで）' : ''}。`, progress: 100 })
      } finally {
        await worker.terminate()
      }
    } catch (error) {
      setImportStatus(null)
      setMessage(error instanceof Error ? error.message : '画像OCRに失敗しました。')
    } finally {
      setMediaBusy(null)
    }
  }

  const handlePaste = (event: React.ClipboardEvent<HTMLTextAreaElement>) => {
    const imageItem = Array.from(event.clipboardData.items).find((item) => item.kind === 'file' && item.type.startsWith('image/'))
    const image = imageItem?.getAsFile()
    if (image) {
      event.preventDefault()
      void importImage(image)
      return
    }
    setSource('paste')
    setImportStatus({ kind: 'clipboard', text: '貼り付けた内容を入力しました。' })
    clearDerivedResults()
  }

  const toggleSpeech = () => {
    setMessage(null)
    if (listening) { recognitionRef.current?.stop(); return }
    const Recognition = window.SpeechRecognition ?? window.webkitSpeechRecognition
    if (!Recognition) {
      setMessage('このブラウザーは音声認識に対応していません。端末キーボードのマイク入力をご利用ください。')
      return
    }
    const recognition = new Recognition()
    recognition.lang = 'ja-JP'; recognition.continuous = true; recognition.interimResults = false
    recognition.onresult = (event) => {
      let transcript = ''
      for (let index = event.resultIndex; index < event.results.length; index += 1) if (event.results[index].isFinal) transcript += event.results[index][0].transcript
      if (transcript) {
        appendExtractedText(transcript, 'voice')
        setImportStatus({ kind: 'voice', text: '認識した音声を入力欄へ追加しました。続けて話せます。' })
      }
    }
    recognition.onerror = () => {
      setMessage('音声を認識できませんでした。マイク権限とブラウザー設定をご確認ください。')
      setImportStatus(null)
    }
    recognition.onend = () => {
      setListening(false)
      setImportStatus((current) => current?.kind === 'voice' ? { kind: 'voice', text: '音声入力を終了しました。' } : current)
    }
    recognitionRef.current = recognition
    recognition.start()
    setListening(true)
    setImportStatus({ kind: 'voice', text: '音声を聞いています。登録したい内容を日本語で話してください。' })
  }

  const organize = async () => {
    if (!rawText.trim()) { setMessage('登録したい内容を入力してください。'); inputRef.current?.focus(); return }
    setBusy('organize'); setMessage(null); setRegistration(null)
    try {
      const result = await requestJson<OrganizeResponse>('/api/task-requests/organize', { method: 'POST', body: JSON.stringify({ rawText, source }) })
      setCandidate(toDraft(result.candidate)); window.setTimeout(() => document.getElementById('candidate-title')?.focus(), 0)
    } catch (error) {
      setMessage(error instanceof Error ? error.message : 'AI整理に失敗しました。')
    } finally {
      setBusy(null)
    }
  }

  const register = async () => {
    if (!candidate) return
    if (!candidate.title.trim()) { setMessage('タスクタイトルを入力してください。'); document.getElementById('candidate-title')?.focus(); return }
    setBusy('register'); setMessage(null)
    try {
      const result = await requestJson<RegistrationResponse>(`/api/task-candidates/${candidate.id}/register`, { method: 'POST', body: JSON.stringify(candidatePayload(candidate)) })
      setRegistration(result); window.chrome?.webview?.postMessage({ type: 'registration-complete', succeeded: true })
    } catch (error) {
      setMessage(error instanceof Error ? error.message : 'Asana登録に失敗しました。')
    } finally {
      setBusy(null)
    }
  }

  const reset = () => {
    recognitionRef.current?.stop()
    setRawText(''); setSource('text'); setCandidate(null); setRegistration(null); setMessage(null); setImportStatus(null); setImagePreviewUrl(null)
    window.setTimeout(() => inputRef.current?.focus(), 0)
  }

  const handleShortcut = (event: React.KeyboardEvent) => {
    if ((event.ctrlKey || event.metaKey) && event.key === 'Enter') { event.preventDefault(); if (candidate) void register(); else void organize() }
  }

  return (
    <main className={`app-shell ${isLauncher ? 'launcher-shell' : ''}`} onKeyDown={handleShortcut}>
      <header className="topbar">
        <h1>Asanaへタスク登録</h1>
        <div className={`connection ${health ? 'online' : 'offline'}`} title={health ? `DB: ${health.database} / AI: ${health.organizer} / Asana: ${health.asana}` : 'APIへ接続できません'}><span aria-hidden="true" />{health ? 'API接続' : '未接続'}</div>
      </header>

      {!isLauncher && <nav className="mode-switch" aria-label="登録方法">
        <button type="button" className={mode === 'capture' ? 'active' : ''} onClick={() => setMode('capture')}>すぐ登録</button>
        <button type="button" className={mode === 'wbs' ? 'active' : ''} onClick={() => setMode('wbs')}>WBS一括取込</button>
      </nav>}

      {mode === 'wbs' && !isLauncher
        ? <WbsImport />
        : <>

      <section className="panel input-panel" aria-labelledby="input-heading">
        <div className="section-heading">
          <h2 id="input-heading">内容を入力</h2>
          <span className="source-badge">{readableSource(source)}</span>
        </div>

        <div className="input-source-grid" aria-label="入力方法">
          <button type="button" className="source-button" onClick={readClipboard} disabled={isWorking} title="クリップボードから貼り付ける">
            <span className="tool-icon" aria-hidden="true">▣</span><strong>貼り付け</strong>
          </button>
          <button type="button" className="source-button" onClick={() => minutesInputRef.current?.click()} disabled={isWorking} title="TXT・MD・CSVを読み込む">
            <span className="tool-icon" aria-hidden="true">文</span><strong>議事録</strong>
          </button>
          <button type="button" className="source-button" onClick={() => imageInputRef.current?.click()} disabled={isWorking} title="画像を端末内で文字化する">
            <span className="tool-icon" aria-hidden="true">画</span><strong>画像</strong>
          </button>
          <button type="button" className={`source-button ${listening ? 'recording' : ''}`} onClick={toggleSpeech} aria-pressed={listening} disabled={isWorking && !listening} title={speechSupported ? '日本語で音声入力する' : 'ブラウザー非対応時は端末のマイク入力を利用できます'}>
            <span className="tool-icon microphone" aria-hidden="true">●</span><strong>{listening ? '停止' : '音声'}</strong>
          </button>
        </div>
        <input ref={minutesInputRef} hidden type="file" tabIndex={-1} aria-hidden="true" accept=".txt,.md,.csv,text/plain,text/markdown,text/csv" onChange={(event) => { const file = event.target.files?.[0]; event.target.value = ''; if (file) void importMinutes(file) }} />
        <input ref={imageInputRef} hidden type="file" tabIndex={-1} aria-hidden="true" accept="image/jpeg,image/png,image/webp" capture="environment" onChange={(event) => { const file = event.target.files?.[0]; event.target.value = ''; if (file) void importImage(file) }} />

        {importStatus && <div className={`import-status ${importStatus.kind}`} aria-live="polite">
          {imagePreviewUrl && importStatus.kind === 'image' && <img src={imagePreviewUrl} alt="読み取る画像のプレビュー" />}
          <span className="status-icon" aria-hidden="true">{mediaBusy ? '…' : '✓'}</span>
          <div><strong>{importStatus.kind === 'image' ? '画像OCR' : importStatus.kind === 'minutes' ? '議事録' : importStatus.kind === 'voice' ? '音声入力' : '取り込み'}</strong><p>{importStatus.text}</p>{typeof importStatus.progress === 'number' && <div className="progress-track"><span style={{ width: `${importStatus.progress}%` }} /></div>}</div>
        </div>}

        <div className="input-label-row"><label htmlFor="task-input">タスク内容</label><span className="char-count">{rawText.length.toLocaleString()} / {maxInputLength.toLocaleString()}</span></div>
        <textarea id="task-input" ref={inputRef} value={rawText} maxLength={maxInputLength} rows={isLauncher ? 3 : 5} placeholder={'例：明日までに見積書を確認する\n担当：田中さん'}
          onChange={(event) => { setRawText(event.target.value); setSource('text'); setMessage(null); setImportStatus(null); setImagePreviewUrl(null); clearDerivedResults() }} onPaste={handlePaste} aria-label="タスクにしたい内容" />
        <button type="button" className="primary-button" onClick={organize} disabled={isWorking || !rawText.trim()} title="Ctrl+Enterでも実行できます">
          {busy === 'organize' ? <span className="spinner" aria-hidden="true" /> : <span aria-hidden="true">✦</span>}
          <span>{mediaBusy === 'image' ? '画像を読み取り中…' : busy === 'organize' ? '整理中…' : 'AIで整理'}</span>
        </button>
      </section>

      {candidate && <section className="panel candidate-panel" aria-labelledby="candidate-heading">
        <div className="section-heading"><h2 id="candidate-heading">登録内容を確認</h2><span className="ready-badge">編集可</span></div>
        <div className="field full-field"><label htmlFor="candidate-title">タスクタイトル <strong>必須</strong></label><input id="candidate-title" value={candidate.title} maxLength={200} onChange={(event) => setDraftField('title', event.target.value)} /></div>
        <div className="field full-field"><label htmlFor="candidate-description">タスク内容</label><textarea id="candidate-description" value={candidate.description} maxLength={maxInputLength} rows={isLauncher ? 3 : 4} onChange={(event) => setDraftField('description', event.target.value)} /></div>
        <div className="field-grid">
          <div className="field"><label htmlFor="candidate-assignee">担当者</label><input id="candidate-assignee" value={candidate.assignee ?? ''} maxLength={200} placeholder="名前 / me / Asana GID" onChange={(event) => setDraftField('assignee', event.target.value)} /></div>
          <div className="field"><label htmlFor="candidate-due">期限</label><input id="candidate-due" type="date" value={candidate.dueDate ?? ''} onChange={(event) => setDraftField('dueDate', event.target.value || null)} /></div>
        </div>
        <div className="field full-field subtask-field">
          <div className="subtask-label-row"><label htmlFor="candidate-subtasks">サブタスク</label><span>{parseSubtasks(candidate.subtasksText).length}件</span></div>
          <textarea id="candidate-subtasks" value={candidate.subtasksText} maxLength={2_000} rows={isLauncher ? 4 : 5} placeholder={'1行に1件ずつ入力\n例：冷蔵庫の食材を確認する'} onChange={(event) => setDraftField('subtasksText', event.target.value)} />
        </div>
        <details className="advanced"><summary>詳細設定</summary><div className="advanced-grid">
          <div className="field"><label htmlFor="project-gid">プロジェクト GID</label><input id="project-gid" inputMode="numeric" value={candidate.projectGid ?? ''} onChange={(event) => setDraftField('projectGid', event.target.value)} /></div>
          <div className="field"><label htmlFor="section-gid">セクション GID</label><input id="section-gid" inputMode="numeric" value={candidate.sectionGid ?? ''} onChange={(event) => setDraftField('sectionGid', event.target.value)} /></div>
          <div className="field"><label htmlFor="tags">タグ GID</label><input id="tags" value={candidate.tagsText} placeholder="123, 456" onChange={(event) => setDraftField('tagsText', event.target.value)} /></div>
          <div className="field"><label htmlFor="priority">優先度</label><select id="priority" value={candidate.priority ?? ''} onChange={(event) => setDraftField('priority', event.target.value || null)}><option value="">未設定</option><option value="low">低</option><option value="normal">通常</option><option value="high">高</option><option value="urgent">緊急</option></select></div>
          <div className="field full-field"><label htmlFor="custom-fields">カスタムフィールド</label><textarea id="custom-fields" rows={3} value={candidate.customFieldsText} placeholder={'{"フィールドGID":"値"}'} onChange={(event) => setDraftField('customFieldsText', event.target.value)} /></div>
        </div></details>
        <button type="button" className="asana-button" onClick={register} disabled={busy !== null || Boolean(registration)}>{busy === 'register' ? <span className="spinner" aria-hidden="true" /> : <span className="check" aria-hidden="true">✓</span>}{busy === 'register' ? '登録しています…' : registration ? '登録済み' : 'この内容でAsanaへ登録'}</button>
      </section>}

      {registration && <section className="success-card" aria-live="polite"><div className="success-icon" aria-hidden="true">✓</div><div><strong>{registration.provider === 'Mock' ? 'モックへ登録しました' : 'Asanaへ登録しました'}</strong><p>{registration.externalTaskGid ? `Task ID: ${registration.externalTaskGid}` : '登録履歴を保存しました。'}{registration.subtasks.length > 0 ? ` / サブタスク ${registration.subtasks.filter((item) => item.succeeded).length}件` : ''}</p>{registration.resolvedAssigneeName && <p className="assignee-result">担当者: {registration.resolvedAssigneeName}</p>}{registration.warningMessage && <p className="registration-warning">! {registration.warningMessage}</p>}{registration.externalTaskUrl && <a href={registration.externalTaskUrl} target="_blank" rel="noreferrer">Asanaで確認する ↗</a>}</div><button type="button" onClick={reset}>続けて登録</button></section>}
      {message && <div className="error-message" role="alert"><span aria-hidden="true">!</span>{message}</div>}
      <footer><button type="button" onClick={reset} disabled={!rawText && !candidate}>入力をクリア</button></footer>
        </>}
    </main>
  )
}

export default App
