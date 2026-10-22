// Isolated world: finds the 3CX web client's Answer / Decline control and clicks it. Only the controls' own labels (aria-label, title, short
// button text, test id, id, class) are matched, never anything else on the page, and nothing read here leaves it. relay.js decides when this
// may run (only while a call rings in this tab). The scoring is pure so node can test it (test/controls.test.mjs).
(() => {
  'use strict';

  // Label patterns are anchored at the start (a control labelled "Answer" or "Answer call"); attribute patterns look anywhere ("btn-answer").
  const LABEL = {
    answer: /^\s*(answer|accept|pick\s?up)\b/i,
    reject: /^\s*(reject|decline|ignore|dismiss)\b/i,
  };
  const ATTR = {
    answer: /answer|accept|pick-?up/i,
    reject: /reject|decline|dismiss|ignore/i,
  };
  // An Answer that starts video is the second choice; a Decline that is really a hang up or a hold never matches by label (anchored above).
  const PENALTY = { answer: /video/i, reject: /hang-?\s?up|end-?\s?call|hold|transfer/i };
  const MAX_TEXT = 40; // a longer text is a container, not a button label

  /**
   * The best candidate for `command` ('answer' or 'reject'), or null. A candidate is
   * { ariaLabel, title, text, testId, id, className, visible, disabled } (strings may be missing). Hidden and disabled ones never count.
   * Scores: aria-label 8, title 6, text 6, test id 4, id 3, class 2; the first best candidate in page order wins ties.
   */
  function pick(candidates, command) {
    const label = LABEL[command], attr = ATTR[command], penalty = PENALTY[command];
    if (!label) return null;
    let best = null, bestScore = 0;
    for (const c of candidates) {
      if (!c || c.disabled || c.visible === false) continue;
      let score = 0;
      const text = c.text && c.text.length <= MAX_TEXT ? c.text : '';
      if (c.ariaLabel && label.test(c.ariaLabel)) score += 8;
      if (c.title && label.test(c.title)) score += 6;
      if (text && label.test(text)) score += 6;
      if (c.testId && attr.test(c.testId)) score += 4;
      if (c.id && attr.test(c.id)) score += 3;
      if (c.className && attr.test(c.className)) score += 2;
      if (score === 0) continue;
      if ([c.ariaLabel, c.title, text, c.testId, c.id].some((v) => v && penalty.test(v))) score -= 5;
      if (score > bestScore) { best = c; bestScore = score; }
    }
    return best;
  }

  const SELECTOR = 'button, [role="button"], a[role="button"], [aria-label], [title], [data-testid], [data-test], [data-cy]';

  function visible(el) {
    if (!el.isConnected || el.getClientRects().length === 0) return false;
    const style = getComputedStyle(el);
    return style.visibility !== 'hidden' && style.display !== 'none' && style.pointerEvents !== 'none';
  }

  function describe(el) {
    return {
      el,
      ariaLabel: el.getAttribute('aria-label') || '',
      title: el.getAttribute('title') || '',
      text: (el.innerText || el.textContent || '').trim(),
      testId: el.getAttribute('data-testid') || el.getAttribute('data-test') || el.getAttribute('data-cy') || '',
      id: el.id || '',
      className: typeof el.className === 'string' ? el.className : '',
      visible: visible(el),
      disabled: el.disabled === true || el.getAttribute('aria-disabled') === 'true',
    };
  }

  /** Clicks the 3CX control for `command` in `root` (the document). True when a control was found and clicked. */
  function click(root, command) {
    const found = pick(Array.from(root.querySelectorAll(SELECTOR), describe), command);
    if (!found) return false;
    const target = found.el.closest('button, [role="button"], a') || found.el;
    target.click();
    return true;
  }

  const api = { pick, click };
  if (typeof module === 'object' && module.exports) module.exports = api;
  else globalThis.SlenderControls = api;
})();
