// Drive ZWarden.Web with a headless browser: log in as the seeded admin and screenshot a page.
//
// Prereqs (run once, in a scratch dir with a package.json):
//   npm install playwright && npx playwright install chromium
// The msedge channel closes immediately on this host — use the bundled Chromium (default here).
//
// Usage:  node shot.mjs <outfile.png> [path] [baseUrl]
//   path     default "/servers"
//   baseUrl  default "http://localhost:5063"
// Env:  ZW_ADMIN_EMAIL / ZW_ADMIN_PASSWORD (must match run-web.sh).
import { chromium } from 'playwright';

const OUT = process.argv[2] || 'shot.png';
// Git Bash mangles a leading-slash arg (e.g. "/servers") into a Windows path like
// "C:/Program Files/Git/servers". Recover the intended route by taking the last path segment.
const rawPath = process.argv[3] || '/servers';
const PATHNAME = '/' + rawPath.split(/[\\/]/).filter(Boolean).pop();
const BASE = process.argv[4] || 'http://localhost:5063';
const EMAIL = process.env.ZW_ADMIN_EMAIL || 'admin@zwarden.test';
const PASSWORD = process.env.ZW_ADMIN_PASSWORD || 'Sup3r-Str0ng-P@ss!';

const browser = await chromium.launch({ headless: true });
const ctx = await browser.newContext({ viewport: { width: 1360, height: 1000 }, deviceScaleFactor: 2 });
const page = await ctx.newPage();

// Log in (static-SSR form: BbInput renders name="Input.Email"/"Input.Password").
await page.goto(`${BASE}/login`, { waitUntil: 'networkidle' });
await page.fill('input[name="Input.Email"]', EMAIL);
await page.fill('input[name="Input.Password"]', PASSWORD);
await Promise.all([
  page.waitForLoadState('networkidle'),
  page.click('button[type="submit"]'),
]);

await page.goto(`${BASE}${PATHNAME}`, { waitUntil: 'networkidle' });
// Drop Blazor's post-navigation focus ring on <h1> (FocusOnNavigate) for a clean shot.
await page.evaluate(() => { const a = document.activeElement; if (a instanceof HTMLElement) a.blur(); });

console.log(JSON.stringify({ title: await page.title(), url: page.url() }));
await page.screenshot({ path: OUT, fullPage: true });
console.log('wrote ' + OUT);
await browser.close();
