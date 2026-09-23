#!/usr/bin/env python3
"""Render the Markdown documentation in docs/ into the static help center pages in website/docs/.

Usage (from the repository root):
    pip install markdown
    python website/tools/build_docs.py

The script is dependency-light on purpose: it needs only the `markdown` package.
It writes website/docs/<slug>.html plus website/docs/search-index.json, copies
docs/images/ into website/assets/docs/, and never touches other website files.
The GitHub Pages workflow reruns it before deploying, so committed output is a
convenience for local preview, not the deploy source of truth.
"""
import html
import json
import posixpath
import re
import shutil
from html.parser import HTMLParser
from pathlib import Path

import markdown
from markdown.extensions.toc import slugify_unicode

REPO = Path(__file__).resolve().parents[2]
DOCS_DIR = REPO / "docs"
SITE_DIR = REPO / "website"
OUT_DIR = SITE_DIR / "docs"
IMAGES_OUT = SITE_DIR / "assets" / "docs"
GH = "https://github.com/Furina-1314/Equora"
SITE_URL = "https://furina-1314.github.io/Equora"

# Render order drives the sidebar, the prev/next pager and the search index.
PAGES = [
    {"source": "user-guide.md", "slug": "user-guide", "title": "用户指南", "group": "使用文档",
     "desc": "从安装到核心功能：任务管理、日历时间块、四象限、快速收集与专注会话的完整使用说明。"},
    {"source": "help.md", "slug": "usage-help", "title": "使用帮助与快捷键", "group": "使用文档",
     "desc": "快捷键一览，以及日历、专注模式、后台运行与数据目录的日常操作说明。"},
    {"source": "usage-restrictions.md", "slug": "usage-restrictions", "title": "应用与网站限制", "group": "使用文档",
     "desc": "建立「使用限制」规则，安装并连接 Edge / Chrome 扩展的完整指南。"},
    {"source": "privacy-and-security.md", "slug": "privacy-and-security", "title": "隐私与安全说明", "group": "使用文档",
     "desc": "衡序的数据存储、备份恢复、导出格式，以及一条清晰的隐私边界。"},
    {"source": "release-notes.md", "slug": "release-notes", "title": "发布说明 · 更新日志", "group": "版本与更新",
     "desc": "全部版本的发布说明与更新日志，附各版本安装包说明与已知限制。"},
    {"source": "v1.0.3.md", "slug": "v1.0.3", "title": "v1.0.3 稳定性与自保护", "group": "版本与更新",
     "desc": "移除最小化隐藏任务栏窗口的行为；Equora 相关进程自保护回归测试。"},
    {"source": "v1.0.2.md", "slug": "v1.0.2", "title": "v1.0.2 日历与使用限制", "group": "版本与更新",
     "desc": "ICS 日程编辑与删除、进程封锁页与临时允许、分心提醒和网站/进程额度倒计时。"},
    {"source": "v1.0.1.md", "slug": "v1.0.1", "title": "v1.0.1 正式版修复", "group": "版本与更新",
     "desc": "修复中文等非 ASCII 路径下 ICS 导入失败的问题，同类修复覆盖导入导出与备份路径。"},
    {"source": "v1.0.0.md", "slug": "v1.0.0", "title": "v1.0.0 正式版", "group": "版本与更新",
     "desc": "独立时间段标题、可选创建任务、跨周批量管理、学期归档与三次确认的数据重置。"},
    {"source": "v0.2.2.md", "slug": "v0.2.2", "title": "v0.2.2 更新说明", "group": "版本与更新",
     "desc": "v0.2.2「学期模式」：学期设置、周次显示与按周批量添加任务和时间段。"},
    {"source": "v0.2.1.md", "slug": "v0.2.1", "title": "v0.2.1 更新说明", "group": "版本与更新",
     "desc": "v0.2.1：修复网站临时允许误判，新增暂停限制倒计时提示与回收站永久删除。"},
    {"source": "architecture.md", "slug": "architecture", "title": "系统架构", "group": "开发者资料",
     "desc": "衡序的分层架构：WinUI 3 桌面壳、C++20 原生核心与可选的自托管同步服务器。"},
    {"source": "data-model.md", "slug": "data-model", "title": "数据模型", "group": "开发者资料",
     "desc": "本地 SQLite 数据模型与版本化数据库迁移的设计说明。"},
    {"source": "../THIRD-PARTY-NOTICES.md", "slug": "third-party-notices", "title": "第三方开源组件", "group": "开发者资料",
     "desc": "衡序使用的第三方开源组件完整清单与许可证信息。"},
]
GROUPS = ["使用文档", "版本与更新", "开发者资料"]

