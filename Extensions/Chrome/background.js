async function getTabTitle(tabId) {
	try { const t = await chrome.tabs.get(tabId); return t?.title || ''; } catch { return ''; }
}

function updateItemTitle(tabId, url, title) {
	try {
		const entry = mediaStore.get(tabId);
		if (!entry) return;
		const it = entry.videos.find(v => v.url === url);
		if (it) it.title = title;
	} catch {}
}
const PORT_URLS = ["http://127.0.0.1:17890", "http://localhost:17890"]; // fallback order

// File types captured (defaults; will be refreshed from app)
let FILE_TYPES = [
  ".mp4", ".webm", ".mkv", ".mov", ".avi", ".mp3", ".aac", ".flac", ".wav", ".m3u8",
  ".zip", ".rar", ".7z", ".pdf", ".exe"
];
const VIDEO_TYPES = [".mp4", ".webm", ".mkv", ".mov", ".avi", ".m3u8"]; // do NOT auto-intercept videos
// Shared set to avoid handling the same Chrome download twice across events
const _handledIds = new Set();

// Helpers to detect extensions in URL/filenames
function parseExtSet(list) {
  const out = new Set();
  try {
    (list || []).forEach(s => {
      if (!s) return;
      let t = String(s).trim().toLowerCase();
      if (!t) return;
      if (!t.startsWith('.')) t = '.' + t;
      out.add(t);
    });
  } catch {}
  return out;
}

function getExtFromPath(path) {
  try {
    const u = new URL(path);
    path = u.pathname || '';
  } catch {}
  const last = (path || '').split('/').pop() || '';
  const dot = last.lastIndexOf('.');
  if (dot >= 0) return last.substring(dot).toLowerCase();
  return '';
}

function isVideoExt(ext) {
  return VIDEO_TYPES.includes(ext);
}

async function loadFileTypes() {
  for (const base of PORT_URLS) {
    try {
      const res = await fetch(`${base}/filetypes`, { method: "GET" });
      if (res.ok) {
        const data = await res.json();
        if (Array.isArray(data?.types) && data.types.length) {
          FILE_TYPES = data.types.map(s => (s || "").toLowerCase());
          try { await chrome.storage.local.set({ FILE_TYPES }); } catch {}
          // Broadcast to all tabs so content can rescan
          try {
            const tabs = await chrome.tabs.query({});
            for (const t of tabs) {
              try {
                chrome.tabs.sendMessage(t.id, { type: 'SET_FILETYPES', types: FILE_TYPES }, () => {
                  void chrome.runtime.lastError; // swallow "Receiving end does not exist"
                });
              } catch {}
            }
          } catch {}
          return true;
        }
      }
    } catch {}
  }
  // Fallback to stored types if any
  try {
    const st = await chrome.storage.local.get('FILE_TYPES');
    if (Array.isArray(st.FILE_TYPES) && st.FILE_TYPES.length) FILE_TYPES = st.FILE_TYPES;
  } catch {}
  return false;
}

function i18n(key, substitutions) {
	try {
		return chrome.i18n.getMessage(key, substitutions) || key;
	} catch { return key; }
}

function showNotification(title, message) {
	try {
		chrome.notifications.create({
			type: 'basic',
			iconUrl: 'silk2_128_mm0.png',
			title,
			message
		});
	} catch {}
}

// In-memory store of detected media per tabId
const mediaStore = new Map(); // tabId -> { videos: [ {url, title, type} ] }

chrome.runtime.onInstalled.addListener(() => {
  chrome.action.setBadgeBackgroundColor({ color: "#1976D2" });
  loadFileTypes();
});

chrome.runtime.onStartup?.addListener?.(() => {
  loadFileTypes();
});

