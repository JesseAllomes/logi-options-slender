// Run with: node tools/Slender.EdgeRinger/extension/test/decode.test.mjs
import assert from 'node:assert/strict';
import { createRequire } from 'node:module';

const { ringerLocation, fieldOn, fullUsage } = createRequire(import.meta.url)('../main.js');

const PAGE = 0x0B, RINGER = (PAGE << 16) | 0x9E, HOOK = (PAGE << 16) | 0x20;
const item = (usages, size, count, extra = {}) => ({ usages, reportSize: size, reportCount: count, isRange: false, ...extra });
const padding = (bits) => item([], bits, 1, { isConstant: true });
const collection = (items, reportId = 1, children = []) => ({ usagePage: PAGE, outputReports: [{ reportId, items }], children });

// Ringer is the third 1-bit field after two other usages.
{
  const c = collection([item([HOOK, (PAGE << 16) | 0x2A, RINGER], 1, 3), padding(5)]);
  assert.deepEqual(ringerLocation([c], 1), { bit: 2, size: 1 });
  assert.equal(ringerLocation([c], 2), null, 'other report ids have no ringer');
  assert.equal(fieldOn(new Uint8Array([0b100]), { bit: 2, size: 1 }), true);
  assert.equal(fieldOn(new Uint8Array([0b011]), { bit: 2, size: 1 }), false);
}

// Padding and a range item before it shift the position; the bit may land in the second byte.
{
  const c = collection([padding(4), item([HOOK], 1, 1), { isRange: true, usageMinimum: (PAGE << 16) | 0x90, usageMaximum: (PAGE << 16) | 0x9F, reportSize: 1, reportCount: 16 }]);
  assert.deepEqual(ringerLocation([c], 1), { bit: 5 + (0x9E - 0x90), size: 1 });
  assert.equal(fieldOn(new Uint8Array([0, 0b0010_0000]), { bit: 13, size: 1 }), true);
}

// Bare (page-less) usages take the collection's page; nested collections are searched; a short usage list repeats its last entry.
{
  assert.equal(fullUsage(0x9E, PAGE), RINGER);
  assert.equal(fullUsage(RINGER, 0xFF), RINGER);
  const inner = collection([item([0x9E], 1, 1)], 7);
  assert.deepEqual(ringerLocation([{ usagePage: 1, outputReports: [], children: [inner] }], 7), { bit: 0, size: 1 });
  const repeated = collection([item([0x20, 0x9E], 1, 4)]);
  assert.equal(ringerLocation([repeated], 1).bit, 1, 'the second field is the ringer; later ones reuse the last usage but the first match wins');
}

// No ringer anywhere.
assert.equal(ringerLocation([collection([item([HOOK], 1, 8)])], 1), null);
assert.equal(ringerLocation([], 1), null);
assert.equal(ringerLocation(undefined, 1), null);

console.log('decode tests passed');
