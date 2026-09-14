import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { ArrowLeft, ArrowRight, BookOpen, Check, CheckCheck, ChevronRight, CircleHelp, HardDrive as CloudCheck, Copy, Download, Eraser, Flower2, Hand, LayoutGrid, LoaderCircle, Maximize, Maximize2, Minimize2, Minus, MousePointer2, Paintbrush, Pencil, Plus, Redo2, Search, ShieldCheck, Cable as Spool, SquareDashed, Trash2, Undo2, Upload, X } from 'lucide-react';
import { api, ApiError, request, resetSession, setSessionIdentity, components, effectiveCode, usageLabel, usageBackground, count, percentage, stitchTypes, typeName } from './types';
import type { Command, Definition, Inventory, Project, ProjectState, ProjectCard, Region, Stitch, Thread } from './types';
import { PatternCanvas } from './PatternCanvas';
import { StitchSymbol } from './StitchSymbol';
import { PageBuilder } from './PageBuilder';
import { LandingPage } from './LandingPage';
import { DataRetentionPage } from './DataRetentionPage';
import { PublicLegalPage, LegalAccountPage, LegalBoundary, AdminLegalPage } from './LegalDocuments';
import { AccountMenu } from './AccountMenu';
import { BetaBoundary, BetaDialog, BetaTools, WhatsNew } from './BetaExperience';
import { BetaAdmin } from './BetaAdmin';
import { Notification, readableError, SuccessNotification } from './Notifications';

