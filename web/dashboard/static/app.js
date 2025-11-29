let lastSince = 0;
let fastGradChart, adxChart;
let lastItem = null;
let defaultParamsCache = {};
let overridesCache = {};
const toFixed = (v, d=2) => (typeof v === 'number' ? v.toFixed(d) : (v ? Number(v).toFixed(d) : '0'));
let diagsTotal = 0;
let noDataPolls = 0;

function initCharts() {
  const fgCtx = document.getElementById('fastGradChart');
  // Destroy existing chart if it exists
  if (fastGradChart) fastGradChart.destroy();
  fastGradChart = new Chart(fgCtx, {
    type: 'line',
    data: { labels: [], datasets: [{ label: 'FastGrad', data: [], borderColor: '#00d1b2' }] },
    options: { animation: false, scales: { x: { display: false } }, maintainAspectRatio: true }
  });
  const adxCtx = document.getElementById('adxChart');
  // Destroy existing chart if it exists
  if (adxChart) adxChart.destroy();
  adxChart = new Chart(adxCtx, {
    type: 'line',
    data: { labels: [], datasets: [{ label: 'ADX', data: [], borderColor: '#ffdd57' }] },
    options: { animation: false, scales: { x: { display: false } }, maintainAspectRatio: true }
  });
  console.log('[CHARTS] Initialized fresh charts');
}

async function pollDiags() {
  try {
    const res = await fetch(`/diags?since=${lastSince}`);
    const items = await res.json();
    console.log('[DIAGS] since=', lastSince, ' received=', items.length);
    if (items.length) {
      noDataPolls = 0;
      diagsTotal += items.length;
      const dcEl = document.getElementById('diagsCount');
      if (dcEl) { dcEl.textContent = `Diags: ${diagsTotal}`; dcEl.className = 'tag is-success'; }
      lastSince = items[items.length - 1].receivedAt;
      lastItem = items[items.length - 1];
      const out = document.getElementById('diagsOut');
      if (!out) { console.warn('[DIAGS] diagsOut element not found'); return; }
      const lines = items.map(d => {
        const t = d.time || '';
        const fg = d.fastGrad != null ? d.fastGrad.toFixed(4) : 'N/A';
        const sg = d.slowGrad != null ? d.slowGrad.toFixed(4) : 'N/A';
        const ac = d.accel != null ? d.accel.toFixed(4) : 'N/A';
        const adx = d.adx != null ? d.adx.toFixed(1) : 'N/A';
        const gs = d.gradStab != null ? d.gradStab.toFixed(4) : 'N/A';
        return `${t} bar=${d.barIndex} fastGrad=${fg} slowGrad=${sg} accel=${ac} gradStab=${gs} adx=${adx} blockersL=${(d.blockersLong||[]).length} blockersS=${(d.blockersShort||[]).length}`;
      });
      const isPlaceholder = out.textContent === 'Waiting for data… ensure server is running and strategy is posting.';
      const prev = isPlaceholder ? '' : out.textContent;
      const appended = prev ? (prev + '\n' + lines.join('\n')) : lines.join('\n');
      out.textContent = appended;
      // Auto-scroll to bottom if enabled (default on)
      const autoToggle = document.getElementById('autoScrollToggle');
      const autoEnabled = !autoToggle || !!autoToggle.checked;
      if (autoEnabled) {
        out.scrollTop = out.scrollHeight;
      }
      // Update charts (keep only last 100 points for readability)
      const MAX_CHART_POINTS = 100;
      items.forEach(d => {
        fastGradChart.data.labels.push('');
        fastGradChart.data.datasets[0].data.push(d.fastGrad || 0);
        adxChart.data.labels.push('');
        adxChart.data.datasets[0].data.push(d.adx || 0);
      });
      // Trim old data if over limit
      if (fastGradChart.data.labels.length > MAX_CHART_POINTS) {
        const excess = fastGradChart.data.labels.length - MAX_CHART_POINTS;
        fastGradChart.data.labels.splice(0, excess);
        fastGradChart.data.datasets[0].data.splice(0, excess);
      }
      if (adxChart.data.labels.length > MAX_CHART_POINTS) {
        const excess = adxChart.data.labels.length - MAX_CHART_POINTS;
        adxChart.data.labels.splice(0, excess);
        adxChart.data.datasets[0].data.splice(0, excess);
      }
      fastGradChart.update();
      adxChart.update();
    } else {
      noDataPolls += 1;
      if (noDataPolls >= 5 && lastSince > 0) {
        console.warn('[DIAGS] No data for 5 polls; resetting watermark to 0');
        lastSince = 0;
        noDataPolls = 0;
      }
    }
    const ss = document.getElementById('serverStatus');
    if (ss) ss.textContent = `Server: ${res.ok ? 'online' : 'error ' + res.status}`;
    // If we never received anything but server is online and stats say we have data, reset since to bootstrap
    if (diagsTotal === 0 && res.ok) {
      try {
        const s = await fetch('/stats').then(r => r.json());
        if (s && s.diags_count > 0 && lastSince === 0) {
          console.log('[DIAGS] Bootstrap reset: server has', s.diags_count, 'diags; reloading from 0');
          lastSince = 0; // redundant but explicit
        }
      } catch {}
    }
  } catch (e) { /* ignore */ }
  setTimeout(pollDiags, 1000);
}