chrome.runtime.onMessage.addListener((msg, sender, sendResponse) => {
  if (msg && msg.type === "VIDEOS_FOUND" && sender.tab) {
    const tabId = sender.tab.id;
    const tabTitle = sender.tab.title || '';
    const tabReferer = sender.tab.url || undefined;
    const payload = Array.isArray(msg.payload) ? msg.payload : [];
    const existing = mediaStore.get(tabId) || { videos: [] };
    let merged = existing.videos.slice();
    if (payload.length) {
      const urls = new Set(merged.map(v => v.url));
      for (const it of payload) {
        try {
          if (!it || !it.url) continue;
          if (!(it.url.startsWith('http://') || it.url.startsWith('https://'))) continue;
          if (urls.has(it.url)) continue;
          urls.add(it.url);
          merged.push({ ...it, title: tabTitle || it.title || it.url, referer: it.referer || tabReferer });
        } catch {}
      }
    }
    mediaStore.set(tabId, { videos: merged });
    updateBadge(tabId);
    try { sendResponse({ ok: true }); } catch {}
    return;
  }
  if (msg?.type === 'GET_FILETYPES') {
    try { sendResponse({ types: FILE_TYPES }); } catch {}
    // kick off background refresh so future calls see latest
    try { loadFileTypes(); } catch {}
    return;
  }
  if (msg?.type === 'REFRESH_FILETYPES') {
    loadFileTypes().then(ok => sendResponse({ ok })).catch(() => sendResponse({ ok: false }));
    return true;
  }
  if (msg?.type === 'CLEAR_VIDEOS') {
    try {
      const tabId = msg.tabId;
      if (typeof tabId === 'number') {
        mediaStore.set(tabId, { videos: [] });
        updateBadge(tabId);
      }
      try { sendResponse({ ok: true }); } catch {}
    } catch { try { sendResponse({ ok: false }); } catch {} }
    return;
  }
  if (msg?.type === 'GET_VIDEOS_WITH_QUALITY') {
    try {
      const tabId = msg.tabId;
      const entry = mediaStore.get(tabId) || { videos: [] };
      enrichList(entry.videos)
        .then(list => { try { sendResponse({ videos: list }); } catch {} })
        .catch(() => { try { sendResponse({ videos: entry.videos }); } catch {} });
      return true;
    } catch { try { sendResponse({ videos: [] }); } catch {} }
  }
});

// -------------- Quality/Duration enrichment (HLS/DASH/Heuristic) --------------
const _qualityCache = new Map(); // url -> quality string
const _durationCache = new Map(); // url -> duration seconds

async function fetchText(url, referer) {
  const ctrl = new AbortController();
  const t = setTimeout(() => ctrl.abort(), 8000);
  try {
    const init = { signal: ctrl.signal, cache: 'no-store', credentials: 'include' };
    if (referer) {
      // No se puede establecer el header Referer manualmente, pero sí la propiedad referrer
      init.referrer = referer;
      init.referrerPolicy = 'no-referrer-when-downgrade';
    }
    const res = await fetch(url, init);
    if (!res.ok) throw new Error('bad status');
    return await res.text();
  } finally { clearTimeout(t); }
}

function formatHlsLabel(qs) {
  try {
    const joined = qs.join(', ');
    return i18n('qualityHls', joined) || `HLS: ${joined}`;
  } catch { return `HLS: ${qs.join(', ')}`; }
}
function formatDashLabel(qs) {
  try {
    const joined = qs.join(', ');
    return i18n('qualityDash', joined) || `DASH: ${joined}`;
  } catch { return `DASH: ${qs.join(', ')}`; }
}

function normalizeResolution(h) {
  const n = parseInt(h, 10);
  if (!isFinite(n) || n <= 0) return null;
  // Map height to common labels
  const common = [2160, 1440, 1080, 720, 480, 360, 240];
  let pick = n;
  for (const c of common) { if (Math.abs(n - c) <= 24) { pick = c; break; } }
  return `${pick}p`;
}

function resolveRelative(baseUrl, rel) {
  try { return new URL(rel, baseUrl).toString(); } catch { return rel; }
}

