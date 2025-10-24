# GitHub Actions Workflows

## Sync Fork with Upstream

### Overview
The `sync-upstream.yml` workflow automatically synchronizes this forked repository with its upstream repository (renode/renode-infrastructure).

### Features
- **Automatic Sync**: Runs daily at 00:00 UTC
- **Manual Trigger**: Can be manually triggered via GitHub Actions UI
- **Conflict Resolution**: Uses "ours" merge strategy to automatically resolve conflicts in favor of this fork's changes

### How It Works
1. Checks out the repository with full history
2. Adds the upstream remote (renode/renode-infrastructure)
3. Fetches the latest changes from upstream
4. Merges upstream changes using `-X ours` strategy
5. If conflicts occur, automatically resolves them by keeping this fork's version
6. Pushes the merged changes back to this repository

### Conflict Resolution Strategy
The workflow uses Git's "ours" merge strategy (`-X ours`), which means:
- When both repositories have modified the same file, **this fork's version is kept**
- New files from upstream are added
- Deleted files in upstream are also deleted here
- The merge history from upstream is preserved

### Manual Trigger
To manually trigger the sync:
1. Go to the "Actions" tab in GitHub
2. Select "Sync Fork with Upstream" workflow
3. Click "Run workflow" button
4. Select the branch and click "Run workflow"

### Monitoring
Check the workflow runs in the "Actions" tab to see:
- When the last sync occurred
- Whether the sync was successful
- Any merge conflicts that were resolved
- What changes were brought in from upstream