type Screen = 'library' | 'inventory' | 'backups' | 'workspace';
export function App() {
  return location.pathname === '/data-retention' ? <DataRetentionPage/> : /^\/legal(?:\/|$)/.test(location.pathname) ? <PublicLegalPage/> : <AuthenticatedApp/>;
}
function AuthenticatedApp() {
  const [user, setUser] = useState<{ id: string; displayName: string; email: string } | null>(null);
  const [loading, setLoading] = useState(true);
  const [expired, setExpired] = useState(false);
  const [error, setError] = useState(location.search.includes('signin=failed') ? 'Google sign-in was cancelled or could not be completed. Please try again.' : '');
  useEffect(() => {
    api<typeof user>('/me').then(u => { setUser(u); if (u) setSessionIdentity(u.id); }).catch(e => { if (e.status !== 401) setError(e.message); }).finally(() => setLoading(false));
    const expired = () => { setExpired(true); setError('Your session expired. Sign in again to continue.'); };
    window.addEventListener('session-expired', expired); return () => window.removeEventListener('session-expired', expired);
  }, []);
  if (loading) return <main className="signin-page"><LoaderCircle className="spin"/>Opening your stitching space…</main>;
  if (!user) return <LandingPage error={error}/>;
  const logout = async () => { await request('/auth/logout', 'POST'); resetSession(); setUser(null); setError(''); };
  const deleteAccount = async () => { await api('/account', 'DELETE', { confirmation: 'DELETE' }); resetSession(); setUser(null); setError(''); };
  if (location.pathname === '/admin/legal') return <AdminLegalPage/>;
  if (location.pathname === '/account/legal') return <LegalAccountPage/>;
  return <><LegalBoundary key={user.id} logout={logout} deleteAccount={deleteAccount}><BetaBoundary logout={logout}>{location.pathname === '/whats-new' ? <WhatsNew/> : location.pathname === '/admin/beta' ? <BetaAdmin/> : location.pathname !== '/' ? <main className="beta-page"><h1>Page not found</h1><p>This page does not exist. <a href="/">Return to your projects</a>.</p></main> : <StitchingApp user={user} logout={logout} deleteAccount={deleteAccount}/>}</BetaBoundary></LegalBoundary>{expired && <BetaDialog title="Your session expired" close={() => setExpired(false)}><p role="alert">{error}</p><p>Keep this tab open to preserve pending work. Sign in to the same account in a new tab, then return here.</p><a href="/auth/google" target="_blank" rel="noopener noreferrer">Sign in with Google</a><button onClick={async () => { try { const current = await api<{ id: string }>('/me'); if (current.id !== user.id) { setError('A different account is signed in. Sign in to the original account to retry this work.'); return; } resetSession(); setExpired(false); setError(''); } catch (e) { setError((e as Error).message); } }}>I signed in — resume this tab</button></BetaDialog>}</>;
}
function StitchingApp({ user, logout, deleteAccount }: { user: { displayName: string; email: string }; logout: () => Promise<void>; deleteAccount: () => Promise<void> }) {
  const [screen, setScreen] = useState<Screen>('library');
  const [projects, setProjects] = useState<ProjectCard[]>([]);
  const [catalog, setCatalog] = useState<Thread[]>([]);
  const [maxUploadMb, setMaxUploadMb] = useState(45);
  const [inventory, setInventory] = useState<Inventory[]>([]);
  const [project, setProject] = useState<Project | null>(null);
  const [busy, setBusy] = useState(false);
  const [ready, setReady] = useState(false);
  const [error, setError] = useState('');
  const [notice, setNotice] = useState('');
  const [retry, setRetry] = useState<(() => void) | null>(null);
  const [importOpen, setImportOpen] = useState(false);
  const [accountOpen, setAccountOpen] = useState(false);
  const [deleteConfirmation, setDeleteConfirmation] = useState('');
  const [query, setQuery] = useState('');
  const [status, setStatus] = useState('all');
  const fileInput = useRef<HTMLInputElement>(null);
  const locked = useRef(false);
  const mutationFailed = useRef(false);
  const failedCommand = useRef<string | null>(null);
  async function refresh() {
    const [p, c, i, capabilities] = await Promise.all([api<ProjectCard[]>('/projects'), api<Thread[]>('/catalog'), api<Inventory[]>('/inventory'), api<{ maxUploadMegabytes: number }>('/capabilities')]);
    setProjects(p); setCatalog(c); setInventory(i); setMaxUploadMb(capabilities.maxUploadMegabytes); setReady(true);
  }
  useEffect(() => { refresh().catch(e => setError(e.message)); }, []);
  const dismissNotice = useCallback(() => setNotice(''), []);
  useEffect(() => { const prevent = (e: BeforeUnloadEvent) => { if (locked.current) { e.preventDefault(); e.returnValue = ''; } }; window.addEventListener('beforeunload', prevent); return () => window.removeEventListener('beforeunload', prevent); }, []);
  async function run(action: () => Promise<void>) {
    if (progressActive.current && !await flushProgress()) return;
    if (locked.current) return;
    locked.current = true; setBusy(true); setError(''); setRetry(null);
    try { await action(); }
    catch (e) { setError(e instanceof ApiError ? e.message : new ApiError(e instanceof Error ? e.message : 'Something went wrong. Please try again.', 0, { code: 'CLIENT_FAILURE', referenceId: `client-${crypto.randomUUID()}` }).message); }
    finally { locked.current = false; setBusy(false); }
  }
  const open = (id: string) => run(async () => { const p = await api<Project>(`/projects/${id}`); mutationFailed.current = false; setProject(p); setScreen('workspace'); });
  async function navigate(next: Screen) { await run(async () => { setScreen(next); if (next === 'library' || next === 'inventory') await refresh(); }); }
  const sample = () => run(async () => { const p = await api<Project>('/projects/sample', 'POST'); setProject(p); setScreen('workspace'); setImportOpen(false); setNotice('Your little garden is ready. Try painting a few stitches!'); });
  const importPdf = (file: File) => run(async () => { if (file.size > maxUploadMb * 1024 * 1024) throw new Error(`Choose a PDF no larger than ${maxUploadMb} MB.`); const form = new FormData(); form.append('file', file); const p = await api<Project>('/imports', 'POST', form); setProject(p); setScreen('workspace'); setImportOpen(false); });
  const projectRef = useRef(project); projectRef.current = project;
  const [progressSaving, setProgressSaving] = useState(false);
  const progressActive = useRef(false);
  const pending = useRef(new Map<string, boolean>());
  const batch = useRef<{ requestId: string; changes: { stitchId: string; complete: boolean }[] } | null>(null);
  const timer = useRef<ReturnType<typeof setTimeout> | undefined>(undefined);
  const progressFlight = useRef<Promise<boolean> | null>(null);
  const generation = useRef(0);
  const alive = useRef(true);
  useEffect(() => { alive.current = true; return () => { alive.current = false; clearTimeout(timer.current); }; }, []);
  async function reconcile(state: ProjectState, before: Project) {
    const p = state.dataRevision !== before.dataRevision ? await api<Project>(`/projects/${before.id}`) : { ...before, ...state };
    const done = new Set(p.completed); pending.current.forEach((complete, id) => complete ? done.add(id) : done.delete(id));
    if (alive.current) { projectRef.current = { ...p, completed: [...done] }; setProject(projectRef.current); }
  }
  function flushProgress(): Promise<boolean> {
    if (progressFlight.current) return progressFlight.current;
    if (!progressActive.current) return Promise.resolve(true);
    clearTimeout(timer.current);
    const flight = sendProgress();
    progressFlight.current = flight;
    void flight.finally(() => { progressFlight.current = null; });
    return flight;
  }
  async function sendProgress(): Promise<boolean> {
    setError(''); setRetry(null);
    try {
      while (batch.current || pending.current.size) {
        if (!batch.current) { const changes = [...pending.current].slice(0, 100000).map(([stitchId, complete]) => ({ stitchId, complete })); batch.current = { requestId: crypto.randomUUID(), changes }; changes.forEach(c => pending.current.delete(c.stitchId)); }
        const current = projectRef.current!;
        let saved: ProjectState;
        try { saved = await api<ProjectState>(`/projects/${current.id}/progress`, 'POST', batch.current); }
        catch (e) { if (e instanceof ApiError && e.status < 500) throw e; saved = await api<ProjectState>(`/projects/${current.id}/progress`, 'POST', batch.current); }
        batch.current = null; await reconcile(saved, current);
        const milestones = saved.milestones.filter(m => !current.milestones.includes(m));
        if (milestones.length) setNotice(`${Math.max(...milestones)}% complete. Look how far you’ve come!`);
      }
      mutationFailed.current = false; locked.current = false; progressActive.current = false; setProgressSaving(false);
      return true;
    } catch (e) {
      mutationFailed.current = true;
      setError(`${e instanceof Error ? e.message : 'Could not save.'} Your highlighted changes are not yet confirmed. Keep this tab open and retry saving.`);
      setRetry(() => flushProgress);
      return false;
    }
  }
  async function discardProgress() {
    if (!window.confirm('Discard unconfirmed changes and reload the server state? A request that already reached the server may be included in that state.')) return;
    if (progressFlight.current) await progressFlight.current;
    try {
      const saved = await api<Project>(`/projects/${projectRef.current!.id}`);
      clearTimeout(timer.current); pending.current.clear(); batch.current = null; mutationFailed.current = false;
      locked.current = false; progressActive.current = false; setProgressSaving(false); projectRef.current = saved; setProject(saved); setError(''); setRetry(null);
    } catch (e) { setError((e as Error).message); }
  }
  const command = useCallback(async (change: Command) => {
    // The page builder retains its exact draft/revision. Replaying that same command cannot apply twice:
    // a committed first attempt increments the expected revision and rejects the replay.
    if (mutationFailed.current && (change.kind !== 'layout' || failedCommand.current !== JSON.stringify(change))) return false;
    const before = projectRef.current;
    if (!before) return false;
    if (change.kind === 'complete' && (!locked.current || progressActive.current)) {
      generation.current++; locked.current = true; progressActive.current = true; setProgressSaving(true);
      const done = new Set(before.completed);
      for (const id of change.stitchIds || []) { pending.current.set(id, !!change.complete); if (change.complete) done.add(id); else done.delete(id); }
      projectRef.current = { ...before, completed: [...done] }; setProject(projectRef.current);
      clearTimeout(timer.current); timer.current = setTimeout(flushProgress, 300); return true;
    }
    // Navigation and revision-based edits wait for the same save, without fading the UI.
    if (progressActive.current && !await flushProgress()) return false;
    if (locked.current) return false;
    const current = projectRef.current;
    if (!current) return false;
    generation.current++; locked.current = true; setBusy(true); setError(''); setRetry(null);
    try { const saved = await api<Project>(`/projects/${current.id}/commands`, 'POST', { ...change, revision: change.revision ?? current.revision }); mutationFailed.current = false; failedCommand.current = null; projectRef.current = saved; setProject(saved); return true; }
    catch (e) { mutationFailed.current = true; failedCommand.current = JSON.stringify(change); setError(`${e instanceof Error ? e.message : 'Could not save.'} Reload before editing again.`); setRetry(() => () => open(current.id)); if (change.kind === 'layout') throw e; return false; }
    finally { locked.current = false; setBusy(false); }
  }, []);
  useEffect(() => {
    if (screen !== 'workspace' || !project?.id) return;
    const refreshActive = async () => {
      if (locked.current || document.visibilityState === 'hidden') return;
      const before = projectRef.current!; const version = generation.current;
      try {
        const state = await api<ProjectState | undefined>(`/projects/${before.id}/state?revision=${before.revision}`);
        if (!state || locked.current || version !== generation.current || projectRef.current?.id !== before.id) return;
        const fresh = state.dataRevision !== before.dataRevision ? await api<Project>(`/projects/${before.id}`) : { ...before, ...state };
        if (!locked.current && version === generation.current && projectRef.current?.id === before.id) { projectRef.current = fresh; setProject(fresh); }
      } catch (e) { if (!locked.current) setError(`Could not refresh this project. ${e instanceof Error ? e.message : 'Please try again.'}`); }
    };
    const interval = setInterval(refreshActive, 30000); window.addEventListener('focus', refreshActive);
    return () => { clearInterval(interval); window.removeEventListener('focus', refreshActive); };
  }, [project?.id, screen]);
  const totalDone = projects.reduce((n, p) => n + p.completed, 0);
  return <div className="app-shell">
    <aside className="sidebar">
      <button className="brand" onClick={() => navigate('library')} aria-label="Stitch Helper home"><span className="brand-mark"><Spool size={25}/></span><span>stitch<span className="brand-light">helper</span><small>A LITTLE, EVERY DAY.</small></span></button>
      <div className="nav-caption">YOUR STITCHING SPACE</div>
      <nav aria-label="Main navigation">
        <button className={screen === 'library' || screen === 'workspace' ? 'nav-item active' : 'nav-item'} disabled={busy} onClick={() => navigate('library')}><LayoutGrid size={18}/>My projects<span>{projects.length || ''}</span></button>
        <button className={screen === 'inventory' ? 'nav-item active' : 'nav-item'} disabled={busy} onClick={() => navigate('inventory')}><Spool size={18}/>Thread collection</button>
        <button className={screen === 'backups' ? 'nav-item active' : 'nav-item'} disabled={busy} onClick={() => navigate('backups')}><ShieldCheck size={18}/>Backups & Export</button>
      </nav>
      <div className="sidebar-note"><Flower2 size={30} strokeWidth={1.2}/><p>One stitch at a time.<br/>Something lovely is growing.</p></div>
      <AccountMenu user={user} busy={busy} logout={() => { void run(logout); }} openAccount={() => { setDeleteConfirmation(''); setAccountOpen(true); }}/>
    </aside>
    <div className="main-shell">
      <BetaTools route={screen} project={project} importOpen={importOpen} savePending={busy || progressSaving} saveError={!!error || mutationFailed.current} openImport={() => setImportOpen(true)}/>
      <header className="topbar"><div className="breadcrumb">Your stitching space <ChevronRight size={14}/><strong>{screen === 'workspace' ? project?.status === 'audit' ? 'Review import' : 'At the hoop' : screen === 'inventory' ? 'Thread collection' : screen === 'backups' ? 'Backups & Export' : 'My projects'}</strong></div><span className="topbar-note"><span className="tiny-flower">✳</span> A little time for something you love</span></header>
      {error && <Notification className="error-banner" tone="error" title={error.includes('NETWORK_FAILURE') ? 'Connection interrupted' : retry ? 'Your changes need attention' : 'Something needs attention'} details={error}
        close={!retry && ready ? () => setError('') : undefined} closeLabel="Dismiss error"
        actions={<>{retry ? <button className="notice-action-primary" disabled={busy} onClick={retry}>{progressSaving ? 'Retry saving' : 'Reload saved project'}</button> : !ready && <button className="notice-action-primary" disabled={busy} onClick={() => run(refresh)}>Retry connection</button>}
          <button onClick={() => navigator.clipboard.writeText(error).catch(() => window.prompt('Copy error details', error))}><Copy size={14} aria-hidden="true" />Copy error details</button>
          {progressSaving && retry && <button className="notice-action-quiet" onClick={discardProgress}>Discard unconfirmed changes</button>}</>}>{readableError(error)}</Notification>}
      {screen === 'workspace' && project ? <Workspace key={project.id} project={project} catalog={catalog} inventory={inventory} busy={busy} progressSaving={progressSaving} saveError={!!error || mutationFailed.current} command={command} onBack={() => navigate('library')} onInventory={() => navigate('inventory')} onRename={name => command({ kind: 'rename', name })}/> : screen === 'inventory' ? <InventoryScreen catalog={catalog} inventory={inventory} busy={busy} save={entry => run(async () => { await api(`/inventory/${entry.code}`, 'PUT', entry); setInventory(await api<Inventory[]>('/inventory')); setNotice(`DMC ${entry.code} saved to your collection.`); })}/> : screen === 'backups' ? <Backups busy={busy} run={run} notify={setNotice}/> : <main className="library page">
        <div className="page-heading"><div><div className="eyebrow">MAKE ROOM FOR MAKING</div><h1>My projects</h1><p>A few quiet moments. A few more stitches. All your works in progress, here.</p></div><button data-tour="import" className="primary" disabled={busy} onClick={() => setImportOpen(true)}><Plus size={18}/>Import a pattern</button></div>
        <section className="welcome-banner"><div className="welcome-copy"><span className="eyebrow">YOUR NEXT LITTLE MOMENT</span><h2>{projects.some(p => p.status === 'active') ? 'Pick up where you left off.' : 'Every lovely thing starts\nwith a single stitch.'}</h2><p>{projects.some(p => p.status === 'active') ? 'Your place is saved. Your threads are waiting.\nLet’s make a little more progress.' : 'Bring your pattern, find your thread, and settle in.\nWe’ll keep your place along the way.'}</p><button className="text-button" disabled={busy} onClick={projects.some(p => p.status === 'active') ? () => open(projects.find(p => p.status === 'active')!.id) : sample}>{projects.some(p => p.status === 'active') ? 'Continue stitching' : 'Try the little garden sampler'}<ArrowRight size={17}/></button></div><div className="hoop-art" aria-hidden="true"><div className="hoop-screw"/><div className="hoop"><div className="embroidered-flower f1">✿</div><div className="embroidered-flower f2">✿</div><div className="embroidered-flower f3">✿</div><div className="embroidered-stem s1"/><div className="embroidered-stem s2"/><div className="embroidered-leaf l1"/><div className="embroidered-leaf l2"/><div className="embroidered-leaf l3"/><span className="hoop-star">✧</span></div><span className="art-caption">GOOD THINGS TAKE A LITTLE THREAD.</span></div></section>
        <div className="library-stats"><div><span className="stat-icon"><BookOpen size={20}/></span><strong>{projects.filter(p => p.status === 'active' && p.completed < p.total).length}</strong><span>in progress</span></div><div><span className="stat-icon blush"><CheckCheck size={20}/></span><strong>{count(totalDone)}</strong><span>stitches made</span></div><div><span className="stat-icon gold"><Spool size={20}/></span><strong>{inventory.filter(i => i.bobbinCount > 0).length}</strong><span>threads in your collection</span></div></div>
        <div className="library-controls"><div className="tabs">{[['all', 'All projects'], ['active', 'In progress'], ['finished', 'Finished'], ['audit', 'To review']].map(([value, label]) => <button key={value} className={status === value ? 'selected' : ''} onClick={() => setStatus(value)}>{label}</button>)}</div><label className="search"><Search size={17}/><input placeholder="Find a project…" aria-label="Find a project" value={query} onChange={e => setQuery(e.target.value)}/></label></div>
        {!ready && !error ? <div className="empty"><LoaderCircle className="spin"/>Getting your stitching space ready…</div> : <div className="project-grid">{projects.filter(p => p.name.toLowerCase().includes(query.toLowerCase()) && (status === 'all' || status === 'finished' ? status === 'all' || p.total > 0 && p.completed === p.total : status === 'active' ? p.status === 'active' && p.completed < p.total : p.status === status)).map(p => <article className="project-card" key={p.id}><button className="project-image" disabled={busy} onClick={() => open(p.id)} aria-label={`Open ${p.name}`}>{p.preview ? <PatternCanvas data={p.preview} catalog={catalog} preview/> : <Flower2 size={64} strokeWidth={1}/>}<span className={`project-badge ${p.status === 'audit' ? 'audit' : ''}`}>{p.status === 'audit' ? 'Ready to review' : p.total > 0 && p.completed === p.total ? 'Finished with love' : 'On the hoop'}</span></button><div className="project-card-body"><button className="project-title" disabled={busy} onClick={() => open(p.id)}>{p.name}<ArrowRight size={16}/></button><p>{p.width} × {p.height} stitches<span>·</span>{p.colors} thread colors</p><div className="progress-label"><span>{count(p.completed)} of {count(p.total)} stitches</span><strong>{percentage(p.completed, p.total).toFixed(1)}%</strong></div><Progress value={percentage(p.completed, p.total)}/><div className="card-footer"><span>{p.status === 'audit' ? 'Check the chart before you begin' : `Saved ${new Date(p.updatedAt).toLocaleDateString(undefined, { month: 'short', day: 'numeric' })}`}</span><button className="icon-button" title="Start an independent project from the source" aria-label={`New start of ${p.name}`} disabled={busy} onClick={() => run(async () => { const copy = await api<Project>(`/projects/${p.id}/duplicate`, 'POST'); setProject(copy); setScreen('workspace'); })}><Copy size={15}/></button><button className="icon-button" title="Delete project" aria-label={`Delete ${p.name}`} disabled={busy} onClick={() => { if (window.confirm(`Delete “${p.name}”? This permanently removes its progress and edits, plus your retained backup archives. If this is the last project using the pattern, its original PDF is deleted too. Download a copy first if you want to keep it.`)) run(async () => { await api(`/projects/${p.id}`, 'DELETE'); await refresh(); }); }}><Trash2 size={15}/></button></div></div></article>)}<button className="new-project-card" onClick={() => setImportOpen(true)} disabled={busy}><span><Plus size={25}/></span><strong>A new beginning</strong><p>Bring a PDF pattern into<br/>your stitching space.</p><small>Import a pattern <ArrowRight size={13}/></small></button></div>}
        {ready && projects.length === 0 && <p className="empty">No projects or imports yet. Import a PDF or try the garden sampler to begin.</p>}
        {ready && projects.length > 0 && status === 'audit' && !projects.some(p => p.status === 'audit') && <p className="empty">No imports waiting for review. Import a PDF to start a new project.</p>}
        <div className="library-footnote"><ShieldCheck size={16}/>Your patterns are private. Your saved progress follows you across computers.</div>
      </main>}
    </div>
    {notice && <SuccessNotification message={notice} close={dismissNotice}/>}
    {accountOpen && <Modal title="Account & data" close={() => !busy && setAccountOpen(false)}><p>Download a copy from Backups & Export before deleting anything you want to keep.</p><p><a href="/account/legal" target="_blank" rel="noreferrer">My legal acceptances &amp; documents</a></p><p><a href="/data-retention" target="_blank" rel="noreferrer">Read our data retention and deletion policy</a></p><h3>Delete your account permanently</h3><p>This removes your Stitch Helper profile, projects, original PDFs, progress, thread inventory, preferences, and retained backups. It cannot be undone. Your Google account is unaffected.</p><label className="field-label">Type DELETE to confirm<input autoComplete="off" value={deleteConfirmation} onChange={e => setDeleteConfirmation(e.target.value)}/></label><button className="secondary full" disabled={busy || deleteConfirmation !== 'DELETE'} onClick={() => run(deleteAccount)}><Trash2 size={17}/>Delete my account</button></Modal>}
    {importOpen && <Modal title="A new beginning" close={() => !busy && setImportOpen(false)}>{error && <div role="alert"><p>{error}</p><button onClick={() => { setImportOpen(false); window.dispatchEvent(new Event("open-feedback")); }}>Report this upload problem</button></div>}<p className="muted">Choose a cross-stitch PDF. We’ll find its grid and key, then help you check the result before stitching.</p><button className="upload-zone" disabled={busy} onClick={() => fileInput.current?.click()} onDragOver={e => e.preventDefault()} onDrop={e => { e.preventDefault(); if (e.dataTransfer.files[0]) importPdf(e.dataTransfer.files[0]); }}>{busy ? <LoaderCircle className="spin" size={32}/> : <Upload size={32}/>}<strong>{busy ? 'Finding the threads in your pattern…' : 'Choose a PDF or drop it here'}</strong><span>Text and vector PDFs · up to {maxUploadMb} MB</span></button><input ref={fileInput} type="file" accept=".pdf,application/pdf" hidden onChange={e => { if (e.target.files?.[0]) importPdf(e.target.files[0]); e.target.value = ''; }}/><div className="soft-note"><CircleHelp size={18}/><span>Every import gets a review. Scans, unusual symbols, and specialty stitches may need another format or manual corrections.</span></div><button data-tour="sampler" className="secondary full" disabled={busy} onClick={sample}><Flower2 size={17}/>Explore with the little garden sampler</button></Modal>}
  </div>;
}
function Progress({ value }: { value: number }) { return <div className="progress-track" role="progressbar" aria-label="Stitches completed" aria-valuenow={Math.round(value)} aria-valuemin={0} aria-valuemax={100}><div style={{ width: `${Math.min(100, value)}%` }}/></div>; }
function Modal({ title, close, children }: { title: string; close: () => void; children: React.ReactNode }) {
  const dialog = useRef<HTMLDialogElement>(null);
  useEffect(() => { const previous = document.activeElement as HTMLElement | null; const el = dialog.current!; el.showModal(); return () => { el.close(); if (previous?.isConnected) previous.focus(); }; }, []);
  return <dialog ref={dialog} className="modal" onCancel={e => { e.preventDefault(); close(); }}><div className="modal-heading"><h2>{title}</h2><button className="icon-button" aria-label="Close dialog" onClick={close}><X size={21}/></button></div>{children}</dialog>;
}