function renderTrend(data) {
  const cur = data?.current || {};
  const segments = Array.isArray(data?.segments) ? data.segments : [];
  const curEl = document.getElementById('trend-current');
  const dir = cur.side || cur.dir || '-';
  const bars = cur.count ?? cur.bars ?? 0;
  const good = cur.good ?? cur.good_candles ?? 0;
  const bad = cur.bad ?? cur.bad_candles ?? 0;
  const pnl = cur.pnlProxy ?? cur.pnl_proxy ?? 0;
  const caught = !!(cur.caught);
  const start = cur.startLocal || cur.start_time || '';
  const startBar = cur.startBarIndex ?? '';
  curEl.innerHTML = `
    <span class="tag ${dir==='BULL' ? 'is-success' : (dir==='BEAR' ? 'is-danger' : '')}">Dir: ${dir}</span>
    <span class="tag is-link">Bars: ${bars}</span>
    <span class="tag is-info">Good: ${good}</span>
    <span class="tag is-warning">Bad: ${bad}</span>
    <span class="tag is-primary">PnL: ${toFixed(pnl, 3)}</span>
    <span class="tag ${caught ? 'is-success' : 'is-dark'}">Caught: ${caught ? 'Yes' : 'No'}</span>
    <span class="tag is-light">Start: ${start}</span>
    <span class="tag is-light">Start Bar: ${startBar}</span>
  `;
  const tsEl = document.getElementById('trendStatus');
  if (tsEl) tsEl.textContent = `Trend: ${dir || 'waiting'}`;

  const tbody = document.getElementById('trend-tbody');
  if (!tbody) return;
  // Build rows with current trend first (so ongoing streaks are visible)
  const rows = [];
  if (dir && dir !== '-') {
    rows.push({ ...cur, isCurrent: true });
  }
  rows.push(...segments.slice().reverse());

  tbody.innerHTML = rows.map(seg => {
    const d = seg.side || seg.dir || '-';
    const st = seg.startLocal || seg.start_time || '';
    const et = seg.endLocal || seg.end_time || (seg.isCurrent ? '(current)' : '');
    const sb = seg.startBarIndex ?? '';
    const eb = seg.endBarIndex ?? (seg.isCurrent ? '' : '');
    const b = seg.count ?? seg.bars ?? 0;
    const g = seg.good ?? seg.good_candles ?? 0;
    const bd = seg.bad ?? seg.bad_candles ?? 0;
    const p = toFixed(seg.pnlProxy ?? seg.pnl_proxy ?? 0, 3);
    const c = seg.caught ? 'Yes' : 'No';
    const rowClassBase = d === 'BULL' ? 'has-background-success-light' : (d === 'BEAR' ? 'has-background-danger-light' : '');
    const rowClass = seg.isCurrent ? `${rowClassBase} current-row` : rowClassBase;
    return `<tr class="${rowClass}"><td>${d}</td><td>${st}</td><td>${et}</td><td>${sb}</td><td>${eb}</td><td>${b}</td><td>${g}</td><td>${bd}</td><td>${p}</td><td>${c}</td></tr>`;
  }).join('');
}

async function pollTrendLog() {
  try {
    const res = await fetch('/trendlog?limit=50');
    const data = await res.json();
    renderTrend(data);
    const ss = document.getElementById('serverStatus');
    if (ss) ss.textContent = `Server: ${res.ok ? 'online' : 'error ' + res.status}`;
  } catch (e) { /* ignore */ }
  setTimeout(pollTrendLog, 1500);
}

