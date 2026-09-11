/** @type {import('tailwindcss').Config} */
module.exports = {
  // Scan ZWarden's own markup for utility classes. Blueprint's component CSS is
  // pre-built and shipped separately (blazorblueprint.css); this build only emits
  // the utilities ZWarden writes. Class names in .cs switch expressions (e.g.
  // StatusBadge's status ramp) must be full literal strings to be detected.
  content: ["./Components/**/*.{razor,cs,html}"],
  theme: {
    extend: {},
  },
  plugins: [],
};