interface WorkspaceProps { project: Project; catalog: Thread[]; inventory: Inventory[]; busy: boolean; progressSaving: boolean; saveError: boolean; command: (command: Command) => Promise<boolean>; onBack: () => void; onInventory: () => void; onRename: (name: string) => void }
function Workspace({ project: p, catalog, inventory, busy, progressSaving, saveError, command, onBack, onInventory, onRename }: WorkspaceProps) {
  const [selected, setSelected] = useState<string[]>([]);
  const [tool, setTool] = useState('inspect');
  const [type, setType] = useState('all');
  const [status, setStatus] = useState('all');
  const [isolate, setIsolate] = useState(false);
  const [pages, setPages] = useState(false);
  const [builder, setBuilder] = useState(false);
  const [scope, setScope] = useState('viewport');
  const [visible, setVisible] = useState<Region>({ minX: 0, minY: 0, maxX: p.data.width - 1, maxY: p.data.height - 1 });
  const [zoom, setZoom] = useState<number | undefined>();
  const [currentZoom, setCurrentZoom] = useState(8);
  const [fit, setFit] = useState(0);
  const [focused, setFocused] = useState(false);
  const focusButton = useRef<HTMLButtonElement>(null);
  const leaveFocus = () => { setFocused(false); focusButton.current?.focus({ preventScroll: true }); };
  const [keyQuery, setKeyQuery] = useState('');
  const [inspected, setInspected] = useState<{ stitch?: Stitch; x: number; y: number } | null>(null);
  const [editing, setEditing] = useState<Definition | null>(null);
  const [tab, setTab] = useState('work');
  const [focus, setFocus] = useState<{ x: number; y: number; nonce: number }>();
  const [rename, setRename] = useState(false);
  const [name, setName] = useState(p.name);
  const done = useMemo(() => new Set(p.completed), [p.completed]);
  const threads = useMemo(() => new Map(catalog.map(t => [t.code, t])), [catalog]);
  const definitions = useMemo(() => new Map(p.data.definitions.map(d => [d.id, d])), [p.data.definitions]);
  const stats = useMemo(() => {
    const result = new Map<string, { total: number; completed: number }>();
    for (const stitch of p.data.stitches) { const entry = result.get(stitch.definitionId) || { total: 0, completed: 0 }; entry.total++; if (done.has(stitch.id)) entry.completed++; result.set(stitch.definitionId, entry); }
    return result;
  }, [p.data.stitches, done]);
  const candidates = useMemo(() => {
    const region = scope === 'area' ? p.workingArea : scope === 'viewport' ? visible : null;
    if (scope === 'area' && !region) return [];
    return p.data.stitches.filter(s => (!selected.length || selected.includes(s.definitionId)) && (type === 'all' || definitions.get(s.definitionId)?.stitchType === type) && (status === 'all' || done.has(s.id) === (status === 'complete')) && (!region || s.x >= region.minX && s.x <= region.maxX && s.y >= region.minY && s.y <= region.maxY));
  }, [scope, p.workingArea, visible, p.data.stitches, selected, type, status, definitions, done]);
  const audit = p.status === 'audit';
  const total = p.data.stitches.length;
  const percent = percentage(done.size, total);
  const currentStitch = inspected?.stitch ? p.data.stitches.find(s => s.id === inspected.stitch!.id) : undefined;
  const activeDefinition = currentStitch ? definitions.get(currentStitch.definitionId) : selected.length === 1 ? definitions.get(selected[0]) : undefined;
  const effective = (d: Definition) => usageLabel(d, p.substitutions);
  const physicalCodes = (d: Definition) => components(d).map(c => effectiveCode(c, p.substitutions));
  const names = (d: Definition) => physicalCodes(d).map(code => threads.get(code)?.name || 'Choose a thread').join(' + ');
  useEffect(() => {
    const handler = (e: KeyboardEvent) => {
      if (e.defaultPrevented || (e.target as HTMLElement).closest('dialog')) return;
      if (e.key === 'Escape' && focused) { e.preventDefault(); leaveFocus(); return; }
      if ((e.target as HTMLElement).closest('input,textarea,select,dialog') || busy) return;
      if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'z') { e.preventDefault(); command({ kind: e.shiftKey ? 'redo' : 'undo' }); }
      if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'y') { e.preventDefault(); command({ kind: 'redo' }); }
      if (e.key === 'Escape') { setSelected([]); setInspected(null); }
    };
    window.addEventListener('keydown', handler); return () => window.removeEventListener('keydown', handler);
  }, [command, busy, focused]);
  const choose = (id: string, multi = false) => setSelected(old => multi ? old.includes(id) ? old.filter(x => x !== id) : [...old, id] : old.length === 1 && old[0] === id ? [] : [id]);
  return <main className={`workspace${focused ? ' is-focused' : ''}`}>
    <div className="workspace-heading"><div className="workspace-title"><button className="icon-button" onClick={onBack} disabled={busy} aria-label="Back to projects"><ArrowLeft size={20}/></button><div><div className="eyebrow">{audit ? 'A LITTLE CHECK BEFORE YOU BEGIN' : 'AT THE HOOP'}</div><button className="title-button" onClick={() => setRename(true)}><h1>{p.name}</h1><Pencil size={13}/></button><span className="muted">{p.data.width} × {p.data.height} · {p.data.definitions.length} colors · {p.data.pages.length} chart pages</span></div></div><div data-tour="progress" className="workspace-progress"><div><strong>{percent.toFixed(1)}<small>%</small></strong><span>{count(done.size)} / {count(total)} stitches<br/><b>{count(total - done.size)} steps to go</b></span></div><Progress value={percent}/></div><span data-tour="save" className={`save-status ${busy || progressSaving ? 'saving' : ''}`} role="status">{busy || progressSaving ? <LoaderCircle size={17} className="spin"/> : <CloudCheck size={17}/>} {saveError ? 'Unable to save or refresh - retry above' : busy || progressSaving ? 'Saving…' : `Saved ${new Date(p.updatedAt).toLocaleTimeString()}`}</span></div>
    {audit && <div className="audit-strip"><BookOpen size={20}/><div><strong>Does this look like your pattern?</strong><span>Check the key, chart, and page placement. Your original PDF is always available.</span></div>{p.sourceAvailable && <a className="secondary" href={`/api/projects/${p.id}/source`} target="_blank" rel="noreferrer">Open original PDF</a>}<button className="secondary builder-entry" disabled={busy || !p.data.pages.length} onClick={() => setBuilder(true)}><LayoutGrid size={16}/>Arrange pages</button><button data-tour="confirm-import" className="primary" disabled={busy || !total} onClick={() => command({ kind: 'confirm' })}>Looks good, start stitching<ArrowRight size={16}/></button></div>}
    <div className="workspace-body">
      <aside className="pattern-key"><div className="panel-heading"><h3>Pattern key</h3><span className="pill">{p.data.definitions.length}</span></div><label className="search compact"><Search size={15}/><input aria-label="Search pattern key" placeholder="Find a color or symbol…" value={keyQuery} onChange={e => setKeyQuery(e.target.value)}/></label><div className="key-help">Click to highlight · Ctrl-click for multiple</div><div className="key-list">{p.data.definitions.filter(d => `${d.symbol} ${effective(d)} ${names(d)} ${typeName(d.stitchType)}`.toLowerCase().includes(keyQuery.toLowerCase())).map(d => {
        const stat = stats.get(d.id) || { total: 0, completed: 0 };
        return <div className={`key-entry ${selected.includes(d.id) ? 'chosen' : ''}`} key={d.id}><button data-tour="thread" className="key-choice" onClick={e => choose(d.id, e.ctrlKey || e.metaKey || e.shiftKey)} aria-pressed={selected.includes(d.id)}><span className="symbol-swatch" style={{ background: usageBackground(d, threads, p.substitutions) }}><span><StitchSymbol definition={d}/></span></span><span className="key-name"><strong>DMC {effective(d)}{components(d).some(c => p.substitutions[c.id]) && <small> ↔</small>}</strong><span>{names(d)}</span><small>{typeName(d.stitchType)} · {count(stat.total - stat.completed)} left</small></span><span className="key-percent">{Math.round(percentage(stat.completed, stat.total))}%</span></button><button className="key-edit" aria-label={`Edit symbol ${d.symbol}`} title="Edit symbol, thread or stitch type" disabled={busy} onClick={() => setEditing({ ...d })}><Pencil size={12}/></button></div>;
      })}</div><div className="key-bottom"><button className="text-button" onClick={() => { setSelected([]); setType('all'); setStatus('all'); setIsolate(false); }}>Clear selection & filters</button><span>{selected.length ? `${selected.length} definitions selected` : 'All thread colors shown'}</span></div></aside>
      <section className="canvas-panel"><div className="canvas-toolbar"><div className="tool-group">{[['inspect', MousePointer2, 'Inspect'], ['pan', Hand, 'Pan'], ['paint', Paintbrush, 'Paint complete'], ['erase', Eraser, 'Paint incomplete'], ['area', SquareDashed, 'Working area'], ['edit', Pencil, 'Edit stitches']].map(([id, Icon, label]) => { const ToolIcon = Icon as typeof Pencil; return <button key={id as string} className={tool === id ? 'tool selected' : 'tool'} title={label as string} aria-label={label as string} aria-pressed={tool === id} disabled={busy || audit && (id === 'paint' || id === 'erase')} onClick={() => { setTool(id as string); if (id === 'edit') { setFocused(false); setTab('work'); } }}><ToolIcon size={18}/></button>; })}</div><div className="tool-group"><button className="tool" aria-label="Undo" title="Undo (Ctrl+Z)" disabled={busy || !p.canUndo} onClick={() => command({ kind: 'undo' })}><Undo2 size={17}/></button><button className="tool" aria-label="Redo" title="Redo (Ctrl+Shift+Z)" disabled={busy || !p.canRedo} onClick={() => command({ kind: 'redo' })}><Redo2 size={17}/></button></div><div className="zoom-controls"><button className="tool" aria-label="Zoom out" onClick={() => setZoom(Math.max(.3, currentZoom / 1.3))}><Minus size={16}/></button><span>{Math.round(currentZoom / 20 * 100)}%</span><button className="tool" aria-label="Zoom in" onClick={() => setZoom(Math.min(48, currentZoom * 1.3))}><Plus size={16}/></button><button className="tool" title="Fit pattern" aria-label="Fit pattern" onClick={() => setFit(f => f + 1)}><Maximize size={16}/></button></div><button ref={focusButton} className="focus-toggle secondary" aria-pressed={focused} disabled={!total} title={focused ? 'Exit focus (Esc)' : 'Fill the window with your pattern'} onClick={() => setFocused(value => !value)}>{focused ? <Minimize2 size={16}/> : <Maximize2 size={16}/>}<span>{focused ? 'Exit focus' : 'Focus pattern'}</span></button></div>
        {focused && <div className="focus-context"><span>{audit ? 'Review import · ' : ''}{selected.length === 1 ? 'DMC ' + effective(definitions.get(selected[0])!) : selected.length ? selected.length + ' selected colors' : 'All colors'} · {type === 'all' ? 'All stitch types' : typeName(type)} · {status === 'all' ? 'All stitches' : status === 'complete' ? 'Completed stitches' : 'Still to stitch'}{isolate ? ' · Isolated' : ''}{p.workingArea ? ' · Working area active' : ''}</span><button className="text-button" onClick={leaveFocus}>Change thread or filters</button></div>}
        {total ? <PatternCanvas data={p.data} catalog={catalog} completed={done} substitutions={p.substitutions} selected={selected} statusFilter={status} typeFilter={type} isolate={isolate} pages={pages} area={p.workingArea} tool={tool} busy={busy} fitToken={fit} zoom={zoom} focus={focus} onVisible={setVisible} onZoom={setCurrentZoom} onPaint={(ids, complete) => command({ kind: 'complete', stitchIds: ids, complete })} onArea={area => { setScope('area'); command({ kind: 'area', area }); }} onStitch={(stitch, x, y) => { setInspected({ stitch, x, y }); if (stitch && tool === 'inspect') setSelected([stitch.definitionId]); }}/>: <div className="empty unreadable"><BookOpen size={36}/><h2>This pattern needs another look.</h2><p>We couldn’t extract a stitch grid. Your PDF is saved.<br/>Review the notes or try another PDF from your library.</p><a className="secondary" href={`/api/projects/${p.id}/source`} target="_blank" rel="noreferrer">View original PDF</a></div>}
        <div className="canvas-footer"><span>{tool === 'paint' ? 'Drag across matching stitches to complete them' : tool === 'erase' ? 'Drag across matching stitches to mark incomplete' : tool === 'area' ? 'Drag a rectangle to set your working area' : tool === 'edit' ? 'Click a cell to add, change, or remove a stitch' : tool === 'pan' ? 'Drag the chart to move around' : 'Click a stitch to see its thread and details'}</span><label><input type="checkbox" checked={pages} onChange={e => setPages(e.target.checked)}/>Page boundaries</label></div></section>
      <aside className="tools-panel"><div className="tabs panel-tabs">{[['work', audit ? 'Review' : 'Stitch'], ['progress', 'Progress'], ['threads', 'Threads']].map(([value, label]) => <button key={value} className={tab === value ? 'selected' : ''} onClick={() => setTab(value)}>{label}</button>)}</div><div className="tools-scroll">
        {tab === 'work' && <>
          {audit && <div className="audit-notes"><h3>Import notes <span className="pill">{p.data.warnings.length}</span></h3>{p.data.warnings.map((w, i) => <button className="warning-note" key={i} onClick={() => { const page = p.data.pages.find(pg => pg.number === w.page); if (w.x !== undefined && w.y !== undefined || page) { setFocus({ x: w.x ?? page!.x, y: w.y ?? page!.y, nonce: Date.now() }); setPages(true); } }}>{w.page && <strong>Page {w.page} · </strong>}{w.message}</button>)}{p.data.pages.length > 1 && <details><summary>Adjust page placement</summary><p className="muted">Offsets count from zero. Matching repeated stitches are merged; conflicting joins must be corrected.</p>{p.data.pages.map(page => <PageOffset key={`${page.number}-${page.x}-${page.y}`} page={page} busy={busy} save={(x, y) => command({ kind: 'page', pageNumber: page.number, x, y })}/>)}</details>}</div>}
          <h3>Focus your stitching</h3><label className="field-label">Stitch type<select value={type} onChange={e => setType(e.target.value)}><option value="all">All stitch types</option>{stitchTypes.map(t => <option key={t} value={t}>{typeName(t)}</option>)}</select></label><label className="field-label">Show<select value={status} onChange={e => setStatus(e.target.value)}><option value="all">All stitches</option><option value="incomplete">Still to stitch</option><option value="complete">Completed stitches</option></select></label><label className="toggle-row"><span>Isolate matching stitches<small>Hide the rest of the pattern</small></span><input type="checkbox" checked={isolate} onChange={e => setIsolate(e.target.checked)}/></label>
          <div className="section-divider"/><h3>Your working area</h3>{p.workingArea ? <div className="area-card"><SquareDashed size={18}/><span>Columns {p.workingArea.minX + 1}–{p.workingArea.maxX + 1}<br/>Rows {p.workingArea.minY + 1}–{p.workingArea.maxY + 1}</span><button className="icon-button" aria-label="Clear working area" disabled={busy} onClick={() => { setScope('viewport'); command({ kind: 'area', area: null }); }}><X size={15}/></button></div> : <button className="secondary full" onClick={() => setTool('area')}><SquareDashed size={16}/>Draw a working area</button>}
          {!audit && <><label className="field-label">Apply completion to<select value={scope} onChange={e => setScope(e.target.value)}><option value="viewport">Visible stitches</option><option value="area" disabled={!p.workingArea}>Working area</option><option value="pattern">Entire pattern</option></select></label><div className="scope-summary"><strong>{count(candidates.length)} matching stitches</strong><span>{scope === 'area' ? 'Inside your working area' : scope === 'pattern' ? 'Across the entire pattern' : 'Currently in view'} · {selected.length ? `${selected.length} selected definitions` : 'all definitions'}{type !== 'all' ? ` · ${typeName(type)}` : ''}{status !== 'all' ? ` · ${status}` : ''}</span></div><button data-tour="complete" className="primary full" disabled={busy || !candidates.some(s => !done.has(s.id))} onClick={() => command({ kind: 'complete', stitchIds: candidates.map(s => s.id), complete: true })}><CheckCheck size={17}/>Mark matching complete</button><button className="secondary full" disabled={busy || !candidates.some(s => done.has(s.id))} onClick={() => command({ kind: 'complete', stitchIds: candidates.map(s => s.id), complete: false })}>Mark matching incomplete</button><p className="undo-note"><Undo2 size={12}/>Changed your mind? Every action has undo.</p></>}
          {inspected && <><div className="section-divider"/><h3>Column {inspected.x + 1}, row {inspected.y + 1}</h3>{currentStitch && activeDefinition ? <div className="stitch-detail"><strong><StitchSymbol definition={activeDefinition}/> · DMC {effective(activeDefinition)}</strong><span>{names(activeDefinition)}</span><span>{typeName(activeDefinition.stitchType)} · {done.has(currentStitch.id) ? 'Complete' : 'Still to stitch'}</span>{!audit && <button className="secondary full" disabled={busy} onClick={() => command({ kind: 'complete', stitchIds: [currentStitch.id], complete: !done.has(currentStitch.id) })}>{done.has(currentStitch.id) ? 'Mark stitch incomplete' : 'Mark stitch complete'}</button>}</div> : <p className="muted">An empty cell.</p>}{tool === 'edit' && <StitchEditor key={`${inspected.x}-${inspected.y}-${p.revision}`} stitch={currentStitch} x={inspected.x} y={inspected.y} data={p.data} busy={busy} save={stitch => command({ kind: 'stitch', stitch, stitchId: currentStitch?.id })}/>}</>}
        </>}
        {tab === 'progress' && <><div className="progress-celebration"><Flower2 size={34} strokeWidth={1.2}/><h2>{percent.toFixed(1)}% made with love</h2><p>{count(done.size)} stitches behind you.<br/>{count(total - done.size)} still to enjoy.</p></div><h3>By stitch type</h3>{[...new Set(p.data.definitions.map(d => d.stitchType))].map(t => { const ds = p.data.definitions.filter(d => d.stitchType === t); const n = ds.reduce((n, d) => n + (stats.get(d.id)?.total || 0), 0); const c = ds.reduce((n, d) => n + (stats.get(d.id)?.completed || 0), 0); return <div className="mini-progress" key={t}><div><span>{typeName(t)}</span><strong>{c}/{n}</strong></div><Progress value={percentage(c, n)}/></div>; })}<h3>By effective thread</h3>{[...new Set(p.data.definitions.flatMap(physicalCodes))].map(code => { const ds = p.data.definitions.filter(d => physicalCodes(d).includes(code)); const n = ds.reduce((n, d) => n + (stats.get(d.id)?.total || 0), 0), c = ds.reduce((n, d) => n + (stats.get(d.id)?.completed || 0), 0); return <div className="mini-progress" key={code}><div><span>DMC {code}</span><strong>{count(c)}/{count(n)}</strong></div><Progress value={percentage(c, n)}/></div>; })}<h3>By source page</h3>{p.data.pages.map(page => { const stitches = p.data.stitches.filter(s => s.x >= page.x && s.x < page.x + page.width && s.y >= page.y && s.y < page.y + page.height); const c = stitches.filter(s => done.has(s.id)).length; return <div className="mini-progress" key={page.number}><div><span>Page {page.number}</span><strong>{count(c)}/{count(stitches.length)}</strong></div><Progress value={percentage(c, stitches.length)}/></div>; })}<h3>Little milestones</h3><div className="milestones">{[10, 25, 50, 75, 90, 100].map(m => <span key={m} className={p.milestones.includes(m) ? 'reached' : ''}>{p.milestones.includes(m) && <Check size={12}/>} {m}%</span>)}</div></>}
        {tab === 'threads' && <><h3>Everything for this pattern</h3><Materials project={p} catalog={catalog} inventory={inventory}/><button className="secondary full" disabled={busy} onClick={onInventory}><Spool size={15}/>Open thread collection</button><div className="section-divider"/><h3>Make it your own</h3><p className="muted">Replace one component of a symbol. Other components and your source pattern stay preserved.</p>{p.data.definitions.flatMap(d => components(d).map(c => <label className="field-label substitution" key={c.id}>Symbol {d.symbol} · original DMC {c.threadCode}{c.strandCount == null ? ' · strands unspecified' : ' · ' + c.strandCount + ' strands'}<select aria-label={'Replacement for symbol ' + d.symbol + ' component ' + c.threadCode} disabled={busy} value={p.substitutions[c.id] || ''} onChange={e => command({ kind: 'substitute', code: c.id, replacement: e.target.value || null })}><option value="">Use original thread</option>{catalog.map(t => <option value={t.code} key={t.code}>{t.code} · {t.name}</option>)}</select>{p.substitutions[c.id] && <small>Now using DMC {p.substitutions[c.id]} · undo available</small>}</label>))}</>}
      </div></aside>
    </div>
    {builder && <PageBuilder project={p} catalog={catalog} save={command} close={() => { setBuilder(false); setFit(f => f + 1); }}/> }
    {editing && <Modal title="Edit this stitch definition" close={() => setEditing(null)}><p className="muted">Updates every stitch using this definition in this project. You can undo this change.</p>{editing.symbolGlyph && <p className="muted">Original pattern symbol: <StitchSymbol definition={editing}/>. Change the text symbol below only to replace it.</p>}<label className="field-label">Symbol<input maxLength={4} value={editing.symbol} onChange={e => setEditing({ ...editing, symbol: e.target.value })}/></label><h3>Thread components</h3><p className="muted">Add two or more threads for a blend. Leave strands blank if the pattern does not specify them.</p>{components(editing).map((c, i) => <div className="component-editor" key={c.id}><label className="field-label">Thread {i + 1}<select value={c.threadCode} onChange={e => setEditing({ ...editing, components: components(editing).map(x => x.id === c.id ? { ...x, threadCode: e.target.value } : x) })}>{!threads.has(c.threadCode) && <option value={c.threadCode}>Choose thread</option>}{catalog.map(t => <option key={t.code} value={t.code}>{t.code} · {t.name}</option>)}</select></label><label className="field-label">Strands<input type="number" min={1} step={1} value={c.strandCount ?? ''} onChange={e => setEditing({ ...editing, components: components(editing).map(x => x.id === c.id ? { ...x, strandCount: e.target.value === '' ? null : Number(e.target.value) } : x) })}/></label>{components(editing).length > 1 && <button className="text-button" onClick={() => setEditing({ ...editing, components: components(editing).filter(x => x.id !== c.id) })}>Remove component</button>}</div>)}<button className="secondary" disabled={components(editing).length >= 12} onClick={() => setEditing({ ...editing, components: [...components(editing), { id: crypto.randomUUID(), threadCode: catalog.find(t => !components(editing).some(c => c.threadCode === t.code))!.code, strandCount: null }] })}><Plus size={15}/>Add blend component</button><label className="field-label">Stitch type<select value={editing.stitchType} onChange={e => setEditing({ ...editing, stitchType: e.target.value })}>{stitchTypes.map(t => <option key={t} value={t}>{typeName(t)}</option>)}</select></label><button className="primary full" disabled={busy || !editing.symbol.trim() || components(editing).some(c => !threads.has(c.threadCode) || c.strandCount !== null && (!Number.isInteger(c.strandCount) || c.strandCount < 1)) || new Set(components(editing).map(c => c.threadCode)).size !== components(editing).length} onClick={() => { command({ kind: 'definition', definition: editing }); setEditing(null); }}>Save definition</button></Modal>}
    {rename && <Modal title="Name your project" close={() => setRename(false)}><label className="field-label">Project name<input value={name} maxLength={160} onChange={e => setName(e.target.value)}/></label><button className="primary full" disabled={!name.trim() || busy} onClick={() => { onRename(name); setRename(false); }}>Save name</button></Modal>}
  </main>;
}

