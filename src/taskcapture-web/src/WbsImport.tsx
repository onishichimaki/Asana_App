import { useEffect, useMemo, useRef, useState } from 'react'
import readXlsxFile from 'read-excel-file/browser'
import { apiBase, getClientKey, requestJson } from './api'

type Cell = string | number | boolean | Date | null
type SheetData = { name: string; rows: Cell[][] }
type HierarchyMode = 'none' | 'parentKey' | 'level' | 'columns'
type ColumnRole = 'ignore' | 'title' | 'description' | 'assignee' | 'dueDate' | 'key' | 'parentKey' | 'level' | 'hierarchy'
type Mapping = {
  hierarchyMode: HierarchyMode
  roles: Record<number, ColumnRole>
  titleSeparator: string
  descriptionSeparator: string
  dateFormat: 'auto' | 'yyyy-MM-dd' | 'yyyy/MM/dd' | 'yyyy.MM.dd' | 'MM/dd/yyyy'
}
type Profile = {
  id: string
  name: string
  layoutSignature: string
  sheetName: string
  headerRow: number
  dataStartRow: number
  mapping: Mapping
  projectGid: string | null
  sectionGid: string | null
  updatedAtUtc: string
}
type DraftRow = {
  sourceRowNumber: number
  sourceKey: string
  isGeneratedKey: boolean
  parentSourceKey: string | null
  depth: number
  sortOrder: number
  included: boolean
  title: string
  description: string
  assignee: string | null
  dueDate: string | null
  validationErrors: string[]
}
type BatchRow = DraftRow & {
  id: string
  parentRowId: string | null
  status: string
  provider: string | null
  externalTaskGid: string | null
  externalTaskUrl: string | null
  errorMessage: string | null
  assigneeResolutionStatus: string | null
  resolvedAssigneeName: string | null
  warningMessage: string | null
}
type Batch = {
  id: string
  status: string
  alreadyRegistered: boolean
  totalRows: number
  validRows: number
  succeededRows: number
  failedRows: number
  rows: BatchRow[]
}

function isBatchRow(row: DraftRow | BatchRow): row is BatchRow {
  return 'status' in row
}

const maxFileBytes = 10 * 1024 * 1024
const maxRows = 5_000
const emptyMapping: Mapping = {
  hierarchyMode: 'none',
  roles: {},
  titleSeparator: ' ',
  descriptionSeparator: '\n',
  dateFormat: 'auto',
}
const roleOptions: Array<{ value: ColumnRole; label: string }> = [
  { value: 'ignore', label: '使用しない' },
  { value: 'title', label: 'タイトル' },
  { value: 'description', label: '説明' },
  { value: 'assignee', label: '担当者' },
  { value: 'dueDate', label: '期限' },
  { value: 'key', label: '識別キー' },
  { value: 'parentKey', label: '親キー' },
  { value: 'level', label: '階層レベル' },
  { value: 'hierarchy', label: '階層列' },
]
const singleRoles = new Set<ColumnRole>(['assignee', 'dueDate', 'key', 'parentKey', 'level'])

function cellText(value: Cell | undefined) {
  if (value === null || value === undefined) return ''
  if (value instanceof Date) {
    const year = value.getFullYear()
    const month = String(value.getMonth() + 1).padStart(2, '0')
    const day = String(value.getDate()).padStart(2, '0')
    return `${year}-${month}-${day}`
  }
  return String(value).trim()
}

function toCell(value: unknown): Cell {
  if (value === null || typeof value === 'string' || typeof value === 'number' || typeof value === 'boolean') {
    return value
  }
  if (Object.prototype.toString.call(value) === '[object Date]') return value as Date
  return String(value)
}

function parseCsv(text: string) {
  const firstLine = text.split(/\r?\n/, 1)[0] ?? ''
  const delimiter = (firstLine.match(/\t/g)?.length ?? 0) > (firstLine.match(/,/g)?.length ?? 0) ? '\t' : ','
  const rows: string[][] = []
  let row: string[] = []
  let value = ''
  let quoted = false
  for (let index = 0; index < text.length; index += 1) {
    const character = text[index]
    if (quoted) {
      if (character === '"' && text[index + 1] === '"') {
        value += '"'
        index += 1
      } else if (character === '"') {
        quoted = false
      } else {
        value += character
      }
    } else if (character === '"') {
      quoted = true
    } else if (character === delimiter) {
      row.push(value)
      value = ''
    } else if (character === '\n') {
      row.push(value.replace(/\r$/, ''))
      rows.push(row)
      row = []
      value = ''
    } else {
      value += character
    }
  }
  if (value.length > 0 || row.length > 0) {
    row.push(value.replace(/\r$/, ''))
    rows.push(row)
  }
  return rows
}

