import { useEffect, useId, useRef, useState } from 'react';
import { LogOut, UserRound } from 'lucide-react';

export function AccountMenu({ user, busy, openAccount, logout }: {
  user: { displayName: string; email: string }; busy: boolean; openAccount: () => void; logout: () => void;
}) {
  const [open, setOpen] = useState(false);
  const container = useRef<HTMLDivElement>(null), trigger = useRef<HTMLButtonElement>(null);
  const panelId = useId();
  const name = user.displayName.trim() || user.email.trim() || 'Account';
  const initial = Array.from(name)[0].toLocaleUpperCase();
  useEffect(() => {
    if (!open) return;
    const outside = (event: PointerEvent) => { if (!container.current?.contains(event.target as Node)) setOpen(false); };
    const escape = (event: KeyboardEvent) => {
      if (event.key === 'Escape') { event.preventDefault(); setOpen(false); trigger.current?.focus(); }
    };
    document.addEventListener('pointerdown', outside);
    document.addEventListener('keydown', escape);
    return () => { document.removeEventListener('pointerdown', outside); document.removeEventListener('keydown', escape); };
  }, [open]);
  function choose(action: () => void) { setOpen(false); trigger.current?.focus(); action(); }
  return <div className="account-menu" ref={container} onBlur={event => {
    if (!event.currentTarget.contains(event.relatedTarget as Node | null)) setOpen(false);
  }}>
    <button ref={trigger} className="account-trigger" aria-label={`Account menu for ${name}`} title={`Account menu for ${name}`}
      aria-expanded={open} aria-controls={panelId} onClick={() => setOpen(!open)}>
      <span className="account-avatar" aria-hidden="true">{initial}</span><span className="account-trigger-name">{name}</span>
    </button>
    {open && <div id={panelId} className="account-menu-panel" role="group" aria-label="Account actions">
      <div className="account-menu-identity"><strong>{name}</strong>{user.email && <small>{user.email}</small>}</div>
      <button className="account-menu-action" disabled={busy} onClick={() => choose(openAccount)}><UserRound size={18} aria-hidden="true"/>Account &amp; data</button>
      <button className="account-menu-action" disabled={busy} onClick={() => choose(logout)}><LogOut size={18} aria-hidden="true"/>Sign out</button>
    </div>}
  </div>;
}
