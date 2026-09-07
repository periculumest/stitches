export interface Thread { code: string; name: string; displayColor: string; brand: string; family: string }
export interface SymbolGlyph { path: string; minX: number; minY: number; width: number; height: number }
export interface Definition { id: string; symbol: string; threadCode: string; stitchType: string; symbolGlyph?: SymbolGlyph | null }
export interface Stitch { id: string; x: number; y: number; definitionId: string; endX?: number | null; endY?: number | null }
export interface Region { minX: number; minY: number; maxX: number; maxY: number }
export interface SourcePage { number: number; x: number; y: number; width: number; height: number }
export interface PatternData { width: number; height: number; definitions: Definition[]; stitches: Stitch[]; pages: SourcePage[]; warnings: { message: string; page?: number; x?: number; y?: number }[] }
export interface Project { id: string; patternId: string; name: string; status: string; data: PatternData; completed: string[]; substitutions: Record<string, string>; milestones: number[]; workingArea?: Region | null; revision: number; updatedAt: string; sourceAvailable: boolean; canUndo: boolean; canRedo: boolean }
export interface ProjectCard { id: string; name: string; status: string; updatedAt: string; width: number; height: number; total: number; completed: number; colors: number; preview?: PatternData }
export interface Inventory { code: string; bobbinCount: number; location: string }
export type Command = { kind: string; stitchIds?: string[]; complete?: boolean; code?: string; replacement?: string | null; definition?: Definition; stitch?: Stitch | null; stitchId?: string; area?: Region | null; name?: string; pageNumber?: number; x?: number; y?: number };
export const stitchTypes = ['FullCross', 'HalfCross', 'QuarterCross', 'ThreeQuarterCross', 'Backstitch', 'FrenchKnot', 'Bead', 'Other'];
export const typeName = (name: string) => name.replace(/([a-z])([A-Z])/g, '$1 $2');
export const count = (n: number) => n.toLocaleString();
export const percentage = (done: number, total: number) => total ? done / total * 100 : 0;
export async function api<T>(path: string, method = 'GET', body?: unknown): Promise<T> {
  const response = await fetch(`/api${path}`, { method, headers: body && !(body instanceof FormData) ? { 'Content-Type': 'application/json' } : {}, body: body instanceof FormData ? body : body ? JSON.stringify(body) : undefined });
  if (!response.ok) { const data = await response.json().catch(() => null); throw new Error(data?.error || `Request failed (${response.status}). Please try again.`); }
  return response.status === 204 ? undefined as T : response.json();
}
