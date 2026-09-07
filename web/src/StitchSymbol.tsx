import type { Definition } from './types';

export function StitchSymbol({ definition }: { definition: Definition }) {
  const glyph = definition.symbolGlyph;
  return glyph ? <svg className="source-symbol" viewBox={`${glyph.minX} ${glyph.minY} ${glyph.width} ${glyph.height}`} role="img" aria-label={`Pattern symbol for DMC ${definition.threadCode}`}><path d={glyph.path} fill="currentColor"/></svg> : <>{definition.symbol}</>;
}
