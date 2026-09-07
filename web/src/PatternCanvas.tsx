import { useEffect, useMemo, useRef, useState } from 'react';
import type { PatternData, Region, Stitch, Thread } from './types';

export interface View { x: number; y: number; cell: number }
interface Props {
  data: PatternData; catalog: Thread[]; completed?: Set<string>; substitutions?: Record<string, string>;
  selected?: string[]; statusFilter?: string; typeFilter?: string; isolate?: boolean; pages?: boolean;
  area?: Region | null; tool?: string; busy?: boolean; fitToken?: number; zoom?: number;
  focus?: { x: number; y: number; nonce: number }; preview?: boolean;
  onStitch?: (stitch: Stitch | undefined, x: number, y: number) => void;
  onPaint?: (ids: string[], complete: boolean) => void; onArea?: (region: Region) => void;
  onVisible?: (region: Region) => void; onZoom?: (zoom: number) => void;
}
export function PatternCanvas({ data, catalog, completed = new Set(), substitutions = {}, selected = [], statusFilter = 'all', typeFilter = 'all', isolate = false, pages = false, area, tool = 'inspect', busy, fitToken = 0, zoom, focus, preview, onStitch, onPaint, onArea, onVisible, onZoom }: Props) {
  const canvas = useRef<HTMLCanvasElement>(null);
  const container = useRef<HTMLDivElement>(null);
  const [size, setSize] = useState({ width: 0, height: 0 });
  const previousLayout = useRef<{ width: number; height: number; patternWidth: number; patternHeight: number; fitToken: number; preview?: boolean } | null>(null);
  const [view, setView] = useState<View>({ x: 30, y: 30, cell: 8 });
  const [draft, setDraft] = useState<Region | null>(null);
  const [paint, setPaint] = useState<Set<string>>(new Set());
  const gesture = useRef<{ x: number; y: number; screenX: number; screenY: number; view: View; ids: Set<string>; complete: boolean; lastX: number; lastY: number } | null>(null);
  const [hover, setHover] = useState<{ x: number; y: number } | null>(null);
  const definitions = useMemo(() => new Map(data.definitions.map(d => [d.id, d])), [data.definitions]);
  const glyphPaths = useMemo(() => new Map(data.definitions.filter(d => d.symbolGlyph).map(d => [d.id, new Path2D(d.symbolGlyph!.path)])), [data.definitions]);
  const threads = useMemo(() => new Map(catalog.map(t => [t.code, t])), [catalog]);
  // Spatial bins bound drawing and hit testing to the current viewport, including line endpoints.
  const bins = useMemo(() => {
    const index = new Map<string, Stitch[]>();
    for (const s of data.stitches) {
      for (let y = Math.floor(Math.min(s.y, s.endY ?? s.y) / 32); y <= Math.floor(Math.max(s.y, s.endY ?? s.y) / 32); y++)
        for (let x = Math.floor(Math.min(s.x, s.endX ?? s.x) / 32); x <= Math.floor(Math.max(s.x, s.endX ?? s.x) / 32); x++) {
          const key = `${x},${y}`; const list = index.get(key) || []; list.push(s); index.set(key, list);
        }
    }
    return index;
  }, [data.stitches]);
  const visible = useMemo<Region>(() => ({ minX: Math.max(0, Math.floor(-view.x / view.cell)), minY: Math.max(0, Math.floor(-view.y / view.cell)), maxX: Math.min(data.width - 1, Math.floor((size.width - view.x) / view.cell)), maxY: Math.min(data.height - 1, Math.floor((size.height - view.y) / view.cell)) }), [view, size, data.width, data.height]);
  const visibleStitches = useMemo(() => {
    const found = new Map<string, Stitch>();
    for (let y = Math.floor(visible.minY / 32); y <= Math.floor(visible.maxY / 32); y++)
      for (let x = Math.floor(visible.minX / 32); x <= Math.floor(visible.maxX / 32); x++)
        for (const s of bins.get(`${x},${y}`) || []) found.set(s.id, s);
    return [...found.values()];
  }, [bins, visible]);
  const matches = (s: Stitch) => (!selected.length || selected.includes(s.definitionId)) && (typeFilter === 'all' || definitions.get(s.definitionId)?.stitchType === typeFilter) && (statusFilter === 'all' || completed.has(s.id) === (statusFilter === 'complete'));
  const inArea = (s: Stitch) => !area || s.x >= area.minX && s.x <= area.maxX && s.y >= area.minY && s.y <= area.maxY;
  useEffect(() => { if (!container.current) return; const observer = new ResizeObserver(([entry]) => setSize({ width: entry.contentRect.width, height: entry.contentRect.height })); observer.observe(container.current); return () => observer.disconnect(); }, []);
  useEffect(() => {
    if (!size.width || !size.height || !data.width || !data.height) return;
    const previous = previousLayout.current;
    previousLayout.current = { ...size, patternWidth: data.width, patternHeight: data.height, fitToken, preview };
    if (!preview && previous && previous.patternWidth === data.width && previous.patternHeight === data.height && previous.fitToken === fitToken && previous.preview === preview) {
      // Keep the same stitch at the center when focus mode or window resizing changes the available space.
      setView(v => ({ ...v, x: v.x + (size.width - previous.width) / 2, y: v.y + (size.height - previous.height) / 2 }));
      return;
    }
    const cell = Math.max(.3, Math.min(preview ? 20 : 28, (size.width - (preview ? 30 : 90)) / data.width, (size.height - (preview ? 30 : 90)) / data.height));
    setView({ x: (size.width - data.width * cell) / 2, y: (size.height - data.height * cell) / 2, cell });
  }, [size.width, size.height, data.width, data.height, fitToken, preview]);
  useEffect(() => { if (zoom === undefined) return; setView(v => ({ x: size.width / 2 - (size.width / 2 - v.x) * zoom / v.cell, y: size.height / 2 - (size.height / 2 - v.y) * zoom / v.cell, cell: zoom })); }, [zoom]); // parent requests zoom; gestures update view directly
  useEffect(() => { onVisible?.(visible); }, [visible.minX, visible.minY, visible.maxX, visible.maxY]);
  useEffect(() => { onZoom?.(view.cell); }, [view.cell]);
  useEffect(() => {
    const el = canvas.current; if (!el || preview) return;
    const wheel = (e: WheelEvent) => {
      e.preventDefault();
      if (gesture.current) return;
      if (e.ctrlKey || e.metaKey) {
        const box = el.getBoundingClientRect(), x = e.clientX - box.left, y = e.clientY - box.top;
        setView(v => { const cell = Math.max(.3, Math.min(48, v.cell * Math.exp(-e.deltaY * .004))); return { cell, x: x - (x - v.x) * cell / v.cell, y: y - (y - v.y) * cell / v.cell }; });
      } else setView(v => ({ ...v, x: v.x - e.deltaX, y: v.y - e.deltaY }));
    };
    el.addEventListener('wheel', wheel, { passive: false });
    return () => el.removeEventListener('wheel', wheel);
  }, [preview]);
  useEffect(() => { if (focus) setView(v => ({ cell: Math.max(v.cell, 14), x: size.width / 2 - focus.x * Math.max(v.cell, 14), y: size.height / 2 - focus.y * Math.max(v.cell, 14) })); }, [focus]);
  useEffect(() => {
    const el = canvas.current; if (!el || !size.width) return;
    const ratio = window.devicePixelRatio || 1;
    el.width = Math.round(size.width * ratio); el.height = Math.round(size.height * ratio);
    const ctx = el.getContext('2d')!; ctx.scale(ratio, ratio);
    ctx.fillStyle = preview ? '#f0ece2' : '#eceae3'; ctx.fillRect(0, 0, size.width, size.height);
    const { x: ox, y: oy, cell } = view;
    ctx.fillStyle = '#fffcf5'; ctx.fillRect(ox, oy, data.width * cell, data.height * cell);
    for (const s of visibleStitches) {
      if (Math.max(s.x, s.endX ?? s.x) < visible.minX - 1 || Math.min(s.x, s.endX ?? s.x) > visible.maxX + 1 || Math.max(s.y, s.endY ?? s.y) < visible.minY - 1 || Math.min(s.y, s.endY ?? s.y) > visible.maxY + 1) continue;
      const d = definitions.get(s.definitionId); if (!d) continue;
      const match = matches(s); if (!match && isolate) continue;
      const color = threads.get(substitutions[d.threadCode] || d.threadCode)?.displayColor || '#b7b0a6';
      const done = paint.has(s.id) && gesture.current ? gesture.current.complete : completed.has(s.id);
      ctx.globalAlpha = !match ? .13 : !inArea(s) ? .3 : done && !preview ? .25 : 1;
      const x = ox + s.x * cell; const y = oy + s.y * cell;
      ctx.fillStyle = color; ctx.strokeStyle = color; ctx.lineWidth = Math.max(1.5, cell * .14);
      if (d.stitchType === 'Backstitch') { ctx.beginPath(); ctx.moveTo(x, y); ctx.lineTo(ox + (s.endX ?? s.x + 1) * cell, oy + (s.endY ?? s.y + 1) * cell); ctx.stroke(); }
      else if (d.stitchType === 'FrenchKnot' || d.stitchType === 'Bead') { ctx.beginPath(); ctx.ellipse(x + cell / 2, y + cell / 2, cell * .24, cell * (d.stitchType === 'Bead' ? .36 : .24), -.5, 0, Math.PI * 2); ctx.fill(); }
      else if (d.stitchType === 'HalfCross') { ctx.beginPath(); ctx.moveTo(x + cell * .15, y + cell * .85); ctx.lineTo(x + cell * .85, y + cell * .15); ctx.stroke(); }
      else if (d.stitchType === 'QuarterCross' || d.stitchType === 'ThreeQuarterCross') { ctx.beginPath(); ctx.moveTo(x, y); ctx.lineTo(x + cell / 2, y + cell / 2); ctx.lineTo(x + cell, y); if (d.stitchType === 'ThreeQuarterCross') ctx.lineTo(x + cell, y + cell); ctx.closePath(); ctx.fill(); }
      else ctx.fillRect(x + (cell > 5 ? .5 : 0), y + (cell > 5 ? .5 : 0), cell - (cell > 5 ? 1 : 0), cell - (cell > 5 ? 1 : 0));
      if (cell >= 12 && !preview) {
        const rgb = color.slice(1).match(/.{2}/g)!.map(c => parseInt(c, 16));
        ctx.fillStyle = rgb[0] * .299 + rgb[1] * .587 + rgb[2] * .114 > 145 ? '#252b25' : '#fffef9';
        const glyph = d.symbolGlyph, path = glyphPaths.get(d.id);
        if (glyph && path) {
          const scale = cell * .82 / Math.max(glyph.width, glyph.height);
          ctx.save(); ctx.translate(x + (cell - glyph.width * scale) / 2, y + (cell - glyph.height * scale) / 2); ctx.scale(scale, scale); ctx.translate(-glyph.minX, -glyph.minY); ctx.fill(path); ctx.restore();
        } else {
          ctx.font = `${Math.round(cell * .63)}px "Segoe UI Symbol", sans-serif`; ctx.textAlign = 'center'; ctx.textBaseline = 'middle'; ctx.fillText(d.symbol, x + cell / 2, y + cell / 2, cell * .88);
        }
      }
      if (done && !preview && cell >= 6) { ctx.globalAlpha = match ? .75 : .12; ctx.strokeStyle = '#345b45'; ctx.lineWidth = 1.5; ctx.beginPath(); ctx.moveTo(x + cell * .22, y + cell * .53); ctx.lineTo(x + cell * .43, y + cell * .74); ctx.lineTo(x + cell * .8, y + cell * .25); ctx.stroke(); }
    }
    ctx.globalAlpha = 1;
    if (!preview && view.cell >= 4) {
      for (let x = visible.minX; x <= visible.maxX + 1; x++) { ctx.strokeStyle = x % 10 === 0 ? '#a9afa09a' : '#a9afa030'; ctx.lineWidth = x % 10 === 0 ? 1 : .5; ctx.beginPath(); ctx.moveTo(ox + x * cell, Math.max(0, oy)); ctx.lineTo(ox + x * cell, Math.min(size.height, oy + data.height * cell)); ctx.stroke(); }
      for (let y = visible.minY; y <= visible.maxY + 1; y++) { ctx.strokeStyle = y % 10 === 0 ? '#a9afa09a' : '#a9afa030'; ctx.lineWidth = y % 10 === 0 ? 1 : .5; ctx.beginPath(); ctx.moveTo(Math.max(0, ox), oy + y * cell); ctx.lineTo(Math.min(size.width, ox + data.width * cell), oy + y * cell); ctx.stroke(); }
      ctx.font = '10px "Segoe UI", sans-serif'; ctx.fillStyle = '#637063'; ctx.textAlign = 'center';
      for (let x = Math.ceil(visible.minX / 10) * 10; x <= visible.maxX; x += 10) ctx.fillText(String(x + 1), ox + (x + .5) * cell, Math.max(13, oy - 10));
      for (let y = Math.ceil(visible.minY / 10) * 10; y <= visible.maxY; y += 10) ctx.fillText(String(y + 1), Math.max(13, ox - 15), oy + (y + .5) * cell);
    }
    if (pages && !preview) for (const page of data.pages) { ctx.strokeStyle = '#a765b8'; ctx.lineWidth = 2; ctx.setLineDash([6, 4]); ctx.strokeRect(ox + page.x * cell, oy + page.y * cell, page.width * cell, page.height * cell); ctx.setLineDash([]); ctx.fillStyle = '#744083'; ctx.font = '12px sans-serif'; ctx.fillText(`Page ${page.number}`, ox + page.x * cell + 32, oy + page.y * cell + 15); }
    const region = draft || area;
    if (region && !preview) { ctx.fillStyle = '#5d815517'; ctx.strokeStyle = '#3f634c'; ctx.lineWidth = 2; const x = ox + region.minX * cell, y = oy + region.minY * cell, w = (region.maxX - region.minX + 1) * cell, h = (region.maxY - region.minY + 1) * cell; ctx.fillRect(x, y, w, h); ctx.setLineDash([6, 3]); ctx.strokeRect(x, y, w, h); ctx.setLineDash([]); }
    if (hover && !preview) { ctx.strokeStyle = '#314d3c'; ctx.lineWidth = 2; ctx.strokeRect(ox + hover.x * cell, oy + hover.y * cell, cell, cell); }
  }, [data, size, view, visibleStitches, definitions, threads, completed, substitutions, selected, statusFilter, typeFilter, isolate, pages, area, draft, paint, hover, preview]);
  const position = (e: React.PointerEvent) => { const bounds = canvas.current!.getBoundingClientRect(); return { sx: e.clientX - bounds.left, sy: e.clientY - bounds.top, x: Math.floor((e.clientX - bounds.left - view.x) / view.cell), y: Math.floor((e.clientY - bounds.top - view.y) / view.cell) }; };
  const hit = (x: number, y: number) => (bins.get(`${Math.floor(x / 32)},${Math.floor(y / 32)}`) || []).filter(s => Math.floor(s.x) === x && Math.floor(s.y) === y);
  const bounded = (x: number, y: number) => x >= 0 && y >= 0 && x < data.width && y < data.height;
  function down(e: React.PointerEvent<HTMLCanvasElement>) {
    if (preview || busy || e.button !== 0) return;
    const p = position(e); if (tool !== 'pan' && !bounded(p.x, p.y)) return;
    e.currentTarget.setPointerCapture(e.pointerId);
    gesture.current = { x: p.x, y: p.y, screenX: p.sx, screenY: p.sy, view, ids: new Set(), complete: tool !== 'erase', lastX: p.x, lastY: p.y };
    if (tool === 'paint' || tool === 'erase') { for (const s of hit(p.x, p.y).filter(s => matches(s) && inArea(s))) gesture.current.ids.add(s.id); setPaint(new Set(gesture.current.ids)); }
    if (tool === 'inspect' || tool === 'edit') onStitch?.(hit(p.x, p.y)[0], p.x, p.y);
  }
  function move(e: React.PointerEvent<HTMLCanvasElement>) {
    const p = position(e); setHover(bounded(p.x, p.y) ? { x: p.x, y: p.y } : null);
    const g = gesture.current; if (!g) return;
    if (tool === 'pan') { setView({ ...g.view, x: g.view.x + p.sx - g.screenX, y: g.view.y + p.sy - g.screenY }); return; }
    if (tool === 'area') setDraft({ minX: Math.max(0, Math.min(g.x, p.x)), minY: Math.max(0, Math.min(g.y, p.y)), maxX: Math.min(data.width - 1, Math.max(g.x, p.x)), maxY: Math.min(data.height - 1, Math.max(g.y, p.y)) });
    if (tool === 'paint' || tool === 'erase') {
      const steps = Math.max(Math.abs(p.x - g.lastX), Math.abs(p.y - g.lastY), 1);
      for (let i = 0; i <= steps; i++) { const x = Math.round(g.lastX + (p.x - g.lastX) * i / steps), y = Math.round(g.lastY + (p.y - g.lastY) * i / steps); for (const s of hit(x, y)) if (matches(s) && inArea(s)) g.ids.add(s.id); }
      g.lastX = p.x; g.lastY = p.y; setPaint(new Set(g.ids));
    }
  }
  function up(e: React.PointerEvent<HTMLCanvasElement>) {
    const g = gesture.current; if (!g) return;
    if (g.ids.size) onPaint?.([...g.ids], g.complete);
    if (tool === 'area') onArea?.(draft || { minX: g.x, minY: g.y, maxX: g.x, maxY: g.y });
    gesture.current = null; setDraft(null); setPaint(new Set());
    if (e.currentTarget.hasPointerCapture(e.pointerId)) e.currentTarget.releasePointerCapture(e.pointerId);
  }
  return <div ref={container} className={`pattern-canvas ${preview ? 'preview' : ''}`}><canvas ref={canvas} role="img" aria-label={preview ? 'Pattern preview' : `Interactive pattern, ${data.width} columns by ${data.height} rows. Use the toolbar to inspect, paint, or select an area.`} style={{ cursor: preview ? 'inherit' : busy ? 'wait' : tool === 'pan' ? 'grab' : 'crosshair' }} onPointerDown={down} onPointerMove={move} onPointerUp={up} onPointerCancel={() => { gesture.current = null; setDraft(null); setPaint(new Set()); }} onPointerLeave={() => setHover(null)} />{!preview && <span className="canvas-coordinate">{hover ? `Column ${hover.x + 1} · Row ${hover.y + 1}` : 'Scroll to pan · Ctrl + scroll to zoom'}</span>}</div>;
}
