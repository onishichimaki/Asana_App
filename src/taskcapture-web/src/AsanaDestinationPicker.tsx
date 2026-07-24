import { useEffect, useMemo, useState } from 'react'
import { requestJson } from './api'

type ProjectOption = { gid: string; name: string }
type SectionOption = { gid: string; name: string }
type ProjectCatalog = { defaultProjectGid: string | null; projects: ProjectOption[] }

type Props = {
  projectGid: string
  sectionGid: string
  onChange: (projectGid: string, sectionGid: string) => void
  onResolvedLabel?: (label: { projectName: string | null; sectionName: string | null }) => void
  disabled?: boolean
  idPrefix: string
}

export default function AsanaDestinationPicker({
  projectGid,
  sectionGid,
  onChange,
  onResolvedLabel,
  disabled = false,
  idPrefix,
}: Props) {
  const [catalog, setCatalog] = useState<ProjectCatalog | null>(null)
  const [sections, setSections] = useState<SectionOption[]>([])
  const [projectsLoading, setProjectsLoading] = useState(true)
  const [sectionsLoading, setSectionsLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    let active = true
    setProjectsLoading(true)
    requestJson<ProjectCatalog>('/api/asana/projects')
      .then(result => {
        if (!active) return
        setCatalog(result)
        setError(null)
      })
      .catch(() => {
        if (!active) return
        setCatalog({ defaultProjectGid: null, projects: [] })
        setError('プロジェクト一覧を取得できません。必要ならGIDを直接入力できます。')
      })
      .finally(() => {
        if (active) setProjectsLoading(false)
      })
    return () => { active = false }
  }, [])

  const effectiveProjectGid = projectGid || catalog?.defaultProjectGid || ''

  useEffect(() => {
    let active = true
    if (!effectiveProjectGid) {
      setSections([])
      return
    }
    setSectionsLoading(true)
    requestJson<SectionOption[]>(`/api/asana/projects/${effectiveProjectGid}/sections`)
      .then(result => {
        if (active) setSections(result)
      })
      .catch(() => {
        if (active) setSections([])
      })
      .finally(() => {
        if (active) setSectionsLoading(false)
      })
    return () => { active = false }
  }, [effectiveProjectGid])

  const projectName = useMemo(() => {
    const gid = projectGid || catalog?.defaultProjectGid
    return catalog?.projects.find(project => project.gid === gid)?.name
  }, [catalog, projectGid])
  const sectionName = useMemo(
    () => sections.find(section => section.gid === sectionGid)?.name,
    [sectionGid, sections])

  useEffect(() => {
    onResolvedLabel?.({
      projectName: projectName ?? null,
      sectionName: sectionName ?? null,
    })
  }, [onResolvedLabel, projectName, sectionName])

  const unknownProject = projectGid && !catalog?.projects.some(project => project.gid === projectGid)
  const unknownSection = sectionGid && !sections.some(section => section.gid === sectionGid)

  return (
    <div className="destination-picker">
      <div className="field">
        <label htmlFor={`${idPrefix}-project`}>Asanaプロジェクト</label>
        <select
          id={`${idPrefix}-project`}
          value={projectGid}
          disabled={disabled || projectsLoading}
          onChange={event => onChange(event.target.value, '')}
        >
          <option value="">
            {projectsLoading
              ? 'プロジェクトを読み込み中…'
              : projectName
                ? `既定を使用（${projectName}）`
                : '既定のプロジェクトを使用'}
          </option>
          {unknownProject && <option value={projectGid}>現在の設定（{projectGid}）</option>}
          {catalog?.projects.map(project =>
            <option key={project.gid} value={project.gid}>{project.name}</option>)}
        </select>
        <small>タスクを追加するプロジェクトを名前で選べます。</small>
      </div>

      <div className="field">
        <label htmlFor={`${idPrefix}-section`}>セクション <span>任意</span></label>
        <select
          id={`${idPrefix}-section`}
          value={sectionGid}
          disabled={disabled || !effectiveProjectGid || sectionsLoading}
          onChange={event => onChange(projectGid || effectiveProjectGid, event.target.value)}
        >
          <option value="">{sectionsLoading ? 'セクションを読み込み中…' : '指定しない'}</option>
          {unknownSection && <option value={sectionGid}>現在の設定（{sectionGid}）</option>}
          {sections.map(section =>
            <option key={section.gid} value={section.gid}>{section.name}</option>)}
        </select>
        <small>未指定ならプロジェクト内の既定位置へ追加します。</small>
      </div>

      {error && <p className="destination-warning">! {error}</p>}
      <details className="destination-manual">
        <summary>一覧にない場合はGIDを直接入力</summary>
        <div className="advanced-grid">
          <div className="field">
            <label htmlFor={`${idPrefix}-project-gid`}>プロジェクトGID</label>
            <input
              id={`${idPrefix}-project-gid`}
              inputMode="numeric"
              value={projectGid}
              disabled={disabled}
              onChange={event => onChange(event.target.value, '')}
            />
          </div>
          <div className="field">
            <label htmlFor={`${idPrefix}-section-gid`}>セクションGID</label>
            <input
              id={`${idPrefix}-section-gid`}
              inputMode="numeric"
              value={sectionGid}
              disabled={disabled}
              onChange={event => onChange(projectGid, event.target.value)}
            />
          </div>
        </div>
      </details>
    </div>
  )
}
