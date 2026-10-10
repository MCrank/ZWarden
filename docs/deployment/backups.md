# Backups and restore

This guide covers taking, restoring and deleting backups of a Project Zomboid server's world in ZWarden: what a
backup holds, where it lives, what can go wrong, and who is allowed to do what. The last section is a checklist for
testing the whole lifecycle on a real host.

## What a backup is

A backup is a `.tar.gz` copy of one server's **world data**: everything under its `/pz/data` volume. That includes
saves, the server's `.ini`/`SandboxVars` configuration, player databases, logs, and the generated admin-password
file (`.zwarden-adminpw`). The Agent writes it on the host, and ZWarden records a **SHA-256** checksum of it.

A backup does **not** include:

- **The game install** (`<server-id>.server`, the SteamCMD download). It's a sibling of the world, not inside it,
  and an update or Recreate downloads it again.
- **Workshop content.** `data/workshop` is a link to the download cache on the install volume, and the Agent never
  follows links. Mods download again on the next start.
- **Empty folders.** A restore rebuilds folders from the files in the archive. PZ never depends on an empty one.

## Where backups live

On the Docker Compose deployment, archives are written on the Agent's host under
`/srv/zwarden/pz-backups/<server-id>/`, named `world-<UTC time>-<operation id>.tar.gz`. A restore's protective
backup ends in `-pre-restore.tar.gz`. The world itself is at `/srv/zwarden/pz-data/<server-id>`.

These backups protect a world against mistakes such as a bad config change, a broken mod or a griefed base. They
don't protect against the host itself being lost, because they sit on the same disk. Include `/srv/zwarden` in your
own host backup routine as well.

## Taking a backup

On a server's page, open **Backups** and click **Take backup**. The backup runs as an Operation and locks the server
against other changes until it finishes. It then appears in the list as **Manual**, with its size and the start of
its checksum.

**A running server is saved first.** Before archiving, the Agent sends PZ's `save` command over RCON. PZ only
queues the save when it replies, so the Agent waits 10 seconds (`Agent__BackupSaveSettle`) before writing the
archive. A stopped server's world is already on disk, so no save is sent.

**If the save can't be sent, the backup is still taken.** This happens when RCON is disabled, not answering, or
rejects its password. The row then shows a **Not saved first** badge: hover it to see why. The Operation's result line
says the same. Changes made in the last few minutes before such a backup may be missing from it. If that matters,
fix RCON (or stop the server) and take another.

## When backups happen automatically

Today, the only automatic backup is a restore's **protective** backup: before a restore replaces the world, the
current world is archived first. It shows as **Pre-operation** and can itself be restored, which undoes a mistaken
restore. Automatic backups before config, mod and game changes are coming in #379.

## Retention

There's no automatic pruning yet: every backup is kept until someone deletes it. Retention for automatic backups
is planned in #379. Delete old backups from the list to free disk space. Deleting removes the archive from the
host as well.

## Restoring a backup

1. **Stop the server.** A restore refuses a running server, because the game would be writing the world while it's
   replaced.
2. In **Backups**, click **Restore** on the backup you want.
3. The Agent then works through these steps:
   1. It checks the archive against its recorded SHA-256. A changed or damaged archive is refused before anything
      is touched.
   2. It takes the protective **Pre-operation** backup of the current world.
   3. It unpacks the archive next to the world and swaps it in all at once.
   4. It checks the result. If that check fails, the previous world is put back.
4. **Start the server** yourself. A restore never starts it, so you can check the result first.

The admin password, server configuration and saves all come back as they were in the backup.

### After a host is replaced

When a host's machine is wiped and enrolled again, the Owner replaces the old host from the new host's card (see
[remote-agent.md](remote-agent.md)). That moves the old host's servers **and their backups** to the new host, and the
audit entry for the replacement says how many backups moved. They restore from the new host as normal, as long as
the archives are still on disk under `/srv/zwarden/pz-backups`.

## When something goes wrong

| Message | What it means | What to do |
|---|---|---|
| This server has no data directory on its host to back up. | The server has never been provisioned and started on this host. | Start the server once, then back it up. |
| The backup could not be written: *…* | The Agent couldn't read a world file or write the archive (disk full, or a file it isn't allowed to read). | Check free space on `/srv/zwarden`. For an "access denied" on a file, see the note below. |
| The server is busy with another operation; try again once it finishes. | Another Operation holds the server's lock. | Wait for it to finish in the Operations feed. |
| Stop the server before restoring — a restore overwrites the live world. | The server is running. | Stop it, then restore. |
| The backup archive to restore was not found on the server's host. | The archive file is gone from `/srv/zwarden/pz-backups/<server-id>/`. | Delete the stale backup row. Restore a different backup. |
| The backup archive failed its integrity check (checksum mismatch); the restore was refused. | The archive changed after it was taken (disk error or tampering). Nothing was touched. | Use a different backup, and check the disk. |
| The backup archive contained no world data; the restore was refused. | The archive is empty. | Use a different backup. |
| The restored world did not verify after the swap; the previous world was kept. | The swap was rolled back. | Try again. If it repeats, collect a support package. |
| **Not saved first** badge | RCON couldn't take `save`; the archive may miss very recent changes. | Fix RCON or stop the server, and take another backup if needed. |

**"Access to the path '…/.zwarden-adminpw' is denied".** PZ images from before #377 wrote this file readable only
by the game, so the Agent couldn't archive it. Rebuild the PZ image and **restart** the server. On start, the
container makes the file readable by the Agent's group (mode 0640, still unreadable to anyone else) without
changing the password.

## Who can do what

Each action needs a server-scoped permission, and ZWarden checks it again on every action.

| Role | View | Take | Restore | Delete |
|---|:-:|:-:|:-:|:-:|
| Tenant Owner | ✓ | ✓ | ✓ | ✓ |
| Administrator | ✓ | ✓ | ✓ | ✓ |
| Operator | ✓ | ✓ | ✓ | — |
| Moderator | — | — | — | — |
| Viewer | ✓ | — | — | — |
| Support/Diagnostics | ✓ | — | — | — |

The permissions are `Backup.View`, `Backup.Create`, `Backup.Restore` and `Backup.Delete`. Custom roles can grant
any of them.

## Testing the lifecycle on a real host

Rebuild **web, agent and the PZ image** first, then run these checks.

1. **Password file.** Restart a server that predates #377. On the host, `stat -c '%a %U:%G'
   /srv/zwarden/pz-data/<id>/.zwarden-adminpw` shows `640` for the game user and group `10000`. Logging in as admin
   with the old password still works.
2. **Running backup.** Take a backup while the server runs. The Agent log shows the save, then the archive.
   The row is **Manual**, with no badge.
3. **Not saved first.** Take a backup while RCON can't answer, for example during early boot. The row shows **Not
   saved first**, and its tooltip gives the reason.
4. **Restore.** Make a visible in-game change, stop the server, and restore the backup from step 2. A
   **Pre-operation** backup appears, the server starts cleanly, the change is gone, and the admin password works.
5. **Tampered archive (optional).** Change a byte in an archive on the host, then restore it. The restore is
   refused with the checksum message and the world is untouched.
6. **Host replacement.** Restore, on the replacing host, a backup that a Replace moved there. It succeeds.
7. **Permissions.** As a Viewer, there are no Take, Restore or Delete buttons. As an Operator, Take and Restore are
   shown and Delete isn't.
