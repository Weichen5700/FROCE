(() => {
  let activeNavigation = null;
  let renderedPage = window.location.pathname + window.location.search;
  let scanPollTimer = null;
  let scanPollController = null;
  const financialRefreshStarted = new Set();
  const mainSelector = "main.shell";
  const navigationStatus = document.getElementById("page-navigation-status");

  document.addEventListener("click", (event) => {
    if (!(event.target instanceof Element)) return;

    const confirmTarget = event.target.closest("[data-confirm]");
    if (confirmTarget && !window.confirm(confirmTarget.dataset.confirm)) {
      event.preventDefault();
      return;
    }

    const link = event.target.closest("a[href]");
    if (!link || event.defaultPrevented || event.button !== 0 ||
        event.metaKey || event.ctrlKey || event.shiftKey || event.altKey ||
        link.target || link.hasAttribute("download") || link.dataset.noAjax !== undefined) return;

    const destination = new URL(link.href, window.location.href);
    if (destination.origin !== window.location.origin ||
        (destination.pathname === window.location.pathname &&
         destination.search === window.location.search && destination.hash)) return;

    event.preventDefault();
    navigate(destination.href, { history: "push" });
  });

  document.addEventListener("submit", (event) => {
    const form = event.target;
    if (!(form instanceof HTMLFormElement) || form.dataset.noAjax !== undefined || form.target) return;
    if (form.querySelector('input[type="file"]')) return;

    const method = (form.method || "get").toUpperCase();
    if (method !== "GET" && method !== "POST") return;

    const submitter = event.submitter;
    const destination = new URL(form.action || window.location.href, window.location.href);
    if (destination.origin !== window.location.origin) return;

    event.preventDefault();
    if (method === "GET") {
      const data = new FormData(form);
      if (submitter?.name) data.append(submitter.name, submitter.value);
      destination.search = "";
      for (const [key, value] of data.entries()) {
        if (typeof value === "string") destination.searchParams.append(key, value);
      }
      navigate(destination.href, { history: "push" });
      return;
    }

    const data = new FormData(form);
    if (submitter?.name) data.append(submitter.name, submitter.value);
    navigate(destination.href, { method: "POST", body: data, history: "push" });
  }, true);

  window.addEventListener("popstate", (event) => {
    // Native same-page anchors also raise popstate; keep the current content and let them scroll.
    if (renderedPage === window.location.pathname + window.location.search && window.location.hash) {
      return;
    }
    navigate(window.location.href, { history: "none", restoreScroll: event.state?.scrollY ?? 0 });
  });

  window.addEventListener("DOMContentLoaded", () => initializePage(document.querySelector(mainSelector)));
  window.roceNavigate = (url, replace = false) =>
    navigate(url, { history: replace ? "replace" : "push" });

  async function navigate(url, options = {}) {
    stopScanStatusPolling();
    const main = document.querySelector(mainSelector);
    if (!main) {
      window.location.assign(url);
      return;
    }

    activeNavigation?.abort();
    const controller = new AbortController();
    activeNavigation = controller;
    main.setAttribute("aria-busy", "true");
    if (navigationStatus) navigationStatus.hidden = false;

    const request = {
      method: options.method || "GET",
      headers: { "X-Requested-With": "XMLHttpRequest" },
      signal: controller.signal
    };
    if (options.body) request.body = options.body;

    try {
      const response = await fetch(url, request);
      if (!response.ok) throw new Error(`Page request failed: ${response.status}`);

      const documentText = await response.text();
      const nextDocument = new DOMParser().parseFromString(documentText, "text/html");
      const nextMain = nextDocument.querySelector(mainSelector);
      if (!nextMain) throw new Error("Page response did not contain the app content.");

      if (options.history === "push") {
        history.replaceState({ ...(history.state || {}), scrollY: window.scrollY }, "");
        history.pushState({ scrollY: 0 }, "", response.url);
      } else if (options.history === "replace") {
        history.replaceState({ scrollY: 0 }, "", response.url);
      }

      main.innerHTML = nextMain.innerHTML;
      renderedPage = window.location.pathname + window.location.search;
      main.removeAttribute("aria-busy");
      if (nextDocument.title) document.title = nextDocument.title;
      executePageScripts(main);
      initializePage(main);

      if (Number.isFinite(options.restoreScroll)) window.scrollTo(0, options.restoreScroll);
      else if (options.history !== "none") window.scrollTo(0, 0);
      main.setAttribute("tabindex", "-1");
      main.focus({ preventScroll: true });
    } catch (error) {
      if (error.name !== "AbortError") window.location.assign(url);
    } finally {
      if (activeNavigation === controller) {
        activeNavigation = null;
        main.removeAttribute("aria-busy");
        if (navigationStatus) navigationStatus.hidden = true;
      }
    }
  }

  function executePageScripts(root) {
    for (const oldScript of root.querySelectorAll("script")) {
      const script = document.createElement("script");
      for (const attribute of oldScript.attributes) script.setAttribute(attribute.name, attribute.value);
      script.textContent = oldScript.textContent;
      oldScript.replaceWith(script);
    }
  }

  function initializePage(root) {
    if (!root) return;
    const scanPanel = root.querySelector("[data-scan-panel]");
    if (scanPanel) startScanStatusPolling(scanPanel);
    for (const notice of root.querySelectorAll("[data-auto-financial-refresh-stock]")) loadMissingFinancialData(notice);
    for (const panel of root.querySelectorAll("[data-price-data-stock]")) loadDetailPrices(panel);
    for (const details of root.querySelectorAll("details[data-annual-prices-stock][open]")) loadWatchlistPrices(details);
  }

  function startScanStatusPolling(panel) {
    stopScanStatusPolling();
    if (panel.dataset.scanStatus !== "Running") return;
    scanPollTimer = window.setTimeout(() => pollScanStatus(panel), 5000);
  }

  function stopScanStatusPolling() {
    if (scanPollTimer !== null) window.clearTimeout(scanPollTimer);
    scanPollTimer = null;
    scanPollController?.abort();
    scanPollController = null;
  }

  async function pollScanStatus(panel) {
    if (!panel.isConnected || panel.dataset.scanStatus !== "Running") return;
    const controller = new AbortController();
    scanPollController = controller;
    try {
      const response = await fetch("/api/market-scan/status", { signal: controller.signal });
      if (!response.ok) throw new Error("掃描進度暫時無法取得。");
      const progress = await response.json();
      if (controller.signal.aborted || !panel.isConnected) return;

      if (Number(progress.status) !== 1) {
        if (window.location.pathname === "/Screen" && window.roceNavigate) {
          window.roceNavigate(window.location.href, true);
        }
        return;
      }

      const completed = Number(progress.completed || 0);
      const total = Number(progress.total || 0);
      const percent = Math.min(100, Math.max(0, Number(progress.percent || 0)));
      const rangeEnd = progress.rangeEndFiscalYear ?? progress.fiscalYear;
      const rangeStart = progress.rangeStartFiscalYear ?? progress.fiscalYear;
      const countNode = panel.querySelector("[data-scan-count]");
      const percentNode = panel.querySelector("[data-scan-percent]");
      const progressBar = panel.querySelector("[data-scan-progress-bar]");
      const rangeNode = panel.querySelector("[data-scan-range]");
      const yearNode = panel.querySelector("[data-scan-year]");
      const successNode = panel.querySelector("[data-scan-success]");
      const failedNode = panel.querySelector("[data-scan-failed]");
      const currentStockWrap = panel.querySelector("[data-scan-current-stock-wrap]");
      const currentStockNode = panel.querySelector("[data-scan-current-stock]");

      if (countNode) countNode.textContent = `${formatInteger(completed)} / ${formatInteger(total)}`;
      if (percentNode) percentNode.textContent = `${percent.toFixed(1)}%`;
      if (progressBar) progressBar.style.width = `${percent}%`;
      if (rangeNode) rangeNode.textContent = `範圍 ${rangeEnd} 至 ${rangeStart}`;
      if (yearNode) yearNode.textContent = String(progress.fiscalYear);
      if (successNode) successNode.textContent = formatInteger(progress.success);
      if (failedNode) failedNode.textContent = formatInteger(progress.failed);
      if (currentStockNode) currentStockNode.textContent = progress.currentStockNo || "";
      if (currentStockWrap) currentStockWrap.hidden = !progress.currentStockNo;
    } catch (error) {
      if (error.name !== "AbortError" && panel.isConnected) {
        const status = panel.querySelector("[data-scan-status-label]");
        if (status) status.title = "掃描進度暫時無法更新，稍後會自動重試。";
      }
    } finally {
      if (scanPollController === controller) scanPollController = null;
    }

    if (!controller.signal.aborted && panel.isConnected && panel.dataset.scanStatus === "Running") {
      scanPollTimer = window.setTimeout(() => pollScanStatus(panel), 5000);
    }
  }

  async function loadMissingFinancialData(notice) {
    const stockNo = notice.dataset.autoFinancialRefreshStock;
    if (!stockNo || financialRefreshStarted.has(stockNo)) return;
    financialRefreshStarted.add(stockNo);
    notice.textContent = "部分財報欄位尚未取得，正在背景補抓；頁面可繼續使用。";
    const requestedUrl = window.location.href;
    try {
      const response = await fetch(`/api/stocks/${encodeURIComponent(stockNo)}/refresh-missing`, { method: "POST" });
      if (!response.ok) throw new Error("財報補抓失敗。");
      const result = await response.json();
      notice.textContent = result.message || "背景補抓已完成。";
      if (result.success && window.location.href === requestedUrl && window.roceNavigate) {
        window.roceNavigate(requestedUrl, true);
      }
    } catch {
      notice.textContent = "背景補抓暫時失敗；可按「重新抓取資料」再次嘗試。";
    }
  }

  document.addEventListener("toggle", (event) => {
    const details = event.target;
    if (details instanceof HTMLDetailsElement && details.open && details.dataset.annualPricesStock) {
      loadWatchlistPrices(details);
    }
  }, true);

  async function loadWatchlistPrices(details) {
    if (details.dataset.pricesLoaded === "true" || details.dataset.pricesLoading === "true") return;
    details.dataset.pricesLoading = "true";
    const stockNo = details.dataset.annualPricesStock;
    const years = details.dataset.annualPricesYears || "";
    try {
      const query = new URLSearchParams({ years });
      const response = await fetch(`/api/stocks/${encodeURIComponent(stockNo)}/annual-prices?${query}`);
      if (!response.ok) throw new Error("年度股價暫時無法取得。");
      const prices = await response.json();
      const byYear = new Map(prices.map((item) => [Number(item.fiscalYear), item]));
      for (const closeCell of details.querySelectorAll("[data-annual-close]")) {
        const year = Number(closeCell.dataset.annualClose);
        const price = byYear.get(year)?.close;
        closeCell.textContent = price == null ? "—" : formatNumber(price);
        const peCell = details.querySelector(`[data-annual-pe="${year}"]`);
        const eps = Number(peCell?.dataset.eps);
        const pe = price > 0 && eps > 0 ? price / eps : null;
        if (peCell) peCell.textContent = pe == null ? "—" : formatNumber(pe);
      }
      details.dataset.pricesLoaded = "true";
    } catch {
      const summary = details.querySelector("summary");
      if (summary) summary.title = "年度股價載入失敗，重新展開即可重試。";
    } finally {
      delete details.dataset.pricesLoading;
    }
  }

  async function loadDetailPrices(panel) {
    if (panel.dataset.pricesLoaded === "true" || panel.dataset.pricesLoading === "true") return;
    panel.dataset.pricesLoading = "true";
    const stockNo = panel.dataset.priceDataStock;
    const status = panel.querySelector("[data-price-data-status]");
    try {
      const response = await fetch(`/api/stocks/${encodeURIComponent(stockNo)}/price-data`);
      if (!response.ok) throw new Error("行情資料暫時無法取得。");
      const data = await response.json();
      const latestClose = data.latestDailyClose;

      for (const average of data.movingAverages || []) {
        const value = average.value;
        const valueNode = panel.querySelector(`[data-ma-value="${average.tradingDays}"]`);
        const statusNode = panel.querySelector(`[data-ma-status="${average.tradingDays}"]`);
        const distanceNode = panel.querySelector(`[data-ma-distance="${average.tradingDays}"]`);
        if (valueNode) valueNode.textContent = value == null ? "—" : `${formatNumber(value)} 元`;
        if (statusNode) statusNode.textContent = value == null
          ? `尚缺 ${Math.max(0, average.tradingDays - data.dailyPriceCount)} 個交易日`
          : `最近 ${average.tradingDays} 個交易日`;
        if (distanceNode) {
          const gap = value > 0 && latestClose != null ? (latestClose - value) / value * 100 : null;
          distanceNode.classList.toggle("tw-rise", gap > 0);
          distanceNode.classList.toggle("tw-fall", gap < 0);
          distanceNode.textContent = gap == null ? "暫缺現價比較"
            : `${gap >= 0 ? "▲ 現價高於均線" : "▼ 現價低於均線"} ${formatNumber(Math.abs(gap))}%`;
        }
      }

      const annualPrices = data.annualPrices || [];
      for (const price of annualPrices) {
        const year = Number(price.fiscalYear);
        const peNode = document.querySelector(`${mainSelector} [data-year-pe="${year}"]`);
        const staticPeNode = document.querySelector(`${mainSelector} [data-static-pe-year="${year}"]`);
        const staticPbNode = document.querySelector(`${mainSelector} [data-static-pb-year="${year}"]`);
        if (peNode) peNode.textContent = price.staticPe == null ? "—" : `${formatNumber(price.staticPe)}x`;
        if (staticPeNode) staticPeNode.textContent = price.staticPe == null ? "—" : formatNumber(price.staticPe);
        if (staticPbNode) staticPbNode.textContent = price.staticPb == null ? "—" : formatNumber(price.staticPb);

        const tooltip = price.close == null
          ? "年度股價參考：尚無官方收盤資料"
          : `年度末收盤價：${formatNumber(price.close)} 元（交易日 ${price.tradingDate || "日期不明"}）\nEPS：${price.eps == null ? "無資料" : `${formatNumber(price.eps)} 元`}\n本益比：${price.staticPe == null ? "無法計算" : `${formatNumber(price.staticPe)} 倍`}`;
        for (const node of document.querySelectorAll(`${mainSelector} [data-annual-tooltip-year="${year}"]`)) {
          node.title = tooltip;
          node.dataset.tooltip = tooltip;
        }
      }

      const peValues = annualPrices.map((item) => item.staticPe).filter((value) => value != null).sort((a, b) => a - b);
      const medianNode = document.querySelector(`${mainSelector} [data-median-pe]`);
      if (medianNode) {
        if (peValues.length) {
          const middle = Math.floor(peValues.length / 2);
          const median = peValues.length % 2 ? peValues[middle] : (peValues[middle - 1] + peValues[middle]) / 2;
          medianNode.textContent = `本益比中位數：${formatNumber(median)} 倍`;
        } else medianNode.textContent = "本益比：無資料";
      }

      const countNode = document.querySelector(`${mainSelector} [data-daily-price-count]`);
      if (countNode) countNode.textContent = String(data.dailyPriceCount || 0);
      if (status) status.textContent = "行情資料已載入。";
      panel.dataset.pricesLoaded = "true";
    } catch {
      if (status) status.textContent = "行情資料暫時無法取得；可稍後重整頁面再試。";
    } finally {
      delete panel.dataset.pricesLoading;
    }
  }

  function formatNumber(value) {
    return new Intl.NumberFormat("zh-TW", { minimumFractionDigits: 2, maximumFractionDigits: 2 }).format(value);
  }

  function formatInteger(value) {
    return new Intl.NumberFormat("zh-TW", { maximumFractionDigits: 0 }).format(Number(value || 0));
  }

  document.addEventListener("submit", (event) => {
    const button = event.submitter;
    if (!button || button.dataset.confirm) return;

    const form = event.target;
    const originalMarkup = button.innerHTML;
    const label = button.querySelector("[data-button-label]");
    const status = form.querySelector("[data-progress-status]");
    const timeout = Number(button.dataset.busyTimeout || 45000);

    button.disabled = true;
    button.classList.add("is-loading");
    button.setAttribute("aria-busy", "true");
    if (label) label.textContent = button.dataset.progressLabel || "處理中…";
    else button.textContent = "處理中…";
    if (status) {
      status.hidden = false;
      status.textContent = button.dataset.progressStatus || status.textContent;
    }

    if (timeout > 0) {
      window.setTimeout(() => {
        button.disabled = false;
        button.classList.remove("is-loading");
        button.removeAttribute("aria-busy");
        button.innerHTML = originalMarkup;
        if (status) status.hidden = true;
      }, timeout);
    }
  });
})();