async function sha256(value: ArrayBuffer | string) {
  const source = typeof value === 'string' ? new TextEncoder().encode(value) : value
  const hash = await crypto.subtle.digest('SHA-256', source)
  return Array.from(new Uint8Array(hash), byte => byte.toString(16).padStart(2, '0')).join('')
}

function columnLetter(index: number) {
  let value = index + 1
  let result = ''
  while (value > 0) {
    value -= 1
    result = String.fromCharCode(65 + (value % 26)) + result
    value = Math.floor(value / 26)
  }
  return result
}

function inferRoles(headers: string[]) {
  const roles: Record<number, ColumnRole> = {}
  headers.forEach((header, index) => {
    const normalized = header.replace(/\s/g, '').toLowerCase()
    if (/親.*(id|番号|キー)|上位.*(wbs|id|番号|キー)|parent/.test(normalized)) roles[index] = 'parentKey'
    else if (/wbs(no|番号)|管理番号|タスクid|^id$/.test(normalized)) roles[index] = 'key'
    else if (/階層|レベル|level/.test(normalized)) roles[index] = 'level'
    else if (/担当|assignee|owner/.test(normalized)) roles[index] = 'assignee'
    else if (/期限|完了予定|終了予定|due/.test(normalized)) roles[index] = 'dueDate'
    else if (/備考|説明|内容|成果物|description|notes/.test(normalized)) roles[index] = 'description'
    else if (/作業名|タスク名|件名|タイトル|task|title/.test(normalized)) roles[index] = 'title'
    else roles[index] = 'ignore'
  })
  const values = Object.values(roles)
  const hierarchyMode: HierarchyMode =
    values.includes('parentKey') && values.includes('key') ? 'parentKey'
      : values.includes('level') ? 'level' : 'none'
  return { ...emptyMapping, hierarchyMode, roles }
}

function parseDateValue(value: string, format: Mapping['dateFormat']) {
  const normalized = value.trim().replace(/[年月]/g, '/').replace(/日/g, '').replace(/[.]/g, '/')
  if (!normalized) return { value: null, error: null }
  let year: number
  let month: number
  let day: number
  const parts = normalized.split(/[/-]/).map(Number)
  if (format === 'MM/dd/yyyy') {
    [month, day, year] = parts
  } else {
    [year, month, day] = parts
  }
  if (format === 'auto' && parts[0] < 100 && parts[2] >= 1000) {
    [month, day, year] = parts
  }
  const date = new Date(Date.UTC(year!, month! - 1, day!))
  const valid = parts.length === 3 &&
    Number.isInteger(year!) &&
    date.getUTCFullYear() === year! &&
    date.getUTCMonth() === month! - 1 &&
    date.getUTCDate() === day!
  return valid
    ? { value: `${year!.toString().padStart(4, '0')}-${month!.toString().padStart(2, '0')}-${day!.toString().padStart(2, '0')}`, error: null }
    : { value: null, error: `期限「${value}」を日付に変換できません。` }
}