function PageOffset({ page, busy, save }: { page: { number: number; x: number; y: number }; busy: boolean; save: (x: number, y: number) => void }) {
  const [x, setX] = useState(page.x); const [y, setY] = useState(page.y);
  return <div className="page-offset"><strong>Page {page.number}</strong><label>X<input type="number" min={0} max={5000} value={x} onChange={e => setX(Number(e.target.value))}/></label><label>Y<input type="number" min={0} max={5000} value={y} onChange={e => setY(Number(e.target.value))}/></label><button className="secondary" disabled={busy} onClick={() => save(x, y)}>Set</button></div>;
}
function StitchEditor({ stitch, x, y, data, busy, save }: { stitch?: Stitch; x: number; y: number; data: Project['data']; busy: boolean; save: (s: Stitch | null) => void }) {
  const [definition, setDefinition] = useState(stitch?.definitionId || data.definitions[0]?.id || '');
  const [endX, setEndX] = useState(stitch?.endX ?? x + 1); const [endY, setEndY] = useState(stitch?.endY ?? y + 1);
  const line = data.definitions.find(d => d.id === definition)?.stitchType === 'Backstitch';
  return <div><label className="field-label">Assign definition<select value={definition} onChange={e => setDefinition(e.target.value)}>{data.definitions.map(d => <option value={d.id} key={d.id}>{d.symbol} · {usageLabel(d, {})} · {typeName(d.stitchType)}</option>)}</select></label>{line && <><p className="muted">Line endpoint (grid coordinates from zero)</p><label className="field-label">End X<input type="number" step="0.5" min={0} max={data.width} value={endX} onChange={e => setEndX(Number(e.target.value))}/></label><label className="field-label">End Y<input type="number" step="0.5" min={0} max={data.height} value={endY} onChange={e => setEndY(Number(e.target.value))}/></label></>}<button className="primary full" disabled={busy || !definition} onClick={() => save({ id: stitch?.id || `manual-${crypto.randomUUID()}`, x, y, definitionId: definition, endX: line ? endX : null, endY: line ? endY : null })}>{stitch ? 'Change stitch' : 'Add stitch'}</button>{stitch && <button className="secondary full" disabled={busy} onClick={() => save(null)}>Remove stitch</button>}</div>;
}
function Materials({ project, catalog, inventory }: { project: Project; catalog: Thread[]; inventory: Inventory[] }) {
  const required = [...new Set(project.data.definitions.filter(d => project.data.stitches.some(s => s.definitionId === d.id)).flatMap(d => components(d).map(c => effectiveCode(c, project.substitutions))))];
  const owned = required.filter(code => inventory.some(i => i.code === code && i.bobbinCount > 0)).length;
  return <><div className="material-summary"><span><strong>{required.length}</strong>required</span><span><strong>{owned}</strong>owned</span><span><strong>{required.length - owned}</strong>missing</span></div><p className="muted small">Ownership checks colors, not whether you have enough thread for the whole pattern.</p>{required.map(code => { const item = inventory.find(i => i.code === code); const thread = catalog.find(t => t.code === code); const symbols = project.data.definitions.filter(d => components(d).some(c => effectiveCode(c, project.substitutions) === code)); return <div className="material-row" key={code}><span className="thread-dot" style={{ background: thread?.displayColor }}/><div><strong>{symbols.map(d => <StitchSymbol key={d.id} definition={d}/>)} · DMC {code}</strong><small>{item?.bobbinCount ? `${item.bobbinCount} bobbins · ${item.location || 'No location'}` : 'Not in your collection'}</small></div><span className={item?.bobbinCount ? 'owned-badge' : 'missing-badge'}>{item?.bobbinCount ? 'Owned' : 'Missing'}</span></div>; })}</>;
}