async function suggest(json) {
  try {
    console.log('[SUGGEST] Sending:', json);
    const res = await fetch('/suggest', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(json) });
    console.log('[SUGGEST] Response status:', res.status);
    if (!res.ok) {
      const text = await res.text().catch(() => '');
      document.getElementById('suggestOut').textContent = `HTTP ${res.status}: ${text || 'Request failed'}`;
      return;
    }
    const data = await res.json().catch(async () => ({ raw: await res.text() }));
    console.log('[SUGGEST] Received data:', data);
    const output = JSON.stringify(data, null, 2);
    console.log('[SUGGEST] Setting output to:', output);
    document.getElementById('suggestOut').textContent = output;
    console.log('[SUGGEST] Done, element textContent is now:', document.getElementById('suggestOut').textContent);
    renderSuggestions(data);
  } catch (e) {
    console.error('[SUGGEST] Error:', e);
    document.getElementById('suggestOut').textContent = 'Error: ' + e.message;
  }
}

function renderSuggestions(data) {
  const listEl = document.getElementById('suggestionsList');
  if (!listEl) return;
  const suggestions = data?.suggestions || [];
  if (!Array.isArray(suggestions) || suggestions.length === 0) {
    listEl.innerHTML = '<em>No suggestions.</em>';
    return;
  }
  listEl.innerHTML = suggestions.map((s, idx) => {
    const canApply = s.canApply;
    const prop = s.property;
    const rec = s.recommend;
    const reason = s.reason || '';
    return `<div class="box has-background-grey-darker" style="padding:0.5rem; margin-bottom:0.4rem;">
      <div class="is-flex is-justify-content-space-between is-align-items-center">
        <div>
          <span class="tag is-info">${prop}</span>
          <span class="tag is-link">Recommend: ${rec}</span>
          <span class="tag is-dark">${reason}</span>
        </div>
        <div>
          ${canApply ? `<button class="button is-small is-success" data-apply-index="${idx}">Apply</button>` : ''}
        </div>
      </div>
    </div>`;
  }).join('');
  // Attach listeners
  suggestions.forEach((s, idx) => {
    if (s.canApply) {
      const btn = listEl.querySelector(`button[data-apply-index='${idx}']`);
      if (btn) {
        btn.onclick = () => applySuggestion(s);
      }
    }
  });
}

function renderAutoSuggestions(data) {
  const listEl = document.getElementById('autoSuggestionsList');
  if (!listEl) return;
  const suggestions = data?.suggestions || [];
  const weakCount = data?.weakGradConsec ?? 0;
  const threshold = data?.threshold ?? 0;
  const rsiBelow = data?.rsiBelowConsec ?? 0;
  const rsiFloor = data?.rsiFloor ?? 50;
  const wcTag = document.getElementById('weakGradCount');
  if (wcTag) {
    wcTag.textContent = `WeakGrad: ${weakCount}`;
    wcTag.className = 'tag ' + (weakCount >= threshold && suggestions.length ? 'is-danger' : 'is-warning');
  }
  const rsiTag = document.getElementById('rsiBelowCount');
  if (rsiTag) {
    rsiTag.textContent = `RSIBelow: ${rsiBelow}`;
    rsiTag.className = 'tag ' + (rsiBelow >= 3 ? 'is-danger' : 'is-warning');
    rsiTag.title = `Consecutive RSI < floor (${rsiFloor}) bars`;
  }
  if (!Array.isArray(suggestions) || suggestions.length === 0) {
    listEl.innerHTML = `<em>No auto suggestions. Consecutive weak bars: ${weakCount}/${threshold}</em>`;
  } else {
    listEl.innerHTML = suggestions.map((s, idx) => {
      const prop = s.property;
      const rec = s.recommend;
      const reason = s.reason || '';
      return `<div class="box has-background-grey-dark" style="padding:0.4rem; margin-bottom:0.4rem;">
        <div class="is-flex is-justify-content-space-between is-align-items-center">
          <div>
            <span class="tag is-link">${prop}</span>
            <span class="tag is-info">${rec}</span>
            <span class="tag is-dark" title="${reason}">${reason}</span>
          </div>
          <div>
            <button class="button is-small is-success" data-auto-apply-index="${idx}">Apply</button>
          </div>
        </div>
      </div>`;
    }).join('');
    suggestions.forEach((s, idx) => {
      const btn = listEl.querySelector(`button[data-auto-apply-index='${idx}']`);
      if (btn) { btn.onclick = () => applySuggestion(s); }
    });
  }

  // Auto-apply status & events
  const auto = data?.autoApply || {};
  const statusTag = document.getElementById('autoApplyStatus');
  if (statusTag) {
    const enabled = !!auto.enabled;
    statusTag.textContent = `Auto-Apply: ${enabled ? 'ON' : 'OFF'}`;
    statusTag.className = 'tag ' + (enabled ? 'is-success' : 'is-danger');
  }
  const eventsEl = document.getElementById('autoApplyEvents');
  if (eventsEl) {
    const evs = auto.recentEvents || [];
    if (!evs.length) {
      eventsEl.innerHTML = '<em>No recent auto applies.</em>';
    } else {
      eventsEl.innerHTML = evs.map(e => {
        const dt = new Date(e.ts * 1000).toISOString().split('T')[1].split('.')[0];
        return `<div>(${dt}) <strong>${e.property}</strong>: ${e.oldValue} -> ${e.newValue} (streak ${e.streakCount})</div>`;
      }).join('');
    }
  }
}

