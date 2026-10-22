// Page world (3CX web client). Watches for the two signs that a call is ringing and posts start/stop only. It never reads, keeps or
// sends call content (names, numbers, titles): only the event and which signal fired ("hid" or "notification").
(() => {
  'use strict';
  const TYPE = 'slender-3cx-ringer';
  const RINGER_USAGE = ((0x0B << 16) | 0x9E) >>> 0; // Telephony page, Ringer

  /** The full 32-bit usage (page << 16 | id) of a WebHID usage value; a bare id takes the collection's page. */
  function fullUsage(value, page) {
    return value > 0xFFFF ? value >>> 0 : (((page << 16) | value) >>> 0);
  }

  function* walk(collections) {
    for (const c of collections || []) {
      yield c;
      yield* walk(c.children);
    }
  }

  /**
   * Where the Ringer bit sits in the data of output report `reportId` (the report id byte is not part of the data): bits are packed in
   * item order, least significant bit first. Returns { bit, size } or null when that report has no Ringer.
   */
  function ringerLocation(collections, reportId) {
    for (const c of walk(collections)) {
      for (const report of c.outputReports || []) {
        if (report.reportId !== reportId) continue;
        let offset = 0;
        for (const item of report.items || []) {
          const count = item.reportCount, size = item.reportSize;
          for (let i = 0; i < count; i++) {
            let usage = null;
            if (item.isRange) {
              usage = fullUsage(item.usageMinimum, c.usagePage) + i;
              if (item.usageMaximum != null && usage > fullUsage(item.usageMaximum, c.usagePage)) usage = null;
            } else if (item.usages && item.usages.length) {
              usage = fullUsage(item.usages[Math.min(i, item.usages.length - 1)], c.usagePage);
            }
            if (usage === RINGER_USAGE) return { bit: offset + i * size, size };
          }
          offset += count * size;
        }
      }
    }
    return null;
  }

  /** True when the field at `loc` holds a non-zero value in `bytes`. */
  function fieldOn(bytes, loc) {
    for (let b = loc.bit; b < loc.bit + loc.size; b++) {
      if (((bytes[b >> 3] >> (b & 7)) & 1) !== 0) return true;
    }
    return false;
  }

  if (typeof module === 'object' && module.exports) { module.exports = { ringerLocation, fieldOn, fullUsage }; return; }

  // Each signal reports changes only: a repeated "on" is not a new ring.
  const active = { hid: false, notification: false };
  function set(source, on) {
    if (active[source] === on) return;
    active[source] = on;
    window.postMessage({ type: TYPE, event: on ? 'ring-start' : 'ring-stop', source }, window.location.origin);
  }

  // Primary signal: the headset ringer bit in the report 3CX sends to the USB base.
  if (typeof HIDDevice === 'function') {
    const proto = HIDDevice.prototype;
    const original = proto.sendReport;
    const locations = new WeakMap(); // device -> Map(reportId -> location | null), worked out the first time a report id is seen
    proto.sendReport = function sendReport(reportId, data) {
      try {
        let byId = locations.get(this);
        if (!byId) locations.set(this, byId = new Map());
        if (!byId.has(reportId)) byId.set(reportId, ringerLocation(this.collections, reportId));
        const loc = byId.get(reportId);
        if (loc) {
          const bytes = ArrayBuffer.isView(data) ? new Uint8Array(data.buffer, data.byteOffset, data.byteLength) : new Uint8Array(data);
          set('hid', fieldOn(bytes, loc));
        }
      } catch { /* never break the page's own call */ }
      return original.call(this, reportId, data);
    };
  }

  // Fallback: the call notification (requireInteraction, numeric tag). Only those are counted; others are left alone.
  if (typeof Notification === 'function') {
    const Native = Notification;
    let open = 0;
    const isCall = (o) => !!o && o.requireInteraction === true && (typeof o.tag === 'number' || (typeof o.tag === 'string' && /^\d+$/.test(o.tag)));
    class SlenderNotification extends Native {
      constructor(title, options) {
        super(title, options);
        try {
          if (!isCall(options)) return;
          open++;
          set('notification', true);
          let done = false;
          const end = () => { if (done) return; done = true; if (--open <= 0) { open = 0; set('notification', false); } };
          this.addEventListener('close', end);
          this.addEventListener('error', end);
        } catch { /* ignore */ }
      }
    }
    Object.defineProperty(window, 'Notification', { value: SlenderNotification, writable: true, configurable: true });
  }

  window.addEventListener('pagehide', () => { set('hid', false); set('notification', false); });
})();