function normalizeRows(rows: Cell[][], dataStartRow: number, mapping: Mapping): DraftRow[] {
  const roleIndexes = (role: ColumnRole) =>
    Object.entries(mapping.roles)
      .filter(([, value]) => value === role)
      .map(([index]) => Number(index))
      .sort((left, right) => left - right)
  const titleColumns = roleIndexes('title')
  const descriptionColumns = roleIndexes('description')
  const assigneeColumn = roleIndexes('assignee')[0]
  const dueDateColumn = roleIndexes('dueDate')[0]
  const keyColumn = roleIndexes('key')[0]
  const parentColumn = roleIndexes('parentKey')[0]
  const levelColumn = roleIndexes('level')[0]
  const hierarchyColumns = roleIndexes('hierarchy')
  const sourceRows = rows.slice(dataStartRow - 1, dataStartRow - 1 + maxRows)
  const result: DraftRow[] = []
  const levelStack = new Map<number, string>()
  const hierarchyStack: string[] = []
  const hierarchyTasks = new Map<string, DraftRow>()

  const rowFields = (row: Cell[], sourceRowNumber: number) => {
    const title = titleColumns.map(index => cellText(row[index])).filter(Boolean).join(mapping.titleSeparator).trim()
    const description = descriptionColumns.map(index => cellText(row[index])).filter(Boolean).join(mapping.descriptionSeparator).trim()
    const assignee = assigneeColumn === undefined ? null : cellText(row[assigneeColumn]) || null
    const rawDueDate = dueDateColumn === undefined ? '' : cellText(row[dueDateColumn])
    const dueDate = parseDateValue(rawDueDate, mapping.dateFormat)
    return { title, description, assignee, dueDate, sourceRowNumber }
  }

  sourceRows.forEach((row, offset) => {
    const sourceRowNumber = dataStartRow + offset
    if (row.every(cell => !cellText(cell))) return
    const fields = rowFields(row, sourceRowNumber)

    if (mapping.hierarchyMode === 'columns') {
      let deepestKey: string | null = null
      hierarchyColumns.forEach((columnIndex, level) => {
        const value = cellText(row[columnIndex])
        if (!value) return
        hierarchyStack.splice(level)
        hierarchyStack[level] = value
        const parentSourceKey = level > 0 ? hierarchyStack.slice(0, level).join(' / ') : null
        const sourceKey = hierarchyStack.slice(0, level + 1).join(' / ')
        let task = hierarchyTasks.get(sourceKey)
        if (!task) {
          task = {
            sourceRowNumber,
            sourceKey: sourceKey.slice(0, 256),
            isGeneratedKey: false,
            parentSourceKey: parentSourceKey?.slice(0, 256) ?? null,
            depth: level,
            sortOrder: result.length,
            included: true,
            title: value,
            description: '',
            assignee: null,
            dueDate: null,
            validationErrors: level > 0 && !parentSourceKey ? ['上位の階層列が空です。'] : [],
          }
          hierarchyTasks.set(sourceKey, task)
          result.push(task)
        }
        deepestKey = sourceKey
      })
      const deepest = deepestKey ? hierarchyTasks.get(deepestKey) : undefined
      if (deepest) {
        deepest.description ||= fields.description
        deepest.assignee ||= fields.assignee
        deepest.dueDate ||= fields.dueDate.value
        if (fields.dueDate.error && !deepest.validationErrors.includes(fields.dueDate.error)) {
          deepest.validationErrors.push(fields.dueDate.error)
        }
      }
      return
    }

    const explicitKey = keyColumn === undefined ? '' : cellText(row[keyColumn])
    const sourceKey = explicitKey || `row-${sourceRowNumber}`
    let parentSourceKey: string | null = null
    let depth = 0
    const validationErrors = fields.dueDate.error ? [fields.dueDate.error] : []

    if (mapping.hierarchyMode === 'parentKey') {
      parentSourceKey = parentColumn === undefined ? null : cellText(row[parentColumn]) || null
    } else if (mapping.hierarchyMode === 'level') {
      const rawLevel = levelColumn === undefined ? '' : cellText(row[levelColumn])
      const parsedLevel = Number.parseInt(rawLevel, 10)
      if (!Number.isInteger(parsedLevel) || parsedLevel < 0) {
        validationErrors.push(`階層レベル「${rawLevel}」を数値に変換できません。`)
      } else {
        const lowerLevels = Array.from(levelStack.keys()).filter(level => level < parsedLevel).sort((a, b) => b - a)
        parentSourceKey = lowerLevels.length > 0 ? levelStack.get(lowerLevels[0]) ?? null : null
        depth = lowerLevels.length > 0 ? lowerLevels.length : 0
        Array.from(levelStack.keys()).filter(level => level >= parsedLevel).forEach(level => levelStack.delete(level))
        levelStack.set(parsedLevel, sourceKey)
      }
    }

    result.push({
      sourceRowNumber,
      sourceKey,
      isGeneratedKey: !explicitKey,
      parentSourceKey,
      depth,
      sortOrder: result.length,
      included: true,
      title: fields.title,
      description: fields.description,
      assignee: fields.assignee,
      dueDate: fields.dueDate.value,
      validationErrors,
    })
  })

  const keyCounts = new Map<string, number>()
  result.forEach(row => keyCounts.set(row.sourceKey, (keyCounts.get(row.sourceKey) ?? 0) + 1))
  const rowsByKey = new Map(result.map(row => [row.sourceKey, row]))
  const resolvedDepths = new Map<string, number>()
  const visiting = new Set<string>()
  const resolveDepth = (row: DraftRow): number => {
    const cached = resolvedDepths.get(row.sourceKey)
    if (cached !== undefined) return cached
    if (!row.parentSourceKey) {
      resolvedDepths.set(row.sourceKey, 0)
      return 0
    }
    if (!visiting.add(row.sourceKey)) {
      row.validationErrors.push('親子関係が循環しています。')
      return 0
    }
    const parent = rowsByKey.get(row.parentSourceKey)
    if (!parent) {
      row.validationErrors.push(`親キー「${row.parentSourceKey}」が見つかりません。`)
      visiting.delete(row.sourceKey)
      return 0
    }
    const depth = Math.min(resolveDepth(parent) + 1, 20)
    resolvedDepths.set(row.sourceKey, depth)
    visiting.delete(row.sourceKey)
    return depth
  }
  result.forEach(row => {
    if (mapping.hierarchyMode === 'parentKey') row.depth = resolveDepth(row)
    if ((keyCounts.get(row.sourceKey) ?? 0) > 1) {
      row.validationErrors.push(`識別キー「${row.sourceKey}」が重複しています。`)
    }
    if (!row.title.trim()) row.validationErrors.push('タスクタイトルがありません。')
  })
  return result
}

