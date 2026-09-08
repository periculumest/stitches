import type { PatternData, SourcePage, Stitch, Thread } from './types';
import { components } from './types';

export const overlaps = (a: SourcePage, b: SourcePage) => a.x < b.x + b.width && b.x < a.x + a.width && a.y < b.y + b.height && b.y < a.y + a.height;
export const pageNumber = (s: Stitch) => Number(/^p(\d+)-/.exec(s.id)?.[1]);
export function pageSources(data: PatternData) {
  const sources = new Map(data.pages.map(p => [p.number, [] as Stitch[]]));
  for (const s of [...data.stitches, ...(data.pageOverlapStitches || [])]) sources.get(pageNumber(s))?.push(s);
  return sources;
}
export function layoutChecks(data: PatternData, pages: SourcePage[], sources: Map<number, Stitch[]>) {
  const width = Math.max(...pages.map(p => p.x + p.width)), height = Math.max(...pages.map(p => p.y + p.height));
  let covered = 0, repeated = 0, conflicts = 0;
  const joins: { a: number; b: number }[] = [];
  for (let y = 0; y < Math.min(height, 5000); y++) {
    let end = 0;
    for (const p of pages.filter(p => p.y <= y && p.y + p.height > y).sort((a, b) => a.x - b.x)) {
      covered += Math.max(0, p.x + p.width - Math.max(end, p.x)); end = Math.max(end, p.x + p.width);
    }
  }
  const originals = new Map(data.pages.map(p => [p.number, p]));
  // Only examine stitches in overlapping page pairs, keeping dragging cheap for large charts.
  for (let i = 0; i < pages.length; i++) for (let j = i + 1; j < pages.length; j++) {
    const a = pages[i], b = pages[j]; if (!overlaps(a, b)) continue;
    joins.push({ a: a.number, b: b.number });
    const oa = originals.get(a.number)!, ob = originals.get(b.number)!;
    const positions = new Map<string, string>();
    const key = (s: Stitch, dx: number, dy: number) => `${s.x + dx},${s.y + dy},${s.endX == null ? '' : s.endX + dx},${s.endY == null ? '' : s.endY + dy}`;
    for (const s of sources.get(a.number) || []) {
      const x = s.x + a.x - oa.x, y = s.y + a.y - oa.y;
      if (x >= b.x && x < b.x + b.width && y >= b.y && y < b.y + b.height) positions.set(key(s, a.x - oa.x, a.y - oa.y), s.definitionId);
    }
    for (const s of sources.get(b.number) || []) {
      const other = positions.get(key(s, b.x - ob.x, b.y - ob.y));
      if (other !== undefined) { if (other === s.definitionId) repeated++; else conflicts++; }
    }
  }
  return { width, height, gaps: width * height - covered, repeated, conflicts, joins };
}
export function thumbnails(data: PatternData, catalog: Thread[], sources: Map<number, Stitch[]>) {
  const threads = new Map(catalog.map(t => [t.code, t.displayColor]));
  const colors = new Map(data.definitions.map(d => [d.id, threads.get(components(d)[0].threadCode) || '#9aa58c']));
  return new Map(data.pages.map(p => {
    const canvas = document.createElement('canvas'); const scale = Math.min(3, 400 / Math.max(p.width, p.height));
    canvas.width = Math.max(1, Math.ceil(p.width * scale)); canvas.height = Math.max(1, Math.ceil(p.height * scale));
    const ctx = canvas.getContext('2d')!; ctx.fillStyle = '#f8f6ee'; ctx.fillRect(0, 0, canvas.width, canvas.height);
    for (const s of sources.get(p.number) || []) {
      ctx.fillStyle = colors.get(s.definitionId) || '#888'; ctx.fillRect((s.x - p.x) * scale, (s.y - p.y) * scale, Math.max(1, scale), Math.max(1, scale));
    }
    return [p.number, canvas.toDataURL()] as const;
  }));
}
