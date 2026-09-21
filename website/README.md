# Equora official website

Static, dependency-free website for https://furina-1314.github.io/Equora/.

Edit `index.html`, `styles.css`, and `script.js`. Assets are copied from the application's brand mark and the documented application screenshots. All local URLs are relative to support the GitHub Pages project subdirectory. Download links use GitHub Releases so no version is hardcoded.

Preview from the repository root with `python -m http.server 4173 --directory website`, then open http://localhost:4173. Check desktop and mobile layouts, the mobile menu, FAQ disclosure controls, anchor links and release links before publishing.

The header and footer wordmarks match the desktop shell: Segoe UI Variable / Segoe UI, uppercase EQUORA, regular 18px type, and 0.12em letter spacing (WinUI CharacterSpacing=120). The six chapters each fill the viewport below the header; FAQ and footer share the final chapter. Wheel gestures move one chapter at a time, with native scroll snapping for touch and support for keyboard navigation and reduced motion. On short screens or enlarged text, overflowing chapter content scrolls before moving to the next chapter.

GitHub Pages must use the GitHub Actions build source. Changes to `website/` on `main` deploy automatically through `.github/workflows/pages.yml`; manual runs are also supported. Only this directory is uploaded, never application source or local data. No build step, third-party scripts, analytics, cookies, or external fonts are required.
