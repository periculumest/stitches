import { useEffect, useState, type ReactNode } from 'react';
import { Check, CircleAlert, Info, Megaphone, X } from 'lucide-react';
import './notifications.css';

export function Notification({ title, children, tone = 'info', className = '', actions, close, closeLabel = 'Dismiss message', busy = false, details, announcement = false }: {
  title: string; children: ReactNode; tone?: 'info' | 'success' | 'warning' | 'error'; className?: string;
  actions?: ReactNode; close?: () => void; closeLabel?: string; busy?: boolean; details?: string; announcement?: boolean;
}) {
  const Icon = announcement ? Megaphone : tone === 'success' ? Check : tone === 'info' ? Info : CircleAlert;
  return <section className={`app-notice app-notice-${tone} ${className}`} aria-label={title} role={tone === 'error' || tone === 'warning' ? 'alert' : 'status'}>
    <span className="app-notice-icon"><Icon size={20} strokeWidth={1.7} aria-hidden="true" /></span>
    <div className="app-notice-content">
      {announcement && <span className="app-notice-eyebrow">From Stitch Helper</span>}
      <h3>{title}</h3><div className="app-notice-message">{children}</div>
      {details && <details className="app-notice-details"><summary>Technical details</summary><p>{details}</p></details>}
      {actions && <div className="app-notice-actions">{actions}</div>}
    </div>
    {close && <button className="app-notice-close" aria-label={closeLabel} disabled={busy} onClick={close}><X size={18} aria-hidden="true" /></button>}
  </section>;
}

// Keep the exact diagnostic text available without making references the main message.
export function readableError(error: string) {
  return error.replace(/ Code: \S+\. Reference: \S+\./, '').replace(' This client reference does not identify a server log.', '').trim();
}

export function SuccessNotification({ message, close }: { message: string; close: () => void }) {
  const [hovered, setHovered] = useState(false), [focused, setFocused] = useState(false);
  useEffect(() => {
    if (hovered || focused) return;
    const timer = setTimeout(close, 6500);
    return () => clearTimeout(timer);
  }, [message, hovered, focused, close]);
  return <div className="success-notification" onMouseEnter={() => setHovered(true)} onMouseLeave={() => setHovered(false)}
    onFocus={() => setFocused(true)} onBlur={event => { if (!event.currentTarget.contains(event.relatedTarget)) setFocused(false); }}>
    <Notification title="All set" tone="success" close={close} closeLabel="Dismiss notification">{message}</Notification>
  </div>;
}