function InventoryScreen({ catalog, inventory, busy, save }: { catalog: Thread[]; inventory: Inventory[]; busy: boolean; save: (entry: Inventory) => void }) {
  const [query, setQuery] = useState(''); const [ownedOnly, setOwnedOnly] = useState(false); const [edit, setEdit] = useState<Inventory | null>(null);
  const filtered = catalog.filter(t => `${t.code} ${t.name}`.toLowerCase().includes(query.toLowerCase()) && (!ownedOnly || inventory.some(i => i.code === t.code && i.bobbinCount > 0)));
  return <main className="page inventory-page"><div className="page-heading"><div><div className="eyebrow">A PALETTE OF POSSIBILITIES</div><h1>Thread collection</h1><p>Know what you have, and just where you tucked it away.</p></div></div><div className="inventory-banner"><Spool size={36} strokeWidth={1.3}/><div><h2>{inventory.filter(i => i.bobbinCount > 0).length} colors, so many possibilities.</h2><p>{count(inventory.reduce((n, i) => n + i.bobbinCount, 0))} bobbins in your collection</p></div></div><div className="library-controls"><div className="tabs"><button className={!ownedOnly ? 'selected' : ''} onClick={() => setOwnedOnly(false)}>All colors ({catalog.length})</button><button className={ownedOnly ? 'selected' : ''} onClick={() => setOwnedOnly(true)}>My collection</button></div><label className="search"><Search size={17}/><input value={query} onChange={e => setQuery(e.target.value)} placeholder="Search DMC code or name…" aria-label="Search thread catalog"/></label></div><p className="catalog-note">DMC thread catalog · Screen colors may differ from physical floss.</p><div className="thread-table"><div className="thread-table-head"><span>THREAD</span><span>FAMILY</span><span>BOBBINS</span><span>STORAGE LOCATION</span><span/></div>{filtered.map(t => { const item = inventory.find(i => i.code === t.code); return <div className="thread-table-row" key={t.code}><div><span className="floss-swatch" style={{ background: t.displayColor }}/><span><strong>DMC {t.code}</strong><small>{t.name}</small></span></div><span>{typeName(t.family)}</span><span className={item?.bobbinCount ? 'bobbin-count' : 'muted'}>{item?.bobbinCount || 0}</span><span>{item?.location || '—'}</span><button className="secondary" disabled={busy} onClick={() => setEdit(item ? { ...item } : { code: t.code, bobbinCount: 1, location: '' })}>{item ? 'Edit' : 'Add to collection'}</button></div>; })}{!filtered.length && <div className="empty">No matching catalog threads. Try another search.</div>}</div>
    {edit && <Modal title={`DMC ${edit.code} · your collection`} close={() => setEdit(null)}><label className="field-label">Whole bobbins<input type="number" min={0} max={10000} step={1} value={edit.bobbinCount} onChange={e => setEdit({ ...edit, bobbinCount: Number(e.target.value) })}/></label><label className="field-label">Where is it stored?<input placeholder="Box 1 / Row 3" maxLength={200} value={edit.location} onChange={e => setEdit({ ...edit, location: e.target.value })}/></label><button className="primary full" disabled={busy || !Number.isInteger(edit.bobbinCount) || edit.bobbinCount < 0} onClick={() => { save(edit); setEdit(null); }}>Save to collection</button><button className="text-button danger" disabled={busy} onClick={() => { save({ ...edit, bobbinCount: 0, location: '' }); setEdit(null); }}>Remove from collection</button></Modal>}
  </main>;
}
function Backups({ busy, run, notify }: { busy: boolean; run: (action: () => Promise<void>) => Promise<void>; notify: (text: string) => void }) {
  const [backups, setBackups] = useState<{ id: string; kind: string; bytes: number; createdAt: string }[]>([]);
  useEffect(() => { run(async () => setBackups(await api('/backups'))); }, []);
  const download = () => run(async () => {
    const response = await request('/api/exports/current', 'POST');
    const url = URL.createObjectURL(await response.blob()); const link = document.createElement('a');
    link.href = url; link.download = 'stitch-helper-backup-' + new Date().toISOString().slice(0, 10) + '.zip'; link.click();
    setTimeout(() => URL.revokeObjectURL(url), 60000); notify('Your current data has been downloaded.');
  });
  return <main className="page backups-page"><div className="page-heading"><div><div className="eyebrow">CARE FOR EVERY STITCH</div><h1>Backups & Export</h1><p>A portable copy of your patterns, progress, threads, and original PDFs.</p></div><button className="primary" disabled={busy} onClick={download}>{busy ? <LoaderCircle size={17} className="spin"/> : <Download size={17}/>}Download current data</button></div><div className="backup-info"><ShieldCheck size={38}/><div><h2>Your work deserves a safety net.</h2><p>Your export contains your stitching data, private source files, and legal acceptance history. Keep a downloaded copy somewhere safe.</p><p>Scheduled backups keep the latest daily copy for up to 7 days and the latest weekly copy for up to 30 days. Deleting a project removes existing backup archives that may contain it. You can download a fresh export of your remaining data at any time.</p></div></div><h3>Retained backups</h3><div className="backup-list">{['daily', 'weekly'].map(kind => { const b = backups.find(x => x.kind === kind); return <div className="backup-row" key={kind}><ShieldCheck size={22}/><div><strong>{kind === 'daily' ? 'Latest daily backup' : 'Latest weekly backup'}</strong><small>{b ? new Date(b.createdAt).toLocaleString() + ' · ' + (b.bytes / 1024 / 1024).toFixed(2) + ' MB' : 'Not available yet'}</small></div>{b && <a className="secondary" href={'/api/backups/' + b.id + '/download'} download><Download size={16}/>Download</a>}</div>; })}</div><p className="muted">Archives use a versioned, portable format. Self-service restore will be available in a future release.</p></main>;
}
