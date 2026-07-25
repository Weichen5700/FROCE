document.addEventListener("click", (event) => {
  const target = event.target.closest("[data-confirm]");
  if (target && !window.confirm(target.dataset.confirm)) event.preventDefault();
});

document.addEventListener("submit", (event) => {
  const button = event.submitter;
  if (!button || button.dataset.confirm) return;
  button.disabled = true;
  const original = button.textContent;
  button.textContent = "處理中…";
  window.setTimeout(() => { button.disabled = false; button.textContent = original; }, 45000);
});
