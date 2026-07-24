import { useEffect, useMemo, useRef, useState } from 'react'
import readXlsxFile from 'read-excel-file/browser'
import AsanaDestinationPicker from './AsanaDestinationPicker'
import { apiBase, getClientKey, requestJson } from './api'

type Cell = string | number | boolean | Date | null
type SheetData = { name: string; rows: Cell[][] }
type HierarchyMode = 'none' | 'parentKey' | 'level' | 'columns'
type ColumnRole = 'ignore' | 'title' | 'description' | 'assignee' | 'startDate' | 'dueDate' | 'include' | 'key' | 'parentKey' | 'level' | 'hierarchy'
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
  startDate: string | null
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
  { value: 'startDate', label: '開始日' },
  { value: 'dueDate', label: '期限' },
  { value: 'include', label: '登録対象' },
  { value: 'key', label: '識別キー' },
  { value: 'parentKey', label: '親キー' },
  { value: 'level', label: '階層レベル' },
  { value: 'hierarchy', label: '階層列' },
]
const singleRoles = new Set<ColumnRole>(['assignee', 'startDate', 'dueDate', 'include', 'key', 'parentKey', 'level'])
const roleHelp: Record<ColumnRole, string> = {
  ignore: 'Asanaへ送らない参考列',
  title: 'タスク名。複数列を結合可能',
  description: 'タスクの説明。複数列を結合可能',
  assignee: '担当者名・me・ユーザーGID',
  startDate: '作業を開始する日',
  dueDate: '完了期限',
  include: 'はい/○/1は対象、いいえ/×/0は除外',
  key: '行を一意に識別するWBS番号',
  parentKey: '親タスクのWBS番号',
  level: '0、1、2などの階層レベル',
  hierarchy: '大項目・中項目・小項目の列',
}

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
    else if (/開始|着手|start/.test(normalized)) roles[index] = 'startDate'
    else if (/期限|完了予定|終了予定|due/.test(normalized)) roles[index] = 'dueDate'
    else if (/asana対象|登録対象|取込対象|取り込み対象|include/.test(normalized)) roles[index] = 'include'
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

function detectHeaderRow(rows: Cell[][]) {
  let best = { row: 1, score: -1 }
  rows.slice(0, 20).forEach((row, index) => {
    const headers = row.map(cellText)
    const inferred = inferRoles(headers)
    const values = Object.values(inferred.roles)
    const recognized = values.filter(role => role !== 'ignore')
    const nonEmpty = headers.filter(Boolean).length
    let score = recognized.length * 5 + Math.min(nonEmpty, 10)
    if (values.includes('title')) score += 12
    if (values.includes('key') && values.includes('parentKey')) score += 10
    if (values.includes('assignee')) score += 3
    if (values.includes('dueDate')) score += 3
    if (values.includes('startDate')) score += 2
    if (values.includes('include')) score += 2
    if (nonEmpty < 2) score -= 12
    if (score > best.score) best = { row: index + 1, score }
  })
  return best.score >= 10 ? best.row : 1
}

function parseDateValue(value: string, format: Mapping['dateFormat'], label: string) {
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
    : { value: null, error: `${label}「${value}」を日付に変換できません。` }
}