function statusLabel(status: string) {
  const labels: Record<string, string> = {
    Ready: '登録可能',
    Invalid: '要修正',
    Excluded: '除外',
    Registering: '登録中',
    Registered: '登録済み',
    Duplicate: '登録済み・スキップ',
    Failed: '失敗',
    Blocked: '親タスク待ち',
    PartiallyRegistered: '一部失敗',
  }
  return labels[status] ?? status
}

export default function WbsImport() {
  const [file, setFile] = useState<File | null>(null)
  const [fileHash, setFileHash] = useState('')
  const [sheets, setSheets] = useState<SheetData[]>([])
  const [sheetName, setSheetName] = useState('')
  const [headerRow, setHeaderRow] = useState(1)
  const [dataStartRow, setDataStartRow] = useState(2)
  const [layoutSignature, setLayoutSignature] = useState('')
  const [mapping, setMapping] = useState<Mapping>(emptyMapping)
  const [projectGid, setProjectGid] = useState('')
  const [sectionGid, setSectionGid] = useState('')
  const [profiles, setProfiles] = useState<Profile[]>([])
  const [selectedProfileId, setSelectedProfileId] = useState('')
  const [profileName, setProfileName] = useState('')
  const [previewRows, setPreviewRows] = useState<DraftRow[] | null>(null)
  const [batch, setBatch] = useState<Batch | null>(null)
  const [busy, setBusy] = useState<'file' | 'profile' | 'preview' | 'register' | null>(null)
  const [message, setMessage] = useState<string | null>(null)
  const fileInputRef = useRef<HTMLInputElement>(null)

  const selectedSheet = sheets.find(sheet => sheet.name === sheetName)
  const headers = useMemo(() => {
    const rows = selectedSheet?.rows ?? []
    const header = rows[headerRow - 1] ?? []
    const sample = rows.slice(Math.max(headerRow, dataStartRow - 1), Math.max(headerRow, dataStartRow - 1) + 20)
    const columnCount = Math.max(header.length, ...sample.map(row => row.length), 0)
    return Array.from({ length: columnCount }, (_, index) => cellText(header[index]) || `見出しなし ${columnLetter(index)}`)
  }, [selectedSheet, headerRow, dataStartRow])

  useEffect(() => {
    requestJson<Profile[]>('/api/wbs-imports/profiles').then(setProfiles).catch(() => setProfiles([]))
  }, [])

  useEffect(() => {
    if (headers.length === 0) {
      setLayoutSignature('')
      return
    }
    let active = true
    void sha256(JSON.stringify(headers.map(header => header.normalize('NFKC').trim().toLowerCase())))
      .then(signature => {
        if (!active) return
        setLayoutSignature(signature)
        const exact = profiles.find(profile => profile.layoutSignature === signature)
        if (exact) {
          applyProfile(exact)
        } else {
          setSelectedProfileId('')
          setMapping(inferRoles(headers))
          setPreviewRows(null)
          setBatch(null)
        }
      })
    return () => { active = false }
    // Profile application is intentionally triggered only by a layout change.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [headers.join('\u0001')])

  const resetAfterMapping = () => {
    setPreviewRows(null)
    setBatch(null)
    setMessage(null)
  }

  const applyProfile = (profile: Profile) => {
    setSelectedProfileId(profile.id)
    setProfileName(profile.name)
    setSheetName(current => sheets.some(sheet => sheet.name === profile.sheetName) ? profile.sheetName : current)
    setHeaderRow(profile.headerRow)
    setDataStartRow(profile.dataStartRow)
    setMapping(profile.mapping)
    setProjectGid(profile.projectGid ?? '')
    setSectionGid(profile.sectionGid ?? '')
    setPreviewRows(null)
    setBatch(null)
  }

  const importFile = async (nextFile: File) => {
    setMessage(null)
    if (!/\.(xlsx|csv)$/i.test(nextFile.name)) {
      setMessage('WBSファイルは .xlsx または .csv を選択してください。')
      return
    }
    if (nextFile.size > maxFileBytes) {
      setMessage('WBSファイルは10MB以下にしてください。')
      return
    }
    setBusy('file')
    try {
      const buffer = await nextFile.arrayBuffer()
      const nextHash = await sha256(buffer)
      let parsedSheets: SheetData[]
      if (/\.csv$/i.test(nextFile.name)) {
        let text: string
        try {
          text = new TextDecoder('utf-8', { fatal: true }).decode(buffer)
        } catch {
          text = new TextDecoder('shift_jis').decode(buffer)
        }
        parsedSheets = [{ name: 'CSV', rows: parseCsv(text) }]
      } else {
        const workbook = await readXlsxFile(nextFile)
        parsedSheets = workbook.map(sheet => ({
          name: sheet.sheet,
          rows: sheet.data.map(row => row.map(toCell)),
        }))
      }
      if (parsedSheets.length === 0 || parsedSheets.every(sheet => sheet.rows.length === 0)) {
        throw new Error('ファイルに読み込める行がありません。')
      }
      setFile(nextFile)
      setFileHash(nextHash)
      setSheets(parsedSheets)
      setSheetName(parsedSheets[0].name)
      setHeaderRow(1)
      setDataStartRow(2)
      setSelectedProfileId('')
      setProfileName('')
      setPreviewRows(null)
      setBatch(null)
    } catch (error) {
      setMessage(error instanceof Error ? error.message : 'WBSファイルを読み込めませんでした。')
    } finally {
      setBusy(null)
    }
  }

  const updateRole = (index: number, role: ColumnRole) => {
    setMapping(current => {
      const roles = { ...current.roles }
      if (singleRoles.has(role)) {
        Object.entries(roles).forEach(([column, assigned]) => {
          if (assigned === role) roles[Number(column)] = 'ignore'
        })
      }
      roles[index] = role
      return { ...current, roles }
    })
    resetAfterMapping()
  }

  const saveProfile = async (overwrite: boolean) => {
    if (!profileName.trim()) {
      setMessage('テンプレート名を入力してください。')
      return
    }
    setBusy('profile')
    setMessage(null)
    try {
      const path = overwrite && selectedProfileId
        ? `/api/wbs-imports/profiles/${selectedProfileId}`
        : '/api/wbs-imports/profiles'
      const saved = await requestJson<Profile>(path, {
        method: overwrite && selectedProfileId ? 'PUT' : 'POST',
        body: JSON.stringify({
          name: profileName,
          layoutSignature,
          sheetName,
          headerRow,
          dataStartRow,
          mapping,
          projectGid: projectGid.trim() || null,
          sectionGid: sectionGid.trim() || null,
        }),
      })
      setProfiles(current => [...current.filter(profile => profile.id !== saved.id), saved].sort((a, b) => a.name.localeCompare(b.name, 'ja')))
      setSelectedProfileId(saved.id)
      setMessage(`テンプレート「${saved.name}」を保存しました。`)
    } catch (error) {
      setMessage(error instanceof Error ? error.message : 'テンプレートを保存できませんでした。')
    } finally {
      setBusy(null)
    }
  }

  const deleteProfile = async () => {
    if (!selectedProfileId) return
    setBusy('profile')
    try {
      await requestJson<void>(`/api/wbs-imports/profiles/${selectedProfileId}`, { method: 'DELETE' })
      setProfiles(current => current.filter(profile => profile.id !== selectedProfileId))
      setSelectedProfileId('')
      setProfileName('')
      setMessage('テンプレートを削除しました。')
    } catch (error) {
      setMessage(error instanceof Error ? error.message : 'テンプレートを削除できませんでした。')
    } finally {
      setBusy(null)
    }
  }

  const buildPreview = () => {
    if (!selectedSheet) return
    const next = normalizeRows(selectedSheet.rows, dataStartRow, mapping)
    setPreviewRows(next)
    setBatch(null)
    setMessage(next.length > 0 ? null : '登録対象になる行がありません。')
  }

  const updatePreviewRow = (index: number, patch: Partial<DraftRow>) => {
    setPreviewRows(current => current?.map((row, rowIndex) => rowIndex === index ? { ...row, ...patch } : row) ?? null)
    setBatch(null)
  }

  const createServerPreview = async () => {
    if (!file || !previewRows) return
    const unresolved = previewRows.some(row => row.included && row.validationErrors.length > 0)
    if (unresolved) {
      setMessage('エラー行を修正するか、登録対象のチェックを外してください。')
      return
    }
    setBusy('preview')
    setMessage(null)
    try {
      const result = await requestJson<Batch>('/api/wbs-imports/batches', {
        method: 'POST',
        body: JSON.stringify({
          fileName: file.name,
          fileHash,
          sheetName,
          layoutSignature,
          profileId: selectedProfileId || null,
          projectGid: projectGid.trim() || null,
          sectionGid: sectionGid.trim() || null,
          rows: previewRows,
        }),
      })
      setBatch(result)
      if (result.status === 'Invalid') setMessage('サーバー検証でエラーが見つかりました。')
    } catch (error) {
      setMessage(error instanceof Error ? error.message : '取込内容を検証できませんでした。')
    } finally {
      setBusy(null)
    }
  }

  const registerBatch = async () => {
    if (!batch) return
    setBusy('register')
    setMessage(null)
    try {
      const result = await requestJson<Batch>(`/api/wbs-imports/batches/${batch.id}/register`, { method: 'POST' })
      setBatch(result)
      setMessage(result.status === 'Registered'
        ? `${result.succeededRows}件を登録しました。`
        : `${result.succeededRows}件成功、${result.failedRows}件を確認してください。`)
    } catch (error) {
      setMessage(error instanceof Error ? error.message : 'WBSを登録できませんでした。')
    } finally {
      setBusy(null)
    }
  }

  const downloadErrors = async () => {
    if (!batch) return
    try {
      const response = await fetch(`${apiBase}/api/wbs-imports/batches/${batch.id}/errors.csv`, {
        headers: { 'X-TaskCapture-Client': getClientKey() },
      })
      if (!response.ok) throw new Error('エラーCSVを取得できませんでした。')
      const url = URL.createObjectURL(await response.blob())
      const anchor = document.createElement('a')
      anchor.href = url
      anchor.download = `wbs-import-${batch.id}-errors.csv`
      anchor.click()
      URL.revokeObjectURL(url)
    } catch (error) {
      setMessage(error instanceof Error ? error.message : 'エラーCSVを取得できませんでした。')
    }
  }

  const mappingErrors = useMemo(() => {
    const roles = Object.values(mapping.roles)
    const errors: string[] = []
    if (mapping.hierarchyMode !== 'columns' && !roles.includes('title')) errors.push('タイトル列を指定してください。')
    if (mapping.hierarchyMode === 'parentKey' && (!roles.includes('key') || !roles.includes('parentKey'))) errors.push('識別キー列と親キー列を指定してください。')
    if (mapping.hierarchyMode === 'level' && !roles.includes('level')) errors.push('階層レベル列を指定してください。')
    if (mapping.hierarchyMode === 'columns' && !roles.includes('hierarchy')) errors.push('階層列を1つ以上指定してください。')
    return errors
  }, [mapping])

  const visibleRows: Array<DraftRow | BatchRow> = batch ? batch.rows : previewRows ?? []
  const includedCount = visibleRows.filter(row => row.included).length
  const errorCount = visibleRows.filter(row => row.included &&
    (row.validationErrors.length > 0 || (isBatchRow(row) && ['Invalid', 'Failed', 'Blocked'].includes(row.status)))).length

  return (
    <div className="wbs-page">
      <section className="panel wbs-file-panel">
        <div className="section-heading"><h2>1. WBSファイル</h2><span className="ready-badge">端末内で解析</span></div>
        <button type="button" className="wbs-dropzone" onClick={() => fileInputRef.current?.click()} disabled={busy !== null}>
          <strong>{busy === 'file' ? '読み込んでいます…' : file?.name ?? 'ExcelまたはCSVを選択'}</strong>
          <span>.xlsx / .csv・10MB・5,000行まで</span>
        </button>
        <input ref={fileInputRef} hidden type="file" accept=".xlsx,.csv,application/vnd.openxmlformats-officedocument.spreadsheetml.sheet,text/csv" onChange={event => { const selected = event.target.files?.[0]; event.target.value = ''; if (selected) void importFile(selected) }} />
        {file && <div className="wbs-file-meta"><span>{sheets.length}シート</span><span>ファイル識別: {fileHash.slice(0, 10)}</span></div>}
      </section>

      {selectedSheet && <section className="panel wbs-mapping-panel">
        <div className="section-heading"><h2>2. レイアウトと列を指定</h2><span className="source-badge">{headers.length}列</span></div>
        <div className="wbs-layout-grid">
          <label>シート<select value={sheetName} onChange={event => { setSheetName(event.target.value); resetAfterMapping() }}>{sheets.map(sheet => <option key={sheet.name}>{sheet.name}</option>)}</select></label>
          <label>見出し行<input type="number" min={1} max={selectedSheet.rows.length} value={headerRow} onChange={event => { const value = Number(event.target.value); setHeaderRow(value); setDataStartRow(Math.max(value + 1, dataStartRow)); resetAfterMapping() }} /></label>
          <label>データ開始行<input type="number" min={headerRow + 1} max={selectedSheet.rows.length + 1} value={dataStartRow} onChange={event => { setDataStartRow(Number(event.target.value)); resetAfterMapping() }} /></label>
          <label>階層方式<select value={mapping.hierarchyMode} onChange={event => { setMapping(current => ({ ...current, hierarchyMode: event.target.value as HierarchyMode })); resetAfterMapping() }}>
            <option value="none">親子関係なし</option>
            <option value="parentKey">識別キー・親キー</option>
            <option value="level">階層レベル</option>
            <option value="columns">大項目・中項目などの階層列</option>
          </select></label>
        </div>

        <div className="wbs-template-row">
          <select aria-label="保存済みテンプレート" value={selectedProfileId} onChange={event => {
            const profile = profiles.find(item => item.id === event.target.value)
            if (profile) applyProfile(profile)
            else setSelectedProfileId('')
          }}>
            <option value="">テンプレートを選択</option>
            {profiles.map(profile => <option key={profile.id} value={profile.id}>{profile.name}{profile.layoutSignature === layoutSignature ? '（一致）' : ''}</option>)}
          </select>
          <input aria-label="テンプレート名" value={profileName} maxLength={200} placeholder="例：A社WBS・親ID形式" onChange={event => setProfileName(event.target.value)} />
          <button type="button" onClick={() => void saveProfile(false)} disabled={busy !== null || mappingErrors.length > 0}>新規保存</button>
          {selectedProfileId && <button type="button" onClick={() => void saveProfile(true)} disabled={busy !== null || mappingErrors.length > 0}>上書き</button>}
          {selectedProfileId && <button type="button" className="danger-link" onClick={() => void deleteProfile()} disabled={busy !== null}>削除</button>}
        </div>

        <div className="wbs-column-map" role="table" aria-label="列マッピング">
          <div className="wbs-map-head" role="row"><span>元の列</span><span>サンプル</span><span>Asana項目</span></div>
          {headers.map((header, index) => <div className="wbs-map-row" role="row" key={`${index}-${header}`}>
            <strong>{columnLetter(index)}: {header}</strong>
            <span title={cellText(selectedSheet.rows[dataStartRow - 1]?.[index])}>{cellText(selectedSheet.rows[dataStartRow - 1]?.[index]) || '—'}</span>
            <select aria-label={`${header}の割り当て`} value={mapping.roles[index] ?? 'ignore'} onChange={event => updateRole(index, event.target.value as ColumnRole)}>
              {roleOptions.map(option => <option key={option.value} value={option.value}>{option.label}</option>)}
            </select>
          </div>)}
        </div>

        <details className="advanced wbs-advanced"><summary>変換と登録先</summary><div className="advanced-grid">
          <div className="field"><label>タイトル結合文字</label><input maxLength={20} value={mapping.titleSeparator} onChange={event => { setMapping(current => ({ ...current, titleSeparator: event.target.value })); resetAfterMapping() }} /></div>
          <div className="field"><label>説明結合文字</label><select value={mapping.descriptionSeparator} onChange={event => { setMapping(current => ({ ...current, descriptionSeparator: event.target.value })); resetAfterMapping() }}><option value={'\n'}>改行</option><option value=" ">空白</option><option value=" / "> / </option></select></div>
          <div className="field"><label>日付形式</label><select value={mapping.dateFormat} onChange={event => { setMapping(current => ({ ...current, dateFormat: event.target.value as Mapping['dateFormat'] })); resetAfterMapping() }}><option value="auto">自動</option><option value="yyyy-MM-dd">yyyy-MM-dd</option><option value="yyyy/MM/dd">yyyy/MM/dd</option><option value="yyyy.MM.dd">yyyy.MM.dd</option><option value="MM/dd/yyyy">MM/dd/yyyy</option></select></div>
          <div className="field"><label>AsanaプロジェクトGID</label><input inputMode="numeric" value={projectGid} onChange={event => { setProjectGid(event.target.value); resetAfterMapping() }} /></div>
          <div className="field"><label>AsanaセクションGID</label><input inputMode="numeric" value={sectionGid} onChange={event => { setSectionGid(event.target.value); resetAfterMapping() }} /></div>
        </div></details>

        {mappingErrors.length > 0 && <div className="wbs-inline-errors">{mappingErrors.map(error => <span key={error}>! {error}</span>)}</div>}
        <button type="button" className="primary-button" onClick={buildPreview} disabled={busy !== null || mappingErrors.length > 0}>変換プレビューを作る</button>
      </section>}

      {previewRows && <section className="panel wbs-preview-panel">
        <div className="section-heading"><h2>3. 登録前プレビュー</h2><span className="ready-badge">{includedCount}件</span></div>
        <p className="wbs-help">親子構造、タイトル、担当者、期限を確認してください。エラー行は修正するか、登録対象から外せます。</p>
        <div className="wbs-summary"><span>全{visibleRows.length}件</span><span>登録対象{includedCount}件</span><span className={errorCount ? 'has-error' : ''}>エラー{errorCount}件</span></div>
        <div className="wbs-preview-table">
          <div className="wbs-preview-head"><span>対象</span><span>タスク</span><span>担当者</span><span>期限</span><span>状態</span></div>
          {visibleRows.slice(0, 200).map((row, index) => <div className={`wbs-preview-row ${row.included ? '' : 'excluded'}`} key={`${row.sourceKey}-${index}`}>
            <input type="checkbox" aria-label={`${row.title || row.sourceKey}を登録対象にする`} checked={row.included} disabled={Boolean(batch)} onChange={event => updatePreviewRow(index, { included: event.target.checked })} />
            <div className="wbs-task-cell" style={{ paddingLeft: `${Math.min(row.depth, 8) * 16}px` }}>
              <input value={row.title} maxLength={200} disabled={Boolean(batch)} onChange={event => updatePreviewRow(index, { title: event.target.value, validationErrors: row.validationErrors.filter(error => error !== 'タスクタイトルがありません。') })} />
              <small>行{row.sourceRowNumber}・{row.sourceKey}</small>
            </div>
            <input value={row.assignee ?? ''} maxLength={200} disabled={Boolean(batch)} onChange={event => updatePreviewRow(index, { assignee: event.target.value || null })} />
            <input type="date" value={row.dueDate ?? ''} disabled={Boolean(batch)} onChange={event => updatePreviewRow(index, { dueDate: event.target.value || null, validationErrors: row.validationErrors.filter(error => !error.startsWith('期限「')) })} />
            <div className="wbs-row-status">
              {isBatchRow(row) ? <span className={`status-pill ${row.status.toLowerCase()}`}>{statusLabel(row.status)}</span> : <span className={`status-pill ${row.validationErrors.length ? 'invalid' : 'ready'}`}>{row.validationErrors.length ? '要修正' : '登録可能'}</span>}
              {row.validationErrors.map(error => <small className="row-error" key={error}>{error}</small>)}
              {isBatchRow(row) && row.resolvedAssigneeName && <small>担当: {row.resolvedAssigneeName}</small>}
              {isBatchRow(row) && row.warningMessage && <small className="row-warning">{row.warningMessage}</small>}
              {isBatchRow(row) && row.errorMessage && <small className="row-error">{row.errorMessage}</small>}
              {isBatchRow(row) && row.externalTaskUrl && <a href={row.externalTaskUrl} target="_blank" rel="noreferrer">Asana ↗</a>}
            </div>
          </div>)}
        </div>
        {visibleRows.length > 200 && <p className="wbs-help">画面には先頭200件を表示しています。{visibleRows.length.toLocaleString()}件すべてが登録対象です。</p>}

        {!batch && <button type="button" className="primary-button" onClick={() => void createServerPreview()} disabled={busy !== null || includedCount === 0 || errorCount > 0}>{busy === 'preview' ? '検証しています…' : 'この内容を確定する'}</button>}
        {batch && <div className="wbs-register-actions">
          <button type="button" className="asana-button" onClick={() => void registerBatch()} disabled={busy !== null || batch.status === 'Registered'}>{busy === 'register' ? 'Asanaへ登録しています…' : batch.status === 'Registered' ? '登録完了' : 'Asanaへ一括登録'}</button>
          {batch.failedRows > 0 && <button type="button" className="secondary-button" onClick={() => void downloadErrors()}>エラーCSV</button>}
          {batch.status === 'PartiallyRegistered' && <button type="button" className="secondary-button" onClick={() => void registerBatch()} disabled={busy !== null}>失敗行を再試行</button>}
        </div>}
      </section>}

      {message && <div className={message.includes('登録しました') || message.includes('保存しました') ? 'wbs-message success' : 'error-message'} role="status">{message}</div>}
    </div>
  )
}
