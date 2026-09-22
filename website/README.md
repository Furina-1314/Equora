# Equora official website

Static, dependency-free website for https://furina-1314.github.io/Equora/.

Edit `index.html`, `styles.css`, and `script.js`. `styles.css` is shared by all pages; the homepage full-screen chapter paging rules are scoped under `html.paged`, which only `index.html` sets.

The help center (`help.html`) is a documentation-first portal: `help.css`, `help.js`, and the generated reading pages in `docs/`. It renders the repository Markdown in place instead of linking to GitHub. `website/tools/build_docs.py` converts `docs/*.md` plus `THIRD-PARTY-NOTICES.md` into `docs/<slug>.html` with a shared sidebar, prev/next pager, rewritten links and images (`assets/docs/`), and a client-side search index (`docs/search-index.json`) used by `help.js`. It needs only `pip install markdown`; run `python website/tools/build_docs.py` from the repository root after editing those sources. Landing-page entries (hot links, version list) are hand-written in `help.html` and may need matching updates when versions change.

Assets are copied from the application's brand mark and the documented application screenshots. All local URLs are relative to support the GitHub Pages project subdirectory. Download links use GitHub Releases so no version is hardcoded.

Preview from the repository root with `python -m http.server 4173 --directory website`, then open http://localhost:4173, http://localhost:4173/help.html, and a generated page such as http://localhost:4173/docs/user-guide.html. Check desktop and mobile layouts, the mobile menu, the docs sidebar and its mobile toggle, FAQ disclosure controls, anchor links and release links before publishing. On the help center, additionally run a search (e.g. 「快捷键」), open a result, and check tables, code blocks, images and prev/next pager on generated pages.

The header and footer wordmarks match the desktop shell: Segoe UI Variable / Segoe UI, uppercase EQUORA, regular 18px type, and 0.12em letter spacing (WinUI CharacterSpacing=120). The six chapters each fill the viewport below the header; FAQ and footer share the final chapter. Wheel gestures move one chapter at a time, with native scroll snapping for touch and support for keyboard navigation and reduced motion. On short screens or enlarged text, overflowing chapter content scrolls before moving to the next chapter.

GitHub Pages must use the GitHub Actions build source. Changes to `website/` or `docs/` on `main` deploy automatically through `.github/workflows/pages.yml`; the workflow regenerates the help center documentation pages before uploading, so the committed files under `docs/` are only a convenience for local preview. Manual runs are also supported. Only this directory is uploaded, never application source or local data. No third-party scripts, analytics, cookies, or external fonts are used; the pages build needs only the standard `markdown` package.