function parseIncludedValue(value: string) {
  const normalized = value.normalize('NFKC').trim().toLowerCase()
  if (!normalized) return { value: false, error: null }
  if (/^(はい|yes|true|1|○|〇|対象|登録)$/.test(normalized)) return { value: true, error: null }
  if (/^(いいえ|no|false|0|×|x|対象外|除外)$/.test(normalized)) return { value: false, error: null }
  return { value: true, error: `登録対象「${value}」は、はい/いいえ・○/×・1/0のいずれかにしてください。` }
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
  const startDateColumn = roleIndexes('startDate')[0]
  const dueDateColumn = roleIndexes('dueDate')[0]
  const includeColumn = roleIndexes('include')[0]
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
    const rawStartDate = startDateColumn === undefined ? '' : cellText(row[startDateColumn])
    const rawDueDate = dueDateColumn === undefined ? '' : cellText(row[dueDateColumn])
    const startDate = parseDateValue(rawStartDate, mapping.dateFormat, '開始日')
    const dueDate = parseDateValue(rawDueDate, mapping.dateFormat, '期限')
    const included = includeColumn === undefined
      ? { value: true, error: null }
      : parseIncludedValue(cellText(row[includeColumn]))
    const errors = [startDate.error, dueDate.error, included.error].filter((error): error is string => Boolean(error))
    if (startDate.value && !dueDate.value) errors.push('開始日を設定する場合は期限も必要です。')
    if (startDate.value && dueDate.value && startDate.value > dueDate.value) {
      errors.push('開始日は期限以前の日付にしてください。')
    }
    return { title, description, assignee, startDate, dueDate, included, errors, sourceRowNumber }
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
            included: fields.included.value,
            title: value,
            description: '',
            assignee: null,
            startDate: null,
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
        deepest.startDate ||= fields.startDate.value
        deepest.dueDate ||= fields.dueDate.value
        deepest.included = deepest.included && fields.included.value
        fields.errors.forEach(error => {
          if (!deepest.validationErrors.includes(error)) deepest.validationErrors.push(error)
        })
        if (fields.startDate.error && !deepest.validationErrors.includes(fields.startDate.error)) {
          deepest.validationErrors.push(fields.startDate.error)
        }
      }
      return
    }

    const explicitKey = keyColumn === undefined ? '' : cellText(row[keyColumn])
    const sourceKey = explicitKey || `row-${sourceRowNumber}`
    let parentSourceKey: string | null = null
    let depth = 0
    const validationErrors = [...fields.errors]

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
      included: fields.included.value,
      title: fields.title,
      description: fields.description,
      assignee: fields.assignee,
      startDate: fields.startDate.value,
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
  const [detectionMessage, setDetectionMessage] = useState<string | null>(null)
  const [showReadingDetails, setShowReadingDetails] = useState(false)
  const [confirmingRegistration, setConfirmingRegistration] = useState(false)
  const [destinationLabel, setDestinationLabel] = useState<{
    projectName: string | null
    sectionName: string | null
  }>({ projectName: null, sectionName: null })
  const fileInputRef = useRef<HTMLInputElement>(null)

  const selectedSheet = sheets.find(sheet => sheet.name === sheetName)
  const headers = useMemo(() => {
    const rows = selectedSheet?.rows ?? []
    const header = rows[headerRow - 1] ?? []
    const sample = rows.slice(Math.max(headerRow, dataStartRow - 1), Math.max(headerRow, dataStartRow - 1) + 20)
    const columnCount = Math.max(header.length, ...sample.map(row => row.length), 0)
    return Array.from({ length: columnCount }, (_, index) => cellText(header[index]) || `見出しなし ${columnLetter(index)}`)
  }, [selectedSheet, headerRow, dataStartRow])
  const sourceRowCount = useMemo(() => (selectedSheet?.rows ?? [])
    .slice(dataStartRow - 1, dataStartRow - 1 + maxRows)
    .filter(row => row.some(cell => Boolean(cellText(cell))))
    .length, [dataStartRow, selectedSheet])

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
          const inferred = inferRoles(headers)
          setMapping(inferred)
          setDetectionMessage(`見出し${headerRow}行目を見つけ、登録に必要な項目を自動設定しました。`)
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
    setConfirmingRegistration(false)
  }

  const applyRecommended = (sheet: SheetData) => {
    const detectedHeaderRow = detectHeaderRow(sheet.rows)
    const nextDataStartRow = Math.min(detectedHeaderRow + 1, sheet.rows.length + 1)
    const headerCells = sheet.rows[detectedHeaderRow - 1] ?? []
    const sampleRows = sheet.rows.slice(detectedHeaderRow, detectedHeaderRow + 20)
    const columnCount = Math.max(headerCells.length, ...sampleRows.map(row => row.length), 0)
    const detectedHeaders = Array.from(
      { length: columnCount },
      (_, index) => cellText(headerCells[index]) || `見出しなし ${columnLetter(index)}`)
    const inferred = inferRoles(detectedHeaders)
    setHeaderRow(detectedHeaderRow)
    setDataStartRow(nextDataStartRow)
    setMapping(inferred)
    setSelectedProfileId('')
    setDetectionMessage(`見出し${detectedHeaderRow}行目を見つけ、登録に必要な項目を自動設定しました。`)
    setShowReadingDetails(false)
    resetAfterMapping()
  }

  const selectSheet = (nextSheetName: string) => {
    setSheetName(nextSheetName)
    const nextSheet = sheets.find(sheet => sheet.name === nextSheetName)
    if (nextSheet) applyRecommended(nextSheet)
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
    setDetectionMessage(`保存済みテンプレート「${profile.name}」を自動適用しました。`)
    setShowReadingDetails(false)
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
      const detectedHeaderRow = detectHeaderRow(parsedSheets[0].rows)
      setHeaderRow(detectedHeaderRow)
      setDataStartRow(Math.min(detectedHeaderRow + 1, parsedSheets[0].rows.length + 1))
      setMapping(inferRoles((parsedSheets[0].rows[detectedHeaderRow - 1] ?? []).map(cellText)))
      setSelectedProfileId('')
      setProfileName('')
      setPreviewRows(null)
      setBatch(null)
      setShowReadingDetails(false)
      setConfirmingRegistration(false)
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

  const updatePreviewDate = (index: number, field: 'startDate' | 'dueDate', value: string | null) => {
    setPreviewRows(current => current?.map((row, rowIndex) => {
      if (rowIndex !== index) return row
      const startDate = field === 'startDate' ? value : row.startDate
      const dueDate = field === 'dueDate' ? value : row.dueDate
      const validationErrors = row.validationErrors.filter(error =>
        !error.startsWith('開始日「') &&
        !error.startsWith('期限「') &&
        error !== '開始日を設定する場合は期限も必要です。' &&
        error !== '開始日は期限以前の日付にしてください。')
      if (startDate && !dueDate) validationErrors.push('開始日を設定する場合は期限も必要です。')
      if (startDate && dueDate && startDate > dueDate) {
        validationErrors.push('開始日は期限以前の日付にしてください。')
      }
      return { ...row, startDate, dueDate, validationErrors }
    }) ?? null)
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
    setConfirmingRegistration(false)
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
  const mappedFields = useMemo(() => {
    const roles = new Set(Object.values(mapping.roles))
    const fields = [
      roles.has('title') ? 'タスク名' : null,
      roles.has('description') ? '説明' : null,
      roles.has('assignee') ? '担当者' : null,
      roles.has('startDate') ? '開始日' : null,
      roles.has('dueDate') ? '期限' : null,
      roles.has('include') ? '登録する行' : null,
      mapping.hierarchyMode !== 'none' ? '親子関係' : null,
    ]
    return fields.filter((field): field is string => Boolean(field))
  }, [mapping.hierarchyMode, mapping.roles])
  useEffect(() => {
    if (mappingErrors.length > 0) setShowReadingDetails(true)
  }, [mappingErrors.length])

  const visibleRows: Array<DraftRow | BatchRow> = batch ? batch.rows : previewRows ?? []
  const includedCount = visibleRows.filter(row => row.included).length
  const errorCount = visibleRows.filter(row => row.included &&
    (row.validationErrors.length > 0 || (isBatchRow(row) && ['Invalid', 'Failed', 'Blocked'].includes(row.status)))).length
  const currentStep = !file ? 1 : previewRows ? 3 : 2
  const completed = batch?.status === 'Registered'
  const stepClass = (step: number) =>
    completed || step < currentStep ? 'complete' : step === currentStep ? 'active' : ''

  return (
    <div className="wbs-page">
      <ol className="wbs-steps" aria-label="WBS登録の流れ">
        <li className={stepClass(1)} aria-current={currentStep === 1 ? 'step' : undefined}><span>1</span><strong>ファイルを選ぶ</strong></li>
        <li className={stepClass(2)} aria-current={currentStep === 2 ? 'step' : undefined}><span>2</span><strong>読み取りを確認</strong></li>
        <li className={stepClass(3)} aria-current={currentStep === 3 ? 'step' : undefined}><span>3</span><strong>Asanaへ登録</strong></li>
      </ol>

      <section className="panel wbs-file-panel">
        <div className="section-heading"><h2>WBSファイルを選ぶ</h2><span className="ready-badge">Excel / CSV</span></div>
        {!file && <p className="wbs-start-guide">ファイルを選ぶだけで、タスク名や担当者、日付、親子関係を自動で読み取ります。</p>}
        <button type="button" className="wbs-dropzone" onClick={() => fileInputRef.current?.click()} disabled={busy !== null}>
          <span className="wbs-file-icon" aria-hidden="true">{file ? '✓' : '＋'}</span>
          <strong>{busy === 'file' ? '読み込んでいます…' : file?.name ?? 'ExcelまたはCSVを選ぶ'}</strong>
          <span>{file ? 'クリックして別のファイルに変更' : 'ファイルは端末内で読み取ります'}</span>
        </button>
        <input ref={fileInputRef} hidden type="file" accept=".xlsx,.csv,application/vnd.openxmlformats-officedocument.spreadsheetml.sheet,text/csv" onChange={event => { const selected = event.target.files?.[0]; event.target.value = ''; if (selected) void importFile(selected) }} />
        {file && <div className="wbs-file-meta"><span>{sheets.length}シートを読み込みました</span></div>}
      </section>

      {selectedSheet && <section className="panel wbs-mapping-panel">
        <div className="section-heading"><h2>読み取り結果を確認</h2><span className="source-badge">{sourceRowCount}行</span></div>
        <p className="wbs-help">自動で読み取りました。下の内容と登録先に問題がなければ、そのまま次へ進めます。</p>

        {sheets.length > 1 && <div className="wbs-sheet-choice">
          <label>使うシート<select value={sheetName} onChange={event => selectSheet(event.target.value)}>{sheets.map(sheet => <option key={sheet.name}>{sheet.name}</option>)}</select></label>
        </div>}

        <div className={`wbs-reading-summary ${mappingErrors.length ? 'needs-attention' : ''}`}>
          <span className="wbs-reading-icon" aria-hidden="true">{mappingErrors.length ? '!' : '✓'}</span>
          <div>
            <strong>{mappingErrors.length ? '確認が必要な項目があります' : `${sourceRowCount}行を登録用に読み取りました`}</strong>
            <p>{detectionMessage ?? 'ファイルの項目名から自動判定しました。'}</p>
            <div className="wbs-field-chips" aria-label="読み取った項目">
              {mappedFields.map(field => <span key={field}>✓ {field}</span>)}
            </div>
          </div>
        </div>

        <div className="wbs-destination-card">
          <div>
            <strong>Asanaの登録先</strong>
            <span>通常は表示されている既定のプロジェクトのままで進めます。</span>
          </div>
          <AsanaDestinationPicker
            idPrefix="wbs"
            projectGid={projectGid}
            sectionGid={sectionGid}
            disabled={busy !== null}
            onResolvedLabel={setDestinationLabel}
            onChange={(nextProjectGid, nextSectionGid) => {
              setProjectGid(nextProjectGid)
              setSectionGid(nextSectionGid)
              resetAfterMapping()
            }}
          />
        </div>

        {mappingErrors.length > 0 && <div className="wbs-inline-errors">{mappingErrors.map(error => <span key={error}>! {error}</span>)}</div>}

        <details className="wbs-reading-details" open={showReadingDetails} onToggle={event => setShowReadingDetails(event.currentTarget.open)}>
          <summary>読み取り方を確認・修正 <small>通常は変更不要です</small></summary>
          <div className="wbs-detail-content">
            <div className="wbs-layout-grid">
              <label>項目名が書かれた行<input type="number" min={1} max={selectedSheet.rows.length} value={headerRow} onChange={event => { const value = Number(event.target.value); setHeaderRow(value); setDataStartRow(Math.max(value + 1, dataStartRow)); resetAfterMapping() }} /><small>例：タスク名、担当者、期限が並ぶ行</small></label>
              <label>最初のタスク行<input type="number" min={headerRow + 1} max={selectedSheet.rows.length + 1} value={dataStartRow} onChange={event => { setDataStartRow(Number(event.target.value)); resetAfterMapping() }} /><small>項目名の次の行が一般的です</small></label>
              <label>親子関係の読み方<select value={mapping.hierarchyMode} onChange={event => { setMapping(current => ({ ...current, hierarchyMode: event.target.value as HierarchyMode })); resetAfterMapping() }}>
                <option value="none">親子関係を作らない</option>
                <option value="parentKey">WBS番号と親WBS番号を使う</option>
                <option value="level">階層レベル列を使う</option>
                <option value="columns">大項目・中項目の列を使う</option>
              </select><small>分からなければ自動設定のままで進めます</small></label>
              <button type="button" className="wbs-retry-detection" onClick={() => applyRecommended(selectedSheet)} disabled={busy !== null}>もう一度自動で読み取る</button>
            </div>

            <div className="wbs-column-map" role="table" aria-label="列の読み取り設定">
              <div className="wbs-map-head" role="row"><span>Excelの列</span><span>最初のデータ</span><span>Asanaでの使い方</span></div>
              {headers.map((header, index) => {
                const assignedRole = mapping.roles[index] ?? 'ignore'
                return <div className={`wbs-map-row ${assignedRole !== 'ignore' ? 'mapped' : ''}`} role="row" key={`${index}-${header}`}>
                  <strong>{columnLetter(index)}: {header}</strong>
                  <span title={cellText(selectedSheet.rows[dataStartRow - 1]?.[index])}>{cellText(selectedSheet.rows[dataStartRow - 1]?.[index]) || '—'}</span>
                  <div className="wbs-role-cell">
                    <select aria-label={`${header}の割り当て`} value={assignedRole} onChange={event => updateRole(index, event.target.value as ColumnRole)}>
                      {roleOptions.map(option => <option key={option.value} value={option.value}>{option.label}</option>)}
                    </select>
                    <small>{roleHelp[assignedRole]}</small>
                  </div>
                </div>
              })}
            </div>

            <details className="advanced wbs-advanced"><summary>文字と日付の細かい設定</summary><div className="advanced-grid">
              <div className="field"><label>タイトル結合文字</label><input maxLength={20} value={mapping.titleSeparator} onChange={event => { setMapping(current => ({ ...current, titleSeparator: event.target.value })); resetAfterMapping() }} /></div>
              <div className="field"><label>説明結合文字</label><select value={mapping.descriptionSeparator} onChange={event => { setMapping(current => ({ ...current, descriptionSeparator: event.target.value })); resetAfterMapping() }}><option value={'\n'}>改行</option><option value=" ">空白</option><option value=" / "> / </option></select></div>
              <div className="field"><label>日付形式</label><select value={mapping.dateFormat} onChange={event => { setMapping(current => ({ ...current, dateFormat: event.target.value as Mapping['dateFormat'] })); resetAfterMapping() }}><option value="auto">自動</option><option value="yyyy-MM-dd">yyyy-MM-dd</option><option value="yyyy/MM/dd">yyyy/MM/dd</option><option value="yyyy.MM.dd">yyyy.MM.dd</option><option value="MM/dd/yyyy">MM/dd/yyyy</option></select></div>
            </div></details>
          </div>
        </details>

        <details className="wbs-profile-details">
          <summary>この読み取り設定を次回も使う <small>任意</small></summary>
          <div className="wbs-template-row">
            <select aria-label="保存済みテンプレート" value={selectedProfileId} onChange={event => {
              const profile = profiles.find(item => item.id === event.target.value)
              if (profile) applyProfile(profile)
              else setSelectedProfileId('')
            }}>
              <option value="">保存済み設定を選ぶ</option>
              {profiles.map(profile => <option key={profile.id} value={profile.id}>{profile.name}{profile.layoutSignature === layoutSignature ? '（このファイルに一致）' : ''}</option>)}
            </select>
            <input aria-label="テンプレート名" value={profileName} maxLength={200} placeholder="保存名（例：給食WBS）" onChange={event => setProfileName(event.target.value)} />
            <button type="button" onClick={() => void saveProfile(false)} disabled={busy !== null || mappingErrors.length > 0}>新しく保存</button>
            {selectedProfileId && <button type="button" onClick={() => void saveProfile(true)} disabled={busy !== null || mappingErrors.length > 0}>上書き</button>}
            {selectedProfileId && <button type="button" className="danger-link" onClick={() => void deleteProfile()} disabled={busy !== null}>削除</button>}
          </div>
        </details>

        <button type="button" className="primary-button wbs-next-button" onClick={buildPreview} disabled={busy !== null || mappingErrors.length > 0}>
          <span>登録する内容を確認する</span><span aria-hidden="true">→</span>
        </button>
      </section>}

      {previewRows && <section className="panel wbs-preview-panel">
        {!batch && <button type="button" className="wbs-back-button" onClick={resetAfterMapping}>← 読み取り結果に戻る</button>}
        <div className="section-heading"><h2>Asanaへ登録する内容</h2><span className="ready-badge">{includedCount}件</span></div>
        <p className="wbs-help">内容を確認し、直したい項目があればこの画面で編集できます。</p>
        <div className="wbs-summary"><span>全{visibleRows.length}件</span><span>登録対象{includedCount}件</span><span className={errorCount ? 'has-error' : ''}>エラー{errorCount}件</span></div>
        <div className="wbs-preview-table">
          <div className="wbs-preview-head"><span>対象</span><span>タスク</span><span>担当者</span><span>開始日</span><span>期限</span><span>状態</span></div>
          {visibleRows.slice(0, 200).map((row, index) => <div className={`wbs-preview-row ${row.included ? '' : 'excluded'}`} key={`${row.sourceKey}-${index}`}>
            <input type="checkbox" aria-label={`${row.title || row.sourceKey}を登録対象にする`} checked={row.included} disabled={Boolean(batch)} onChange={event => updatePreviewRow(index, { included: event.target.checked })} />
            <div className="wbs-task-cell" style={{ paddingLeft: `${Math.min(row.depth, 8) * 16}px` }}>
              <input aria-label={`${row.sourceKey}のタスク名`} value={row.title} maxLength={200} disabled={Boolean(batch)} onChange={event => updatePreviewRow(index, { title: event.target.value, validationErrors: row.validationErrors.filter(error => error !== 'タスクタイトルがありません。') })} />
              <small>行{row.sourceRowNumber}・{row.sourceKey}</small>
            </div>
            <div className="wbs-preview-field" data-label="担当者"><input aria-label={`${row.title || row.sourceKey}の担当者`} value={row.assignee ?? ''} maxLength={200} disabled={Boolean(batch)} onChange={event => updatePreviewRow(index, { assignee: event.target.value || null })} /></div>
            <div className="wbs-preview-field" data-label="開始日"><input aria-label={`${row.title || row.sourceKey}の開始日`} type="date" value={row.startDate ?? ''} disabled={Boolean(batch)} onChange={event => updatePreviewDate(index, 'startDate', event.target.value || null)} /></div>
            <div className="wbs-preview-field" data-label="期限"><input aria-label={`${row.title || row.sourceKey}の期限`} type="date" value={row.dueDate ?? ''} disabled={Boolean(batch)} onChange={event => updatePreviewDate(index, 'dueDate', event.target.value || null)} /></div>
            <div className="wbs-row-status" data-label="確認">
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
          <button type="button" className="asana-button" onClick={() => setConfirmingRegistration(true)} disabled={busy !== null || batch.status === 'Registered'}>{busy === 'register' ? 'Asanaへ登録しています…' : batch.status === 'Registered' ? '登録完了' : 'Asanaへ一括登録'}</button>
          {batch.failedRows > 0 && <button type="button" className="secondary-button" onClick={() => void downloadErrors()}>エラーCSV</button>}
          {batch.status === 'PartiallyRegistered' && <button type="button" className="secondary-button" onClick={() => void registerBatch()} disabled={busy !== null}>失敗行を再試行</button>}
        </div>}
      </section>}

      {confirmingRegistration && batch && <div className="wbs-confirm-overlay" role="presentation" onKeyDown={event => {
        if (event.key === 'Escape') setConfirmingRegistration(false)
      }} onMouseDown={event => {
        if (event.currentTarget === event.target) setConfirmingRegistration(false)
      }}>
        <div className="wbs-confirm-dialog" role="dialog" aria-modal="true" aria-labelledby="wbs-confirm-title">
          <span className="wbs-confirm-icon" aria-hidden="true">A</span>
          <h2 id="wbs-confirm-title">{includedCount}件をAsanaへ登録しますか？</h2>
          <p>親タスクから順に登録します。登録後の取り消しはAsana側で行います。</p>
          <dl>
            <div><dt>プロジェクト</dt><dd>{destinationLabel.projectName ?? (projectGid ? `選択済み（${projectGid}）` : 'サーバーの既定プロジェクト')}</dd></div>
            <div><dt>セクション</dt><dd>{destinationLabel.sectionName ?? (sectionGid ? `選択済み（${sectionGid}）` : '指定なし')}</dd></div>
            <div><dt>エラー</dt><dd>{errorCount}件</dd></div>
          </dl>
          <div className="wbs-confirm-actions">
            <button type="button" className="secondary-button" autoFocus onClick={() => setConfirmingRegistration(false)}>戻って確認</button>
            <button type="button" className="asana-button" onClick={() => void registerBatch()}>登録する</button>
          </div>
        </div>
      </div>}

      {message && <div className={message.includes('登録しました') || message.includes('保存しました') ? 'wbs-message success' : 'error-message'} role="status">{message}</div>}
    </div>
  )
}
