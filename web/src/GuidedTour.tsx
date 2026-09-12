import { useEffect, useId, useLayoutEffect, useRef, useState } from 'react';
import { createPortal } from 'react-dom';
import type { Project } from './types';
import './guided-tour.css';

type Stage = 'choose' | 'sampler' | 'review' | 'inspect' | 'complete' | 'saving' | 'progress' | 'feedback';
const steps: Record<Stage, { number: number; target: string; title: string; text: string }> = {
  choose: { number: 1, target: 'import', title: 'Start with a pattern', text: 'Click Import a pattern. We’ll help you start with the provided garden sampler—no PDF needed.' },
  sampler: { number: 1, target: 'sampler', title: 'Try the little garden', text: 'Click Explore with the little garden sampler. It creates your own practice project, ready to stitch. You can also choose your own PDF.' },
  review: { number: 1, target: 'confirm-import', title: 'Check your imported pattern', text: 'You chose your own PDF. Check its chart and import notes before clicking Looks good, start stitching. If it cannot be read, try the garden sampler instead.' },
  inspect: { number: 2, target: 'thread', title: 'Find your thread', text: 'Click the highlighted symbol in the pattern key to highlight its stitches. You can also choose another symbol in the key.' },
  complete: { number: 3, target: 'complete', title: 'Mark matching stitches complete', text: 'Click Mark matching complete to finish the selected thread’s matching stitches in the current scope. The count is shown above the button. You can undo this practice action.' },
  saving: { number: 3, target: 'save', title: 'Wait for your save', text: 'Your changes are saving. We’ll move on when the server confirms them. If saving fails, keep this tab open and use Retry saving.' },
  progress: { number: 3, target: 'progress', title: 'See your progress grow', text: 'Your stitches are saved. This display tracks the completed stitches in your project. Click Continue to learn where to find help.' },
  feedback: { number: 4, target: 'feedback', title: 'A little help when you need it', text: 'Click Send Feedback to see where you can report a problem or suggest an improvement. Opening the form finishes the tour; nothing is sent unless you submit a report.' },
};
interface Props {
  route: string;
  project: Project | null;
  importOpen: boolean;
  savePending: boolean;
  saveError: boolean;
  openImport: () => void;
  finish: () => void;
}

