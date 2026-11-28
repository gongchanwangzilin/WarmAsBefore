(async () => {
  const wait = (ms) => new Promise((r) => setTimeout(r, ms));
  const routes = ["/settingssection?key=appearance","/save","/roster","/studio","/main","/chat"];
  const out = [];
  for (const r of routes) {
    location.hash = "#" + r;
    await wait(900);
    const layer = document.querySelector(".layer:not([data-leaving])");
    const backs = [...layer.querySelectorAll("[data-back], .ph__back, .bar__back, button")].filter((b) => { const t = (b.textContent || "").trim(); return t === "返回" || /^[‹<◀]/.test(t) || t.indexOf("返回") >= 0; });
    const chip = document.querySelector(".backchip__btn");
    const title = layer.querySelector("[data-page-title]");
    let overlap = 0;
    if (chip && title) { const a = chip.getBoundingClientRect(); const b = title.getBoundingClientRect(); const w = Math.min(a.right, b.right) - Math.max(a.left, b.left); const h = Math.min(a.bottom, b.bottom) - Math.max(a.top, b.top); overlap = Math.max(0, Math.round(w)) * Math.max(0, Math.round(h)); }
    out.push(r + " backs=" + backs.length + " chip=" + (chip ? 1 : 0) + " overlapArea=" + overlap);
  }
  return out.join(" | ");
})()
