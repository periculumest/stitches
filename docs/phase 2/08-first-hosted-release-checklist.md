# First Hosted Release Checklist

**Purpose:** Operational checklist for getting Stitch Helper online soon after SH-IDENTITY-002 is implemented.

## Infrastructure

- [ ] Choose hosting provider/region.
- [ ] Create production PostgreSQL.
- [ ] Create private object storage for pattern assets.
- [ ] Create private object storage namespace for backup artifacts.
- [ ] Configure DNS/public hostname.
- [ ] Enable HTTPS.
- [ ] Configure secret/environment values.

## Google Authentication

- [ ] Create/configure production Google OAuth client.
- [ ] Register exact production callback URI.
- [ ] Store client secret in hosting platform secrets.
- [ ] Verify 30-day sliding Stitch Helper session.
- [ ] Verify logout.

## Deployment

- [ ] Build immutable production container image.
- [ ] Run EF Core migrations as release task.
- [ ] Deploy web container.
- [ ] Verify `/health/live`.
- [ ] Verify `/health/ready`.
- [ ] Confirm structured logs reach hosting platform.

## Data & Assets

- [ ] Create user.
- [ ] Import a pattern.
- [ ] Confirm source asset is private.
- [ ] Restart/replace container.
- [ ] Confirm pattern source remains available.
- [ ] Mark project progress.
- [ ] Restart/replace container.
- [ ] Confirm progress remains.

## Backups

- [ ] Generate/download current export.
- [ ] Inspect archive manifest.
- [ ] Confirm owned source assets included.
- [ ] Confirm no auth/session secrets included.
- [ ] Run daily scheduled backup.
- [ ] Download retained daily backup.
- [ ] Verify weekly schedule.
- [ ] Confirm backup artifacts live outside web container.

## Security Isolation

- [ ] Create second user.
- [ ] Confirm user cannot access first user's projects.
- [ ] Confirm user cannot access first user's pattern assets.
- [ ] Confirm user cannot access first user's backups.

## Multi-device

- [ ] Sign same account into two browsers/computers.
- [ ] Make disjoint stitch updates.
- [ ] Confirm both survive.
- [ ] Confirm focus refresh.
- [ ] Confirm ~30-second active refresh.

## Release Decision

The hosted release is ready only when durable data, pattern assets, and user backups survive complete replacement of the running application container.