async function pollAutoSuggest() {
  try {
    const res = await fetch('/autosuggest');
    if (res.ok) {
      const data = await res.json();
      renderAutoSuggestions(data);
    }
  } catch {}
  setTimeout(pollAutoSuggest, 5000);
}

async function applySuggestion(s) {
  try {
    const payload = { property: s.property, value: s.recommend };
    console.log('[APPLY] Sending override:', payload);
    const res = await fetch('/apply', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(payload) });
    if (!res.ok) {
      console.warn('[APPLY] Failed:', res.status);
      return;
    }
    const data = await res.json();
    console.log('[APPLY] Response:', data);
    overridesCache = data.overrides || overridesCache;
    defaultParamsCache = data.defaultParams || defaultParamsCache;
    renderOverrides(data.overrides);
    renderEffectiveParams(data.effectiveParams, defaultParamsCache, overridesCache);
  } catch (e) { console.error('[APPLY] Error:', e); }
}

function renderOverrides(overrides) {
  const el = document.getElementById('overridesList');
  if (!el) return;
  const keys = Object.keys(overrides || {});
  if (!keys.length) { el.innerHTML = '<span class="tag is-dark">None</span>'; return; }
  el.innerHTML = keys.map(k => `<span class="tag is-primary override-tag" data-key="${k}" title="Right-click to remove" style="margin:3px; cursor:context-menu;">${k}=${overrides[k]}</span>`).join('');
  // Attach right-click (contextmenu) to delete override
  el.querySelectorAll('.override-tag').forEach(tag => {
    tag.addEventListener('contextmenu', async (evt) => {
      evt.preventDefault();
      const key = tag.getAttribute('data-key');
      if (!key) return;
      try {
        const res = await fetch(`/override/${encodeURIComponent(key)}`, { method: 'DELETE' });
        if (res.ok) {
          const data = await res.json();
          overridesCache = data.overrides || overridesCache;
          defaultParamsCache = data.defaultParams || defaultParamsCache;
          renderOverrides(data.overrides);
          renderEffectiveParams(data.effectiveParams, defaultParamsCache, overridesCache);
        }
      } catch (e) { console.warn('[DEL] Failed delete override', key, e); }
    });
  });
}

async function refreshOverrides() {
  try {
    const res = await fetch('/overrides');
    if (!res.ok) return;
    const data = await res.json();
    overridesCache = data.overrides || {};
    defaultParamsCache = data.defaultParams || {};
    renderOverrides(data.overrides);
    renderEffectiveParams(data.effectiveParams, defaultParamsCache, overridesCache);
  } catch {}
  setTimeout(refreshOverrides, 4000);
}

