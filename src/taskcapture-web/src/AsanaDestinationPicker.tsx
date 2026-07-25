import { useEffect, useMemo, useState } from 'react'
import { requestJson } from './api'

type ProjectOption = { gid: string; name: string; isFavorite: boolean }
type SectionOption = { gid: string; name: string }
type ProjectCatalog = { defaultProjectGid: string | null; projects: ProjectOption[] }

type Props = {
  projectGid: string
  sectionGid: string
  onChange: (projectGid: string, sectionGid: string) => void
  onResolvedLabel?: (label: { projectName: string | null; sectionName: string | null }) => void
  onEffectiveProjectGid?: (projectGid: string) => void
  disabled?: boolean
  idPrefix: string
}

export default function AsanaDestinationPicker({
  projectGid,
  sectionGid,
  onChange,
  onResolvedLabel,
  onEffectiveProjectGid,
  disabled = false,
  idPrefix,
}: Props) {
  const [catalog, setCatalog] = useState<ProjectCatalog | null>(null)
  const [sections, setSections] = useState<SectionOption[]>([])
  const [projectsLoading, setProjectsLoading] = useState(true)
  const [sectionsLoading, setSectionsLoading] = useState(false)
  const [projectSearch, setProjectSearch] = useState('')
  const [favoriteBusy, setFavoriteBusy] = useState(false)
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
        setError('プロジェクト一覧を取得できません。必要なら番号を直接入力できます。')
      })
      .finally(() => {
        if (active) setProjectsLoading(false)
      })
    return () => { active = false }
  }, [])

  const effectiveProjectGid = projectGid || catalog?.defaultProjectGid || ''

  useEffect(() => {
    onEffectiveProjectGid?.(effectiveProjectGid)
  }, [effectiveProjectGid, onEffectiveProjectGid])

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
  const selectedProject = catalog?.projects.find(project => project.gid === effectiveProjectGid)
  const visibleProjects = useMemo(() => {
    const term = projectSearch.trim().toLocaleLowerCase('ja-JP')
    if (!term) return catalog?.projects ?? []
    return (catalog?.projects ?? []).filter(project =>
      project.name.toLocaleLowerCase('ja-JP').includes(term)
      || project.gid.includes(term))
  }, [catalog, projectSearch])
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

  const toggleFavorite = async () => {
    if (!selectedProject) return
    setFavoriteBusy(true)
    try {
      await requestJson(`/api/asana/projects/${selectedProject.gid}/favorite`, {
        method: 'PUT',
        body: JSON.stringify({
          isFavorite: !selectedProject.isFavorite,
          projectName: selectedProject.name,
        }),
      })
      setCatalog(current => current ? {
        ...current,
        projects: current.projects
          .map(project => project.gid === selectedProject.gid
            ? { ...project, isFavorite: !project.isFavorite }
            : project)
          .sort((left, right) =>
            Number(right.isFavorite) - Number(left.isFavorite)
            || left.name.localeCompare(right.name, 'ja-JP')),
      } : current)
    } catch {
      setError('お気に入りを更新できませんでした。')
    } finally {
      setFavoriteBusy(false)
    }
  }

  return (
    <div className="destination-picker">
      <div className="field">
        <label htmlFor={`${idPrefix}-project`}>Asanaプロジェクト</label>
        {(catalog?.projects.length ?? 0) > 8 && <input className="project-search" value={projectSearch} placeholder="プロジェクト名を検索" onChange={event => setProjectSearch(event.target.value)} />}
        <div className="project-select-row">
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
            {selectedProject && projectSearch && !visibleProjects.some(project => project.gid === selectedProject.gid)
              && <option value={selectedProject.gid}>{selectedProject.isFavorite ? '★ ' : ''}{selectedProject.name}</option>}
            {visibleProjects.map(project =>
              <option key={project.gid} value={project.gid}>{project.isFavorite ? '★ ' : ''}{project.name}</option>)}
          </select>
          {selectedProject && <button type="button" className={`favorite-button ${selectedProject.isFavorite ? 'active' : ''}`} disabled={disabled || favoriteBusy} onClick={() => void toggleFavorite()} aria-label={selectedProject.isFavorite ? 'お気に入りから外す' : 'お気に入りに追加'} title={selectedProject.isFavorite ? 'お気に入りから外す' : 'お気に入りに追加'}>{selectedProject.isFavorite ? '★' : '☆'}</button>}
        </div>
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
        <summary>一覧にない場合は番号を直接入力</summary>
        <div className="advanced-grid">
          <div className="field">
            <label htmlFor={`${idPrefix}-project-gid`}>プロジェクト番号</label>
            <input
              id={`${idPrefix}-project-gid`}
              inputMode="numeric"
              value={projectGid}
              disabled={disabled}
              onChange={event => onChange(event.target.value, '')}
            />
          </div>
          <div className="field">
            <label htmlFor={`${idPrefix}-section-gid`}>セクション番号</label>
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
