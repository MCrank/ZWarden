# 1. Three components, and the words we stopped using

ZWarden has exactly three first-class components — **ZWarden.Web**, **ZWarden.Agent**,
**ZWarden.PZServer** — and those names are the only component vocabulary. **"Manager" is
retired.** **"Control plane"** describes the product as a whole, in prose, and never names a
component. A machine is a **host**; a **Server** is one Project Zomboid server instance.
`CONTEXT.md` is the glossary that holds the terms; this ADR records why they were fixed.

- Status: accepted
- Decided in: [#11](https://github.com/MCrank/ZWarden/issues/11) (recorded as §4 of `docs/scope-and-sequencing.md`), swept by [#16](https://github.com/MCrank/ZWarden/issues/16)

## Context

PRD 13 names three components. The rest of the PRD did not obey it: **"Manager" appeared 19
times**, every one of them meaning ZWarden.Web, and the document header called the same thing a
"Blazor Control Plane". Meanwhile CLAUDE.md, the README and PRD §1 all use "control plane" in a
different and correct sense — the product. One phrase was doing two jobs, and another word was a
ghost of a component that does not exist.

That is tolerable in a document a human reads once. It is not tolerable here, for two reasons:

1. **PRD 15 wants architecture tests**, and architecture tests assert against assembly and type
   names. A vocabulary with two names for one component cannot be asserted.
2. **Most of this codebase will be written by agents reading these documents.** An agent that
   reads "Manager" in the PRD and "ZWarden.Web" in the glossary will produce both, and the drift
   compounds across fifteen features of agent-built code.

The third collision is subtler, and is why "host" had to be pinned: Project Zomboid itself calls
a game instance a *server*, and deployment prose calls a machine a *server*. Two different things
under one word, inside the domain the product is about.

## Alternatives considered

- **Tolerate the synonyms and rely on context.** This is what the PRD did. Rejected for the two
  reasons above; the cost of a rename rises with every feature written against the old words, and
  it was already at 19 occurrences before a line of code existed.
- **Keep "control plane" as the component name and retire "ZWarden.Web".** Rejected: PRD 13, the
  solution layout in PRD 14 and the assembly names all already say ZWarden.Web, and "control
  plane" is the phrase the product is sold with. The product sense is the more valuable one, and
  it is the one that cannot be renamed.

## Consequences

- The sweep is done and merged ([#16](https://github.com/MCrank/ZWarden/issues/16)); no "Manager"
  survives in the PRD, CLAUDE.md or `docs/agents/`. The only remaining occurrences are the places
  that deliberately record the retirement.
- PRD §16 is retitled **ZWarden.Web ↔ Agent Protocol** and Feature 7 is now **Agent Protocol
  Contracts**.
- Two of the sweep ticket's own premises turned out to be wrong, recorded here so nobody
  re-derives them: **PRD §63 contained no "control plane"** — its drift was
  `Manager/Agent trust boundary`, and the real component-name use of the phrase was in the
  document header metadata — and **there was no Server-means-machine ambiguity in the PRD**,
  which already used "host" correctly throughout. What actually collided with the glossary was
  the `server-side` idiom, where "server" named the component tier.
- Deliberately left alone, and not drift: Feature 10's name *SignalR Agent Control Plane* (the
  roadmap documents carry the identical string), Feature 33A's *hosted ZWarden control plane*
  (product sense), and §26's `gameserver` network identifier.
- The cost we accept: four PRD sentences had to be **rewritten** rather than substituted, because
  a literal swap mangled them (§41, Feature 14, Feature 35, §2.5). A rename is never purely
  mechanical, and the next one will not be either.