function parseHlsMaster(baseUrl, text) {
  // Extract #EXT-X-STREAM-INF variants: look for RESOLUTION and following URI
  const lines = text.split(/\r?\n/);
  const out = [];
  for (let i = 0; i < lines.length; i++) {
    const L = lines[i].trim();
    if (L.startsWith('#EXT-X-STREAM-INF:')) {
      const m = L.match(/RESOLUTION=\s*(\d+)x(\d+)/i);
      let label = null;
      if (m) label = normalizeResolution(m[2] || m[1]);
      // Next non-empty non-comment line is the URI
      let j = i + 1;
      while (j < lines.length && (!lines[j].trim() || lines[j].startsWith('#'))) j++;
      const uri = j < lines.length ? resolveRelative(baseUrl, lines[j].trim()) : null;
      if (uri) out.push({ uri, label });
    }
  }
  return out;
}

function chooseHlsVariant(variants) {
  try {
    if (!variants || !variants.length) return null;
    // Prefer highest resolution label; fallback to first
    const scored = variants.map(v => {
      const num = v?.label ? parseInt(String(v.label).replace(/\D/g, ''), 10) || 0 : 0;
      return { ...v, _score: num };
    }).sort((a, b) => b._score - a._score);
    return scored[0] || variants[0];
  } catch { return variants[0] || null; }
}

