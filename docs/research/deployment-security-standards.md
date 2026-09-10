# Deployment and Security Standards — Verified Facts

Research resolution for [issue #6](https://github.com/MCrank/ZWarden/issues/6) ("Verify deployment
and security standards"), a wayfinder research ticket under [#1](https://github.com/MCrank/ZWarden/issues/1).

- **Researched:** 2026-09-10
- **Scope:** the deployment and security baseline asserted by PRD v1.2 sections 2.1, 10, 11, 24, 26,
  27, 45, 46 and 55.
- **Nature of this document:** facts only, each with a primary-source URL. It contains **no
  recommendations** about what ZWarden should choose. Where a fact could not be verified against a
  primary source, that is stated explicitly. Where a widely repeated claim turns out to be vendor or
  blog embellishment rather than documented behaviour, that is called out.
- **Volatility warning:** version numbers, pricing and published limits below are point-in-time as of
  2026-09-10. Pricing pages and plan matrices in particular change without notice.

---

## 1. Caddy (PRD 45, 46, 26)

### 1.1 Current stable version

The latest stable release is **v2.11.4**, released **2026-06-03**. Preceding releases in the same
minor series: v2.11.3 (2026-05-12), v2.11.2 (2026-03-06), v2.11.1 (2026-02-23).
Source: <https://github.com/caddyserver/caddy/releases>

Everything visible on the releases page is in the v2.x line. **Unverified:** no primary statement was
found confirming or denying a v3 roadmap; "no v3" here rests on absence of evidence on the releases
page, not on a positive statement.

### 1.2 Automatic HTTPS behaviour

All from <https://caddyserver.com/docs/automatic-https> unless noted.

- **Activation triggers.** Automatic HTTPS activates whenever Caddy is configured with a domain name
  or IP it should serve — a Caddyfile site address, an HTTP host matcher in JSON config, the
  `--domain`/`--from` CLI flags, or the `automate` certificate loader. It does **not** activate if
  HTTPS is explicitly disabled, if no hostnames/IPs are given, if the site listens only on plain
  HTTP, if the site address is prefixed `http://`, or if certificates are loaded manually/explicitly.
- **Hostname qualification for a publicly trusted certificate.** The hostname must be non-empty,
  contain only alphanumerics, hyphens, dots and wildcards, must not start or end with a dot, and must
  not be a `localhost`-type name, a bare IP address, or a wildcard outside the leftmost label.
- **On-demand TLS.** Caddy can obtain a certificate during the TLS handshake itself for hosts not
  listed in config. It must be explicitly enabled and is documented as expected to be paired with
  restrictions (typically an "ask" endpoint) to prevent abuse.
- **HTTP → HTTPS redirect.** The docs state Caddy "keeps all managed certificates renewed and
  redirects HTTP (default port `80`) to HTTPS (default port `443`) automatically" — no extra config
  once automatic HTTPS is active. The redirect status code is **308 Permanent Redirect**
  (`http.StatusPermanentRedirect`), read from `makeRedirRoute` in the source:
  <https://github.com/caddyserver/caddy/blob/master/modules/caddyhttp/autohttps.go>
- **Ports.** 80 and 443 must be reachable from the internet (or forwarded to Caddy) for HTTP-01 and
  TLS-ALPN-01 respectively.
- **Certificate storage.** Certificates live in Caddy's configured storage facility. On Linux/BSD the
  default data directory is `$HOME/.local/share/caddy`, or `$XDG_DATA_HOME/caddy` when that variable
  is set; the docs emphasise it must be persistent and writable.
  <https://caddyserver.com/docs/conventions>
- **Renewal timing.** CertMagic's `DefaultRenewalWindowRatio = 1.0 / 3.0` — renewal happens once the
  certificate enters the final third of its validity period (i.e. at the two-thirds-elapsed point).
  The renewal check interval is `DefaultRenewCheckInterval = 10 * time.Minute`. Source:
  <https://github.com/caddyserver/certmagic/blob/master/maintain.go>
- **Local/internal hostnames.** Self-signed certificates come from Caddy's own local CA, with
  authority data stored at `pki/authorities/local` under the data directory.

**Default CA / issuer chain.** The prose in the docs is ambiguous and inconsistent: the
automatic-HTTPS page says "By default, Caddy enables two ACME-compatible CAs: Let's Encrypt and
ZeroSSL," with ZeroSSL as fallback, while the global-options page for `acme_ca` states the default as
"ZeroSSL and Let's Encrypt's production endpoints" (opposite order) without saying which is tried
first. Sources: <https://caddyserver.com/docs/automatic-https>,
<https://caddyserver.com/docs/caddyfile/options>.

The **source settles it: Let's Encrypt is first, and ZeroSSL is only added when a user email is
configured.** `DefaultIssuers()` in
<https://github.com/caddyserver/caddy/blob/master/modules/caddytls/automation.go>:

```go
func DefaultIssuers(userEmail string) []certmagic.Issuer {
	issuers := []certmagic.Issuer{new(ACMEIssuer)}
	if strings.TrimSpace(userEmail) != "" {
		issuers = append(issuers, &ACMEIssuer{
			CA:    certmagic.ZeroSSLProductionCA,
			Email: userEmail,
		})
	}
	return issuers
}
```

**Google Trust Services does not appear** in that file, nor is it mentioned as a default issuer on
either doc page — there is no evidence it is part of Caddy's default fallback chain.

**HSTS — marketing claim vs documented behaviour.** The automatic-HTTPS page does **not** mention
HSTS / `Strict-Transport-Security` at all, and a GitHub proposal to make HSTS automatic was closed and
labelled "declined 🚫 Not a fit for this project"
(<https://github.com/caddyserver/caddy/issues/4751>). Third-party how-to blogs claim Caddy "enables
HSTS automatically"; that is **not documented Caddy behaviour**. The documented mechanism is the
explicit `header` directive, e.g. `header Strict-Transport-Security "max-age=63072000;
includeSubDomains; preload"` (<https://caddyserver.com/docs/caddyfile/directives/header>).

### 1.3 ACME / Let's Encrypt HTTP-01 requirements and constraints

From <https://letsencrypt.org/docs/challenge-types/>:

- HTTP-01 requires serving the token at `http://<domain>/.well-known/acme-challenge/<TOKEN>` on
  **port 80 only** — the port is not configurable. The response body is the token, a period, and the
  base64url thumbprint of the account key JWK. It implicitly requires a public DNS A/AAAA record
  resolving to the host.
- HTTP-01 **cannot** issue wildcard certificates.
- **Redirects:** the validating server follows redirects up to 10 levels deep, accepts only `http:`
  and `https:` schemes, and is limited to ports 80 or 443. It does not validate the TLS certificate
  on an HTTPS redirect target.
- **IP addresses:** HTTP-01 (and TLS-ALPN-01) can validate bare IP addresses; DNS-01 cannot.

RFC 8555 §8.3 (<https://www.rfc-editor.org/rfc/rfc8555#section-8.3>) requires the server to respond
to the well-known URI over HTTP with the key authorization. The section does not itself authorise an
alternate port and does not contain the redirect-following rules above — **the redirect behaviour is
Let's Encrypt operational policy, not an RFC 8555 mandate.**

**Current Let's Encrypt rate limits** (<https://letsencrypt.org/docs/rate-limits/>):

| Limit | Value |
|---|---|
| Certificates per Registered Domain | 50 per 7 days (refills 1 per ~202 min) |
| Duplicate Certificate (identical identifier set) | 5 per 7 days (refills 1 per 34 h) |
| New Orders per Account | 300 per 3 hours (refills 1 per 36 s) |
| Authorization failures per identifier per account | 5 per hour (refills 1 per 12 min) |
| Consecutive authorization failures per identifier | 1,152 (refills 1/day; resets to 0 on success) |
| Accounts per IP | 10 per 3 hours |
| Accounts per IPv6 /48 | 500 per 3 hours |

**Certificate lifetimes by ACME profile** (<https://letsencrypt.org/docs/profiles/>):

| Profile | Lifetime |
|---|---|
| `classic` (default) | 90 days |
| `tlsserver` | 45 days (opt-in / early-adopter as of the 2026-05-13 switch) |
| `shortlived` | 160 hours (~6.7 days), selectable by any subscriber today |

6-day and IP-address certificates reached general availability around **2026-01-15**, and Let's
Encrypt has published a timeline moving default issuance to shorter lifetimes (e.g. a 64-day default
profile around 2027-02-10). Sources:
<https://letsencrypt.org/2026/01/15/6day-and-ip-general-availability>,
<https://letsencrypt.org/2025/12/02/from-90-to-45>. **Partially verified:** the per-profile lifetimes
were read from the profiles page directly; the forward-dated migration schedule came from search
snippets of those blog posts rather than a full page read, so treat those specific future dates as
secondary-sourced.

### 1.4 DNS-01 availability and what it needs

- **Protocol requirement.** DNS-01 requires publishing a TXT record at `_acme-challenge.<domain>`
  whose value derives from the challenge token and account key. It is **the only** challenge type
  that supports wildcard certificates. <https://letsencrypt.org/docs/challenge-types/>
- **Not in the stock binary.** DNS-01 requires a DNS-provider plugin module compiled into Caddy.
  Obtain it either via **xcaddy** (`xcaddy build --with github.com/caddy-dns/<provider>`, optionally
  pinned: `xcaddy build v2.0.1 --with github.com/caddy-dns/route53@v0.1.1`) or via Caddy's official
  download page with plugins selected. <https://github.com/caddyserver/xcaddy>
- **Where plugins live.** The `caddy-dns` GitHub organisation, <https://github.com/caddy-dns>, which
  reports 99 repositories (not enumerated exhaustively; observed providers include `route53`,
  `dnsmadeeasy`, `bluecat`, `desec`, `njalla`, `parspack`, `edgeone`, plus an `acmeproxy` module).
- **Credential requirement.** The provider module needs an API token/credential with write access to
  the target DNS zone. The specific parameter names are provider-module-specific; the `tls` directive
  only defines the provider-name-plus-params shape.
- **Caddyfile syntax** (<https://caddyserver.com/docs/caddyfile/directives/tls>):

```caddyfile
tls [internal|force_automate|<email>] | [<cert_file> <key_file>] {
    dns <provider_name> [<params...>]
    propagation_timeout <duration>   # default 2m; -1 disables
    propagation_delay <duration>     # default 0
    dns_ttl <duration>
    dns_challenge_override_domain <domain>
    resolvers <dns_servers...>       # e.g. resolvers 8.8.8.8 8.8.4.4
    ca <ca_dir_url>
    eab <key_id> <mac_key>
    email <address>
}
```

### 1.5 WebSocket / SignalR reverse-proxy configuration

From <https://caddyserver.com/docs/caddyfile/directives/reverse_proxy>:

- **No special configuration is needed for WebSockets in Caddy v2.** The docs state the proxy
  "supports WebSocket connections, performing the HTTP upgrade request then transitioning the
  connection to a bidirectional tunnel." Connection/Upgrade hop-by-hop headers are handled
  automatically as part of that upgrade. *(The fetched page contains no v1-vs-v2 comparison, so the
  often-repeated claim that v1 needed manual config is **unverified** from this source.)*
- **Default headers.** `X-Forwarded-For`, `X-Forwarded-Proto` and `X-Forwarded-Host` are set/augmented
  automatically. Incoming values for these are **ignored by default** to prevent spoofing, unless
  `trusted_proxies` is configured.
- **Host header.** Since Caddy v2.11.0 the `Host` header is automatically matched to the upstream
  address when proxying to HTTPS upstreams.
- **Streaming / long-lived connections.** `flush_interval` controls response buffering; a negative
  value enables low-latency mode (flush immediately after each write). Responses are flushed
  immediately and automatically when the upstream sends `Content-Type: text/event-stream` or has an
  unknown content length — i.e. SSE needs no extra config. On config reload, `stream_timeout`
  forcibly closes streams exceeding a duration and `stream_close_delay` delays forced closure to
  avoid reconnection storms.

From Microsoft Learn, <https://learn.microsoft.com/en-us/aspnet/core/signalr/scale?view=aspnetcore-10.0>:

- "SignalR requires the same server process handle all HTTP requests for a specific connection. When
  SignalR runs on a server farm (multiple servers), 'sticky sessions' must be used." Sticky sessions
  are **not** required in exactly three cases: a single server/single process; using Azure SignalR
  Service (the client is redirected to the service); or when all clients are WebSockets-only with
  `SkipNegotiation` enabled. In every other scenario — **including when a Redis backplane is used** —
  session affinity must be configured.
- The same page's Nginx example shows the minimum reverse-proxy settings SignalR needs on that
  server: `proxy_set_header Upgrade $http_upgrade;`, `proxy_set_header Connection
  $connection_upgrade;` (via a `map`), `proxy_http_version 1.1;` to enable WebSockets,
  `proxy_buffering off;` for Server-Sent Events, and a longer `proxy_read_timeout` for Long Polling.
  Nginx therefore needs explicit Upgrade/Connection forwarding; Caddy, per its own docs above, does
  this without such directives.

### 1.6 Internal CA / locally trusted TLS (PRD 46 "Private" mode)

From <https://caddyserver.com/docs/automatic-https> and
<https://caddyserver.com/docs/caddyfile/directives/tls>:

- `tls internal` on a site block tells Caddy to use locally trusted certificates from its own
  built-in CA instead of requesting a public ACME certificate.
- Relevant `tls` subdirectives: `ca <name>` (names the internal CA; default `local`), `lifetime
  <duration>` (leaf certificate validity; **default `12h`**), and `sign_with_root` (forces leaf
  signing directly by the root — documented as "not recommended").
- Root CA data is stored at `pki/authorities/local` in Caddy's data directory. The root CA certificate
  is automatically installed into the OS trust store (a password prompt may appear, implying elevated
  privileges are needed for that step). The root key signs only intermediates, never leaves, and
  intermediates auto-renew on their own shorter lifetimes.
- **Actual CA lifetimes, from source.**
  `defaultRootLifetime = 24 * time.Hour * 30 * 12 * 10` (**10 years**) and
  `defaultIntermediateLifetime = 24 * time.Hour * 7` (**7 days**), in
  <https://github.com/caddyserver/caddy/blob/master/modules/caddypki/ca.go>.
  The leaf default is `defaultInternalCertLifetime = 12 * time.Hour`, applied via
  `if iss.Lifetime == 0 { iss.Lifetime = caddy.Duration(defaultInternalCertLifetime) }` in
  <https://github.com/caddyserver/caddy/blob/master/modules/caddytls/internalissuer.go> — matching the
  documented 12 h default.
- **Caddy's internal PKI is built on Smallstep's libraries.** `go.mod` at the root of
  `caddyserver/caddy` lists `github.com/smallstep/certificates`, `github.com/smallstep/nosql`,
  `github.com/smallstep/truststore` and `go.step.sm/crypto` as direct dependencies (plus several
  indirect `smallstep/*` modules), and `modules/caddypki/ca.go` imports `go.step.sm/crypto/x509util`
  directly (used for `x509util.DefaultRootTemplate` / `DefaultIntermediateTemplate`).
  Source: <https://github.com/caddyserver/caddy/blob/master/go.mod>
- `tls internal` is documented as usable for arbitrary internal hostnames and for IP addresses — the
  automatic-HTTPS page explicitly contrasts "public domains" against "local/internal hostnames"
  including names that do not qualify for public DNS.

**Internal ACME server.** Caddy can act as an internal CA for *other* ACME clients via the
`acme_server` directive (<https://caddyserver.com/docs/caddyfile/directives/acme_server>):

```caddyfile
acme_server [<matcher>] {
    ca         <id>
    lifetime   <duration>
    resolvers  <resolvers...>
    challenges <challenges...>
    allow_wildcard_names
    allow {
        domains   <domains...>
        ip_ranges <addresses...>
    }
    deny {
        domains   <domains...>
        ip_ranges <addresses...>
    }
}
```

- `ca` defaults to `local` — "Caddy's default CA, intended for locally-used, self-signed
  certificates."
- Supported challenge types: `http-01`, `tls-alpn-01`, `dns-01`.
- `allow_wildcard_names` "enables issuing of certificates with wildcard SAN (Subject Alternative
  Name)".
- Default client directory endpoint in the docs' example: `https://localhost/acme/local/directory`.
- `sign_with_root` is **not** part of `acme_server` — it belongs to the `tls` directive's `internal`
  issuer options.

---

## 2. Docker Engine API client libraries for .NET (PRD 27, and the Agent's Docker runtime)

### 2.1 Docker Engine API baseline

- **Current Engine API version: v1.56** (base path `/v1.56`), per the primary spec at
  <https://raw.githubusercontent.com/moby/moby/master/api/swagger.yaml> and
  <https://docs.docker.com/reference/api/engine/version-history/>.
- **Current Docker Engine release: 29.8.0**, released 2026-09-03.
  <https://docs.docker.com/engine/release-notes/29/>
- **Access model.** Documented as "a RESTful API accessed by an HTTP client such as `wget` or `curl`,
  or the HTTP library which is part of most modern programming languages"
  (<https://docs.docker.com/reference/api/engine/>). A raw HTTP client is therefore a
  documented-viable way to talk to the Engine API, reached over a Unix socket, TCP, or a Windows named
  pipe per standard `DOCKER_HOST` semantics.
- **Version negotiation.** "The Docker Engine API server and client support API-version negotiation.
  If a client connects to an older version of the Docker Engine, it negotiates the highest version of
  the API supported by both the client and daemon, downgrading to an older version of the API if
  necessary." <https://docs.docker.com/reference/api/engine/>
- **`/_ping` mechanism**, verified from the Moby OpenAPI source
  (<https://github.com/moby/moby/blob/master/api/swagger.yaml>): `GET /_ping` (and `HEAD /_ping`) is
  "a dummy endpoint you can use to test if the server is accessible." A 200 response carries headers
  including `Api-Version` — "Max API Version the server supports" — plus `Builder-Version`,
  `Docker-Experimental`, `Swarm`, `Cache-Control` and `Pragma`. This is how clients discover the
  daemon's maximum supported API version.
- **Minimum supported API version.** "API versions before v1.40 are deprecated and no longer supported
  by current versions of the Docker Engine and CLI." <https://docs.docker.com/reference/api/engine/>
  (The version-history page separately notes v1.25 as the point at which the API version became
  required in all calls — a distinct historical milestone, not the current support floor.)
- **Version prefixing.** From the Moby swagger spec: "To lock to a specific version of the API, you
  prefix the URL with its version, for example, call `/v1.30/info`... If you omit the
  version-prefix, the current version of the API is used. For example, calling `/info` is the same as
  calling `/v1.56/info`."

### 2.2 Library inventory

| Package id | Latest stable (date) | Downloads | Declares net10.0? | Repo | License | Last commit | Distinct committers, last 12 mo |
|---|---|---|---|---|---|---|---|
| `Docker.DotNet` | 3.125.15 (2023-05-18) | 77.6 M | **No** — netstandard2.0/2.1 only | [dotnet/Docker.DotNet](https://github.com/dotnet/Docker.DotNet) | MIT | 2024-10-30 | **0** |
| `Docker.DotNet.Enhanced` | 4.3.3 (2026-06-28) | 65.5 M | **Yes** | [testcontainers/Docker.DotNet](https://github.com/testcontainers/Docker.DotNet) | MIT | 2026-09-05 | 9 |
| `Testcontainers` | 4.15.0 (2026-09-06) | 114.2 M | **Yes** | [testcontainers/testcontainers-dotnet](https://github.com/testcontainers/testcontainers-dotnet) | MIT | 2026-09-10 | 15+ |
| `FluentDocker` | 3.1.0 (2026-06-04) | 27 K (legacy id `Ductus.FluentDocker`: 8.07 M) | **Yes** (net8.0, net10.0) | [mariotoffia/FluentDocker](https://github.com/mariotoffia/FluentDocker) | Apache-2.0 | 2026-06-04 | **1** |
| `DockerSdk` | 0.5.12 (2021-09-04) | 5.3 K | No — net5.0 only | [Emdot/DockerSdk](https://github.com/Emdot/DockerSdk) | NOASSERTION | ~2022-12-08 | 0 |
| `TrapTech.Docker.DotNet` | 3.125.14 (2022-10-29) | 16.8 K | No | unconfirmed | unconfirmed | dormant | 0 |
| `Thhave.Docker.DotNet` | 3.125.3.23 (2020-05-19) | 11.7 K | No | unconfirmed | unconfirmed | dormant | 0 |

Version/date/download figures were read from the NuGet registration and search APIs
(`api.nuget.org/v3/registration5-semver1/<id>/index.json`, `azuresearch-usnc.nuget.org/query`);
maintenance figures from the GitHub REST API.

### 2.3 Per-library detail

**`Docker.DotNet`** — <https://www.nuget.org/packages/Docker.DotNet>. Latest 3.125.15 published
2023-05-18. Targets `.NETStandard2.0` and `.NETStandard2.1` only, verified from the package's own
dependency groups; it is consumable from net10.0 only because net10.0 supports netstandard2.x. Last
commit to `master` 2024-10-30. **Zero commits in the trailing 12 months** (GitHub commits API with
`since=2025-09-10` returned an empty list). Open issues 168, open PRs 17 (GitHub search API);
`open_issues_count` 186. **Not archived** and carrying **no deprecation notice or pointer to a fork**
in its README — but effectively dormant for ~22 months. NuGet owners are `Docker.DotNet` and
`dotnetfoundation`, hosted under the `dotnet` GitHub org: **.NET Foundation, not Docker Inc. and not
Microsoft product-supported.** Companion transport packages `Docker.DotNet.X509` (43.2 M downloads)
and `Docker.DotNet.BasicAuth` (1.0 M) share the repo and versioning.

**`Docker.DotNet.Enhanced`** — <https://www.nuget.org/packages/Docker.DotNet.Enhanced>. The actively
maintained fork. Latest 4.3.3 published 2026-06-28. **Explicitly targets `net10.0`, `net9.0`,
`net8.0`, `netstandard2.0`, `netstandard2.1`** — verified from the catalog dependency groups; it is
the only Docker.DotNet-family package that declares net10.0. Its NuGet description states it targets
"Docker Engine API (v29.4.1)". Last commit 2026-09-05; open PRs 0, open issues 6; not archived; 18
forks. **9 distinct committers in the trailing 12 months**, concentrated on two (Andre Hofmeister 31
commits, campersau 25), so maintainer-concentrated but not a single-person project. Maintained by the
Testcontainers GitHub organisation — community-maintained, not first-party. Split sub-packages at the
same version: `.X509` (65.2 M downloads), `.Unix`, `.NPipe`, `.LegacyHttp`, `.NativeHttp`,
`.Handler.Abstractions`, `.BasicAuth`.

**`Testcontainers`** — <https://www.nuget.org/packages/Testcontainers>. Latest 4.15.0 published
2026-09-06. For **every** target framework (`net8.0`, `net9.0`, `net10.0`, netstandard2.0/2.1) it
depends on `Docker.DotNet.Enhanced [4.3.3, )` and `Docker.DotNet.Enhanced.X509 [4.3.3, )` — so
Testcontainers today uses the Testcontainers fork, **not** `dotnet/Docker.DotNet`. Last commit
2026-09-10; open PRs 11, open issues 33 (`open_issues_count` 44); not archived. Committer activity is
concentrated on Andre Hofmeister (63 commits in 12 months) with 14+ other distinct
individuals/bots.

**No first-party Docker Inc. / Moby .NET client exists.** NuGet searches for `docker.engine`, `moby`
and `docker sdk` surfaced no .NET client published by Docker Inc. or under a `docker`/`moby` GitHub
org. `docker.dotnet` on NuGet is owned by the .NET Foundation, not Docker Inc. **This is a negative
finding from the specific searches run, not exhaustive proof.**

**No OpenAPI-generated Engine API client** was found on NuGet (searched `docker engine`, `moby`,
`docker api`). Also a negative finding from those queries, not proof of absence.

**`FluentDocker` / `Ductus.FluentDocker`** — a fluent wrapper rather than a raw API client, but per
its own README it includes a "Docker API driver" that "talks directly to the Docker Engine REST API
over Unix socket, named pipe, or TCP+TLS" (<https://github.com/mariotoffia/FluentDocker>) — i.e. a
hand-rolled HTTP client, not a wrapper over Docker.DotNet. `FluentDocker` 3.1.0 targets `net8.0` and
`net10.0`. Apache-2.0. **Single maintainer: 1 distinct committer (Mario Toffia, 100 commits) in the
trailing 12 months.** Open issues 4, open PRs 1, 104 forks, not archived. The legacy id
`Ductus.FluentDocker` (latest 2.85.0, 2025-07-25, 8.07 M downloads) carries no deprecation notice.

**`DockerSdk`** — <https://www.nuget.org/packages/DockerSdk>. Latest 0.5.12 (2021-09-04), 5,282
downloads, targets `net5.0` only (an out-of-support framework, and not netstandard). GitHub reports
`license: NOASSERTION` despite MIT appearing in the package text. `pushed_at` 2022-12-08; no activity
in 12 months. Effectively abandoned.

**Out of scope but surfaced:** `Docker.Registry.DotNet` 2.1.0 (2026-08-15,
[ChangemakerStudios/Docker.Registry.DotNet](https://github.com/ChangemakerStudios/Docker.Registry.DotNet),
3 open issues, not archived) targets the Docker **Registry** HTTP API, a different API surface from
the Engine API.

### 2.4 Flagged as unverifiable in this area

- The GitHub repo and license for `TrapTech.Docker.DotNet` — its NuGet catalog entry has an empty
  `projectUrl`.
- Whether `DockerSdk` restores/builds cleanly under net10.0 — only its declared `net5.0` target group
  was verified; no real net10.0 restore was performed.
- The two negative findings above (no first-party client, no OpenAPI-generated client) rest on the
  specific search queries run.

---

## 3. Restricted Docker socket proxies (PRD 27)

### 3.1 Docker's own position on socket exposure

- **"Protect the Docker daemon socket"** — <https://docs.docker.com/engine/security/protect-access/>.
  Verbatim: *"anyone with the keys can give any instructions to your Docker daemon, giving them root
  access to the machine hosting the daemon."* Documented mitigations on that page: SSH tunnelling,
  TLS (HTTPS) protection, and — verbatim — *"Authorization plugins offer more fine-grained control to
  supplement authentication from mutual TLS."* Rootless mode is **not** listed as a mitigation on
  this page.
- **Rootless mode** — <https://docs.docker.com/engine/security/rootless/>. Verbatim: *"Rootless mode
  lets you run the Docker daemon and containers as a non-root user to mitigate potential
  vulnerabilities in the daemon and the container runtime."* Distinguished from `userns-remap`
  because in rootless mode "both the daemon and the container are running without root privileges."
  **Unverified:** the fetch did not surface the page's known-limitations list; that is a gap in this
  research, not evidence that rootless mode has no documented limitations.

### 3.2 Docker's native authorization-plugin mechanism

Source: <https://docs.docker.com/engine/extend/plugins_authorization/>

- **Request hook (`AuthZPlugin.AuthZReq`)** receives `User` (from the TLS client certificate
  CommonName when mTLS is configured), `UserAuthNMethod` (`"TLS"`), `RequestMethod`, `RequestURI`
  (full URI including API version), `RequestHeader` (map; the authorization header is excluded), and
  `RequestBody` (raw bytes). So a plugin gets full method/URI/header/body visibility plus caller
  identity when mTLS is set up on the daemon.
- **Response hook (`AuthZPlugin.AuthZRes`)** is also invoked, additionally receiving
  `ResponseStatusCode`, `ResponseHeader` and `ResponseBody` (raw bytes), and may approve or deny.
- **Documented limitations, quoted:**
  - *"For commands that can potentially hijack the HTTP connection (`HTTP Upgrade`), such as `exec`,
    the authorization plugin is only called for the initial HTTP requests"* — streamed data after the
    upgrade bypasses authorization entirely.
  - *"Authorization plugins enforce requests to the Docker daemon's HTTP API only"* — gRPC calls are
    not subject to authorization.
  - The internal buffer holding the response body for inspection *"has a fixed capacity of 64 KiB"* —
    larger or streamed responses (logs, events) are not fully inspectable.
  - The plugin *"receives the raw request body from the daemon"* and *"must apply the same decoding
    semantics as the daemon"* to enforce correctly — a documented correctness burden on the plugin
    author.
- **Docker ships no first-party label-based authorization feature.** The AuthZ hook is generic; label
  logic would have to be implemented inside a plugin (which is possible in principle, since the full
  request and response body are exposed).
- Related plugins surfaced but not deeply examined: `open-policy-agent/opa-docker-authz` (Rego-driven
  AuthZ plugin), `I-am-Roman/docker-auth-plugin`, `rhatdan/docker-rbac` (a design-discussion repo,
  not a shipped implementation).

### 3.3 Capability matrix

The central question for PRD 27 is **label-scoped authorization**: can the proxy restrict operations
to containers carrying a specific label (e.g. `io.zwarden.managed=true`)?

| Project | Endpoint granularity | Method filtering | **Label-scopes target containers?** | Body inspect/mutate | Response filter | Architecture |
|---|---|---|---|---|---|---|
| [Tecnativa/docker-socket-proxy](https://github.com/Tecnativa/docker-socket-proxy) | Top-level API section env-vars only | **Global `POST` switch** | **NO** | No | No | Reverse proxy (HAProxy) |
| [linuxserver/docker-socket-proxy](https://github.com/linuxserver/docker-socket-proxy) | Same section env-var model + more `ALLOW_*` | **Global `POST` switch** | **NO** | No | No | Reverse proxy |
| [wollomatic/socket-proxy](https://github.com/wollomatic/socket-proxy) | **Arbitrary anchored regex paths** | **Per-rule per-method** | **NO** (has a *caller*-identity label feature) | Bind-mount source only | No | Reverse proxy |
| [FoxxMD/docker-proxy-filter](https://github.com/FoxxMD/docker-proxy-filter) | Per-container routes | inherits upstream proxy | **YES** | No | **Yes** | Filter, sits *behind* another proxy |
| [mikesir87/docker-socket-proxy](https://github.com/mikesir87/docker-socket-proxy) | YAML gates, exact-match | per-rule | **YES** | **Yes (mutators)** | **Yes** | Reverse proxy |
| [CodesWhat/sockguard](https://github.com/CodesWhat/sockguard) | YAML method + glob path | per-rule | **YES** (source-corroborated, unproven) | Yes (README) | Yes (README) | Reverse proxy |
| [knrdl/docker-socket-protector](https://github.com/knrdl/docker-socket-protector) | Profile files to regex | per-rule | **NO** | No | No | Reverse proxy |
| Docker native AuthZ hook | Full URI | Full method | **No built-in feature** (buildable) | Full body exposed | Full body exposed | AuthZ plugin |
| [twistlock/authz](https://github.com/twistlock/authz) | Regex "actions" | via action names | **NO** | No | No | AuthZ plugin |

### 3.4 Per-project detail

**Tecnativa/docker-socket-proxy** — Apache-2.0 (GitHub API `license.spdx_id`), HAProxy-based with a
Python-templated config, image `tecnativa/docker-socket-proxy` on Docker Hub. Latest release
**v0.5.0, 2026-07-27**
(<https://github.com/Tecnativa/docker-socket-proxy/releases/tag/v0.5.0>). Last commit 2026-07-27; 27
contributors; **52 open issues, 16 open PRs**; not archived; created 2017-03-29; 2,754 stars, 207
forks — an active multi-contributor org repo, not a single maintainer.
Rule model per <https://raw.githubusercontent.com/Tecnativa/docker-socket-proxy/master/README.md>:
top-level API-section env-var switches only (`CONTAINERS`, `IMAGES`, `NETWORKS`, `VOLUMES`,
`SERVICES`, `SWARM`, `NODES`, `TASKS`, `PLUGINS`, `SECRETS`, `CONFIGS`, `AUTH`, `BUILD`, `COMMIT`,
`DISTRIBUTION`, `EXEC`, `GRPC`, `INFO`, `SESSION`, `SYSTEM`, `EVENTS`, `PING`, `VERSION`), plus
action toggles `ALLOW_START`, `ALLOW_STOP`, `ALLOW_RESTARTS`, `ALLOW_PAUSE`, `ALLOW_UNPAUSE` scoped to
`containers/{id}/start|stop|restart|kill|pause|unpause`. **No arbitrary path or regex patterns.**
Method filtering is a **single global `POST` switch** — "When disabled, only GET and HEAD operations
are allowed, meaning any section of the API is read-only." There is **no per-section POST/DELETE
distinction and no `DELETE` variable at all.** No label filtering appears anywhere in the README;
HAProxy ACLs match method and path only, so request-body inspection and response filtering are
neither documented nor architecturally plausible here. No TLS/mTLS termination documented.

**linuxserver/docker-socket-proxy** — GPL-3.0, a maintained rebuild of Tecnativa's design, described
in its own repo metadata as a "drop-in replacement". Image `lscr.io/linuxserver/socket-proxy`. Latest
release **`3.4.4-r0-ls97`, 2026-09-07** — an automated CI package-rebuild tag on LinuxServer's usual
high cadence. Last commit 2026-09-07; **0 open PRs, 2 open issues**; 29 contributors; not archived;
created 2024-04-07. Same top-level env-var model as Tecnativa, with a larger `ALLOW_*` set
(`ALLOW_CHANGES`, `ALLOW_EXPORT`, `ALLOW_LOGS`, `ALLOW_TOP`, `ALLOW_ARCHIVE`, `ALLOW_PAUSE`,
`ALLOW_RESTARTS`, `ALLOW_START`, `ALLOW_STOP`, `ALLOW_UNPAUSE`) plus a full parallel `LIBPOD_*` set
for Podman's native API. `POST=0` is again a **global read-only switch**, with the documented
exception that the `ALLOW_*` action variables "work even when POST=0." **No label filtering, no body
inspection, no response filtering, no regex, no TLS.**
Source: <https://raw.githubusercontent.com/linuxserver/docker-socket-proxy/main/README.md>

**wollomatic/socket-proxy** — Go, zero-dependency binary, image `wollomatic/socket-proxy`. GitHub
reports license key `"other"` (SPDX `NOASSERTION`) — **read the repo's `LICENSE` file directly before
relying on the terms.** Latest release **1.13.1, 2026-08-15**; last commit 2026-08-18; 11
contributors; **18 open issues, 5 open PRs**; not archived; created 2023-09-16; 427 stars. Effectively
a single-maintainer project (`wollomatic` authors the releases) with Dependabot and occasional
outside PRs.
Rule model per <https://raw.githubusercontent.com/wollomatic/socket-proxy/main/README.md>: **arbitrary
regex path patterns rather than fixed sections**, expressed as `-allowMETHOD=<regexp>` (e.g.
`-allowGET=/v1\..{1,2}/(version|containers/.*|events.*)`), auto-anchored with `^`/`$`. Method
filtering is **per-rule**, not global — architecturally different from Tecnativa's single `POST`
switch, and the finest-grained method/path model among the mature options.

> **Important distinction on labels.** wollomatic *does* have a Docker-label feature — labels with
> prefix `socket-proxy.allow.<method>` (e.g. `socket-proxy.allow.get=.*`). But per the README, that
> label is placed **on the calling client container** (e.g. a Traefik container on the same network
> as the proxy) to grant *that caller* its own allowlist. It is a **per-client-identity** mechanism,
> **not** a way to restrict which *target* containers a request may act on. There is no feature to
> gate "only act on containers where label X=Y".

It does have a distinct **bind-mount source restriction** (`-allowbindmountfrom`) that inspects the
request body for `Binds` / `HostConfig.Mounts` bind-source paths (legacy and modern binds, local
volume-driver bind/rbind options) and rejects `VolumesFrom`. The README explicitly caveats that this
"is a request filter with the limitations documented above, **not a sandbox for untrusted Docker API
clients**." No response filtering. No TLS/mTLS section in the README (confirmed by direct text search
of the raw README).

**FoxxMD/docker-proxy-filter ("DPF")** — Rust, MIT, images `docker.io/foxxmd/docker-proxy-filter` and
`ghcr.io/foxxmd/docker-proxy-filter`. Latest release **0.1.2, 2026-06-11**; last commit 2026-06-11;
created **2025-10-06**; 63 stars; 2 contributors (effectively single-maintainer); 5 open issues, 2
open PRs; not archived.
**Architecture matters here:** per its README it is *not* a standalone socket proxy — "It does not
connect directly to the Docker socket: it [is] designed to be used with another Docker 'Socket Proxy'
container", i.e. it sits **downstream of** Tecnativa/linuxserver/wollomatic in a two-tier chain. It
listens on port 2375 and is a reverse proxy, not an AuthZ plugin.
**Label-scoped authorization: YES**, verified against
<https://raw.githubusercontent.com/FoxxMD/docker-proxy-filter/master/README.md>. `CONTAINER_LABELS` is
a comma-delimited list of label key/value substrings; any container whose labels match is "valid". It
filters the **List Containers** response (removing non-matching containers from the array) and makes
**all other per-container endpoints (inspect, start, stop, exec, ...) return HTTP 404** for
non-matching containers — a genuine request-time gate on target-container labels, not just a cosmetic
list filter. No request-body inspection or mutation beyond this.
This is the clearest verified "yes" in the survey, but it is young (created Oct 2025), small (63
stars, effectively one maintainer), and architecturally a filter bolted in front of another proxy.

**mikesir87/docker-socket-proxy** — JavaScript, Apache-2.0. Latest release **v1.3.1, 2026-02-06**;
last commit 2026-02-06 (**~7 months stale** as of 2026-09-10); created 2024-12-17; 9 stars, 1 fork;
single maintainer. Explicitly Kubernetes-admission-controller-inspired: the pipeline is Mutators to
Validators (Gates) to Docker socket to Response filters, configured via YAML
(`CONFIG_FILE`/`CONFIG_DATA`/`CONFIG_DIR`), no regex, exact-value matching on registries, namespaces,
paths and labels.
Per <https://raw.githubusercontent.com/mikesir87/docker-socket-proxy/main/README.md>:
**label-scoped authorization YES** — a "mount source gate" supports `label:requiredKey=requiredValue`
syntax (AND within a rule, OR across rules), and a response "label filter" can remove or require
items matching specified labels. **Request-body mutation YES** — mutators can remap volume/mount
paths, **inject labels onto created containers/images/networks/volumes**, add containers to networks,
and remap image names. This is the only surveyed project with genuine request-body mutation of that
breadth. **Response filtering YES**, via the same label filter.

**CodesWhat/sockguard** — Go, Apache-2.0, homepage <https://getsockguard.com>. Created **2026-03-22**,
with an unusually rapid cadence: v2.1.0 (2026-09-04), v2.2.0 (2026-09-06), v2.2.1 (2026-09-08) — new
releases every 1-2 days in the week before this research. Last commit 2026-09-08; 6 contributors; 1
open issue, 0 open PRs; **8 stars**; not archived. The repo root contains `CLAUDE.md` and `AGENTS.md`
(confirmed via GitHub code search), indicating active AI-agent-assisted development — flagged as a
provenance/maturity caveat, not a defect.
Rule model (**README claim, not independently source-verified**): YAML declarative first-match-wins
rules with method plus glob path matching (e.g. `match: {method: GET, path: "/containers/json"}`),
plus Tecnativa-compatible env vars for drop-in migration.
**Label-scoped authorization: YES, and code-backed** — the repo contains dedicated source paths
`app/internal/ownership/{commit.go, libpod.go, libpod_paths.go}` and
`app/internal/filter/{node.go, mutation.go, json_mutate.go, service.go}` (confirmed via GitHub code
search, not just README prose), consistent with the README claim that "a proxy instance can stamp
label-capable creates plus build-produced images with an owner label, auto-filter labeled
list/prune/events calls, and deny cross-owner access", plus "client-container label ACLs". Besides
FoxxMD's DPF, this is the only project where source-tree evidence of an ownership/label subsystem was
found rather than README marketing alone.
Further **README claims** (not source-verified): body inspection/mutation denying privileged and
host-bound workloads, non-allowlisted mounts/devices/commands, plus mandatory-label injection and
image-reference remapping; response redaction of env/mount/network/config/plugin/swarm-sensitive
metadata by default with "protected JSON mediation" for fields like `HostConfig.Mounts[].Source`; and
**mTLS** — "Non-loopback TCP listeners require mutual TLS by default", TLS 1.3 minimum, certificate
selectors (CN/DNS/IP/URI SAN/SPKI).
**Caveat, stated plainly:** its capability claims are the most thoroughly documented in the survey and
are partly source-corroborated, but with a March-2026 creation date, an 8-star install base and
release-every-two-days velocity, it has the least track record. Unproven at scale.

**knrdl/docker-socket-protector** — Go, MIT. Latest release **v2.4.3, 2025-11-29**; last commit
2026-06-23; created 2022-01-20; 37 stars, 3 forks, 1 open issue; not archived. Profile-file-based
whitelist rules compiled to regex. **Label-scoped authorization: NO** — the only "label" reference in
its README is to Traefik's own label-based routing-rule extraction, an unrelated feature of what it
proxies for, not a Docker-label ACL of its own. No TLS documented (listens on plain
`tcp://...:2375`). No documented body inspection or response filtering.

**twistlock/authz** — Apache-2.0 (per its README). Architecturally a **genuine Docker AuthZ plugin** —
the README instructs adding `--authorization-plugin=authz-broker` to the daemon flags. **Effectively
dead:** `pushed_at` 2020-06-11, a single tag (`0.1`), **no GitHub Releases at all**
(`/releases/latest` returns 404), 12 open issues, 1 open PR, and zero activity in 6+ years. It is
**not** marked `archived` in the GitHub API, and no archival banner or successor pointer was found in
its README. Twistlock was acquired by Palo Alto Networks and rebranded Prisma Cloud; **no actively
maintained PaloAltoNetworks fork of this repo was found — treat that as an open point, not a
confirmed negative, since forks were not exhaustively enumerated.**
**Label-scoped authorization: NO** — policies are defined by user/CommonName (from mTLS) plus
regex-matched Docker "actions" (method/path-derived action names); no container-label matching.

### 3.5 Confirmed non-matches and dead ends

- **No project named "docker-socket-hardener" exists** on GitHub (both WebSearch and
  `gh api search/repositories` returned nothing under that name).
- **caddy-docker-proxy** (<https://github.com/lucaslorentz/caddy-docker-proxy>) is **not** a
  Docker-API access-control proxy. It reads Docker labels to *generate Caddy reverse-proxy
  configuration* for routing HTTP traffic to containers; it does not sit in front of the Docker
  socket gating Engine API calls. Out of scope.
- **DataDog/docker-filter** (<https://github.com/DataDog/docker-filter>) carries
  "## Auto-archived due to inactivity ##" in its own GitHub description — dead.
- **vmfarms/docker-proxy-filter** (<https://github.com/vmfarms/docker-proxy-filter>) is a fork of
  FoxxMD's DPF ("adds exclude filter support"), Rust/MIT, created 2026-03-03, 0 stars and 0 forks —
  a personal fork, not independently notable.

### 3.6 Flagged as unverified in this area

- Rootless-mode documented limitations were not enumerated (fetch did not surface that section).
- The full endpoint-tag enumeration on the v1.56 Engine API reference page did not render in the
  fetch; only the page's existence and its OpenAPI links were confirmed.
- Whether a maintained PaloAltoNetworks successor to `twistlock/authz` exists — forks not exhaustively
  enumerated.
- sockguard's rule model, body inspection/mutation, response redaction and mTLS claims rest on its
  README; only the ownership/label subsystem was source-tree-corroborated.

---

## 4. OWASP Top 10 and OWASP ASVS (PRD 2.1)

### 4.1 OWASP Top 10 — current version

**Current released version: OWASP Top 10:2025**, the **8th installment**. The project page states
plainly: "The most current released version is the [OWASP Top Ten 2025]"
(<https://github.com/OWASP/www-project-top-ten/blob/master/index.md>, rendered at
<https://owasp.org/www-project-top-ten/>).

**Release date / RC status — verified via git history rather than a release announcement.** The
`OWASP/Top10` repository publishes **no GitHub Releases** and carries only 2017-era tags, so there is
no release artifact to date. The 2025 edition was staged as a release candidate and the "RC" label was
removed from the document title in commit **"Removed RC from ttitle" dated 2025-12-24T21:13:39Z**,
immediately after "updated for 2025" (2025-12-24T19:01:00Z). Earlier commits show the 2025/2021 merge
work in November 2025 and an A09 rename on 2025-12-13.
Source: `gh api "repos/OWASP/Top10/commits?path=2025/docs/en/index.md"`.
**Note:** some search-engine result titles still render `owasp.org/Top10/` as "OWASP Top 10:2025 RC1";
the live page content no longer says RC, and the repo commit above marks the transition. Treat
**2025-12-24** as the date the RC designation was dropped, not as an officially announced GA date —
**no formal GA announcement page was located.**

**The ten categories** (verbatim from
<https://github.com/OWASP/Top10/blob/master/2025/docs/en/index.md>):

| ID | Category |
|---|---|
| A01:2025 | Broken Access Control |
| A02:2025 | Security Misconfiguration |
| A03:2025 | Software Supply Chain Failures |
| A04:2025 | Cryptographic Failures |
| A05:2025 | Injection |
| A06:2025 | Insecure Design |
| A07:2025 | Authentication Failures |
| A08:2025 | Software or Data Integrity Failures |
| A09:2025 | Security Logging & Alerting Failures |
| A10:2025 | Mishandling of Exceptional Conditions |

**What changed from 2021** (from
<https://github.com/OWASP/Top10/blob/master/2025/docs/en/0x00_2025-Introduction.md>): two new
categories and one consolidation.

- **A01 Broken Access Control** stays at #1; **SSRF has been rolled into this category**. 40 CWEs;
  3.73% of tested applications had at least one.
- **A02 Security Misconfiguration** moved up from #5 (2021) to #2. 16 CWEs; 3.00% prevalence.
- **A03 Software Supply Chain Failures** is **new** — an expansion of A06:2021 Vulnerable and Outdated
  Components "to include a broader scope of compromises occurring within or across the entire
  ecosystem of software dependencies, build systems, and distribution infrastructure." Overwhelmingly
  voted a top concern in the community survey. 5 CWEs, limited data presence, **but the highest
  average exploit and impact scores from CVEs.**
- **A04 Cryptographic Failures** fell from #2 to #4. 32 CWEs; 3.80% prevalence.
- **A05 Injection** fell from #3 to #5. 38 CWEs; the most-tested category with the greatest number of
  associated CVEs.
- **A06 Insecure Design** slid from #4 to #6.
- **A07 Authentication Failures** stays at #7, **renamed** from "Identification and Authentication
  Failures". 36 CWEs.
- **A08 Software or Data Integrity Failures** stays at #8, focused on "the failure to maintain trust
  boundaries and verify the integrity of software, code, and data artifacts at a lower level than
  Software Supply Chain Failures."
- **A09 Security Logging & Alerting Failures** stays at #9, **renamed** from "Security Logging and
  Monitoring Failures" to emphasise alerting: "Great logging with no alerting is of minimal value in
  identifying security incidents." 5 CWEs.
- **A10 Mishandling of Exceptional Conditions** is **new** — 24 CWEs covering improper error handling,
  logical errors, failing open, and related abnormal-condition scenarios.

Methodology note from the same source: the 2025 edition analysed **589 CWEs** (up from ~400 in 2021
and ~30 in 2017), ranked 12 categories from contributed data and allowed two to be promoted by the
community survey, capped categories at 40 CWEs each, and covers **248 CWEs across the 10 categories**.

### 4.2 OWASP ASVS — current version

**Current released version: ASVS 5.0.0, released 2025-05-30** — "The initial release of the 5.x
version of ASVS," with extensive changes from the 4.x line. Prior releases: 4.0.3 (2023-10-28), 4.0.2
(2021-10-28), 4.0.1 (2020-03-03). A "Bleeding Edge" rolling build is auto-regenerated (most recently
2026-09-03) and is explicitly "undergoing constant changes and cannot be relied upon for stability."
Source: <https://github.com/OWASP/ASVS/releases>

**Chapter structure — 17 chapters in 5.0** (from the repository's own file layout at
<https://github.com/OWASP/ASVS/tree/master/5.0/en>):

| # | Chapter |
|---|---|
| V1 | Encoding and Sanitization |
| V2 | Validation and Business Logic |
| V3 | Web Frontend Security |
| V4 | API and Web Service |
| V5 | File Handling |
| V6 | Authentication |
| V7 | Session Management |
| V8 | Authorization |
| V9 | Self-contained Tokens |
| V10 | OAuth and OIDC |
| V11 | Cryptography |
| V12 | Secure Communication |
| V13 | Configuration |
| V14 | Data Protection |
| V15 | Secure Coding and Architecture |
| V16 | Security Logging and Error Handling |
| V17 | WebRTC |

Plus appendices: A Glossary, B References, C Cryptography, D Recommendations, E Contributors. This is
a substantial restructure relative to 4.0's 14 chapters — notably OAuth/OIDC (V10) and self-contained
tokens (V9) are now their own chapters, and encoding/sanitization is promoted to V1.

**Level model — still three levels, but redefined as priority-based rather than risk-tier-based.**
From <https://github.com/OWASP/ASVS/blob/master/5.0/en/0x03-What-is-the-ASVS.md>:

- "The ASVS defines three security verification levels, with each level increasing in depth and
  complexity... Levels may be presented as L1, L2, and L3."
- "Levels are defined by **priority-based evaluation** of each requirement based on experience
  implementing and testing security requirements. The main focus is on comparing risk reduction with
  the effort to implement the requirement."
- **L1** — "the minimum requirements to consider when securing an application and represents a
  critical starting point... contains around **20%** of the ASVS requirements." Generally critical
  first-layer-of-defence controls against common attacks. "Level 1 is not necessarily penetration
  testable by an external tester without internal access to documentation or code."
- **L2** — "Most applications should be striving to achieve this level of security. Around **50%** of
  the requirements in the ASVS are L2 meaning that an application needs to implement around **70%** of
  the requirements in the ASVS (all of the L1 and L2 requirements) in order to comply with L2."
- **L3** — "the goal for applications looking to demonstrate the highest levels of security and
  provides the final **~30%** of requirements." Generally defence-in-depth or hard-to-implement
  controls.
- "Rather than the ASVS prescriptively stating what level an application should be at, an organization
  should analyze its risks and decide what level it believes it should be at."
- ASVS 5.0 also introduces **documentation requirements** — always in the first section of a chapter,
  always paired with a related implementation requirement. Rather than "sweeping statements like 'all
  data must be encrypted'", these mandate that the developer's approach and configuration be
  documented so it can be reviewed for appropriateness and compared against the implementation.
- ASVS "only contains requirements (must) and does not contain recommendations (should) as the main
  condition."

### 4.3 Which ASVS 5.0 chapters bind on a control-plane-plus-agent architecture

This maps structural applicability against the architecture described in the PRD (web control plane
with cookie sessions, OIDC federation and local auth with MFA; multi-tenant SaaS plus self-hosted;
deny-by-default RBAC; remote agents on a persistent bidirectional SignalR/WebSocket connection;
agents driving the Docker Engine API; application-layer secret encryption with keys held separately;
audit logging; RCON console and live log streaming; backups with integrity verification;
supply-chain controls). Chapter descriptions are grounded in ASVS 5.0's own chapter set at
<https://github.com/OWASP/ASVS/tree/master/5.0/en>.

**Binds directly:**

| Chapter | Why it applies structurally |
|---|---|
| **V6 Authentication** | Local username/password auth, MFA, recovery codes, account lockout, and the credential lifecycle are all in-product. |
| **V7 Session Management** | Browser sessions over HttpOnly cookies; session fixation, timeout, and termination are product responsibilities. |
| **V8 Authorization** | Deny-by-default RBAC, tenant scoping, and resource-level permissions on servers and agents. |
| **V10 OAuth and OIDC** | Hosted SaaS federates to an OIDC provider (Auth0 Organizations); this chapter did not exist as its own unit in 4.0. |
| **V9 Self-contained Tokens** | Applies to any JWT/access token accepted from the IdP or minted for agent enrolment. |
| **V11 Cryptography** | Application-layer authenticated encryption of secrets, key separation, and key lifecycle. |
| **V12 Secure Communication** | TLS termination at Caddy, the control-plane/agent channel, and the agent/Docker channel. |
| **V13 Configuration** | Secure defaults, container hardening (PRD 24), network segmentation (PRD 26), and — per A02:2025 — the single largest mover in the Top 10. |
| **V14 Data Protection** | Secret storage, sensitive-data-in-logs prohibition, and privacy-aware diagnostics. |
| **V15 Secure Coding and Architecture** | Explicit trust boundaries, privilege separation (PRD 2.5), and third-party/dependency posture — the chapter that carries supply-chain-adjacent requirements. |
| **V16 Security Logging and Error Handling** | Audit logging of security and administrative actions, plus the error-handling surface that A10:2025 newly emphasises. |
| **V4 API and Web Service** | The control plane exposes an HTTP API and the agent protocol rides over it. |
| **V2 Validation and Business Logic** | Command safety (PRD 19), configuration revisions, and operation concurrency are business-logic integrity concerns. |
| **V1 Encoding and Sanitization** | Live log streaming and RCON output are untrusted text rendered into a browser; game-server config values are rendered too. |
| **V3 Web Frontend Security** | The operator UI: CSP, CSRF on cookie-authenticated state changes, clickjacking. |

**Binds conditionally / narrowly:**

- **V5 File Handling** — applies to mod uploads, backup archives, support-package generation and any
  operator-supplied config files. Not applicable to parts of the system that never accept files.
- **V17 WebRTC** — **does not apply**; nothing in the architecture uses WebRTC. SignalR/WebSocket
  traffic is covered by V4/V12, not V17.

**Explicit scope exclusion worth recording.** ASVS 5.0 states its own scope narrowly, and two of the
PRD's concerns fall partly outside it
(<https://github.com/OWASP/ASVS/blob/master/5.0/en/0x03-What-is-the-ASVS.md>):

- Verbatim: *"if an external process interacts with the application or its data, it is considered out
  of scope for ASVS. For instance, **backing up the application or its data is usually the
  responsibility of an external process and is not controlled by the application or its developers**."*
  So PRD 42-44 (backups and backup integrity) are **not** governed by ASVS requirements in the way
  the other areas are — though ASVS does govern the *keys and secrets* those backups contain via V11
  and V14.
- Verbatim: *"ASVS does not prescribe development lifecycle activities or dictate how the application
  should be built via a CI/CD pipeline; instead, it specifies the security outcomes that must be
  achieved within the product itself."* So PRD 54 (CI/CD) and much of PRD 55 (supply-chain
  release engineering — SBOM, signing, provenance) sit **outside ASVS's declared scope**, even though
  **A03:2025 Software Supply Chain Failures** covers them squarely in the Top 10. These two standards
  therefore do not overlap on supply chain: the Top 10 flags it as the #3 risk; ASVS deliberately
  declines to specify it.
- Verbatim: *"Components that serve, modify, or validate HTTP traffic, such as Web Application
  Firewalls (WAFs), load balancers, or proxies, may be considered part of the application for those
  specific purposes."* This is the hook that pulls **Caddy** (PRD 45/46) and a **Docker socket proxy**
  (PRD 27) into ASVS scope for cached responses, rate limiting, and restricting inbound/outbound
  connections by source and destination.

### 4.4 Which OWASP Top 10:2025 categories bind

| Category | Structural applicability |
|---|---|
| **A01 Broken Access Control** | The dominant risk: multi-tenant RBAC, per-server permissions, agent-scoped container ownership. **SSRF now lives here**, and it is directly relevant — the control plane and agents make outbound calls (SteamCMD, mod fetches, backup destinations, webhooks). |
| **A02 Security Misconfiguration** | Container hardening, network segmentation, Docker socket exposure, Caddy TLS configuration, secure defaults. |
| **A03 Software Supply Chain Failures** | PRD 55 directly: NuGet locking, vulnerability and secret scanning, container scanning, SBOM, image digests, checksums, signing, provenance. **Not covered by ASVS** (see above). |
| **A04 Cryptographic Failures** | PRD 10: authenticated encryption of secrets, key separation, password hashing, TLS. |
| **A05 Injection** | Command safety for RCON and container commands; SQL via EF Core; log/console output rendered to the browser. |
| **A06 Insecure Design** | Privilege separation, explicit trust boundaries, the agent-never-a-generic-Docker-admin-interface constraint. |
| **A07 Authentication Failures** | Local auth, MFA, recovery codes, lockout, agent enrolment and agent identity. |
| **A08 Software or Data Integrity Failures** | Backup integrity verification, configuration revisions, pinned image digests, deserialization of agent protocol messages. |
| **A09 Security Logging & Alerting Failures** | Audit logging plus the **alerting** emphasis new to the 2025 rename; health model and heartbeat detection are the alerting surface. |
| **A10 Mishandling of Exceptional Conditions** | New in 2025 and directly relevant to an operations control plane: durable operations engine, agent reconnection, failing open vs closed on agent disconnect, error handling in long-running container operations. |

All ten categories bind on this architecture. None is structurally inapplicable.

### 4.5 On-topic OWASP Cheat Sheets

All confirmed to exist in the index at <https://cheatsheetseries.owasp.org/> (121 cheat sheets total).
Exact titles:

Authentication Cheat Sheet / Authorization Cheat Sheet / Multifactor Authentication Cheat Sheet /
Password Storage Cheat Sheet / Secrets Management Cheat Sheet / Cryptographic Storage Cheat Sheet /
Key Management Cheat Sheet / Session Management Cheat Sheet / REST Security Cheat Sheet /
**WebSocket Security Cheat Sheet** / Docker Security Cheat Sheet / Kubernetes Security Cheat Sheet /
Transport Layer Security Cheat Sheet / Logging Cheat Sheet / **Multi Tenant Security Cheat Sheet** /
Software Supply Chain Security Cheat Sheet

Every cheat sheet looked for was found — including the two whose existence was uncertain (WebSocket
Security and Multi Tenant Security). ASVS 5.0 explicitly defers implementation guidance to this
series: "developers may have a question, 'how do I implement a particular requirement in my
particular technology or environment,' and this should be covered by the Cheat Sheet Series project."

---

## 5. .NET 10 authenticated encryption and password hashing (PRD 10)

### 5.1 .NET 10 release facts

- **GA 2025-11-11**, an **LTS** release, **end of support 2028-11-14** (3-year LTS window).
  Sources: <https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core>,
  <https://github.com/dotnet/core/blob/main/release-notes/10.0/README.md>
- Latest servicing release **10.0.12, released 2026-09-08** per those same sources. *(Read from the
  rendered support/release tables rather than a raw `releases.json` diff.)*

### 5.2 Is AES-256-GCM still the recommendation?

**Yes — no primary source says otherwise.** No Microsoft Learn page was found stating AES-256-GCM is
deprecated or no longer recommended. The cross-platform cryptography page
(<https://learn.microsoft.com/en-us/dotnet/standard/security/cross-platform-cryptography>) and the
`AesGcm` API page continue to present AES-GCM and ChaCha20Poly1305 side by side as the current
supported AEAD choices. **The only documented shift is the SYSLIB0053 push toward being explicit
about tag size — not a shift away from GCM itself.**

**But note the ASP.NET Core Data Protection default is *not* GCM.** Verbatim from
<https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/configuration/overview?view=aspnetcore-10.0>:
*"The default EncryptionAlgorithm is AES-256-CBC, and the default ValidationAlgorithm is
HMACSHA256."* The payload-format page
(<https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/implementation/authenticated-encryption-details>)
confirms the concrete default payload: 128-bit key modifier + 128-bit IV + AES-256-CBC output +
HMACSHA256 tag. GCM is reachable only through manual/custom configuration — the docs show a
`CngGcmAuthenticatedEncryptorConfiguration` path (`EncryptionAlgorithm = "AES"` string plus
`EncryptionAlgorithmKeySize = 256`), and the exact literal name of an `EncryptionAlgorithm.AES_256_GCM`
enum member **could not be confirmed** from the fetched text. **Flagged as unverified.**

### 5.3 `AesGcm` API surface on .NET 10

From <https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.aesgcm?view=net-10.0>
and the runtime source.

- **Constructors:** `AesGcm(byte[])`, `AesGcm(ReadOnlySpan<byte>)`,
  `AesGcm(byte[], int tagSizeInBytes)`, `AesGcm(ReadOnlySpan<byte>, int tagSizeInBytes)`. The
  `tagSizeInBytes` overloads were added in **.NET 8**.
- **The two constructors without `tagSizeInBytes` are `[Obsolete]`**, diagnostic ID **SYSLIB0053**,
  message verbatim: *"AesGcm should indicate the required tag size for encryption and decryption. Use
  a constructor that accepts the tag size."* Confirmed from the attribute itself:
  `[System.Obsolete("...", DiagnosticId="SYSLIB0053", UrlFormat="https://aka.ms/dotnet-warnings/{0}")]`.
  Source: <https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.aesgcm.-ctor?view=net-10.0>
- **`NonceByteSizes` = 12 bytes (96 bits) only.**
  <https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.aesgcm.noncebytesizes?view=net-10.0>
- **`TagByteSizes` = 12, 13, 14, 15 or 16 bytes (96-128 bits).**
  <https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.aesgcm.tagbytesizes?view=net-10.0>
- `Encrypt`/`Decrypt` take `(nonce, plaintext/ciphertext, ciphertext/plaintext, tag,
  associatedData = default)` in both `byte[]` and `Span`/`ReadOnlySpan` forms.
- `IsSupported` (static bool) — "Gets a value that indicates whether the algorithm is supported on the
  current platform."
- `AesGcm` implements **`IDisposable`**.

### 5.4 Documented AesGcm caveats

**(a) Nonce reuse.** Verbatim from the `Encrypt` remarks: *"The security guarantees of the AES-GCM
algorithm mode require that the same nonce value is never used twice with the same key."*
<https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.aesgcm.encrypt?view=net-10.0>
Microsoft states the **requirement** but **does not state the consequence** — there is no "this leaks
the authentication key / reveals plaintext XOR" wording in the docs.

**(b) Tag size.** .NET supports 12-16-byte tags. The `tagSizeInBytes` constructor remarks say:
*"Indicating the required tag size prevents issues where callers of Decrypt may supply a tag as input
and that input is truncated to an unexpected size."* This steers callers toward explicit,
non-truncated tag sizes, but **no Learn text was found mandating 16 bytes / 128 bits** — the
obsoletion is about being *explicit*, not about a specific size. Separately, **on Apple platforms the
tag size is fixed at 16 bytes** due to a CryptoKit limitation.

**(c) Per-key invocation limit.** NIST SP 800-38D section 8.3 states that when the 96-bit IV is
generated by the random construction, the total number of invocations of the authenticated encryption
function with a given key **shall not exceed 2^32**.
<https://nvlpubs.nist.gov/nistpubs/legacy/sp/nistspecialpublication800-38d.pdf>
**No Microsoft Learn page or dotnet/runtime doc was found that mentions this 2^32 limit anywhere.**
This is a confirmed absence in Microsoft's documentation, not a failed search — meaning a caller who
follows only the .NET docs will not learn of the key-rotation obligation that random-nonce GCM
imposes.

**(d) Platform support gaps.** From the authoritative table at
<https://learn.microsoft.com/en-us/dotnet/standard/security/cross-platform-cryptography>:
AES-GCM is supported on Windows, Linux, macOS and Android; **partial on iOS / tvOS / MacCatalyst**
(added in .NET 9 for iOS/tvOS 13.0+ and all MacCatalyst versions, with tag size fixed at 16 bytes);
and **unsupported on Browser/WASM**, where `IsSupported` returns false. The class carries
`[UnsupportedOSPlatform("browser")]`, `[UnsupportedOSPlatform("ios")]` / `[UnsupportedOSPlatform("tvos")]`
alongside `[SupportedOSPlatform("ios13.0")]` / `[SupportedOSPlatform("tvos13.0")]`. **No FIPS-mode
caveat is documented for `AesGcm` on that page.**

### 5.5 ChaCha20Poly1305

<https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.chacha20poly1305?view=net-10.0>
Available since .NET 6, `IDisposable`, constructors `(byte[])` / `(ReadOnlySpan<byte>)` with **no
tag-size constructor** because all sizes are fixed: always a 256-bit key, 96-bit (12-byte) nonce, and
128-bit (16-byte) tag. `IsSupported` is a static bool. Platform support: Linux (OpenSSL 1.1.0+),
macOS, Windows 10 Build 20142+, Android API 28+; partial on iOS/tvOS/MacCatalyst (13.0+, since .NET
9); **unsupported on Browser**.
**No Microsoft text was found positioning ChaCha20Poly1305 as preferred or discouraged relative to
`AesGcm`** — neither API page nor the cross-platform page makes a "we recommend X over Y" statement;
both are simply listed as available AEAD options.

### 5.6 .NET 10 cryptography changes

- **Post-quantum algorithms land in .NET 10**: ML-KEM (FIPS 203), ML-DSA (FIPS 204), SLH-DSA (FIPS
  205) and Composite ML-DSA, per
  <https://learn.microsoft.com/en-us/dotnet/standard/security/cross-platform-cryptography>. Support is
  currently narrow — mostly Linux with OpenSSL 3.5.0+, or Windows 11 Insider builds; essentially
  unsupported on Apple, Android and Browser today.
- **KMAC-128/256/XOF was added in .NET 9, not .NET 10** (same source) — correcting a common
  secondary-source conflation.
- **Obsoletions** (<https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/10.0/obsolete-apis>):
  **SYSLIB0060** — **all `Rfc2898DeriveBytes` constructors are obsolete in .NET 10**, with the
  recommended replacement being the static one-shot `Rfc2898DeriveBytes.Pbkdf2(...)` family. Also
  **SYSLIB0058** (SslStream algorithm-name properties obsolete; use `NegotiatedCipherSuite`).
  SYSLIB0053 (the AesGcm constructor) was introduced in .NET 8 and continues to apply.
- **macOS:** OpenSSL-backed interop types (`RSAOpenSsl`, `ECDsaOpenSsl`, `ECDiffieHellmanOpenSsl`,
  `DSAOpenSsl`, and `AesCcm` via OpenSSL) had their macOS OpenSSL-dylib-loading support **removed in
  .NET 10**.

### 5.7 Key derivation

- **`Rfc2898DeriveBytes`** — every constructor is obsolete as of .NET 10 (SYSLIB0060). Use the static
  `Rfc2898DeriveBytes.Pbkdf2(...)` one-shot methods.
  <https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.rfc2898derivebytes?view=net-10.0>
  (`CryptDeriveKey` has been separately obsolete since .NET 6.)
- **`HKDF`** — a static class implementing RFC 5869, with `Extract`, `Expand` and `DeriveKey` methods
  in `byte[]` and `Span` overloads. Carries `[UnsupportedOSPlatform("browser")]`.
  <https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.hkdf?view=net-10.0>

### 5.8 Password hashing — what ASP.NET Core Identity actually does

Verified against source, not docs:
<https://github.com/dotnet/aspnetcore/blob/main/src/Identity/Extensions.Core/src/PasswordHasher.cs>
and
<https://github.com/dotnet/aspnetcore/blob/main/src/Identity/Extensions.Core/src/PasswordHasherOptions.cs>

- **Algorithm: PBKDF2 with HMAC-SHA512** for the current default format ("Version 3"). Confirmed by
  the in-file comment — `// Version 3: PBKDF2 with HMAC-SHA512, 128-bit salt, 256-bit subkey, 100000
  iterations.` — and by `HashPasswordV3` passing `prf: KeyDerivationPrf.HMACSHA512`.
- **Default iteration count: 100,000.** Exact source line:
  <https://github.com/dotnet/aspnetcore/blob/b56bb17db3ae73ce5a8664a2023a9b9af89499dd/src/Identity/Extensions.Core/src/PasswordHasherOptions.cs#L33>
  gives `public int IterationCount { get; set; } = 100_000;`
- **Salt: 128 bits (16 bytes). Subkey / derived key: 256 bits (32 bytes).**
- **Version marker byte:** `0x00` = "Version 2" (legacy: PBKDF2-HMACSHA1, 1,000 iterations, 128-bit
  salt, 256-bit subkey — matching ASP.NET Identity v1/v2); `0x01` = "Version 3", format
  `{0x01, prf(UInt32), iterCount(UInt32), saltLength(UInt32), salt, subkey}`, all UInt32s
  **big-endian**.
- **`PasswordHasherCompatibilityMode`** — `IdentityV2` uses the legacy `0x00` format unconditionally;
  `IdentityV3` (the default) uses the `0x01` PBKDF2-HMACSHA512 format and returns
  `SuccessRehashNeeded` if a stored hash's embedded iteration count is below the configured
  `_iterCount`, or if its embedded PRF is HMACSHA1/HMACSHA256 rather than HMACSHA512.
  <https://github.com/dotnet/aspnetcore/blob/main/src/Identity/Extensions.Core/src/PasswordHasherCompatibilityMode.cs>

**Argon2 / bcrypt:** ASP.NET Core Identity has **not** adopted either in any built-in form —
`PasswordHasher<TUser>` remains exclusively PBKDF2-based per the source above. **The .NET BCL has no
built-in Argon2 implementation as of .NET 10.** This is a stated policy consequence: .NET's crypto
policy requires OS-library-backed implementations on at least two platforms, and currently only
OpenSSL implements Argon2 (not Windows CNG, not Apple CryptoKit). See the long-standing open issue
<https://github.com/dotnet/runtime/issues/19933> and the discussion
<https://github.com/dotnet/runtime/discussions/117822>. Third-party NuGet packages exist (e.g.
`Konscious.Security.Cryptography.Argon2`) but are **not** part of the BCL.

### 5.9 OWASP Password Storage Cheat Sheet — current published parameters

<https://cheatsheetseries.owasp.org/cheatsheets/Password_Storage_Cheat_Sheet.html>

**Preference ordering:** **Argon2id** first, then **scrypt** if Argon2id is unavailable, then
**bcrypt** only "for legacy systems where Argon2 and scrypt are not available", then **PBKDF2** when
FIPS-140 compliance is required.

**Argon2id** — five equivalent configurations trading memory against iterations:

| Memory | Iterations | Parallelism |
|---|---|---|
| 47,104 KiB (46 MiB) | 1 | 1 |
| 19,456 KiB (19 MiB) | 2 | 1 |
| 12,288 KiB (12 MiB) | 3 | 1 |
| 9,216 KiB (9 MiB) | 4 | 1 |
| 7,168 KiB (7 MiB) | 5 | 1 |

**scrypt** — five tiers from `N=2^17` (128 MiB), `r=8`, `p=1` down to `N=2^13` (8 MiB), `r=8`, `p=10`.

**bcrypt** — work factor "as large as verification server performance will allow, with a minimum of
10". Input is capped at **72 bytes**; the cheat sheet recommends enforcing that limit and **explicitly
cautions against pre-hashing**, citing null-byte and "password shucking" vulnerabilities.

**PBKDF2** — verified verbatim by direct fetch:

| PRF | Iterations |
|---|---|
| PBKDF2-HMAC-SHA256 | **600,000** (recommended) |
| PBKDF2-HMAC-SHA512 | **220,000** |
| PBKDF2-HMAC-SHA1 | 1,400,000 — **legacy only, do not select for new systems** |

*(The 220,000 figure for SHA-512 was double-checked with a second direct fetch of the live page,
because it differs from the 210,000 figure that circulates in secondary sources. **220,000 is what
the page currently prints.**)*

**Password length:** broad Unicode support without arbitrary restriction beyond what the hash function
requires.

**Comparison to what Identity ships:** ASP.NET Core Identity's default is PBKDF2-HMAC-SHA512 at
**100,000** iterations; the cheat sheet's current figure for that PRF is **220,000** — i.e. the
framework default is below the current OWASP figure by a factor of ~2.2. Both numbers are cited above;
no inference is drawn here.

### 5.10 NIST SP 800-63B — current revision

**Current published revision: SP 800-63-4**, finalised and released around **July/August 2025**,
superseding 800-63-3 as of **2025-08-01**. Sources: <https://pages.nist.gov/800-63-4/sp800-63.html>,
PDF <https://nvlpubs.nist.gov/nistpubs/SpecialPublications/NIST.SP.800-63-4.pdf>

Quoted verbatim from <https://pages.nist.gov/800-63-4/sp800-63b.html>:

- **Minimum length:** *"SHALL require passwords... to be a minimum of 15 characters in length"* for
  single-factor use; *"SHALL require them to be a minimum of eight characters"* when used only as one
  factor of MFA.
- **Maximum length:** *"SHOULD permit a maximum password length of at least 64 characters."*
- **Salting / hashing:** salted per SP 800-132-approved hashing schemes; *"salt SHALL be at least 32
  bits in length."*
- **Composition rules:** *"SHALL NOT impose other composition rules."*
- **Rotation:** *"SHALL NOT require subscribers to change passwords periodically."*
- **Blocklist:** *"SHALL compare the prospective secret against a blocklist that contains known
  commonly used, expected, or compromised passwords."*

**Flagged:** the substantive SHALL/SHOULD text above was pulled directly from the primary document and
was consistent across two lookups, but the **exact section number is uncertain** — one pass cited
section 3.1.1.2 and another 5.1.1/5.1.1.1. Cite the requirement text, not the section number, until
verified against the PDF pagination.

### 5.11 Flagged as unverified in this area

- The literal enum member name for AES-256-GCM in ASP.NET Core Data Protection
  (`EncryptionAlgorithm.AES_256_GCM`) — only the custom CNG-GCM configuration path was visible.
- The exact `10.0.12` patch digit — read from rendered support/release tables, not a raw
  `releases.json` diff.
- NIST SP 800-63-4 section numbering for the memorised-secret requirements.
- **Confirmed absence, not a gap:** NIST SP 800-38D's 2^32 per-key GCM invocation limit is nowhere in
  Microsoft's `AesGcm` or cryptography documentation.

---

## 6. Auth0 Organizations (PRD 11, hosted SaaS)

All fetched **2026-09-10**. Pricing and plan matrices change without notice; re-verify before relying
on any figure here.

### 6.1 What Organizations is

Per <https://auth0.com/docs/manage-users/organizations>, Organizations lets B2B customers "manage
their partners and customers" within a single Auth0 tenant, supporting per-organization branding,
connections, members and roles. The documented capabilities are: representing and managing business
customers and partners; configuring branded, federated login flows per organization; providing
machine-to-machine API access; and building self-service administration tools. Subpages:
Understand How Auth0 Organizations Work, Create Your First Organization, Custom Development, Work
with Tokens, Configure Organizations, Machine-to-Machine Access to Organizations.

**Plan requirement is not stated in the docs** — the page says feature availability depends on "your
Auth0 plan or custom agreement" and directs readers to the pricing page. See section 6.4.

### 6.2 Login flows

Per <https://auth0.com/docs/manage-users/organizations/login-flows-for-organizations>:

**Application user-type setting** (exact option names):
- **"Individuals"** — users log in directly without Organization context (consumer apps).
- **"Business Users"** — users access the app only within an Organization context. Users in multiple
  organizations get an Organization Picker showing **"the first 20 organizations they joined"**.
- **"Both"** — supports personal and business accounts simultaneously.

**Login flow type** (chosen after Business Users or Both):
- **"Prompt for Credentials"** — the default, often paired with Identifier First Authentication. Users
  see the application's login prompt and authenticate via enabled connections.
- **"Prompt for Organization"** — users identify their organization "by either the Organization Name
  or Organization Email" before proceeding. With user type "Both", both organization and personal
  account prompts are shown.
- **"No Prompt"** — used when the organization is already known, enabling "branded and customized
  login flows" through custom development (i.e. passing the organization in the authorize request).

**Identifier First Authentication / Home Realm Discovery** — determines the correct identity provider
from the user's email domain. With HRD active, "Home Realm Discovery detects email addresses from a
known domain and automatically sends them to the proper Workforce login."

**Organization Domain Discovery** — optional; helps "detect a user's organization automatically or
narrow organization options when users enter their email address." A single verified-domain match is
selected automatically; multiple matches trigger an organization selector.

**Auto-Membership** — grants access to users authenticated via federated IdPs without an explicit
invitation, activated through the **`assign_membership_on_login`** parameter, and typically paired
with organization-specific login prompts.

### 6.3 Token claims, membership, connections, M2M

**Token claims** — <https://auth0.com/docs/manage-users/organizations/using-tokens>:
- **`org_id`** is present in both **ID tokens and access tokens by default**, included automatically
  when a user authenticates through an organization.
- **`org_name`** is **not included by default**; organizations can be configured to include the
  organization name alongside `org_id`. Machine-to-machine tokens carry both in the documented
  example.
- Validation guidance, quoted: applications should validate that a received `org_id` "correspond[s] to
  an entity your application trusts, such as a paying customer"; when `org_name` is present, validate
  it alongside `org_id`. For APIs: segment data access by `org_id` so "only information about a given
  Organization can be accessed or modified."

**Invitations** —
<https://auth0.com/docs/manage-users/organizations/configure-organizations/invite-members> and the
Management API reference <https://auth0.com/docs/api/management/v2/organizations/post-invitations>:
- Invitations exist to add members who don't yet exist in the data store or aren't yet organization
  members. The invitee receives an email with a link to create an account or log in and join.
- **`ttl_sec`: default 604,800 seconds (7 days)** if unspecified or set to 0; **maximum 2,592,000
  seconds (30 days)**; range 0-2,592,000.
- Body parameters: `inviter` (required), `invitee` (required), `client_id` (required — "Used to
  resolve the application's login initiation endpoint"), `connection_id` (optional — "The id of the
  connection to force invitee to authenticate with"), `app_metadata`, `user_metadata`, `roles` (array
  of role IDs, min 1 item if provided), `send_invitation_email` (default true).
- Expired invitations are "marked as expired and eventually deleted from the Auth0 Dashboard." **The
  docs page does not state a limit on pending invitations.**
- Constraint, quoted: "Auth0 can send user invitations only via email," though invitation URLs can be
  generated and distributed by other channels. **Invited users must log in with the email address
  matching the invitation recipient.**
- **Organization member roles are separate from tenant-level RBAC roles** — quoted: "Organization
  member roles are separate from the roles assigned to users through Auth0's Role-Based Access
  Control." Predefined roles attached to an invitation apply specifically when the user logs in via
  that organization.

**Connections** —
<https://auth0.com/docs/manage-users/organizations/configure-organizations/enable-connections>:
- "Supported connections include database connections, social connections, and enterprise
  connections."
- **`assign_membership_on_login`** — "automatically assigns Organization membership to end-users the
  first time they authenticate with the connection."
- **`show_as_button`** — enterprise connections only; controls "whether or not a specific connection
  displays as an option on the Organization login prompt." When disabled, users can still authenticate
  by passing the connection parameter directly in the authorization request.
- Connection-type restrictions: **database connections only** support the Organization Signup property
  (which requires `assign_membership_on_login` first); **enterprise connections only** support
  `show_as_button`; all types support Organization Connection Name and Admin Access Level.
- Documented failure mode, quoted: "If all enabled connections within the Organization are enterprise
  connections, and all connections are hidden, Auth0 returns an error."
- **Unverified:** the page does not explicitly state whether one connection can be enabled across
  multiple organizations. Configuration is performed per-organization (Dashboard or Management API),
  which implies reuse is possible, but **that is an inference, not a documented statement.**

**Machine-to-machine** — a dedicated docs subpage ("Machine-to-Machine Access to Organizations")
exists, and the entity-limit policy publishes an **M2M Client Grants per Organization** limit (100
public cloud / 1,000 private cloud), confirming M2M organization context is a supported first-class
feature.

**SCIM** — published on the pricing matrix as **"Included" on every plan tier, Free through
Enterprise, in both the B2C and B2B families** (see section 6.4).

### 6.4 Published entity limits

From <https://auth0.com/docs/troubleshoot/customer-support/operational-policies/entity-limit-policy>:

| Entity | Default | Max on request |
|---|---|---|
| **Organizations per tenant** | **100,000** | 2,000,000 (public cloud) |
| **Organization members per organization** | **100,000** | 2,000,000 (public cloud) |
| Connections per organization | 10 | — |
| Role assignments per organization member | 50 | — |
| Role assignments per enterprise group | 10 | — |
| Unique roles assignable to enterprise groups | 100 | — |
| Discovery domains per organization | 100 | — |
| Enterprise groups with assignable roles | 10,000 | — |
| M2M client grants per organization (public cloud) | 100 | — |
| M2M client grants per organization (private cloud) | 1,000 | — |
| Custom token exchange profiles | 100 | — |

Quoted: "Customers on Enterprise plans can request increased entity limits for Organizations per
tenant and Organization members per Organization" via support. On **private cloud**, organizations and
organization members are **unlimited**.

**Not published / could not be verified:**
- **Maximum organization memberships per user** — no published number found.
- **Invitation count limits** — no published number found.

**A practical soft limit that is not on the entity-limit page:** the Universal Login **Organization
Picker displays only "the first 20 organizations they joined"** — documented on
<https://auth0.com/docs/manage-users/organizations/login-flows-for-organizations> and corroborated by
the Auth0 support article
<https://support.auth0.com/center/s/article/Limit-of-20-Organizations-user-is-a-member-of-on-Unviersal-Login-page-Organization-Picker>.
A user who belongs to more than 20 organizations cannot reach the rest through the stock picker.

**Correction of a stale figure:** search-engine snippets attribute a **1,000** organizations-per-tenant
default to the entity-limit page. A direct fetch of that page, twice, returns **100,000**. The 1,000
figure appears to be stale or a mis-summarisation; **100,000 is what the page currently prints.**

**Management API rate limits** — the rate-limit policy page
(<https://auth0.com/docs/troubleshoot/customer-support/operational-policies/rate-limit-policy>)
confirms that "Auth0 limits the number of requests to a specific API", that limits "may vary by"
subscription level, that Management API and Authentication API limits are distinct, and that "Tenant
rate limits are enforced independently" across APIs. The actual numbers live on per-tier sub-pages
indexed at `.../rate-limit-policy/rate-limit-configurations` (Free; Essentials and Professional
shared; Enterprise; and several Private Cloud tiers from Dev through 10,000 RPS). **The specific
per-tier numeric limits were not retrieved and are flagged unverified** — the index page renders only
a table of contents.

### 6.5 Plans and published pricing

Fetched from <https://auth0.com/pricing> on 2026-09-10. Auth0 publishes **two plan families**, B2C and
B2B, each with Free / Essentials / Professional / Enterprise.

| Family | Plan | Published price | MAU at that price |
|---|---|---|---|
| B2C | Free | $0/mo | up to 25,000 |
| B2C | Essentials | **from $35/mo** | 500 |
| B2C | Professional | **from $240/mo** | 500 |
| B2C | Enterprise | **"Contact us"** — no published price | Custom |
| B2B | Free | $0/mo | up to 25,000 |
| B2B | Essentials | **from $150/mo** | 500 |
| B2B | Professional | **from $800/mo** | 500 |
| B2B | Enterprise | **"Contact us"** — no published price | Custom |

The Free tier is published as "Up to 25,000 monthly active users" with "no credit card needed to sign
up."

### 6.6 Which tier is required for Organizations and for RBAC

**This is the headline finding, and it differs from the historical position that Organizations required
a paid B2B plan.**

- **Organizations is available on the Free tier**, capped at **5 Organizations**, in **both** the B2C
  and B2B families.
- **RBAC is NOT available on Free** in either family. It first appears at **Essentials**. On B2C
  Essentials it is listed specifically as **"Role-based Access Control Per Organization"**.

Organizations allowance by plan, as published:

| Plan | B2C Organizations | B2B Organizations |
|---|---|---|
| Free | **5** | **5** |
| Essentials | 10 | **"Unlimited\*\*"** (asterisked on the page) |
| Professional | 10 | **"Unlimited\*\*"** |
| Enterprise | Custom Tiers | Custom Tiers |

Note the shape of this: the **B2B family is where Organizations scales** (unlimited from Essentials
up), while the **B2C family caps Organizations at 10 even on Professional**. The B2B "Unlimited"
carries a `**` footnote on the pricing page whose text was not captured — **flagged as unverified**,
and it should be read against the 100,000-per-tenant entity limit in section 6.4, which is the
technical ceiling regardless of plan wording.

### 6.7 Feature gating by tier, as published

| Feature | B2C Free | B2C Essentials | B2C Professional | B2C Enterprise |
|---|---|---|---|---|
| Organizations | 5 | 10 | 10 | Custom Tiers |
| RBAC | **Not available** | Included (per organization) | Included | Included |
| MFA | **Not included** | Pro + Enterprise MFA Factors | Pro + Enterprise MFA Factors | All factors |
| Passkeys | Included | Included | Included | Included |
| Custom domains | 1 | Included | Included | Included |
| Actions + Forms | 5 | 10 | 15 | 30 + add-on |
| Log streaming | **Not available** | 1 stream | 2 streams | 2 streams |
| **Log retention** | **1 day** | **5 days** | **10 days** | **30 days** |
| Enterprise connections | 1 | **Not available** | **Not available** | Custom Tiers |
| SCIM | Included | Included | Included | Included |
| Attack protection | Brute Force Protection, Suspicious IP Throttling | same | + Enhanced Password Protection, Basic Breached Password Detection | + adaptive MFA, credential guard (add-ons) |

| Feature | B2B Free | B2B Essentials | B2B Professional | B2B Enterprise |
|---|---|---|---|---|
| Organizations | 5 | Unlimited\*\* | Unlimited\*\* | Custom Tiers |
| RBAC | **Not available** | Included | Included | Included |
| MFA | **Not included** | Pro MFA Factors (Enterprise factors as add-on) | Pro + Enterprise MFA Factors | All factors |
| Passkeys | Included | Included | Included | Included |
| Custom domains | 1 | Included | Included | Included |
| Actions + Forms | 5 | 10 | 15 | 30 + add-on |
| Log streaming | **Not available** | 1 stream | 2 streams | 2 streams |
| **Log retention** | **1 day** | **5 days** | **10 days** | **30 days** |
| Enterprise connections | 1 | 3 + add-on | 5 + add-on | Custom Tiers |
| SCIM | Included | Included | Included | Included |
| Attack protection | Brute Force Protection, Suspicious IP Throttling | same | + Enhanced Password Protection, Basic Breached Password Detection | Full suite (advanced features as add-ons) |

Two published oddities worth recording as facts rather than explaining away:

1. **Enterprise connections (SAML/OIDC) are listed as "1" on Free but "Not available" on B2C
   Essentials and B2C Professional**, only returning as "Custom Tiers" on B2C Enterprise. In the B2B
   family they scale normally (1, then 3+add-on, then 5+add-on, then Custom).
2. **MFA is listed as not included on either Free tier**, while **passkeys are listed as Included on
   every tier including Free.**

### 6.8 Flagged as unverified in this area

- Whether a single connection can be enabled for multiple organizations — implied by per-organization
  configuration, not documented explicitly.
- The `**` footnote text behind B2B "Unlimited" Organizations.
- Per-tier numeric Management API rate limits (index page renders only a table of contents).
- Maximum organization memberships per user, and any invitation-count limit — no published figure
  found.
- Auth0 does not state the plan requirement for Organizations in its **documentation**; the tier
  information above comes solely from the **pricing page**, which is marketing-surface material even
  though it is the only published source for it.

---

## 7. Consolidated list of what could not be verified

| # | Item | Area |
|---|---|---|
| 1 | Whether a Caddy v3 exists or is planned (absence of evidence only) | Caddy |
| 2 | Whether Caddy v1 required manual WebSocket config (v2 clearly does not) | Caddy |
| 3 | Let's Encrypt's forward-dated lifetime-reduction schedule (search snippets, not full page reads) | Caddy/ACME |
| 4 | GitHub repo and license for `TrapTech.Docker.DotNet` (empty `projectUrl` on NuGet) | Docker .NET |
| 5 | Whether `DockerSdk` restores cleanly on net10.0 (no restore performed) | Docker .NET |
| 6 | That no first-party Docker/Moby .NET client and no OpenAPI-generated client exist (negative findings from specific queries) | Docker .NET |
| 7 | Docker rootless-mode documented limitations (page section not surfaced) | Socket proxies |
| 8 | Full endpoint-tag enumeration on the v1.56 Engine API reference page | Socket proxies |
| 9 | Whether a maintained Palo Alto successor to `twistlock/authz` exists (forks not enumerated) | Socket proxies |
| 10 | sockguard's rule model, body mutation, response redaction and mTLS claims (README only; only the ownership/label subsystem is source-corroborated) | Socket proxies |
| 11 | A formal GA announcement date for OWASP Top 10:2025 (RC label removed 2025-12-24 per git history; no announcement page found) | OWASP |
| 12 | The literal `EncryptionAlgorithm.AES_256_GCM` enum member name in ASP.NET Core Data Protection | .NET crypto |
| 13 | The exact `.NET 10.0.12` patch digit (rendered tables, not raw `releases.json`) | .NET crypto |
| 14 | NIST SP 800-63-4 section numbering for memorised-secret requirements (text itself verified) | .NET crypto |
| 15 | Whether one Auth0 connection can serve multiple organizations | Auth0 |
| 16 | The `**` footnote behind Auth0 B2B "Unlimited" Organizations | Auth0 |
| 17 | Per-tier numeric Auth0 Management API rate limits | Auth0 |
| 18 | Auth0 max organization memberships per user; invitation-count limits | Auth0 |

**Confirmed absences (searched and genuinely not there, as distinct from the above):**

- NIST SP 800-38D's **2^32 per-key GCM invocation limit is not mentioned anywhere** in Microsoft's
  `AesGcm` or .NET cryptography documentation.
- Microsoft publishes **no** statement positioning ChaCha20Poly1305 as preferred or discouraged
  relative to `AesGcm`.
- Caddy's documentation **never mentions HSTS**, and automatic HSTS was explicitly declined as a
  feature — contradicting third-party blog claims.
- Docker ships **no first-party label-based authorization** feature.
- The .NET BCL contains **no Argon2 implementation**, by stated policy.
- **No project named "docker-socket-hardener" exists.**
- Auth0's **documentation** never states the plan tier required for Organizations; only the pricing
  page does.
