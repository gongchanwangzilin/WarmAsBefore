(async () => {
  const wait = (ms) => new Promise((r) => setTimeout(r, ms));
  let seen = "";
  for (let i = 0; i < 30; i++) {
    const t = document.querySelector(".toast");
    if (t) { seen = (t.textContent || "").slice(0, 80); break; }
    await wait(1000);
  }
  return "toastSeen=" + JSON.stringify(seen);
})()