function parseDashRepresentations(text) {
  const res = new Set();
  const reps = text.match(/<Representation[^>]*height=\"(\d+)\"/gi) || [];
  for (const r of reps) {
    const m = r.match(/height=\"(\d+)\"/i);
    if (m) {
      const q = normalizeResolution(m[1]);
      if (q) res.add(q);
    }
  }
  return Array.from(res).sort((a,b) => parseInt(a) - parseInt(b));
}

function heuristicFromUrl(url) {
  const m = url.match(/(2160|1440|1080|720|480|360|240)p/i);
  return m ? `${m[1]}p` : null;
}

async function qualityOf(url, referer) {
  if (_qualityCache.has(url)) return _qualityCache.get(url);
  try {
    if (/m3u8/i.test(url)) {
      const txt = await fetchText(url, referer);
      // If it appears to be a media playlist (no STREAM-INF), fall back to heuristic
      if (!/#EXT-X-STREAM-INF:/i.test(txt)) {
        const h = heuristicFromUrl(url);
        const lbl = h ? formatHlsLabel([h]) : 'HLS';
        _qualityCache.set(url, lbl);
        // compute duration from media playlist
        try { const d = parseHlsDuration(txt); if (d != null) _durationCache.set(url, d); } catch {}
        return lbl;
      }
      const variants = parseHlsMaster(url, txt);
      const qs = Array.from(new Set(variants.map(v => v.label).filter(Boolean)));
      const lbl = qs.length ? formatHlsLabel(qs) : 'HLS';
      _qualityCache.set(url, lbl);
      // Also try to compute duration from best variant playlist
      try {
        const best = chooseHlsVariant(variants);
        const variantUrl = best?.uri;
        if (variantUrl) {
          const vtxt = await fetchText(variantUrl, referer);
          const d = parseHlsDuration(vtxt);
          if (d != null) _durationCache.set(url, d);
        }
      } catch {}
      return lbl;
    }
    if (/\.mpd(\?|$)/i.test(url)) {
      const txt = await fetchText(url, referer);
      const qs = parseDashRepresentations(txt);
      const lbl = qs.length ? formatDashLabel(qs) : 'DASH';
      _qualityCache.set(url, lbl);
      // try duration from MPD root
      try { const d = parseMpdDuration(txt); if (d != null) _durationCache.set(url, d); } catch {}
      return lbl;
    }
    // Direct file
    const h = heuristicFromUrl(url);
    if (h) { _qualityCache.set(url, h); return h; }
  } catch {}
  _qualityCache.set(url, null);
  return null;
}

function parseHlsDuration(text) {
  try {
    let total = 0;
    const re = /#EXTINF:([0-9]+(?:\.[0-9]+)?)/ig;
    let m;
    while ((m = re.exec(text)) !== null) {
      total += parseFloat(m[1]);
    }
    return isFinite(total) && total > 0 ? Math.round(total) : null;
  } catch { return null; }
}

function parseIsoDurationToSeconds(s) {
  try {
    const m = s.match(/PT(?:(\d+)H)?(?:(\d+)M)?(?:(\d+(?:\.\d+)?)S)?/i);
    if (!m) return null;
    const h = parseFloat(m[1] || '0');
    const min = parseFloat(m[2] || '0');
    const sec = parseFloat(m[3] || '0');
    const total = h * 3600 + min * 60 + sec;
    return isFinite(total) && total > 0 ? Math.round(total) : null;
  } catch { return null; }
}

function parseMpdDuration(text) {
  try {
    const m = text.match(/mediaPresentationDuration="(PT[^"]+)"/i);
    if (m) return parseIsoDurationToSeconds(m[1]);
    return null;
  } catch { return null; }
}

async function durationOf(url, referer) {
  if (_durationCache.has(url)) return _durationCache.get(url);
  try {
    if (/m3u8/i.test(url)) {
      const txt = await fetchText(url, referer);
      const d = parseHlsDuration(txt);
      if (d != null) { _durationCache.set(url, d); return d; }
    } else if (/\.mpd(\?|$)/i.test(url)) {
      const txt = await fetchText(url, referer);
      const d = parseMpdDuration(txt);
      if (d != null) { _durationCache.set(url, d); return d; }
    }
  } catch {}
  _durationCache.set(url, null);
  return null;
}

async function enrichList(items) {
  const cloned = items.map(x => ({ ...x }));
  const limit = 4; // limit concurrent fetches
  const queue = cloned.map((it, idx) => ({ it, idx }));
  let i = 0;
  async function worker() {
    while (i < queue.length) {
      const me = i++;
      const { it } = queue[me];
      try { it.quality = await qualityOf(it.url, it.referer); } catch { it.quality = null; }
      try { it.durationSec = await durationOf(it.url, it.referer); } catch { it.durationSec = null; }
    }
  }
  const workers = new Array(Math.min(limit, queue.length)).fill(0).map(() => worker());
  await Promise.race([
    Promise.all(workers),
    new Promise(resolve => setTimeout(resolve, 10000)) // timeout global
  ]);
  return cloned;
}

// Throttle duplicate sends to the app (same URL within a small time window)
const _recentlySent = new Map(); // url -> ms timestamp

chrome.tabs.onActivated.addListener(({ tabId }) => updateBadge(tabId));
chrome.tabs.onRemoved.addListener((tabId) => {
  mediaStore.delete(tabId);
});

function updateBadge(tabId) {
  const entry = mediaStore.get(tabId);
  const count = entry?.videos?.length || 0;
  chrome.action.setBadgeText({ tabId, text: count ? String(count) : "" });
}

async function sendToBolt(items) {
  // 1) Normalizar y filtrar: solo http/https y nunca reflejar llamadas al propio servidor local
  const normalized = (items || []).filter(it => {
    try {
      const u = String(it?.url || "");
      if (!u.startsWith('http://') && !u.startsWith('https://')) return false;
      if (PORT_URLS.some(base => u.startsWith(base))) return false; // evitar loops con el servidor local
      return true;
    } catch { return false; }
  });
  if (!normalized.length) return false;

  // 2) Throttling por URL para evitar reenvíos en ráfaga/bucles
  const now = Date.now();
  const WINDOW_MS = 8000;
  const toSend = normalized.filter(it => {
    try {
      const last = _recentlySent.get(it.url) || 0;
      return (now - last) > WINDOW_MS;
    } catch { return true; }
  });
  if (!toSend.length) return false;

  for (const base of PORT_URLS) {
    try {
      try { console.debug('[BoltExt] sendToBolt ->', base, toSend); } catch {}
      // 1) Intentar /capture (soporta array)
      let res = await fetch(`${base}/capture`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(toSend)
      });
      try { console.debug('[BoltExt] sendToBolt <- /capture', base, res.status); } catch {}
      if (res.ok) {
        try { for (const it of toSend) _recentlySent.set(it.url, Date.now()); } catch {}
        return true;
      }
      // 2) Fallback: /api/add (un solo elemento)
      if (toSend && toSend.length > 0) {
        const first = toSend[0];
        const body = { url: first.url, referer: first.referer || undefined, title: first.title || undefined, userAgent: navigator.userAgent };
        res = await fetch(`${base}/api/add`, {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify(body)
        });
        try { console.debug('[BoltExt] sendToBolt <- /api/add', base, res.status); } catch {}
        if (res.ok) {
          try { _recentlySent.set(first.url, Date.now()); } catch {}
          return true;
        }
      }
    } catch {
      // intentar siguiente base
    }
  }
  return false;
}

