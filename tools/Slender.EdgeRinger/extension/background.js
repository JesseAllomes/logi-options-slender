// Forwards ring events to the Slender native host (a one-shot process per message), answers the page's once-a-second poll with the
// host's reply, and passes on the page's answer/reject click report. Nothing is stored and nothing is sent anywhere else.
const HOST = 'com.slender.ringer';
const STATES = ['none', 'answer', 'reject'];

chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
  if (sender.id !== chrome.runtime.id) return;
  const { event, source, command, found } = message || {};
  if ((event === 'ring-start' || event === 'ring-stop') && (source === 'hid' || source === 'notification')) {
    chrome.runtime.sendNativeMessage(HOST, { event, source }).catch(() => { /* Slender not set up or not running: nothing to do */ });
  } else if (event === 'poll') {
    // The host replies none (keep polling), answer, reject, or off (Slender is not listening: the page stops polling).
    chrome.runtime.sendNativeMessage(HOST, { event: 'poll' })
      .then((reply) => sendResponse({ state: reply && STATES.includes(reply.state) ? reply.state : 'off' }))
      .catch(() => sendResponse({ state: 'off' }));
    return true; // the response is async
  } else if (event === 'ring-result' && (command === 'answer' || command === 'reject') && typeof found === 'boolean') {
    chrome.runtime.sendNativeMessage(HOST, { event, command, found }).catch(() => { /* Slender not running */ });
  }
});
