(async () => {
  const wait = (ms) => new Promise((r) => setTimeout(r, ms));
  const out = [];
  location.hash = "#/main"; await wait(900);
  const layer = document.querySelector(".layer:not([data-leaving])");
  out.push("main backish=" + [...layer.querySelectorAll("button, [role=button]")].filter((b) => { const t = (b.textContent || "").trim(); const c = (b.className || "").toString(); return t === "返回" || /^[‹<◀]/.test(t) || c.toLowerCase().indexOf("back") >= 0; }).length);
  location.hash = "#/settingssection?key=appearance"; await wait(1000);
  const l2 = document.querySelector(".layer:not([data-leaving])");
  out.push("appearance sliders=" + l2.querySelectorAll("[role=slider]").length + " switches=" + l2.querySelectorAll("[role=switch]").length + " len=" + l2.textContent.trim().length);
  return out.join(" | ");
})()
