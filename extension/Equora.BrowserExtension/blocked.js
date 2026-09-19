// 拦截页逻辑:显示当前任务/剩余时间;返回任务 / 允许 5 分钟。
const $ = (id) => document.getElementById(id);

async function init() {
  const resp = await chrome.runtime.sendMessage({ type: "getGate" });
  if (!resp) return;
  const { gate, remaining } = resp;

  $("task").textContent = gate.taskTitle
    ? `当前任务:${gate.taskTitle}`
    : "专注会话进行中";

  renderTime(remaining);
  setInterval(() => {
    // 就地倒数,避免频繁消息。
    const left = Math.max(0, remaining - (Date.now() - loadedAt));
    renderTime(left);
  }, 1000);

  $("back").addEventListener("click", async () => {
    await chrome.runtime.sendMessage({ type: "backToTask" });
    window.close();
  });

  $("allow").addEventListener("click", async () => {
    await chrome.runtime.sendMessage({ type: "allowTemp" });
    history.back(); // 返回原页面(临时允许窗口内)
  });
}

const loadedAt = Date.now();

function renderTime(ms) {
  const total = Math.floor(ms / 1000);
  const h = Math.floor(total / 3600);
  const m = Math.floor((total % 3600) / 60);
  const s = total % 60;
  $("time").textContent = h > 0
    ? `${h}:${String(m).padStart(2, "0")}:${String(s).padStart(2, "0")}`
    : `${String(m).padStart(2, "0")}:${String(s).padStart(2, "0")}`;
}

init();
