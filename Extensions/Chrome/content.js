// Detect downloadable media on the page
(function detectMedia() {
  let FILE_TYPES = [
    '.mp4','.webm','.mkv','.mov','.avi','.mp3','.aac','.flac','.wav','.m3u8',
    '.zip','.rar','.7z','.pdf','.exe'
  ];
  const VIDEO_TYPES = ['.mp4','.webm','.mkv','.mov','.avi','.m3u8'];

  function isVideoExt(ext) {
    return VIDEO_TYPES.includes(ext);
  }

  function getExtFromUrl(u) {
    try {
      const url = new URL(u, location.href);
      const last = (url.pathname || '').split('/').pop() || '';
      const dot = last.lastIndexOf('.');
      if (dot >= 0) return last.substring(dot).toLowerCase();
    } catch {}
    return '';
  }

  function collect() {
    const videos = [];

    // <video> tags and <source>
    document.querySelectorAll('video').forEach(v => {
      let url = v.currentSrc || v.src || '';
      if (!url) {
        const source = v.querySelector('source');
        if (source) url = source.src || '';
      }
      if (url && (url.startsWith('http://') || url.startsWith('https://'))) {
        videos.push({ url, title: document.title, type: 'video' });
      }
    });

    // Links to configured extensions (strict: by pathname extension)
    document.querySelectorAll('a[href]').forEach(a => {
      const href = a.getAttribute('href');
      if (!href) return;
      let abs = '';
      try { abs = new URL(href, location.href).toString(); } catch { return; }
      if (!(abs.startsWith('http://') || abs.startsWith('https://'))) return;
      const ext = getExtFromUrl(abs);
      if (!ext) return;
      if (!FILE_TYPES.includes(ext)) return;
      const type = isVideoExt(ext) ? 'video' : 'file';
      // Prefer filename for files; fallback to link text or page title
      const filename = (() => { try { return (new URL(abs)).pathname.split('/').pop() || ''; } catch { return ''; } })();
      const title = type === 'file' ? (filename || a.textContent?.trim() || document.title) : (a.textContent?.trim() || document.title);
      videos.push({ url: abs, title, type });
    });

    // <a download> even if no explicit extension (heuristic: try anyway if looks like file)
    document.querySelectorAll('a[download][href]').forEach(a => {
      try {
        const abs = new URL(a.getAttribute('href'), location.href).toString();
        if (!(abs.startsWith('http://') || abs.startsWith('https://'))) return;
        const ext = getExtFromUrl(abs);
        if (ext && !FILE_TYPES.includes(ext)) return; // if extension present but not allowed, skip
        const filename = a.getAttribute('download') || (new URL(abs)).pathname.split('/').pop() || '';
        const title = filename || a.textContent?.trim() || document.title;
        videos.push({ url: abs, title, type: isVideoExt(ext) ? 'video' : 'file' });
      } catch {}
    });

    // Elements with data-url / data-href
    document.querySelectorAll('[data-url], [data-href]').forEach(el => {
      try {
        const raw = el.getAttribute('data-url') || el.getAttribute('data-href');
        if (!raw) return;
        const abs = new URL(raw, location.href).toString();
        if (!(abs.startsWith('http://') || abs.startsWith('https://'))) return;
        const ext = getExtFromUrl(abs);
        if (!ext || !FILE_TYPES.includes(ext)) return;
        const title = el.textContent?.trim() || document.title;
        videos.push({ url: abs, title, type: isVideoExt(ext) ? 'video' : 'file' });
      } catch {}
    });

    // Heuristic: parse URL from inline onclick handlers
    document.querySelectorAll('[onclick]').forEach(el => {
      try {
        const code = String(el.getAttribute('onclick') || '');
        const m = code.match(/https?:\/\/[^'"\s)]+/i);
        if (!m) return;
        const abs = new URL(m[0], location.href).toString();
        const ext = getExtFromUrl(abs);
        if (!ext || !FILE_TYPES.includes(ext)) return;
        const title = el.textContent?.trim() || document.title;
        videos.push({ url: abs, title, type: isVideoExt(ext) ? 'video' : 'file' });
      } catch {}
    });

    return videos;
  }

  function safeSend(msg) {
    try {
      if (typeof chrome !== 'undefined' && chrome.runtime && chrome.runtime.sendMessage) {
        chrome.runtime.sendMessage(msg);
      }
    } catch {}
  }

  const payload = collect();
  if (payload.length) {
    safeSend({ type: 'VIDEOS_FOUND', payload });
  }

  // Re-scan on DOM changes (throttled)
  let timer = null;
  const mo = new MutationObserver(() => {
    if (timer) return;
    timer = setTimeout(() => {
      timer = null;
      const out = collect();
      safeSend({ type: 'VIDEOS_FOUND', payload: out });
    }, 1500);
  });
  mo.observe(document.documentElement, { subtree: true, childList: true, attributes: true });

  // Allow popup/background to request a fresh collection immediately
  try {
    if (typeof chrome !== 'undefined' && chrome.runtime && chrome.runtime.onMessage) {
      chrome.runtime.onMessage.addListener((msg, sender, sendResponse) => {
        if (msg && msg.type === 'COLLECT_NOW') {
          try { sendResponse({ videos: collect() }); } catch { sendResponse({ videos: [] }); }
          return true; // async-safe
        }
        if (msg && msg.type === 'SET_FILETYPES' && Array.isArray(msg.types)) {
          FILE_TYPES = msg.types.map(s => (s || '').toLowerCase());
          // after updating types, rescan quickly
          setTimeout(() => safeSend({ type: 'VIDEOS_FOUND', payload: collect() }), 250);
        }
      });
    }
  } catch {}

  // Bootstrap: request types from background
  try {
    if (typeof chrome !== 'undefined' && chrome.runtime && chrome.runtime.sendMessage) {
      chrome.runtime.sendMessage({ type: 'GET_FILETYPES' }, (res) => {
        try {
          if (res && Array.isArray(res.types) && res.types.length) FILE_TYPES = res.types.map(s => (s || '').toLowerCase());
        } catch {}
      });
    }
  } catch {}
})();
