export function DataRetentionPage() {
  return <main className="page" style={{ maxWidth: 840, margin: '0 auto', lineHeight: 1.7 }}>
    <a href="/">← Back to Stitch Helper</a>
    <h1>Data retention & deletion</h1>
    <p>Updated September 9, 2026. This page explains how deletion works in Stitch Helper.</p>
    <h2>Your projects and PDFs</h2>
    <p>Your projects, imported PDFs, progress, and edits stay with your account until you delete them. Unreadable imports follow the same policy. There is no automatic deletion of active projects for inactivity.</p>
    <p>Deleting a project permanently removes its progress and edits from the app. When you delete the last project using a pattern, we also remove that pattern and its original PDF from your account. A PDF used by another project stays available to that project.</p>
    <h2>Backups and downloaded copies</h2>
    <p>Deleting a project removes your existing daily and weekly backup archives from the app, because they may contain that project. Future backups contain the data that remains in your account.</p>
    <p>When scheduled backups are enabled, we keep the latest daily archive for up to 7 days and the latest weekly archive for up to 30 days. Older archives expire even if a new backup has not been created.</p>
    <p>Copies you already downloaded, and downloads already in progress, cannot be recalled from your devices.</p>
    <h2>Delete your account</h2>
    <p>Sign in and select <strong>Account & data</strong>, then confirm <strong>Delete my account</strong>. This removes your Stitch Helper profile and Google sign-in association, projects, PDFs, progress, thread inventory, preferences, legal acceptance records, and app backup archives. Published legal document versions remain available because they are shared public documents. Deletion is permanent. Signing in again creates a new account.</p>
    <p>This deletes your Stitch Helper account, not your Google account. You can also remove Stitch Helper from your Google account’s third-party connections. Disconnecting Google by itself does not delete your saved Stitch Helper data.</p>
    <h2>File cleanup</h2>
    <p>Deleted data is removed from your account when the deletion request succeeds. We attempt to delete the stored files promptly and retry failed cleanup every 5 minutes while the service is running. Our operational target is to finish within 24 hours; outages or storage failures can delay this. Cleanup continues after service restarts.</p>
    <p>Temporary files are normally removed when processing ends. Abandoned temporary files and unsuccessful uploads are eligible for cleanup after 24 hours.</p>
    <h2>Hosting recovery copies</h2>
    <p>After file cleanup, our supported cloud-storage configuration allows recovery copies for up to 7 additional days. These copies are not available through the app. Infrastructure database backups and operational logs have separate retention settings; their final periods must be specified in the full privacy policy.</p>
  </main>;
}
