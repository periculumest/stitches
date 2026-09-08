import { useEffect, useMemo, useRef, useState } from 'react';
import { ArrowDown, ArrowLeft, ArrowRight, ArrowUp, Check, LayoutGrid, LoaderCircle, RotateCcw, X } from 'lucide-react';
import type { Command, Project, SourcePage, Thread } from './types';
import { count } from './types';
import { layoutChecks, overlaps, pageSources, thumbnails } from './pageLayout';
import './page-builder.css';

export function PageBuilder({ project, catalog, save, close }: { project: Project; catalog: Thread[]; save: (c: Command) => Promise<boolean>; close: () => void }) {
  // Freeze the source and revision for this draft; a background refresh must never silently overwrite it.
  const [base] = useState(project);
  const [pages, setPages] = useState(base.data.pages);
  const [selected, setSelected] = useState(pages[0].number);
  const [first, setFirst] = useState(pages[0].number);
  const [columns, setColumns] = useState(Math.min(pages.length, Math.max(1, Math.round(Math.sqrt(pages.length)))));
  const [reference, setReference] = useState(pages.find(p => p.number !== selected)?.number || selected);
  const [overlap, setOverlap] = useState(0);
  const [scale, setScale] = useState(.8);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');
  const [discard, setDiscard] = useState(false);
  const [history, setHistory] = useState<SourcePage[][]>([]);
  const dialog = useRef<HTMLDialogElement>(null), viewport = useRef<HTMLDivElement>(null);
  const drag = useRef<{ number: number; x: number; y: number; clientX: number; clientY: number; pages: SourcePage[]; moved: boolean } | null>(null);
  const sources = useMemo(() => pageSources(base.data), [base]);
  const images = useMemo(() => thumbnails(base.data, catalog, sources), [base, catalog, sources]);
  const checks = useMemo(() => layoutChecks(base.data, pages, sources), [base, pages, sources]);
  const dirty = pages.some((p, i) => p.x !== base.data.pages[i].x || p.y !== base.data.pages[i].y);
  const selectedPage = pages.find(p => p.number === selected)!;
  const stale = project.revision !== base.revision;
  const legacy = !base.data.pageSourcesComplete && base.data.pages.some((a, i) => base.data.pages.slice(i + 1).some(b => overlaps(a, b)));
  const valid = pages.every(p => Number.isInteger(p.x) && Number.isInteger(p.y) && p.x >= 0 && p.y >= 0 && p.x + p.width <= 5000 && p.y + p.height <= 5000);
  useEffect(() => { const el = dialog.current!; el.showModal(); return () => el.close(); }, []);
  useEffect(() => { if (reference === selected && pages.length > 1) setReference(pages.find(p => p.number !== selected)!.number); }, [selected]);
  useEffect(() => { const el = viewport.current; if (el) setScale(Math.max(.1, Math.min(24, (el.clientWidth - 80) / Math.max(1, checks.width), (el.clientHeight - 80) / Math.max(1, checks.height)))); }, []);
  useEffect(() => { const prevent = (e: BeforeUnloadEvent) => { if (dirty || saving) { e.preventDefault(); e.returnValue = ''; } }; window.addEventListener('beforeunload', prevent); return () => window.removeEventListener('beforeunload', prevent); }, [dirty, saving]);
  function update(next: SourcePage[]) { setHistory(h => [...h.slice(-49), pages]); setPages(next); setError(''); }
  function move(x: number, y: number) { update(pages.map(p => p.number === selected ? { ...p, x: Math.max(0, Math.min(5000 - p.width, Math.round(x))), y: Math.max(0, Math.min(5000 - p.height, Math.round(y))) } : p)); }
  function requestClose() { if (saving) return; if (dirty) setDiscard(true); else close(); }
  function fit() { const el = viewport.current!; setScale(Math.max(.1, Math.min(24, (el.clientWidth - 80) / checks.width, (el.clientHeight - 80) / checks.height))); el.scrollTo(0, 0); }
  function arrange() {
    const ordered = [...pages].sort((a, b) => (a.number === first ? -1 : b.number === first ? 1 : a.number - b.number));
    const placed = new Map<number, SourcePage>(); let y = 0;
    for (let i = 0; i < ordered.length; i += columns) {
      const row = ordered.slice(i, i + columns); let x = 0;
      for (const p of row) { placed.set(p.number, { ...p, x, y }); x += p.width; }
      y += Math.max(...row.map(p => p.height));
    }
    update(pages.map(p => placed.get(p.number)!));
  }
  function beside(direction: string) {
    const other = pages.find(p => p.number === reference)!;
    const joinOverlap = Math.min(overlap, Math.min(selectedPage.width, selectedPage.height, other.width, other.height) - 1);
    const x = direction === 'right' ? other.x + other.width - joinOverlap : direction === 'left' ? other.x - selectedPage.width + joinOverlap : other.x;
    const y = direction === 'below' ? other.y + other.height - joinOverlap : direction === 'above' ? other.y - selectedPage.height + joinOverlap : other.y;
    // Make room to the left/above by shifting the whole draft, keeping offsets nonnegative.
    update(pages.map(p => ({ ...p, x: (p.number === selected ? x : p.x) - Math.min(0, x), y: (p.number === selected ? y : p.y) - Math.min(0, y) })));
  }
  async function apply() {
    setSaving(true); setError('');
    try { if (await save({ kind: 'layout', revision: base.revision, pages: pages.map(({ number, x, y }) => ({ number, x, y })) })) close(); else setError('The arrangement was not saved. Your draft is still here. Check the project message after closing, or retry if the connection failed.'); }
    catch (e) { setError(e instanceof Error ? e.message : 'Could not save. Your draft is still here; try again.'); }
    finally { setSaving(false); }
  }
  return <dialog ref={dialog} className="page-builder" aria-labelledby="builder-title" onCancel={e => { e.preventDefault(); requestClose(); }}>
    <header className="builder-header"><div><div className="eyebrow">PUT YOUR PATTERN TOGETHER</div><h2 id="builder-title">Arrange chart pages</h2><p>Drag pages to match your design. Edges snap together; arrow keys move a selected page by one stitch.</p></div><button className="icon-button" aria-label="Close page builder" disabled={saving} onClick={requestClose}><X/></button></header>
    <div className="builder-content">
      <section className="builder-main" aria-label="Page arrangement">
        <div className="builder-board-toolbar"><span><strong>{pages.length} chart pages</strong> · {checks.width} × {checks.height} stitches</span><div><button className="secondary" onClick={fit}>Fit board</button><label>Zoom<input aria-label="Board zoom" type="range" min="0.1" max="24" step="0.1" value={scale} onChange={e => setScale(Number(e.target.value))}/></label></div></div>
        <div className="builder-viewport" ref={viewport}><div className="builder-board" style={{ width: Math.max(450, checks.width * scale + 160), height: Math.max(380, checks.height * scale + 160) }}>
          <span className="builder-origin">Top-left of design · 0, 0</span>
          {pages.map(p => <button key={p.number} className={`builder-tile${selected === p.number ? ' selected' : ''}${checks.joins.some(j => j.a === p.number || j.b === p.number) ? ' overlapping' : ''}`} aria-label={`Page ${p.number}, column ${p.x + 1}, row ${p.y + 1}`} aria-pressed={selected === p.number} disabled={saving} style={{ left: 40 + p.x * scale, top: 40 + p.y * scale, width: p.width * scale, height: p.height * scale, zIndex: selected === p.number ? 2 : 1 }}
            onClick={() => setSelected(p.number)}
            onKeyDown={e => { const delta = e.shiftKey ? 10 : 1; if (['ArrowLeft', 'ArrowRight', 'ArrowUp', 'ArrowDown'].includes(e.key)) { e.preventDefault(); setSelected(p.number); update(pages.map(s => s.number === p.number ? { ...s, x: Math.max(0, Math.min(5000 - s.width, s.x + (e.key === 'ArrowLeft' ? -delta : e.key === 'ArrowRight' ? delta : 0))), y: Math.max(0, Math.min(5000 - s.height, s.y + (e.key === 'ArrowUp' ? -delta : e.key === 'ArrowDown' ? delta : 0))) } : s)); } }}
            onPointerDown={e => { if (e.button !== 0) return; setSelected(p.number); e.currentTarget.setPointerCapture(e.pointerId); drag.current = { number: p.number, x: p.x, y: p.y, clientX: e.clientX, clientY: e.clientY, pages, moved: false }; }}
            onPointerMove={e => { const d = drag.current; if (!d || d.number !== p.number) return; if (!d.moved && Math.hypot(e.clientX - d.clientX, e.clientY - d.clientY) < 4) return; d.moved = true;
              let x = d.x + (e.clientX - d.clientX) / scale, y = d.y + (e.clientY - d.clientY) / scale;
              const others = pages.filter(t => t.number !== p.number);
              const snap = (v: number, edges: number[]) => edges.reduce((best, edge) => Math.abs(edge - v) < Math.abs(best - v) ? edge : best, v + 9 / scale);
              const sx = snap(x, [0, ...others.flatMap(t => [t.x, t.x + t.width, t.x - p.width, t.x + t.width - p.width])]);
              const sy = snap(y, [0, ...others.flatMap(t => [t.y, t.y + t.height, t.y - p.height, t.y + t.height - p.height])]);
              if (!e.altKey && Math.abs(sx - x) <= 8 / scale) x = sx; if (!e.altKey && Math.abs(sy - y) <= 8 / scale) y = sy;
              setPages(current => current.map(t => t.number === p.number ? { ...t, x: Math.max(0, Math.min(5000 - p.width, Math.round(x))), y: Math.max(0, Math.min(5000 - p.height, Math.round(y))) } : t));
            }}
            onPointerUp={e => { const d = drag.current; if (d?.moved) setHistory(h => [...h.slice(-49), d.pages]); drag.current = null; if (e.currentTarget.hasPointerCapture(e.pointerId)) e.currentTarget.releasePointerCapture(e.pointerId); }}
            onPointerCancel={() => { if (drag.current) setPages(drag.current.pages); drag.current = null; }}>
            <img src={images.get(p.number)} alt="" draggable={false}/><span>Page {p.number}</span>
          </button>)}
        </div></div>
        <div className="builder-board-caption">Previews show extracted stitches. Compare page edges with your original PDF. Hold Alt to drag without snapping; Shift + arrow moves ten stitches.</div>
      </section>
      <aside className="builder-controls"><fieldset disabled={saving}>
        <h3>Start with a layout</h3><p>Keep the current arrangement, or lay out pages in PDF order starting with your chosen page.</p>
        <label className="field-label">Top-left page<select value={first} onChange={e => setFirst(Number(e.target.value))}>{pages.map(p => <option key={p.number} value={p.number}>Page {p.number}</option>)}</select></label>
        <label className="field-label">Pages per row<select value={columns} onChange={e => setColumns(Number(e.target.value))}>{pages.map((_, i) => <option key={i} value={i + 1}>{i + 1}</option>)}</select></label>
        <button className="secondary full" onClick={arrange}><LayoutGrid size={15}/>Arrange in rows</button>
        <div className="section-divider"/><h3>Place a page</h3>
        <label className="field-label">Selected page<select value={selected} onChange={e => setSelected(Number(e.target.value))}>{pages.map(p => <option key={p.number} value={p.number}>Page {p.number}</option>)}</select></label>
        <button className="text-button" onClick={() => viewport.current?.scrollTo({ left: Math.max(0, 40 + (selectedPage.x + selectedPage.width / 2) * scale - viewport.current.clientWidth / 2), top: Math.max(0, 40 + (selectedPage.y + selectedPage.height / 2) * scale - viewport.current.clientHeight / 2) })}>Find this page on the board</button>
        <div className="builder-coordinates"><label className="field-label">Column offset<input type="number" min={0} max={5000 - selectedPage.width} value={selectedPage.x} onChange={e => { if (e.target.value !== '') move(Number(e.target.value), selectedPage.y); }}/></label><label className="field-label">Row offset<input type="number" min={0} max={5000 - selectedPage.height} value={selectedPage.y} onChange={e => { if (e.target.value !== '') move(selectedPage.x, Number(e.target.value)); }}/></label></div>
        <p>Offsets count stitches from zero at the top-left.</p>
        <div className="builder-nudges">{[['left', ArrowLeft, -1, 0], ['up', ArrowUp, 0, -1], ['down', ArrowDown, 0, 1], ['right', ArrowRight, 1, 0]].map(([name, Icon, dx, dy]) => { const I = Icon as typeof ArrowLeft; return <button key={name as string} className="secondary" aria-label={`Move page ${name} one stitch`} onClick={() => move(selectedPage.x + Number(dx), selectedPage.y + Number(dy))}><I size={16}/></button>; })}</div>
        {pages.length > 1 && <><label className="field-label">Neighbor page<select value={reference} onChange={e => setReference(Number(e.target.value))}>{pages.map(p => <option key={p.number} value={p.number}>Page {p.number}</option>)}</select></label>
          <label className="field-label">Overlap at this join (stitches)<input type="number" min={0} max={Math.min(selectedPage.width, selectedPage.height) - 1} value={overlap} onChange={e => setOverlap(Math.max(0, Math.min(Math.min(selectedPage.width, selectedPage.height) - 1, Math.round(Number(e.target.value)))))}/></label>
          <div className="builder-neighbors">{['left', 'right', 'above', 'below'].map(direction => <button className="secondary" key={direction} disabled={reference === selected} onClick={() => beside(direction)}>Place {direction}</button>)}</div></>}
        <div className="section-divider"/><h3>Check the joins</h3>
        <div className="builder-checks" role="status">
          {checks.conflicts > 0 ? <p className="builder-problem">{count(checks.conflicts)} conflicting stitch pairs. Separate these pages or adjust their overlap before saving.</p> : <p><Check size={14}/> No conflicting stitches detected.</p>}
          {checks.repeated > 0 && <p>{count(checks.repeated)} matching stitch pairs overlap. Repeated stitches will be merged when saved.</p>}
          {checks.gaps > 0 && <p>{count(checks.gaps)} grid cells are outside the page coverage. Check for missing pages; blank margins may be intentional.</p>}
          {!checks.gaps && <p>Page footprints cover the assembled rectangle.</p>}
          {checks.joins.slice(0, 12).map(j => <button key={`${j.a}-${j.b}`} className="text-button" onClick={() => { setSelected(j.a); setReference(j.b); }}>Pages {j.a} and {j.b} overlap</button>)}
          {checks.joins.length > 12 && <p>And {checks.joins.length - 12} other page joins.</p>}
        </div>
        {base.sourceAvailable && <a className="secondary full" href={`/api/projects/${base.id}/source#page=${selected}`} target="_blank" rel="noreferrer">Open page {selected} in original PDF</a>}
      </fieldset></aside>
    </div>
    <footer className="builder-footer">
      {(error || stale || legacy || !valid) && <p className="builder-problem" role="alert">{stale ? 'This project changed in another window. Close this draft and reopen the builder to use the latest saved version.' : legacy ? 'This older import lacks repeated page-edge stitches. Re-import its PDF before rearranging overlapping pages.' : !valid ? 'This arrangement exceeds 5,000 × 5,000 stitches. Use fewer pages per row or another layout.' : error}</p>}
      {discard ? <div className="builder-discard"><strong>Discard your unsaved arrangement?</strong><button className="secondary" onClick={() => setDiscard(false)}>Keep arranging</button><button className="secondary" onClick={close}>Discard changes</button></div> : <><div className="builder-history"><button className="secondary" disabled={saving || !history.length} onClick={() => { setPages(history[history.length - 1]); setHistory(h => h.slice(0, -1)); }}>Undo move</button><button className="secondary" disabled={saving || !dirty} onClick={() => update(base.data.pages)}><RotateCcw size={14}/>Reset layout</button></div><span className="builder-draft-note">{dirty ? 'Unsaved arrangement' : 'Current saved arrangement'} · Saving returns to import review.</span><button className="secondary" disabled={saving} onClick={requestClose}>Cancel</button><button className="primary" disabled={saving || !dirty || checks.conflicts > 0 || stale || legacy || !valid} onClick={apply}>{saving ? <LoaderCircle size={16} className="spin"/> : <Check size={16}/>}Save arrangement</button></>}
    </footer>
  </dialog>;
}
