// Forwards a ring event to the Slender native host (a one-shot process per message). Nothing is stored and nothing is sent anywhere else.
const HOST = 'com.slender.ringer';

chrome.runtime.onMessage.addListener((message, sender) => {
  if (sender.id !== chrome.runtime.id) return;
  const { event, source } = message || {};
  if ((event !== 'ring-start' && event !== 'ring-stop') || (source !== 'hid' && source !== 'notification')) return;
  chrome.runtime.sendNativeMessage(HOST, { event, source }).catch(() => { /* Slender not set up or not running: nothing to do */ });
});