function renderEffectiveParams(effective, defaults={}, overrides={}) {
  const el = document.getElementById('effectiveParams');
  if (!el) return;
  const keys = Object.keys(effective || {});
  if (!keys.length) { el.innerHTML = '<span class="tag is-dark">N/A</span>'; return; }
  el.innerHTML = keys.map(k => {
    const cur = effective[k];
    const def = defaults[k];
    const overridden = Object.prototype.hasOwnProperty.call(overrides, k);
    let arrow = '';
    let cls = overridden ? 'is-warning' : 'is-info';
    if (overridden && typeof def === 'number' && typeof cur === 'number') {
      const isMinType = (k === 'MinAdxForEntry' || k === 'MinEntryFastGradientAbs');
      const isMaxType = (k === 'MaxGradientStabilityForEntry' || k === 'MaxBandwidthForEntry');
      const isAdaptiveFloor = (k === 'AdaptiveMinFloor');
      const isAdaptiveNearZero = (k === 'AdaptiveNearZeroMultiplier');
      if (isMinType) {
        if (cur > def) { arrow = '▲'; cls = 'is-danger'; }
        else if (cur < def) { arrow = '▼'; cls = 'is-success'; }
      } else if (isMaxType) {
        if (cur < def) { arrow = '▼'; cls = 'is-danger'; }
        else if (cur > def) { arrow = '▲'; cls = 'is-success'; }
      } else if (isAdaptiveFloor) {
        // Lower floor = looser (permits weaker gradients), higher = stricter
        if (cur < def) { arrow = '▼'; cls = 'is-success'; }
        else if (cur > def) { arrow = '▲'; cls = 'is-danger'; }
      } else if (isAdaptiveNearZero) {
        // Higher multiplier = looser (more permissive near zero-cross), lower = stricter
        if (cur > def) { arrow = '▲'; cls = 'is-success'; }
        else if (cur < def) { arrow = '▼'; cls = 'is-danger'; }
      }
    }
    const tip = overridden ? `Default: ${def} -> Override: ${cur}` : `Default: ${def}`;
    return `<span class="tag ${cls}" style="margin:3px;" title="${tip}">${k}:${cur}${arrow}</span>`;
  }).join('');
}

function main() {
  console.log('[MAIN] Starting dashboard, timestamp:', Date.now());
  initCharts();
  pollDiags();
  pollTrendLog();
  refreshOverrides();
  pollAutoSuggest();
  const toggleBtn = document.getElementById('toggleAutoApplyBtn');
  if (toggleBtn) {
    toggleBtn.onclick = async () => {
      try {
        const r = await fetch('/autoapply/toggle', { method: 'POST' });
        const j = await r.json();
        const tag = document.getElementById('autoApplyStatus');
        if (tag) {
          tag.textContent = `Auto-Apply: ${j.enabled ? 'ON' : 'OFF'}`;
          tag.className = 'tag ' + (j.enabled ? 'is-success' : 'is-danger');
        }
      } catch {}
    };
  }
  document.getElementById('useLastBtn').onclick = () => {
    if (lastItem) {
      document.getElementById('suggestInput').value = JSON.stringify(lastItem, null, 2);
      document.getElementById('suggestOut').textContent = '';
    }
  };
  document.getElementById('suggestBtn').onclick = () => {
    const txt = document.getElementById('suggestInput').value.trim();
    let obj = null;
    if (txt) {
      try { obj = JSON.parse(txt); } catch (e) { document.getElementById('suggestOut').textContent = 'Invalid JSON'; return; }
    } else if (lastItem) {
      obj = lastItem;
      document.getElementById('suggestInput').value = JSON.stringify(lastItem, null, 2);
    } else {
      document.getElementById('suggestOut').textContent = 'No diagnosis available yet.';
      return;
    }
    suggest(obj);
  };
  const postTestBtn = document.getElementById('postTestBtn');
  if (postTestBtn) {
    postTestBtn.onclick = async () => {
      const now = new Date().toISOString();
      const sample = { fastGrad: 0.25, adx: 18, fastEMA: 100, slowEMA: 101, close: 102, barIndex: (Date.now()/1000)|0, time: now };
      try {
        const r = await fetch('/diag', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(sample) });
        const ok = r.ok;
        const ss = document.getElementById('serverStatus');
        if (ss) ss.textContent = `Server: ${ok ? 'online' : 'error ' + r.status}`;
      } catch {}
    };
  }
        fetch('/stats').then(r => r.json()).then(stats => {
          const tag = document.getElementById('diagsCount');
          if (tag && stats && typeof stats.diags_count === 'number') {
            tag.textContent = `Diags: ${stats.diags_count}`;
            tag.className = 'tag is-success';
          }
        }).catch(() => {
          const tag = document.getElementById('diagsCount');
          if (tag) {
            tag.textContent = 'Diags: error';
            tag.className = 'tag is-danger';
          }
        });
}

main();
