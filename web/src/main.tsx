import React from 'react';
import { createRoot } from 'react-dom/client';
import { App } from './App';
import './styles.css';
import { FatalFeedback } from './BetaExperience';

class ErrorBoundary extends React.Component<{ children: React.ReactNode }, { error: boolean }> {
  details = { code: 'CLIENT_RENDER_FAILURE', referenceId: `client-${crypto.randomUUID()}` };
  state = { error: false };
  static getDerivedStateFromError() { return { error: true }; }
  render() { return this.state.error ? <main className="fatal"><h1>Let’s pick up that thread.</h1><p>The screen ran into a problem. Confirmed saves remain on the server. Refreshing loses unconfirmed work in this tab.</p><FatalFeedback details={this.details}/><button onClick={() => location.reload()}>Reload Stitch Helper</button></main> : this.props.children; }
}
createRoot(document.getElementById('root')!).render(<React.StrictMode><ErrorBoundary><App /></ErrorBoundary></React.StrictMode>);
