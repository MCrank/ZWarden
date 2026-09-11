# 9. SteamCMD at runtime is mandatory, not preferred

PRD 22 says Project Zomboid server files *should normally* come from Steam via SteamCMD
"unless redistribution rights and operational requirements clearly support another approach."
**That escape clause cannot be satisfied.** The Project Zomboid EULA closes it. A clean
ZWarden.PZServer container **always** performs a network install on first run.

This carries a consequence straight into Feature 0: **two CI tiers**, because the always-on tier
cannot reach Steam and **no PZ-derived artefact may be committed to the repository**.

- Status: accepted
- Decided in: [#5](https://github.com/MCrank/ZWarden/issues/5) (verified against **Build 42.20.4** by running the game), absorbed by [#11](https://github.com/MCrank/ZWarden/issues/11) §3.2
- Overturns: **PRD 22**'s redistribution escape clause

## Context: why the clause cannot be satisfied

- **PZ EULA §3.2** expressly does not permit you to "*Distribute Project Zomboid yourself, or
  host its download*".
- **§2.1** closes the modified-files route: you may change or distribute base files "*provided
  that those changes do not result in you making Project Zomboid available to play or download
  (this includes making it available open source)*".
- **§4.1** explicitly permits *operating* a server, even commercially. So the product is fine;
  shipping the files is not.
- Steam's SSA prohibits reproducing and distributing Content, and its §2.E unlimited-hosting
  grant is scoped to "*Valve products*", so it does not reach here. The Steamworks
  dedicated-server distribution document contains no redistribution grant at all.

PRD 22's clause therefore cannot be satisfied **without a written exception from
`info@theindiestone.com`**. It is also operationally moot: the payload is roughly **6.72 GiB**.

The good news, verified by execution rather than inference: **anonymous SteamCMD login works for
both the dedicated server and Workshop content**, so **no Steam credentials need to be stored
anywhere**. App **380870** is the dedicated server (TOOL, `freetodownload=1`; not in the
anonymous package 17906, but reachable via the free-to-download flag); Workshop items live under
app **108600**, and `+login anonymous +workshop_download_item 108600 <id>` downloads them with
no account and no ownership.

## Alternatives considered

- **Bake the server files into the image** (the escape clause). Blocked by EULA §3.2 + §2.1.
  Not a judgement call.
- **Bake them and seek a written exception from The Indie Stone.** Not pursued: it makes the
  build reproducibility of the product depend on a private agreement, and at 6.72 GiB the image
  would be unpleasant regardless.
- **Ship a pre-seeded volume rather than an image layer.** Same EULA problem — it is still
  hosting the download — and it moves the distribution question without answering it.
- **Commit a small sample of real PZ files as test fixtures.** Rejected for the same reason,
  sharpened by what the install actually contains: it ships **two conflicting licence
  documents**, neither of which names `media/lua/`, and `servertest_SandboxVars.lua` does not
  ship at all — it is generated on first run.

## Consequences

- **Feature 12's exit condition always includes a network install on first run.** Cold-start
  time is a real product characteristic, not an implementation detail, and air-gapped
  deployment is not supported in v1.0.
- **Two CI tiers, and this is where they come from** (the harness shape itself is ADR 2):
  an **always-on offline tier** running against **hand-written synthetic fixtures** and a
  pre-seeded install layout, and a **scheduled/opt-in tier** that performs the real SteamCMD
  install and validates against **real files generated from a local install at test time**.
  **No PZ-derived artefact is committed.**
- **The canonical image must still be pre-provisioned on the host**, for an unrelated reason —
  ADR 8's allowlist denies `/images/*`, so container creation cannot pull it. The two
  constraints compose: ZWarden neither ships PZ's files nor pulls its own image at create time.
- **SteamCMD's exit codes are undocumented by Valve** — their own tracker issue has been open
  since 2016 and never answered. Observed: `0` success, `7` self-update-restart on Windows, `42`
  the restart code on Linux. Feature 17's state machine therefore **parses stdout** rather than
  branching on exit codes. This is a durable design constraint, not a workaround pending better
  documentation.
- Two smaller runtime corrections absorbed at the same time, both affecting Feature 12's
  supervision: **`-Dsoftreset` is broken as of 42.20.4**, and the developers **explicitly
  discourage SIGTERM shutdown**, documenting a stdin FIFO with `save` then `quit` instead. A
  clean stop path is therefore FIFO-based, not signal-based.
- Workshop content lands under the **Steam root** (`steamapps/workshop/content/108600/<id>/`),
  not under the PZ install or the user-data directory, so PRD 23's `data/workshop` is a
  relocation rather than a passthrough.