// Intercept normal browser downloads like IDM (top-level listener)
chrome.downloads.onCreated.addListener(async (item) => {
  try {
    const url = item?.finalUrl || item?.url || "";
    if (!url || !(url.startsWith("http://") || url.startsWith("https://"))) return;

    const extFromUrl = getExtFromPath(url);
    const extFromFile = getExtFromPath(item?.filename || "");
    const ext = extFromUrl || extFromFile;
    try { console.debug('[BoltExt] onCreated', { url, filename: item?.filename, extFromUrl, extFromFile, ext }); } catch {}
    const matched = ext && FILE_TYPES.includes(ext);
    if (!matched) return;
    // Do not auto-intercept videos, keep manual flow via popup
    if (isVideoExt(ext)) return;

    if (_handledIds.has(item.id)) return;
    _handledIds.add(item.id);
    // For normal browser download events, do NOT send a title to the app;
    // this ensures the desktop app uses the URL's path filename.
    const payload = [{ url, type: 'file' }];
    const ok = await sendToBolt(payload);
    try { console.debug('[BoltExt] onCreated sentToBolt', ok); } catch {}
    if (ok) {
      showNotification(i18n('notifTitle'), i18n('notifAddedOne'));
      try { chrome.downloads.cancel(item.id); } catch {}
      try { chrome.downloads.erase({ id: item.id }); } catch {}
    }
    else {
      // allow a later event to try again
      _handledIds.delete(item.id);
    }
  } catch {}
});

// Some sites only reveal the final filename later; use this hook as a second chance
chrome.downloads.onDeterminingFilename.addListener(async (item, suggest) => {
  try {
    if (_handledIds.has(item.id)) { try { suggest(); } catch {} return; }
    const url = item?.finalUrl || item?.url || "";
    if (!url || !(url.startsWith("http://") || url.startsWith("https://"))) { try { suggest(); } catch {} return; }
    const extFromUrl = getExtFromPath(url);
    const extFromFile = getExtFromPath(item?.filename || "");
    const ext = extFromFile || extFromUrl;
    try { console.debug('[BoltExt] onDeterminingFilename', { url, filename: item?.filename, extFromUrl, extFromFile, ext }); } catch {}
    if (!ext) { try { suggest(); } catch {} return; }
    if (!FILE_TYPES.includes(ext)) { try { suggest(); } catch {} return; }
    if (isVideoExt(ext)) { try { suggest(); } catch {} return; }

    _handledIds.add(item.id);
    // Same rule here: omit title so the app derives name from URL path.
    const payload = [{ url, type: 'file' }];
    const ok = await sendToBolt(payload);
    try { console.debug('[BoltExt] onDeterminingFilename sentToBolt', ok); } catch {}
    if (ok) {
      try { showNotification(i18n('notifTitle'), i18n('notifAddedOne')); } catch {}
      _handledIds.add(item.id);
      try { chrome.downloads.cancel(item.id); } catch {}
      try { chrome.downloads.erase({ id: item.id }); } catch {}
      return; // do not suggest filename, we cancelled
    }
    else {
      // allow other handler to attempt if this failed
      _handledIds.delete(item.id);
    }
  } catch {}
  try { suggest(); } catch {}
});

