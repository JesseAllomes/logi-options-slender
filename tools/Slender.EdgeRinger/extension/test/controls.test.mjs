// Run with: node tools/Slender.EdgeRinger/extension/test/controls.test.mjs
import assert from 'node:assert/strict';
import { createRequire } from 'node:module';

const { pick } = createRequire(import.meta.url)('../controls.js');

const c = (fields) => ({ visible: true, disabled: false, ...fields });
const label = (command, candidates) => pick(candidates, command)?.name;

// aria-label, title, short text, test id and class are each enough on their own.
assert.equal(label('answer', [c({ name: 'a', ariaLabel: 'Answer' })]), 'a');
assert.equal(label('answer', [c({ name: 'a', title: 'Answer call' })]), 'a');
assert.equal(label('answer', [c({ name: 'a', text: 'Accept' })]), 'a');
assert.equal(label('answer', [c({ name: 'a', testId: 'call-answer-btn' })]), 'a');
assert.equal(label('answer', [c({ name: 'a', className: 'btn btn-accept' })]), 'a');
assert.equal(label('answer', [c({ name: 'a', ariaLabel: 'Pick up' })]), 'a');
assert.equal(label('reject', [c({ name: 'r', ariaLabel: 'Decline' })]), 'r');
assert.equal(label('reject', [c({ name: 'r', title: 'Reject call' })]), 'r');
assert.equal(label('reject', [c({ name: 'r', text: 'Dismiss' })]), 'r');
assert.equal(label('reject', [c({ name: 'r', id: 'reject-button' })]), 'r');

// Case is ignored; labels must start with the word; a long text is a container, not a label.
assert.equal(label('answer', [c({ name: 'a', ariaLabel: 'ANSWER' })]), 'a');
assert.equal(label('answer', [c({ name: 'x', ariaLabel: 'Do not answer anything' })]), undefined);
assert.equal(label('answer', [c({ name: 'x', text: 'Answer ' + 'x'.repeat(60) })]), undefined);

// The two commands never pick each other's control, and hang up / hold are not a decline.
const both = [c({ name: 'a', ariaLabel: 'Answer' }), c({ name: 'r', ariaLabel: 'Decline' }), c({ name: 'h', ariaLabel: 'End call' })];
assert.equal(label('answer', both), 'a');
assert.equal(label('reject', both), 'r');
assert.equal(label('reject', [c({ name: 'h', ariaLabel: 'End call' }), c({ name: 'h2', className: 'hangup' })]), undefined);

// Hidden and disabled controls never count; the visible one wins.
assert.equal(label('answer', [c({ name: 'hidden', ariaLabel: 'Answer', visible: false }), c({ name: 'shown', title: 'Answer' })]), 'shown');
assert.equal(label('answer', [c({ name: 'off', ariaLabel: 'Answer', disabled: true })]), undefined);

// The strongest match wins; audio answer beats video answer; ties go to page order.
assert.equal(label('answer', [c({ name: 'cls', className: 'answer' }), c({ name: 'aria', ariaLabel: 'Answer' })]), 'aria');
assert.equal(label('answer', [c({ name: 'video', ariaLabel: 'Answer with video' }), c({ name: 'audio', ariaLabel: 'Answer' })]), 'audio');
assert.equal(label('answer', [c({ name: 'video', ariaLabel: 'Answer with video' })]), 'video'); // only choice: still used
assert.equal(label('answer', [c({ name: 'first', ariaLabel: 'Answer' }), c({ name: 'second', ariaLabel: 'Answer' })]), 'first');

// Nothing matching, or an unknown command, finds nothing.
assert.equal(label('answer', []), undefined);
assert.equal(label('answer', [c({ name: 'x', ariaLabel: 'Mute' })]), undefined);
assert.equal(pick([c({ ariaLabel: 'Answer' })], 'hangup'), null);

console.log('controls tests passed');
