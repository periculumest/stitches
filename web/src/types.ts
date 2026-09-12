export interface Thread { code: string; name: string; displayColor: string; brand: string; family: string }
export interface SymbolGlyph { path: string; minX: number; minY: number; width: number; height: number }
export interface ThreadComponent { id: string; threadCode: string; strandCount: number | null }
export interface Definition { id: string; symbol: string; threadCode: string; stitchType: string; symbolGlyph?: SymbolGlyph | null; components: ThreadComponent[]; usageKind: 'Single' | 'Blend' }
export interface Stitch { id: string; x: number; y: number; definitionId: string; endX?: number | null; endY?: number | null }
export interface Region { minX: number; minY: number; maxX: number; maxY: number }
export interface SourcePage { number: number; x: number; y: number; width: number; height: number }
export interface PatternData { width: number; height: number; definitions: Definition[]; stitches: Stitch[]; pages: SourcePage[]; warnings: { message: string; page?: number; x?: number; y?: number }[]; pageOverlapStitches?: Stitch[]; pageSourcesComplete?: boolean }
export interface Project { id: string; patternId: string; name: string; status: string; data: PatternData; completed: string[]; substitutions: Record<string, string>; milestones: number[]; workingArea?: Region | null; revision: number; dataRevision: number; updatedAt: string; sourceAvailable: boolean; canUndo: boolean; canRedo: boolean }
export type ProjectState = Omit<Project, 'data' | 'patternId' | 'sourceAvailable'>;
export interface ProjectCard { id: string; name: string; status: string; updatedAt: string; width: number; height: number; total: number; completed: number; colors: number; preview?: PatternData }
export interface Inventory { code: string; bobbinCount: number; location: string; revision?: number }
export type Command = { kind: string; revision?: number; pages?: { number: number; x: number; y: number }[]; stitchIds?: string[]; complete?: boolean; code?: string; replacement?: string | null; definition?: Definition; stitch?: Stitch | null; stitchId?: string; area?: Region | null; name?: string; pageNumber?: number; x?: number; y?: number };
export const stitchTypes = ['FullCross', 'HalfCross', 'QuarterCross', 'ThreeQuarterCross', 'Backstitch', 'FrenchKnot', 'Bead', 'Other'];
export const typeName = (name: string) => name.replace(/([a-z])([A-Z])/g, '$1 $2');
export const count = (n: number) => n.toLocaleString();
export const percentage = (done: number, total: number) => total ? done / total * 100 : 0;
let csrf: Promise<string> | null = null;
let sessionUserId: string | null = null;
export function setSessionIdentity(id: string) { sessionUserId = id; }
export interface ErrorDetails { code: string; referenceId: string; errorToken?: string; appVersion?: string }
export let recentError: ErrorDetails | undefined;
export class ApiError extends Error {
  constructor(message: string, public status: number, public details: ErrorDetails = { code: status === 0 ? 'NETWORK_FAILURE' : 'REQUEST_FAILED', referenceId: `client-${crypto.randomUUID()}` }) {
    super(`${message} Code: ${details.code}. Reference: ${details.referenceId}.`);
    recentError = details;
  }
}
export function resetSession() { csrf = null; }
export async function request(path: string, method = 'GET', body?: unknown): Promise<Response> {
  const headers: Record<string, string> = body && !(body instanceof FormData) ? { 'Content-Type': 'application/json' } : {};
  if (!['GET', 'HEAD'].includes(method)) {
    if (sessionUserId) headers['X-Account-Id'] = sessionUserId;
    csrf ??= fetch('/api/antiforgery', { credentials: 'same-origin' }).then(async response => {
      if (!response.ok) { csrf = null; if (response.status === 401) window.dispatchEvent(new Event('session-expired')); throw new ApiError('Sign in again to save your work.', response.status); }
      return (await response.json()).token;
    }).catch(error => { csrf = null; throw error instanceof ApiError ? error : new ApiError('Disconnected. Keep this tab open and retry when connected.', 0); });
    headers['X-CSRF-TOKEN'] = await csrf;
  }
  let response: Response;
  try { response = await fetch(path, { method, credentials: 'same-origin', headers, body: body instanceof FormData ? body : body ? JSON.stringify(body) : undefined }); }
  catch { throw new ApiError('Disconnected. Keep this tab open, check your connection, and retry. This client reference does not identify a server log.', 0); }
  if (!response.ok) {
    if (response.status === 428) window.dispatchEvent(new Event('legal-acceptance-required'));
    if (response.status === 401) { resetSession(); if (path !== '/api/me') window.dispatchEvent(new Event('session-expired')); }
    if (response.status === 400) resetSession();
    const data = await response.json().catch(() => null);
    throw new ApiError(data?.error || (response.status === 401 ? 'Your session expired. Sign in again.' : `Request failed (${response.status}). Please try again.`), response.status, data?.code && data?.referenceId ? data : undefined);
  }
  return response;
}
export async function api<T>(path: string, method = 'GET', body?: unknown): Promise<T> {
  const response = await request(`/api${path}`, method, body);
  return response.status === 204 ? undefined as T : response.json();
}
export const components = (d: Definition): ThreadComponent[] => d.components ?? [{ id: `${d.id}-c0`, threadCode: d.threadCode, strandCount: null }];
export const effectiveCode = (c: ThreadComponent, substitutions: Record<string, string>) => substitutions[c.id] || c.threadCode;
export const usageLabel = (d: Definition, substitutions: Record<string, string>) => components(d).map(c => `${effectiveCode(c, substitutions)}${c.strandCount == null ? '' : ` (${c.strandCount} strands)`}`).join(' + ');
export function usageBackground(d: Definition, catalog: Map<string, Thread>, substitutions: Record<string, string>) {
  const colors = components(d).map(c => catalog.get(effectiveCode(c, substitutions))?.displayColor || '#bbb');
  return colors.length === 1 ? colors[0] : `linear-gradient(135deg, ${colors.map((color, i) => `${color} ${i / colors.length * 100}%, ${color} ${(i + 1) / colors.length * 100}%`).join(', ')})`;
}
