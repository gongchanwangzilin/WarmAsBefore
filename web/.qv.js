(async () => {
  const wait = (ms) => new Promise((r) => setTimeout(r, ms));
  const errs = [];
  const oe = console.error; console.error = (...a) => { errs.push(String(a[0]).slice(0, 120)); oe(...a); };
  window.addEventListener("error", (e) => errs.push("ERR " + (e.message || "").slice(0, 120)));
  location.hash = "#/qv";
  await wait(2200);
  const layer = document.querySelector(".layer:not([data-leaving])");
  const t = layer ? (layer.textContent || "").trim() : "";
  const eb = document.querySelector(".eb, .error-boundary, [data-error]");
  return "len=" + t.length + " head=" + JSON.stringify(t.slice(0, 60)) + " eb=" + (eb ? "yes" : "no") + " errs=" + JSON.stringify(errs.slice(0, 3));
})()
