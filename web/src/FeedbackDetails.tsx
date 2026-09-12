import { useEffect, useRef, type ReactNode } from 'react';
import { Archive, ArrowUpRight, Check, ChevronRight, FileText, Image, MessageSquare, X } from 'lucide-react';
import './feedback-details.css';

export interface FeedbackDetail {
  report: {
    id: string; userId: string; category: string; status: string; message: string;
    createdAt: string; completedAt?: string; completedByAdminUserId?: string;
    route: string; appVersion: string; diagnosticsIncluded: boolean;
    browserMetadata?: string; screenMetadata?: string; projectId?: string;
    patternRevisionId?: string; importRunId?: string; importMetadata?: string;
    errorCode?: string; correlationId?: string; patternAttachmentConsentAt?: string;
  };
  attachments: { id: string; kind: string; available: boolean }[];
}

function date(value: string) {
  return new Date(value).toLocaleString(undefined, { month: 'short', day: 'numeric', year: 'numeric', hour: 'numeric', minute: '2-digit' });
}

function Field({ label, children }: { label: string; children?: ReactNode }) {
  return children ? <div className="feedback-field"><dt>{label}</dt><dd>{children}</dd></div> : null;
}

export function FeedbackDetails({ detail, busy, error, close, changeStatus, restoreFocus }: {
  detail: FeedbackDetail; busy: boolean; error: string; close: () => void; changeStatus: (status: string) => void; restoreFocus: () => void;
}) {
  const ref = useRef<HTMLDialogElement>(null);
  const { report, attachments } = detail;
  useEffect(() => {
    const dialog = ref.current!;
    dialog.showModal();
    return () => { dialog.close(); restoreFocus(); };
  }, [restoreFocus]);

  let importDetails: { status?: string; parserVersion?: string; createdAt?: string } = {};
  try {
    const parsed: unknown = JSON.parse(report.importMetadata || '{}');
    if (parsed && typeof parsed === 'object') {
      for (const key of ['status', 'parserVersion', 'createdAt'] as const) {
        const value = (parsed as Record<string, unknown>)[key];
        if (typeof value === 'string') importDetails[key] = value;
      }
    }
  } catch { /* Older reports may not contain structured import details. */ }

  return <dialog ref={ref} aria-labelledby="feedback-details-title" className="modal feedback-details"
    onCancel={event => { event.preventDefault(); if (!busy) close(); }}>
    <header className="feedback-header">
      <div className="feedback-heading-icon"><MessageSquare size={23} aria-hidden="true" /></div>
      <div><p className="feedback-eyebrow">Beta inbox</p><h2 id="feedback-details-title">Feedback details</h2></div>
      <button className="feedback-close" aria-label="Close dialog" disabled={busy} onClick={close}><X size={21} aria-hidden="true" /></button>
    </header>

    <div className="feedback-body">
      <div className="feedback-report-heading">
        <div className="feedback-tags"><span className={`feedback-status feedback-status-${report.status.toLowerCase()}`} role="status"><span aria-hidden="true" />{report.status}</span><span className="feedback-category">{{ FeatureRequest: 'Feature request', ConfusingExperience: 'Confusing experience' }[report.category] || report.category}</span></div>
        <span className="feedback-reference">Report #{report.id.slice(0, 8)}</span>
      </div>
      {error && <p className="feedback-error" role="alert">{error}</p>}
      <div className="feedback-layout">
        <div className="feedback-main">
          <section aria-labelledby="feedback-message-title" className="feedback-message-card">
            <h3 id="feedback-message-title">The feedback</h3>
            <p className="feedback-message">{report.message}</p>
          </section>
          <section className="feedback-attachments" aria-labelledby="feedback-attachments-title">
            <h3 id="feedback-attachments-title">Attachments <span>{attachments.length}</span></h3>
            {attachments.length === 0 ? <p className="feedback-empty">No files were shared with this report.</p> : <>
              <p className="feedback-hint">Shared by the sender. Opening a file records an access entry.</p>
              <div className="feedback-attachment-list">{attachments.map((attachment, index) => {
                const screenshot = attachment.kind === 'Screenshot';
                const Icon = screenshot ? Image : FileText;
                return <div className="feedback-attachment" key={attachment.id}>
                  <span className="feedback-file-icon"><Icon size={20} aria-hidden="true" /></span>
                  <div><strong>{screenshot ? 'Screenshot' : 'Pattern PDF'}{attachments.filter(a => a.kind === attachment.kind).length > 1 ? ` ${index + 1}` : ''}</strong>
                    <span>{attachment.available ? screenshot ? 'Image shared with this report' : 'Shared with permission' : 'Source removed · no longer available'}</span></div>
                  {attachment.available && <a aria-label={`Open ${screenshot ? 'screenshot' : 'consented pattern'} ${index + 1}`} href={`/api/admin/beta/feedback/${report.id}/attachments/${attachment.id}`} target="_blank" rel="noopener noreferrer">Open <ArrowUpRight size={15} aria-hidden="true" /></a>}
                </div>;
              })}</div>
            </>}
          </section>
        </div>
        <aside className="feedback-context" aria-label="Report context">
          <h3>Report context</h3>
          <dl>
            <Field label="Received">{date(report.createdAt)}</Field>
            <Field label="Sent from">{{ library: 'Project library', workspace: 'Pattern workspace', inventory: 'Thread inventory', backups: 'Backups', 'whats-new': 'What’s new', error: 'Error screen', admin: 'Administration', unknown: 'Not recorded' }[report.route] || report.route}</Field>
            <Field label="App version">{report.appVersion}</Field>
            <Field label="Sender account"><code>{report.userId}</code></Field>
          </dl>
          {report.completedAt && <div className="feedback-completion"><Check size={17} aria-hidden="true" /><div><strong>Completed</strong><span>{date(report.completedAt)}</span></div></div>}
        </aside>
      </div>

      <details className="feedback-diagnostics">
        <summary><ChevronRight size={17} aria-hidden="true" /><span>Technical details</span><span className="feedback-diagnostics-summary">{report.diagnosticsIncluded ? 'Diagnostics included' : 'Diagnostics not shared'}</span></summary>
        <div className="feedback-diagnostics-content">
          <p className="feedback-hint">{report.diagnosticsIncluded ? 'The sender chose to include technical diagnostics with this report.' : 'The sender did not include optional diagnostics. Only report references are available.'}</p>
          <dl>
            <Field label="Report ID"><code>{report.id}</code></Field>
            <Field label="Project ID">{report.projectId}</Field>
            <Field label="Pattern revision">{report.patternRevisionId}</Field>
            <Field label="Import run">{report.importRunId}</Field>
            <Field label="Import status">{importDetails.status}</Field>
            <Field label="Parser version">{importDetails.parserVersion}</Field>
            <Field label="Import received">{importDetails.createdAt && date(importDetails.createdAt)}</Field>
            <Field label="Browser">{report.browserMetadata}</Field>
            <Field label="Screen">{report.screenMetadata}</Field>
            <Field label="Error code">{report.errorCode}</Field>
            <Field label="Error reference">{report.correlationId}</Field>
            <Field label="Pattern sharing consent">{report.patternAttachmentConsentAt && date(report.patternAttachmentConsentAt)}</Field>
            <Field label="Completed by account">{report.completedByAdminUserId}</Field>
          </dl>
        </div>
      </details>
    </div>

    <footer className="feedback-footer">
      <p aria-live="polite">{busy ? 'Saving changes…' : 'Status changes are internal. No email is sent.'}</p>
      <div className="feedback-status-actions">
        <button className="feedback-archive" aria-label="Mark archived" disabled={busy || report.status === 'Archived'} onClick={() => changeStatus('Archived')}><Archive size={16} aria-hidden="true" />Archive</button>
        <div className="feedback-resolution-actions">
          <button className="feedback-secondary" disabled={busy} onClick={() => changeStatus(report.status === 'Read' ? 'Unread' : 'Read')}>{report.status === 'Read' ? 'Mark unread' : 'Mark read'}</button>
          <button className="feedback-primary" disabled={busy || report.status === 'Completed'} onClick={() => changeStatus('Completed')}><Check size={17} aria-hidden="true" />Mark completed</button>
        </div>
      </div>
    </footer>
  </dialog>;
}