TEMPLATE = """<!doctype html>
<html lang="zh-CN">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <meta name="theme-color" content="#C32F37">
  <meta name="description" content="@@DESC@@">
  <meta property="og:title" content="@@TITLE@@ — 衡序 Equora 帮助中心">
  <meta property="og:description" content="@@DESC@@">
  <meta property="og:type" content="article">
  <link rel="canonical" href="@@CANON@@/docs/@@SLUG@@.html">
  <link rel="icon" type="image/svg+xml" href="@@ROOT@@assets/equora.svg">
  <link rel="stylesheet" href="@@ROOT@@styles.css">
  <link rel="stylesheet" href="@@ROOT@@help.css">
  <meta name="help-search-index" content="search-index.json">
  <script src="@@ROOT@@help.js" defer></script>
  <title>@@TITLE@@ — 衡序 Equora 帮助中心</title>
</head>
<body>
  <a class="skip-link" href="#doc-content">跳到主要内容</a>
  <header class="site-header">
    <div class="container header-inner">
      <a class="brand" href="@@ROOT@@index.html" aria-label="衡序 Equora 首页"><img src="@@ROOT@@assets/equora.svg" width="32" height="32" alt=""><span class="app-wordmark">EQUORA</span></a>
      <button class="menu-toggle" type="button" aria-label="打开导航" aria-expanded="false" aria-controls="navigation">菜单 <span aria-hidden="true">☰</span></button>
      <nav id="navigation" aria-label="主导航">
        <a href="@@ROOT@@index.html">首页</a><a href="@@ROOT@@help.html">帮助中心</a><a href="@@ROOT@@docs/release-notes.html">更新日志</a><a href="@@GH@@" class="repo-link">GitHub <span aria-hidden="true">↗</span></a>
        <a class="button small" href="@@GH@@/releases/latest">下载 Equora <span aria-hidden="true">↓</span></a>
      </nav>
    </div>
  </header>
  <main id="main">
    <div class="container help-layout">
      <aside class="help-sidebar" id="help-sidebar" aria-label="文档目录">
@@SIDEBAR@@
      </aside>
      <div class="doc-main">
        <button class="sidebar-toggle" type="button" aria-expanded="false" aria-controls="help-sidebar">文档目录 <span aria-hidden="true">☰</span></button>
        <nav class="doc-breadcrumb" aria-label="面包屑"><a href="@@ROOT@@help.html">帮助中心</a> <span aria-hidden="true">/</span> <span>@@TITLE@@</span></nav>
        <article class="doc-content" id="doc-content" tabindex="-1">
@@CONTENT@@
        </article>
        <nav class="doc-pager" aria-label="上一篇与下一篇">@@PAGER@@</nav>
      </div>
    </div>
  </main>
  <footer class="container footer">
    <div class="footer-top"><a class="brand" href="@@ROOT@@index.html"><img src="@@ROOT@@assets/equora.svg" width="28" height="28" alt=""><span class="app-wordmark">EQUORA</span></a><span>在时间中保持平衡。</span><nav aria-label="页脚导航"><a href="@@ROOT@@index.html">首页</a><a href="@@ROOT@@help.html">帮助中心</a><a href="@@ROOT@@docs/release-notes.html">更新日志</a><a href="@@GH@@/issues">反馈</a></nav></div>
    <div class="footer-bottom"><span>© 2026 Furina-1314 · Equora</span><span>开源于 MIT · 为专注而设计</span><a href="#main">回到顶部 ↑</a></div>
  </footer>
</body>
</html>
"""


def render_sidebar(active_slug, root):
    lines = ['        <div class="sidebar-search">',
             '          <input type="search" id="doc-search" placeholder="搜索帮助文档…" aria-label="搜索帮助文档" autocomplete="off">',
             '          <div class="search-results" id="search-results" hidden></div>',
             '        </div>',
             '        <nav class="sidebar-nav">']
    for group in GROUPS:
        lines.append(f'          <p class="nav-group">{group}</p>')
        for page in [p for p in PAGES if p["group"] == group]:
            active = " active" if page["slug"] == active_slug else ""
            lines.append(f'          <a class="nav-link{active}" href="{root}docs/{page["slug"]}.html">{page["title"]}</a>')
    lines.append('          <p class="nav-group">反馈</p>')
    lines.append(f'          <a class="nav-link external" href="{GH}/issues">问题反馈 <span aria-hidden="true">↗</span></a>')
    lines.append(f'          <a class="nav-link external" href="{GH}/releases">版本下载 <span aria-hidden="true">↗</span></a>')
    lines.append('        </nav>')
    return "\n".join(lines)


def render_pager(index):
    parts = []
    if index > 0:
        prev_page = PAGES[index - 1]
        parts.append(f'<a class="pager-link pager-prev" href="{prev_page["slug"]}.html"><span>上一篇</span><strong>{html.escape(prev_page["title"])}</strong></a>')
    else:
        parts.append('<span class="pager-link pager-prev" aria-hidden="true"></span>')
    if index + 1 < len(PAGES):
        next_page = PAGES[index + 1]
        parts.append(f'<a class="pager-link pager-next" href="{next_page["slug"]}.html"><span>下一篇</span><strong>{html.escape(next_page["title"])}</strong></a>')
    else:
        parts.append('<span class="pager-link pager-next" aria-hidden="true"></span>')
    return "\n          ".join(parts)