/** A nonmodal guide. Targets keep their real click/keyboard behavior; no action is synthesized. */
export function GuidedTour({ route, project, importOpen, savePending, saveError, openImport, finish }: Props) {
  const [stage, setStage] = useState<Stage>(() => importOpen ? 'sampler' : route === 'workspace' && project ? project.status === 'active' ? 'inspect' : 'review' : 'choose');
  const [target, setTarget] = useState<HTMLElement | null>(null), [host, setHost] = useState<HTMLElement>(document.body), [paused, setPaused] = useState(false);
  const [position, setPosition] = useState({ top: 90, left: 16 });
  const panel = useRef<HTMLElement>(null), returnFocus = useRef(document.activeElement as HTMLElement | null);
  const baseline = useRef<{ projectId: string; count: number } | null>(null);
  const descriptionId = useId();
  const step = steps[stage];
  const inWorkspace = route === 'workspace' && project !== null;

  useEffect(() => {
    if (stage === 'choose' && importOpen) setStage('sampler');
    if (['choose', 'sampler', 'review'].includes(stage) && !importOpen && inWorkspace && project) {
      if (project.status === 'active' && project.data.stitches.length) setStage('inspect');
      else setStage('review');
    }
    if (stage === 'sampler' && !importOpen && !inWorkspace) setStage('choose');
    if (importOpen && stage !== 'sampler') setStage('sampler');
    if (stage === 'saving' && baseline.current && project?.id === baseline.current.projectId && !savePending && !saveError && project.completed.length > baseline.current.count) setStage('progress');
  }, [stage, importOpen, inWorkspace, project, savePending, saveError]);

  useEffect(() => {
    let frame = 0;
    const find = () => {
      const dialogs = Array.from(document.querySelectorAll<HTMLDialogElement>('dialog[open]'));
      const dialog = dialogs.at(-1);
      const candidates = Array.from(document.querySelectorAll<HTMLElement>(`[data-tour="${step.target}"]`));
      const found = candidates.find(el => el.getClientRects().length > 0 && getComputedStyle(el).visibility !== 'hidden') ?? null;
      const unrelatedDialog = !!dialog && !dialog.contains(found);
      setPaused(unrelatedDialog);
      setTarget(unrelatedDialog ? null : found);
      setHost(dialog && !unrelatedDialog ? dialog : document.body);
    };
    const schedule = () => { cancelAnimationFrame(frame); frame = requestAnimationFrame(find); };
    find();
    const observer = new MutationObserver(schedule);
    observer.observe(document.body, { childList: true, subtree: true, attributes: true, attributeFilter: ['open', 'class', 'style', 'disabled'] });
    window.addEventListener('resize', schedule);
    return () => { cancelAnimationFrame(frame); observer.disconnect(); window.removeEventListener('resize', schedule); };
  }, [step.target]);

  useEffect(() => {
    if (!target) return;
    const previousDescription = target.getAttribute('aria-describedby');
    target.classList.add('guided-tour-target');
    target.setAttribute('aria-describedby', [previousDescription, descriptionId].filter(Boolean).join(' '));
    target.scrollIntoView({ block: 'center', inline: 'nearest', behavior: 'instant' });
    return () => {
      target.classList.remove('guided-tour-target');
      if (previousDescription === null) target.removeAttribute('aria-describedby'); else target.setAttribute('aria-describedby', previousDescription);
    };
  }, [target, descriptionId]);

  useLayoutEffect(() => {
    if (paused) return;
    const place = () => {
      const viewport = window.visualViewport;
      const width = viewport?.width ?? innerWidth, height = viewport?.height ?? innerHeight;
      const offsetX = viewport?.offsetLeft ?? 0, offsetY = viewport?.offsetTop ?? 0;
      const box = panel.current?.getBoundingClientRect();
      if (!box) return;
      const rect = target?.getBoundingClientRect();
      let top = offsetY + 90, left = offsetX + width - box.width - 16;
      if (rect) {
        left = rect.left + rect.width / 2 - box.width / 2;
        if (rect.bottom + box.height + 24 <= offsetY + height) top = rect.bottom + 12;
        else if (rect.top - box.height - 12 >= offsetY + 12) top = rect.top - box.height - 12;
        else {
          top = offsetY + height - box.height - 12;
          if (rect.right + box.width + 24 <= offsetX + width) left = rect.right + 12;
          else if (rect.left - box.width - 12 >= offsetX + 12) left = rect.left - box.width - 12;
        }
      }
      const next = { top: Math.max(offsetY + 12, Math.min(top, offsetY + height - box.height - 12)), left: Math.max(offsetX + 12, Math.min(left, offsetX + width - box.width - 12)) };
      setPosition(old => old.top === next.top && old.left === next.left ? old : next);
    };
    let frame = 0;
    const schedule = () => { cancelAnimationFrame(frame); frame = requestAnimationFrame(place); };
    const observer = new ResizeObserver(schedule);
    if (target) observer.observe(target);
    if (panel.current) observer.observe(panel.current);
    place();
    window.addEventListener('scroll', schedule, true); window.addEventListener('resize', schedule);
    window.visualViewport?.addEventListener('resize', schedule); window.visualViewport?.addEventListener('scroll', schedule);
    return () => { cancelAnimationFrame(frame); observer.disconnect(); window.removeEventListener('scroll', schedule, true); window.removeEventListener('resize', schedule); window.visualViewport?.removeEventListener('resize', schedule); window.visualViewport?.removeEventListener('scroll', schedule); };
  }, [target, stage, host, paused]);

  useEffect(() => {
    const clicked = (event: MouseEvent) => {
      const element = event.target instanceof Element ? event.target.closest<HTMLElement>('[data-tour]') : null;
      if (!element || element.matches(':disabled') || paused) return;
      const action = element.dataset.tour;
      if (stage === 'inspect' && action === 'thread') setStage('complete');
      if (stage === 'complete' && action === 'complete' && project) {
        baseline.current = { projectId: project.id, count: project.completed.length }; setStage('saving');
      }
      if (stage === 'feedback' && action === 'feedback') finish();
    };
    // Capture the pre-mutation count before the real React button handler updates progress.
    document.addEventListener('click', clicked, true);
    return () => document.removeEventListener('click', clicked, true);
  }, [stage, project, paused, finish]);

  function dismiss() {
    const focus = host !== document.body || returnFocus.current === document.body ? target : returnFocus.current;
    finish();
    if (focus?.isConnected) focus.focus({ preventScroll: true });
  }
  if (paused || !importOpen && route !== 'library' && route !== 'workspace') return null;
  const unavailable = !target || target.matches(':disabled');
  return createPortal(<aside ref={panel} className="guided-tour" style={position} aria-label="Guided tour" data-tour-stage={stage}>
    <div className="guided-tour-heading"><span>GUIDED TOUR · {step.number} OF 4</span><button aria-label="Dismiss tour" onClick={dismiss}>✕</button></div>
    <h2>{step.title}</h2><p id={descriptionId} aria-live="polite">{step.text}</p>
    {unavailable && <p className="guided-tour-wait">{savePending ? 'Please wait for the current action to finish.' : stage === 'complete' ? 'No matching stitches can be marked right now. Choose an unfinished thread, adjust the scope, or continue to feedback.' : 'This control is not available here yet. Open a usable pattern, leave focus mode, or try the sampler to continue.'}</p>}
    <div className="guided-tour-actions">
      {target && !target.matches(':disabled') && stage !== 'progress' && stage !== 'saving' && <button onClick={() => target.focus({ preventScroll: true })}>Focus highlighted control</button>}
      {stage === 'progress' && <button className="primary" onClick={() => setStage('feedback')}>Continue</button>}
      {stage === 'complete' && <button onClick={() => setStage('feedback')}>Skip marking stitches</button>}
      {unavailable && !savePending && stage !== 'complete' && stage !== 'saving' && <button onClick={openImport}>Choose a pattern</button>}
      <button onClick={dismiss}>End tour</button>
    </div>
  </aside>, host);
}