// Context menu for links only (no page-level yt-dlp)
try {
  chrome.runtime.onInstalled.addListener(() => {
		chrome.contextMenus.create({ id: 'bolt-download-link', title: i18n('ctxDownloadLink'), contexts: ['link'] });
		chrome.contextMenus.create({ id: 'bolt-download-selection', title: i18n('ctxDownloadSelection'), contexts: ['selection'] });
  });
  chrome.contextMenus.onClicked.addListener(async (info, tab) => {
    if (info.menuItemId === 'bolt-download-link' && info.linkUrl) {
      const link = info.linkUrl;
      if (!link.startsWith('http://') && !link.startsWith('https://')) return; // skip blob:, data:, etc.
      const ref = info.pageUrl || tab?.url || undefined;
      // Treat context menu links as normal file captures: omit title.
      const items = [{ url: link, type: 'file', referer: ref }];
      await sendToBolt(items);
    }
    if (info.menuItemId === 'bolt-download-selection' && info.selectionText) {
      try {
        const text = String(info.selectionText).trim();
        const m = text.match(/https?:\/\/[^\s]+/i);
        if (!m) return;
        const url = m[0];
        if (!url.startsWith('http://') && !url.startsWith('https://')) return;
        const ref = info.pageUrl || tab?.url || undefined;
        await sendToBolt([{ url, type: 'file', referer: ref }]);
      } catch {}
    }
  });
} catch {}

// Accept CAPTURE_MEDIA from content script and forward to app
try {
  chrome.runtime.onMessage.addListener((msg, sender, sendResponse) => {
    if (msg && msg.type === 'CAPTURE_MEDIA' && sender.tab) {
      const tabId = sender.tab.id;
      const pageUrl = sender.tab.url || undefined;
      const title = sender.tab.title || msg.item?.url || '';
      const url = msg.item?.url || '';
      if (!url || !(url.startsWith('http://') || url.startsWith('https://'))) { try { sendResponse({ ok: false }); } catch {} return; }
      const entry = mediaStore.get(tabId) || { videos: [] };
      entry.videos.push({ url, title, type: 'video' });
      mediaStore.set(tabId, entry);
      updateBadge(tabId);
      sendToBolt([{ url, title, type: 'file', referer: pageUrl }])
        .then(ok => { try { sendResponse({ ok }); } catch {} })
        .catch(() => { try { sendResponse({ ok: false }); } catch {} });
      return true; // async
    }
  });
} catch {}

// Early network capture using webRequest to avoid blob: URLs
const _seenMedia = new Set(); // url dedup across events
const _refByReq = new Map(); // requestId -> referer header

function getHeader(headers, name) {
  try {
    const h = headers?.find(h => (h?.name || '').toLowerCase() === name.toLowerCase());
    return h ? h.value || '' : '';
  } catch { return ''; }
}

function isAllowedMedia(url, contentType) {
  const ext = getExtFromPath(url);
  const allowed = new Set(['.m3u8', '.mpd', '.mp4', '.webm', '.mov', '.avi']);
  const disallowed = new Set(['.ts', '.m4s', '.m4f', '.aac', '.mp3', '.m2ts', '.fmp4']);
  if (disallowed.has(ext)) return false;
  if (allowed.has(ext)) return true;
  const ct = (contentType || '').toLowerCase();
  if (!ct) return false;
  if (ct.includes('application/vnd.apple.mpegurl') || ct.includes('application/x-mpegurl') || ct.includes('application/dash+xml')) return true;
  if (ct.startsWith('video/')) {
    // Allow common progressive types; exclude transport stream
    if (ct.includes('mp2t')) return false;
    if (ct.includes('mp4') || ct.includes('webm') || ct.includes('quicktime') || ct.includes('mov')) return true;
  }
  return false;
}

