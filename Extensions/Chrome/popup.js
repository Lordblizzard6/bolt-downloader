async function getActiveTabId() {
  const [tab] = await chrome.tabs.query({ active: true, currentWindow: true });
  return tab?.id;
}

async function ensureContentInjected(tabId) {
  try {
    await chrome.scripting.executeScript({
      target: { tabId },
      files: ["content.js"],
    });
    // small delay to allow script to initialize
    await new Promise(r => setTimeout(r, 150));
    return true;
  } catch {
    return false;
  }
}

function formatDuration(sec) {
	try {
		if (!sec || isNaN(sec) || sec <= 0) return '';
		const s = Math.floor(sec % 60);
		const m = Math.floor((sec / 60) % 60);
		const h = Math.floor(sec / 3600);
		const pad = (n) => String(n).padStart(2, '0');
		return h > 0 ? `${h}:${pad(m)}:${pad(s)}` : `${m}:${pad(s)}`;
	} catch { return ''; }
}

function renderList(videos) {
	const container = document.getElementById('container');
	container.innerHTML = '';
	if (!videos.length) {
		const div = document.createElement('div');
		div.className = 'item';
		div.innerHTML = `<span></span><span class="muted">${chrome.i18n.getMessage('emptyList') || 'No media detected on this page.'}</span>`;
		container.appendChild(div);
		return;
	}
	videos.forEach((v, i) => {
		const div = document.createElement('div');
		div.className = 'item';
		div.setAttribute('data-index', String(i));
		const fallback = chrome.i18n.getMessage('mediaFallback') || 'Media';
		const parts = [];
		if (v.quality) parts.push(v.quality);
		const dur = formatDuration(v.durationSec);
		if (dur) parts.push(dur);
		let metaStr = parts.join(' • ');
		if (!metaStr) metaStr = chrome.i18n.getMessage('metaUnknown') || '—';
		const meta = `<div class="meta-row">${metaStr}</div>`;
		div.innerHTML = `
		  <div class="title">${v.title || fallback}</div>
		  ${meta}
		`;
		container.appendChild(div);
	});
}

function showToast(msg, kind = 'success') {
  const t = document.getElementById('toast');
  t.className = '';
  t.classList.add(kind);
  t.id = 'toast';
  t.textContent = msg;
  t.style.display = 'block';
  setTimeout(() => { t.style.display = 'none'; }, 2000);
}

async function sendOne(v, tabId) {
  try {
    const tabs = await chrome.tabs.query({ active: true, currentWindow: true });
    const tab = tabs?.[0];
    const referer = tab?.url;
    const item = { url: v.url, title: (tab?.title || v.title || v.url), type: v.type || 'file', referer };
    const res = await chrome.runtime.sendMessage({ type: 'SEND_SELECTED', items: [item] });
    if (res?.ok) {
      showToast(chrome.i18n.getMessage('toastAdded') || 'Added to Bolt', 'success');
    } else {
      showToast(chrome.i18n.getMessage('toastFailed') || 'Failed to send. Is Bolt running?', 'error');
    }
  } catch {
    showToast(chrome.i18n.getMessage('toastFailed') || 'Failed to send. Is Bolt running?', 'error');
  }
}

async function init() {
  const tabId = await getActiveTabId();
  // i18n static texts
  try {
    document.getElementById('title').textContent = chrome.i18n.getMessage('popupTitle') || 'Detected videos';
    document.getElementById('note').textContent = chrome.i18n.getMessage('popupNote') || 'Click an item to add it to Bolt';
    const btn = document.getElementById('btnClear');
    if (btn) {
      const txt = document.getElementById('btnClearText');
      if (txt) txt.textContent = chrome.i18n.getMessage('btnClear') || 'Clear';
      btn.title = chrome.i18n.getMessage('btnClearTitle') || 'Clear detected items';
      btn.addEventListener('click', async () => {
        try {
          await chrome.runtime.sendMessage({ type: 'CLEAR_VIDEOS', tabId });
          renderList([]);
          showToast(chrome.i18n.getMessage('toastCleared') || 'List cleared', 'success');
        } catch {}
      });
    }
  } catch {}
  let videos = [];
  // 1) Ask content script for fresh collection (works even if service worker slept)
  try {
    if (tabId) {
      let res = null;
      try {
        res = await chrome.tabs.sendMessage(tabId, { type: 'COLLECT_NOW' });
      } catch {
        // try to inject content script dynamically, then retry once
        const injected = await ensureContentInjected(tabId);
        if (injected) {
          try { res = await chrome.tabs.sendMessage(tabId, { type: 'COLLECT_NOW' }); } catch {}
        }
      }
      if (res && Array.isArray(res.videos)) videos = res.videos;
    }
  } catch {
    // ignore; will fallback to background store
  }

  // 2) Try to get enriched list with quality
  try {
    const r = await chrome.runtime.sendMessage({ type: 'GET_VIDEOS_WITH_QUALITY', tabId });
    if (r && Array.isArray(r.videos) && r.videos.length) videos = r.videos;
  } catch {}

  // 3) Fallback to background store if still empty
  if (!videos.length) {
    try {
      const res2 = await chrome.runtime.sendMessage({ type: 'GET_VIDEOS', tabId });
      if (res2 && Array.isArray(res2.videos)) videos = res2.videos;
    } catch {}
  }

  renderList(videos);

  // Click-to-send per item
  document.getElementById('container').addEventListener('click', (ev) => {
    const el = ev.target.closest('.item');
    if (!el) return;
    const idx = parseInt(el.getAttribute('data-index') || '-1', 10);
    if (isNaN(idx) || idx < 0 || idx >= videos.length) return;
    const v = videos[idx];
    sendOne(v, tabId);
  });
}

document.addEventListener('DOMContentLoaded', init);
