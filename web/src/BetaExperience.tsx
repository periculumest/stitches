import { createContext, useContext, useEffect, useRef, useState, type ReactNode } from 'react';
import { api, ApiError, recentError, type ErrorDetails, type Project } from './types';
import { GuidedTour } from './GuidedTour';
import './beta.css';

export interface BetaState { appVersion: string; supportContact: string; messageMinLength: number; messageMaxLength: number; screenshotCount: number; screenshotMaxBytes: number; screenshotMaxPixels: number; disclosureVersion: number; onboardingVersion: number; completedOnboardingVersion: number; hasAccess: boolean; isAdmin: boolean }
const BetaContext = createContext<BetaState | null>(null);
interface Context { projectId: string; patternRevisionId: string; importRunId?: string; sourceAssetId?: string }
interface Announcement { id: string; title: string; message: string; linkText?: string; linkUrl?: string; dismissible: boolean }
interface Release { id: string; version: string; releaseDate: string; title: string; bodyMarkdown: string }
export function BetaBoundary({ children, logout }: { children: ReactNode; logout: () => Promise<void> }) {
  const [state, setState] = useState<BetaState | null>(null), [error, setError] = useState('');
  const load = () => api<BetaState>('/beta/state').then(setState).catch(e => setError(e.message));
  useEffect(() => { void load(); }, []);
  if (!state) return <main className="beta-page"><p role={error ? 'alert' : 'status'}>{error || 'Opening the beta…'}</p>{error && <button onClick={load}>Retry connection</button>}</main>;
  if (!state.hasAccess) return <main className="beta-page"><h1>Beta access required</h1><p>Your account is signed in, but has not been admitted to this beta.</p><p>{state.supportContact}</p><button onClick={logout}>Sign out</button><p><a href="/legal">Privacy &amp; Terms</a></p></main>;
  if (location.pathname === '/admin/beta' && !state.isAdmin) return <main className="beta-page"><h1>Administrator access required</h1><p>This account cannot manage beta feedback or releases.</p><a href="/">Return to your projects</a></main>;
  return <BetaContext.Provider value={state}>{children}</BetaContext.Provider>;
}
export function BetaDialog({ title, children, close }: { title: string; children: ReactNode; close: () => void }) {
  const ref = useRef<HTMLDialogElement>(null);
  useEffect(() => { const previous = document.activeElement as HTMLElement | null; const dialog = ref.current!; dialog.showModal(); return () => { dialog.close(); if (previous?.isConnected) previous.focus(); }; }, []);
  return <dialog aria-label={title} className="modal beta-dialog" ref={ref} onCancel={e => { e.preventDefault(); close(); }}><div className="modal-heading"><h2>{title}</h2><button aria-label="Close dialog" onClick={close}>✕</button></div>{children}</dialog>;
}
export function FeedbackForm({ state, route, projectId, errorDetails, close }: { state: BetaState; route: string; projectId?: string; errorDetails?: ErrorDetails; close: () => void }) {
  const [context, setContext] = useState<Context | null>(null), [contextError, setContextError] = useState('');
  const [general, setGeneral] = useState(false);
  const [category, setCategory] = useState('Bug'), [message, setMessage] = useState(''), [diagnostics, setDiagnostics] = useState(false);
  const [pattern, setPattern] = useState(false), [screenshots, setScreenshots] = useState(false), [files, setFiles] = useState<File[]>([]);
  const [error, setError] = useState(''), [busy, setBusy] = useState(false), [receipt, setReceipt] = useState<{ id: string; createdAt: string } | null>(null);
  const submissionKey = useRef(crypto.randomUUID()), frozen = useRef<FormData | null>(null), [locked, setLocked] = useState(false);
  const dirty = !!message || pattern || files.length > 0;
  useEffect(() => { if (projectId) api<Context>(`/beta/context/${projectId}`).then(setContext).catch(e => setContextError(e.message)); }, [projectId]);
  useEffect(() => { const prevent = (e: BeforeUnloadEvent) => { if (dirty && !receipt) { e.preventDefault(); e.returnValue = ''; } }; window.addEventListener('beforeunload', prevent); return () => window.removeEventListener('beforeunload', prevent); }, [dirty, receipt]);
  const dismiss = () => { if (!busy && (receipt || !dirty || window.confirm('Discard this feedback draft? An unconfirmed submission may already have been received; retry to get its receipt.'))) close(); };
  async function submit() {
    if (busy) return; setBusy(true); setError('');
    try {
      if (!frozen.current) {
        const form = new FormData();
        form.append('input', JSON.stringify({ submissionKey: submissionKey.current, category, message, route, diagnosticsIncluded: diagnostics,
          diagnosticsDisclosureVersion: state.disclosureVersion, projectId: general ? null : context?.projectId, sourceAssetId: pattern ? context?.sourceAssetId : null,
          attachPattern: pattern, attachScreenshots: screenshots, screenWidth: diagnostics ? screen.width : null, screenHeight: diagnostics ? screen.height : null,
          errorToken: diagnostics ? errorDetails?.errorToken : null, clientOccurrenceId: diagnostics && errorDetails?.referenceId.startsWith('client-') ? errorDetails.referenceId : null }));
        if (screenshots) files.forEach(f => form.append('screenshot', f)); frozen.current = form; setLocked(true);
      }
      const received = await api<{ id: string; createdAt: string }>('/beta/feedback', 'POST', frozen.current);
      setReceipt(received); setMessage(''); setFiles([]); setPattern(false); frozen.current = null;
    } catch (e) {
      setError(`${e instanceof Error ? e.message : 'Feedback could not be sent.'} Your draft is still in this tab.`);
      if (e instanceof ApiError && [400, 404, 409, 413, 428].includes(e.status)) { frozen.current = null; setLocked(false); }
    } finally { setBusy(false); }
  }
  return <BetaDialog title={receipt ? 'Feedback received' : projectId ? 'Report a problem with this pattern' : 'Send Feedback'} close={dismiss}>
    {receipt ? <><p role="status">Thank you. Your report was received at {new Date(receipt.createdAt).toLocaleString()}.</p><p>Receipt: <strong>{receipt.id}</strong></p><button className="primary" onClick={close}>Done</button></> : <form onSubmit={e => { e.preventDefault(); void submit(); }}>
      <fieldset disabled={busy || locked}>
        <label>Category<select value={category} onChange={e => setCategory(e.target.value)}><option value="Bug">Bug</option><option value="FeatureRequest">Feature request</option><option value="ConfusingExperience">Confusing experience</option><option value="Other">Other</option></select></label>
        <label>What happened or what would help?<textarea required minLength={state.messageMinLength} maxLength={state.messageMaxLength} rows={5} value={message} onChange={e => setMessage(e.target.value)}/></label>
        <p className="muted">{message.length} / {state.messageMaxLength} characters. Reports always include your account, submission time, app version, page name, and authorized pattern/import associations.</p>
        <label className="beta-check"><input type="checkbox" checked={diagnostics} onChange={e => setDiagnostics(e.target.checked)}/>Include technical diagnostics</label>
        <p className="muted">Optional diagnostics include browser information, screen dimensions, bounded import status/parser version, and the recent error code/reference when available. Page addresses never include query strings or fragments. No chart, PDF, or screen content is collected automatically.</p>
        <label className="beta-check"><input type="checkbox" checked={screenshots} onChange={e => { setScreenshots(e.target.checked); if (!e.target.checked) setFiles([]); }}/>Attach screenshots I choose</label>
        {screenshots && <><label>Choose PNG or JPEG screenshots<input type="file" accept="image/png,image/jpeg" multiple={state.screenshotCount > 1} onChange={e => { setError(''); const selected = Array.from(e.target.files || []); if (selected.length > state.screenshotCount || selected.some(f => f.size > state.screenshotMaxBytes)) setError('The selected screenshots exceed the count or byte limit. Choose smaller files.'); else setFiles(selected); e.target.value = ''; }}/></label><p className="muted">Up to {state.screenshotCount} file(s), {Math.floor(state.screenshotMaxBytes / 1024 / 1024)} MB and {state.screenshotMaxPixels.toLocaleString()} pixels each. Image metadata is removed.</p><ul>{files.map((f, i) => <li key={i}>{f.name} ({Math.ceil(f.size / 1024)} KB) <button type="button" onClick={() => setFiles(files.filter((_, index) => index !== i))}>Remove {f.name}</button></li>)}</ul></>}
        {projectId && <><label className="beta-check"><input type="checkbox" disabled={!context?.sourceAssetId || general} checked={pattern} onChange={e => setPattern(e.target.checked)}/>Attach my pattern to this report so the administrator can inspect it.</label>{!context?.sourceAssetId && <p>{contextError || (context ? 'No retained source is available for this pattern. Rejected or removed sources cannot be attached.' : 'Checking the retained source…')}</p>}<label className="beta-check"><input type="checkbox" checked={general} onChange={e => { setGeneral(e.target.checked); setPattern(false); }}/>Send as general feedback without the pattern association</label></>}
      </fieldset>
      <p className="muted">Drafts and selected files stay in this tab only. Refreshing or closing the tab loses unsent content.</p>
      {error && <div role="alert"><p>{error}</p><p>Retry with the selected attachments, or remove them after a rejected upload to submit without them.</p><p>{state.supportContact}</p></div>}
      {locked && !busy && <p>The request is unconfirmed. Retry this unchanged report to retrieve its receipt without creating a duplicate.</p>}
      <div className="beta-actions"><button className="primary" disabled={busy || !locked && (message.trim().length < state.messageMinLength || screenshots && files.length === 0 || !!projectId && !general && !context)}>{busy ? 'Sending…' : locked ? 'Retry submission' : 'Send report'}</button><button type="button" disabled={busy} onClick={dismiss}>Cancel</button></div>
    </form>}
  </BetaDialog>;
}
export function BetaTools({ route, project, importOpen, savePending, saveError, openImport }: { route: string; project: Project | null; importOpen: boolean; savePending: boolean; saveError: boolean; openImport: () => void }) {
  const state = useContext(BetaContext)!;
  const [form, setForm] = useState<{ projectId?: string; errorDetails?: ErrorDetails } | null>(null), [announcement, setAnnouncement] = useState<Announcement | null>(null);
  const [tourRun, setTourRun] = useState(0), [tour, setTour] = useState(state.completedOnboardingVersion < state.onboardingVersion), [error, setError] = useState('');
  useEffect(() => { const report = () => setForm({ errorDetails: recentError }); window.addEventListener('open-feedback', report); return () => window.removeEventListener('open-feedback', report); }, []);
  useEffect(() => { const load = () => api<Announcement | null>('/beta/announcement').then(setAnnouncement).catch(e => setError(e.message)); void load(); const timer = setInterval(load, 60000); return () => clearInterval(timer); }, []);
  const finish = async () => { setTour(false); try { await api('/beta/onboarding', 'POST', { version: state.onboardingVersion }); } catch (e) { setError(`${(e as Error).message} Tour dismissal was not saved. Use Dismiss tour again to retry.`); } };
  return <>
    {announcement && <aside className="beta-announcement" aria-label="Beta announcement"><strong>{announcement.title}</strong><span>{announcement.message}</span>{announcement.linkUrl && <a href={announcement.linkUrl} target={announcement.linkUrl.startsWith('https://') ? '_blank' : undefined} rel="noopener noreferrer">{announcement.linkText}</a>}{announcement.dismissible && <button aria-label="Dismiss announcement" onClick={() => { api(`/beta/announcements/${announcement.id}/dismiss`, 'POST').then(() => setAnnouncement(null)).catch(e => setError(e.message)); }}>Dismiss</button>}</aside>}
    <div className="beta-tools"><span>Beta · {state.appVersion}</span><button data-tour="feedback" onClick={() => setForm({ errorDetails: recentError })}>Send Feedback</button>{route === 'workspace' && project && <button onClick={() => setForm({ projectId: project.id, errorDetails: recentError })}>Report a problem with this pattern</button>}<a href="/whats-new">What's New</a>{state.isAdmin && <a href="/admin/beta">Beta administration</a>}<button onClick={() => { setTour(true); setTourRun(n => n + 1); }}>Guided tour</button></div>
    {tour && <GuidedTour key={tourRun} route={route} project={project} importOpen={importOpen} savePending={savePending} saveError={saveError} openImport={openImport} finish={finish}/>}
    {error && <div className="beta-service-error" role="alert">{error}<button onClick={() => { void finish(); setError(''); }}>Dismiss tour again</button><button onClick={() => setError('')}>Dismiss message</button></div>}
    {form && <FeedbackForm state={state} route={route} {...form} close={() => setForm(null)}/>}
  </>;
}
// Markdown is sanitized by construction: React escapes text; only these structural elements are created.
export function ReleaseMarkdown({ text }: { text: string }) {
  return <div className="release-markdown">{text.split(/\r?\n\r?\n/).map((block, i) => {
    if (/^#{1,3} /.test(block)) return <h3 key={i}>{block.replace(/^#{1,3} /, '')}</h3>;
    if (block.split('\n').every(line => /^[-*] /.test(line))) return <ul key={i}>{block.split('\n').map((line, j) => <li key={j}>{line.slice(2)}</li>)}</ul>;
    return <p key={i}>{block}</p>;
  })}</div>;
}
export function WhatsNew() {
  const state = useContext(BetaContext)!; const [releases, setReleases] = useState<Release[] | null>(null), [error, setError] = useState('');
  const load = () => api<Release[]>('/beta/releases').then(setReleases).catch(e => setError(e.message));
  useEffect(() => { void load(); }, []);
  return <main className="beta-page"><a href="/">Back to projects</a><h1>What's New</h1><p>Beta · {state.appVersion}</p>{error && <p role="alert">{error}<button onClick={load}>Retry</button></p>}{releases === null ? <p>Loading published releases…</p> : releases.length === 0 ? <p>No release notes have been published yet. Check back after the next update.</p> : releases.map(r => <article key={r.id}><h2>{r.title}</h2><p>{r.version} · {r.releaseDate}</p><ReleaseMarkdown text={r.bodyMarkdown}/></article>)}</main>;
}
export function FatalFeedback({ details }: { details: ErrorDetails }) {
  const [state, setState] = useState<BetaState | null>(null), [open, setOpen] = useState(false), [support, setSupport] = useState('Contact the person who invited you to the beta.'), [version, setVersion] = useState('');
  useEffect(() => { api<{ supportContact: string; appVersion: string }>('/beta/public').then(s => { setSupport(s.supportContact); setVersion(s.appVersion); }).catch(() => {}); api<BetaState>('/beta/state').then(setState).catch(() => {}); }, []);
  return <><p>Code: {details.code}. Reference: {details.referenceId}.</p><p>This client occurrence does not identify a server log. Beta {version}</p><button onClick={() => navigator.clipboard.writeText(`Code: ${details.code}; Reference: ${details.referenceId}; Version: ${version}`).catch(() => window.prompt('Copy these safe details', `${details.code} ${details.referenceId}`))}>Copy safe error details</button>{state?.hasAccess && <button onClick={() => setOpen(true)}>Send Feedback</button>}<p>If reporting is unavailable: {support}</p>{open && state && <FeedbackForm state={state} route="error" errorDetails={details} close={() => setOpen(false)}/>}</>;
}