class SectionIndexer(HTMLParser):
    """Collect (heading, anchor, text) sections from rendered HTML for search."""

    def __init__(self, page_title):
        super().__init__(convert_charrefs=True)
        self.sections = []
        self.current = {"t": page_title, "a": "", "parts": []}
        self.heading_tag = None
        self.heading_parts = []

    def handle_starttag(self, tag, attrs):
        if tag in ("h1", "h2", "h3"):
            self._flush()
            self.heading_tag = tag
            self.heading_parts = []
            self.current = {"t": "", "a": dict(attrs).get("id", ""), "parts": []}

    def handle_endtag(self, tag):
        if tag == self.heading_tag:
            self.current["t"] = " ".join("".join(self.heading_parts).split())
            self.heading_tag = None

    def handle_data(self, data):
        if self.heading_tag:
            self.heading_parts.append(data)
        else:
            self.current["parts"].append(data)

    def _flush(self):
        text = " ".join("".join(self.current["parts"]).split())
        if self.current["t"] and (text or self.current["a"]):
            self.sections.append({"t": self.current["t"], "a": self.current["a"], "x": text[:240]})

    def finish(self):
        self.close()
        self._flush()


def repo_relative(source):
    return posixpath.normpath(posixpath.join("docs", source)).replace("\\", "/")


def map_markdown_link(target, by_source):
    path, sep, fragment = target.partition("#")
    keep = sep + fragment if sep else ""
    normalized = posixpath.normpath(path).replace("\\", "/")
    # Links in the Markdown sources are relative to docs/, while by_source is
    # relative to the repository root — match against both, then basenames.
    for key in (normalized, repo_relative(path), posixpath.basename(normalized)):
        if key in by_source:
            return f"{by_source[key]['slug']}.html{keep}"
    for repo_path in (normalized, f"docs/{normalized}"):
        if (REPO / repo_path).is_file():
            return f"{GH}/blob/main/{repo_path}{keep}"
    return f"{GH}/blob/main/{normalized}{keep}"


def convert_page(page, by_source):
    text = (DOCS_DIR / page["source"]).read_text(encoding="utf-8")
    converter = markdown.Markdown(
        extensions=["tables", "fenced_code", "toc", "nl2br", "sane_lists"],
        extension_configs={"toc": {"slugify": slugify_unicode}},
        output_format="html",
    )
    content = converter.convert(text)

    def rewrite_href(match):
        url = match.group(1)
        if re.match(r"^(https?:|#|mailto:)", url):
            return match.group(0)
        mapped = map_markdown_link(url, by_source)
        return f'href="{mapped}"'

    content = re.sub(r'href="([^"]+)"', rewrite_href, content)
    content = re.sub(r'src="images/', 'src="../assets/docs/', content)
    return content


def build():
    # Clear contents instead of removing the directory itself: on Windows a
    # shell sitting inside website/docs would make rmtree of the directory fail.
    if not OUT_DIR.exists():
        OUT_DIR.mkdir(parents=True)
    for entry in OUT_DIR.iterdir():
        if entry.is_dir():
            shutil.rmtree(entry)
        else:
            entry.unlink()
    IMAGES_OUT.mkdir(parents=True, exist_ok=True)
    for image in sorted((DOCS_DIR / "images").glob("*")):
        shutil.copy2(image, IMAGES_OUT / image.name)

    by_source = {repo_relative(page["source"]): page for page in PAGES}
    index_pages = []

    for position, page in enumerate(PAGES):
        content = convert_page(page, by_source)
        indexer = SectionIndexer(page["title"])
        indexer.feed(content)
        indexer.finish()
        page_html = (TEMPLATE
                     .replace("@@DESC@@", html.escape(page["desc"]))
                     .replace("@@TITLE@@", html.escape(page["title"]))
                     .replace("@@CANON@@", SITE_URL)
                     .replace("@@SLUG@@", page["slug"])
                     .replace("@@GH@@", GH)
                     .replace("@@ROOT@@", "../")
                     .replace("@@SIDEBAR@@", render_sidebar(page["slug"], "../"))
                     .replace("@@CONTENT@@", content)
                     .replace("@@PAGER@@", render_pager(position)))
        (OUT_DIR / f"{page['slug']}.html").write_text(page_html, encoding="utf-8")
        index_pages.append({
            "title": page["title"],
            "group": page["group"],
            "summary": page["desc"],
            # Resolved relative to search-index.json itself, so it works from
            # both the landing page and the pages in docs/.
            "url": f"{page['slug']}.html",
            "sections": indexer.sections,
        })
        print(f"  docs/{page['slug']}.html  ({len(indexer.sections)} sections)")

    (OUT_DIR / "search-index.json").write_text(
        json.dumps({"pages": index_pages}, ensure_ascii=False, separators=(",", ":")),
        encoding="utf-8")
    print(f"  docs/search-index.json")
    print(f"Done: {len(PAGES)} pages in {OUT_DIR}")


if __name__ == "__main__":
    build()
