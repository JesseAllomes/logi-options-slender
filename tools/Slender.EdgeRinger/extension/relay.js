// Isolated world: passes main.js's window messages to the service worker. Only this page's own messages of our type are relayed.
(() => {
  const TYPE = 'slender-3cx-ringer';
  window.addEventListener('message', (e) => {
    if (e.source !== window || !e.data || e.data.type !== TYPE) return;
    const { event, source } = e.data;
    if ((event !== 'ring-start' && event !== 'ring-stop') || (source !== 'hid' && source !== 'notification')) return;
    chrome.runtime.sendMessage({ event, source }).catch(() => { /* service worker unavailable */ });
  });
})();
