// Isolated world: passes main.js's window messages to the service worker, and while this tab's call rings polls it once a second for an
// answer/reject command (docs/3cx-ringer.md). Only this page's own messages of our type are relayed. Nothing runs between rings: the poll
// timer exists only from a ring start until the ring stops, a command arrives, Slender says it is not listening, or 95 s.
(() => {
  const TYPE = 'slender-3cx-ringer';
  const POLL_MS = 1000, MAX_POLL_MS = 95000;
  const active = { hid: false, notification: false };
  let timer = null, since = 0, busy = false, finished = false; // finished: this ring is settled, no more polls until the ring has stopped

  const ringing = () => active.hid || active.notification;

  function stopPolling() {
    if (timer !== null) { clearInterval(timer); timer = null; }
  }

  function sync() {
    if (!ringing()) { stopPolling(); finished = false; return; }
    if (timer !== null || finished) return;
    since = Date.now();
    timer = setInterval(poll, POLL_MS);
  }

  function handle(state) {
    if (state === 'none') return;
    stopPolling();
    finished = true;
    // Never act unless a ring is active in this tab.
    if ((state !== 'answer' && state !== 'reject') || !ringing()) return;
    const found = globalThis.SlenderControls.click(document, state);
    chrome.runtime.sendMessage({ event: 'ring-result', command: state, found }).catch(() => { /* service worker unavailable */ });
  }

  function poll() {
    if (!ringing() || Date.now() - since > MAX_POLL_MS) { stopPolling(); finished = true; return; }
    if (busy) return;
    busy = true;
    chrome.runtime.sendMessage({ event: 'poll' })
      .then((reply) => handle(reply && reply.state), () => handle('off'))
      .finally(() => { busy = false; });
  }

  window.addEventListener('message', (e) => {
    if (e.source !== window || !e.data || e.data.type !== TYPE) return;
    const { event, source } = e.data;
    if ((event !== 'ring-start' && event !== 'ring-stop') || (source !== 'hid' && source !== 'notification')) return;
    active[source] = event === 'ring-start';
    chrome.runtime.sendMessage({ event, source }).catch(() => { /* service worker unavailable */ });
    sync();
  });
})();
