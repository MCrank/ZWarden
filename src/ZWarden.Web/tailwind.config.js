/** @type {import('tailwindcss').Config} */
module.exports = {
  // Scan ZWarden's own markup for utility classes. Blueprint's component CSS is
  // pre-built and shipped separately (blazorblueprint.css); this build only emits
  // the utilities ZWarden writes. Class names in .cs switch expressions (e.g.
  // StatusBadge's status ramp) must be full literal strings to be detected.
  content: ["./Components/**/*.{razor,cs,html}"],
  theme: {
    extend: {
      // The "Signal" palette, mapped onto the CSS variables defined in
      // zwarden.css (:root light / .dark dark). Utilities like bg-background,
      // text-foreground, border-border, text-primary resolve per theme with no
      // hard-coded colour - reskinning is editing the token file, nothing else.
      colors: {
        background: "var(--background)",
        foreground: "var(--foreground)",
        card: { DEFAULT: "var(--card)", foreground: "var(--card-foreground)" },
        popover: { DEFAULT: "var(--popover)", foreground: "var(--popover-foreground)" },
        primary: { DEFAULT: "var(--primary)", foreground: "var(--primary-foreground)" },
        secondary: { DEFAULT: "var(--secondary)", foreground: "var(--secondary-foreground)" },
        muted: { DEFAULT: "var(--muted)", foreground: "var(--muted-foreground)" },
        accent: { DEFAULT: "var(--accent)", foreground: "var(--accent-foreground)" },
        destructive: { DEFAULT: "var(--destructive)", foreground: "var(--destructive-foreground)" },
        border: "var(--border)",
        input: "var(--input)",
        ring: "var(--ring)",
        // The server-state ramp and meter thresholds - the StatusBadge / MeterBar
        // wrappers are the only sanctioned consumers (style-guide.md).
        status: {
          running: "var(--status-running)",
          stopped: "var(--status-stopped)",
          unhealthy: "var(--status-unhealthy)",
          busy: "var(--status-busy)",
          unknown: "var(--status-unknown)",
        },
        meter: {
          nominal: "var(--meter-nominal)",
          watch: "var(--meter-watch)",
          hot: "var(--meter-hot)",
          track: "var(--meter-track)",
        },
      },
      // Squared with a hairline round (3px), keyed off the token.
      borderRadius: {
        DEFAULT: "var(--radius)",
        sm: "calc(var(--radius) - 1px)",
        md: "var(--radius)",
        lg: "calc(var(--radius) + 2px)",
      },
      fontFamily: {
        // Chivo for UI, Chivo Mono for tabular data (self-hosted, see app.tailwind.css).
        sans: ["Chivo", "ui-sans-serif", "system-ui", "Segoe UI", "Roboto", "Helvetica Neue", "Arial", "sans-serif"],
        mono: ["Chivo Mono", "ui-monospace", "SFMono-Regular", "Menlo", "Consolas", "monospace"],
      },
    },
  },
  plugins: [],
};