try {
  // Track Referer for each request
  chrome.webRequest.onBeforeSendHeaders.addListener((details) => {
    try {
      const ref = getHeader(details.requestHeaders || [], 'referer');
      if (ref) _refByReq.set(details.requestId, ref);
    } catch {}
  }, { urls: ["<all_urls>"] }, ["requestHeaders", "extraHeaders"]);

  // Early capture by extension match (no headers yet). Do NOT auto-send; only store for popup.
  chrome.webRequest.onBeforeRequest.addListener((details) => {
    try {
      const url = details.url || '';
      if (!url || !(url.startsWith('http://') || url.startsWith('https://'))) return;
      if (PORT_URLS.some(base => url.startsWith(base))) return; // ignore calls to the app
      if (!isAllowedMedia(url, '')) return;
      if (_seenMedia.has(url)) return;
      _seenMedia.add(url);
      const tabId = details.tabId;
      const referer = _refByReq.get(details.requestId) || undefined;
      const title = url;
      if (tabId && tabId > 0) {
        const entry = mediaStore.get(tabId) || { videos: [] };
        entry.videos.push({ url, title, type: 'video', referer });
        mediaStore.set(tabId, entry);
        updateBadge(tabId);
        // try to update with tab title
        getTabTitle(tabId).then(tt => { if (tt) updateItemTitle(tabId, url, tt); }).catch(()=>{});
      }
    } catch {}
  }, { urls: ["<all_urls>"] });

  // Detect media by Content-Type or extension. Do NOT auto-send; only store for popup.
  chrome.webRequest.onHeadersReceived.addListener((details) => {
    try {
      const url = details.url || '';
      if (!url || !(url.startsWith('http://') || url.startsWith('https://'))) return;
      if (PORT_URLS.some(base => url.startsWith(base))) return; // ignore calls to the app
      const ct = getHeader(details.responseHeaders || [], 'content-type');
      if (!isAllowedMedia(url, ct)) return;
      if (_seenMedia.has(url)) return;
      _seenMedia.add(url);

      const tabId = details.tabId;
      const referer = _refByReq.get(details.requestId) || undefined;
      const title = url;

      if (tabId && tabId > 0) {
        const entry = mediaStore.get(tabId) || { videos: [] };
        entry.videos.push({ url, title, type: 'video', referer });
        mediaStore.set(tabId, entry);
        updateBadge(tabId);
        getTabTitle(tabId).then(tt => { if (tt) updateItemTitle(tabId, url, tt); }).catch(()=>{});
      }
    } catch {}
    finally { try { _refByReq.delete(details.requestId); } catch {} }
  }, { urls: ["<all_urls>"] }, ["responseHeaders", "extraHeaders"]);
} catch {}

// Expose an API for popup
chrome.runtime.onMessage.addListener((msg, sender, sendResponse) => {
  if (msg?.type === "GET_VIDEOS") {
    const tabId = msg.tabId;
    const entry = mediaStore.get(tabId) || { videos: [] };
    sendResponse({ videos: entry.videos });
  } else if (msg?.type === "SEND_SELECTED") {
    try {
      const referer = sender?.tab?.url || undefined;
      const items = (msg.items || []).map(it => ({ ...it, referer: it.referer || referer }));
      sendToBolt(items)
        .then(ok => {
          try {
				if (ok) {
					const msg = items.length === 1 ? i18n('notifAddedOne') : i18n('notifAddedMany', String(items.length));
					showNotification(i18n('notifTitle'), msg);
				}
			} catch {}
          sendResponse({ ok });
        })
        .catch(() => sendResponse({ ok: false }));
    } catch { sendResponse({ ok: false }); }
    return true; // async
  }
});
