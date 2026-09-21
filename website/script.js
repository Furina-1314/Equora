const toggle = document.querySelector('.menu-toggle');
const navigation = document.querySelector('#navigation');
function closeMenu() {
  toggle.setAttribute('aria-expanded', 'false');
  toggle.setAttribute('aria-label', '打开导航');
  navigation.classList.remove('open');
}
toggle.addEventListener('click', () => {
  const expanded = toggle.getAttribute('aria-expanded') !== 'true';
  toggle.setAttribute('aria-expanded', String(expanded));
  toggle.setAttribute('aria-label', expanded ? '关闭导航' : '打开导航');
  navigation.classList.toggle('open', expanded);
});
navigation.addEventListener('click', (event) => {
  if (event.target.closest('a')) closeMenu();
});
document.addEventListener('keydown', (event) => {
  if (event.key === 'Escape' && toggle.getAttribute('aria-expanded') === 'true') {
    closeMenu();
    toggle.focus();
  }
});
window.matchMedia('(min-width: 801px)').addEventListener('change', closeMenu);

// Keep each wheel gesture on one panel; allow tall content to scroll first.
const pager = document.querySelector('#main');
const panels = Array.from(pager.children);
const reducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)');
let lockedUntil = 0;
let lastWheel = 0;
let wheelTotal = 0;

function currentPanelIndex() {
  return Math.max(0, Math.min(panels.length - 1, Math.round(pager.scrollTop / pager.clientHeight)));
}

function showPanel(index) {
  const next = panels[Math.max(0, Math.min(panels.length - 1, index))];
  pager.scrollTo({ top: next.offsetTop, behavior: reducedMotion.matches ? 'instant' : 'smooth' });
  lockedUntil = performance.now() + (reducedMotion.matches ? 180 : 700);
}

pager.addEventListener('wheel', (event) => {
  if (event.ctrlKey || Math.abs(event.deltaX) > Math.abs(event.deltaY) || !event.deltaY) return;
  const now = performance.now();
  const gap = now - lastWheel;
  lastWheel = now;
  if (now < lockedUntil) { event.preventDefault(); return; }
  const panel = panels[currentPanelIndex()];
  const direction = Math.sign(event.deltaY);
  const delta = event.deltaY * (event.deltaMode === 1 ? 16 : event.deltaMode === 2 ? pager.clientHeight : 1);
  const remaining = panel.scrollHeight - panel.clientHeight - panel.scrollTop;
  if ((direction > 0 && remaining > 2) || (direction < 0 && panel.scrollTop > 2)) {
    event.preventDefault();
    panel.scrollTop += delta;
    wheelTotal = 0;
    return;
  }
  event.preventDefault();
  // Ignore the inertia tail of the gesture that triggered a page transition.
  if (gap < 180 && wheelTotal === null) return;
  if (gap >= 180 || Math.sign(wheelTotal) !== direction) wheelTotal = 0;
  wheelTotal += delta;
  if (Math.abs(wheelTotal) >= 30) {
    showPanel(currentPanelIndex() + direction);
    wheelTotal = null;
  }
}, { passive: false });

document.addEventListener('click', (event) => {
  const link = event.target.closest('a[href^="#"]');
  if (!link) return;
  const id = link.getAttribute('href').slice(1);
  const target = id ? document.getElementById(id) : panels[0];
  const panel = target === pager ? panels[0] : panels.find(item => item === target || item.contains(target));
  if (!panel) return;
  event.preventDefault();
  closeMenu();
  panel.scrollTop = 0;
  showPanel(panels.indexOf(panel));
  history.replaceState(null, '', id ? `#${id}` : location.pathname + location.search);
  panel.focus({ preventScroll: true });
});

document.addEventListener('keydown', (event) => {
  if (event.defaultPrevented || event.ctrlKey || event.altKey || event.metaKey || event.target.closest('button, a, input, textarea, select, summary, [contenteditable="true"]')) return;
  const index = currentPanelIndex();
  const panel = panels[index];
  const down = event.key === 'PageDown' || event.key === 'ArrowDown' || (event.key === ' ' && !event.shiftKey);
  const up = event.key === 'PageUp' || event.key === 'ArrowUp' || (event.key === ' ' && event.shiftKey);
  if (event.key === 'Home' || event.key === 'End') {
    event.preventDefault();
    showPanel(event.key === 'Home' ? 0 : panels.length - 1);
  } else if ((down && panel.scrollTop + panel.clientHeight >= panel.scrollHeight - 2) || (up && panel.scrollTop <= 2)) {
    event.preventDefault();
    if (performance.now() >= lockedUntil) showPanel(index + (down ? 1 : -1));
  }
});

let resizeFrame;
let activeIndex = 0;
pager.addEventListener('scroll', () => { activeIndex = currentPanelIndex(); }, { passive: true });
window.addEventListener('resize', () => {
  cancelAnimationFrame(resizeFrame);
  resizeFrame = requestAnimationFrame(() => {
    pager.scrollTo({ top: panels[activeIndex].offsetTop, behavior: 'instant' });
  });
});
if (location.hash) {
  const target = document.getElementById(location.hash.slice(1));
  const index = panels.findIndex(panel => panel === target || panel.contains(target));
  if (index >= 0) requestAnimationFrame(() => showPanel(index));
}
