// Draws the Dotburg icon as 16x16 pixel art and writes PNGs and a Windows .ico.
//   node make-icon.mjs preview            -> icon-a.png, icon-b.png, preview.png (both variants, big and small)
//   node make-icon.mjs build a|b          -> dotburg.png (256) and dotburg.ico (16..256) from that variant
import { writeFileSync } from 'node:fs'
import { deflateSync } from 'node:zlib'
import { dirname, join } from 'node:path'
import { fileURLToPath } from 'node:url'

const here = dirname(fileURLToPath(import.meta.url))
const N = 16

// Colours come from the town's meadow scheme and sprites.
const C = {
  grass: 0x7fae5a, grass2: 0x78a655, road: 0xcdbd9b, road2: 0xc4b390, roadShade: 0xa89c80, grassShade: 0x5f8a45,
  roofL: 0xc4503f, roofD: 0x9b3b2f, wall: 0xe8dcc0, win: 0x6b8aa8, door: 0x6b4a32, chim: 0x5a4a44,
  red: 0xd9432f, blue: 0x2f62c9,
}

const blank = () => Array.from({ length: N * N }, () => null)
const put = (g, x, y, c) => { if (x >= 0 && y >= 0 && x < N && y < N) g[y * N + x] = c }
const rect = (g, x, y, w, h, c) => { for (let j = 0; j < h; j++) for (let i = 0; i < w; i++) put(g, x + i, y + j, c) }

// Rounded tile: fill, speckle, then clear the four corner pixels.
function tile(g) {
  rect(g, 0, 0, N, N, C.grass)
  for (const [x, y] of [[5, 1], [13, 2], [1, 3], [14, 7], [2, 13], [8, 14], [12, 13], [1, 7], [14, 14]]) put(g, x, y, C.grass2)
  for (const [x, y] of [[0, 0], [N - 1, 0], [0, N - 1], [N - 1, N - 1]]) put(g, x, y, null)
}

// A dot-man: a 2x2 body with a shadow row below.
function dot(g, x, y, c, shade) {
  rect(g, x, y, 2, 2, c)
  rect(g, x, y + 2, 2, 1, shade)
}

// Variant A: the game's own house sprite on the left, the two dot-men on a road to its right.
function variantA() {
  const g = blank()
  tile(g)
  rect(g, 1, 10, 14, 2, C.road)
  for (const x of [3, 6, 9, 12]) put(g, x, 11, C.road2)
  const house = ['.......', '.rrrrr.', '.RRRRR.', '.RRRRR.', '.WwWwW.', '.WWdWW.']
  const pal = { r: C.roofD, R: C.roofL, W: C.wall, w: C.win, d: C.door }
  house.forEach((row, j) => [...row].forEach((ch, i) => ch !== '.' && put(g, 2 + i, 4 + j, pal[ch])))
  put(g, 7, 4, C.chim)
  dot(g, 10, 8, C.red, C.roadShade)
  dot(g, 13, 8, C.blue, C.roadShade)
  // The dots stand on the road: shadows land on its second row.
  return g
}

// Variant B: a bigger, centred house with the dot-men in front of it on the road.
function variantB() {
  const g = blank()
  tile(g)
  rect(g, 1, 11, 14, 3, C.road)
  for (const x of [3, 7, 10, 13]) put(g, x, 12, C.road2)
  rect(g, 6, 3, 5, 1, C.roofL)
  rect(g, 5, 4, 7, 1, C.roofL)
  rect(g, 4, 5, 9, 1, C.roofD)
  rect(g, 5, 6, 7, 4, C.wall)
  rect(g, 6, 7, 2, 2, C.win)
  rect(g, 10, 7, 1, 2, C.win)
  rect(g, 8, 8, 2, 2, C.door)
  rect(g, 10, 2, 1, 2, C.chim)
  dot(g, 2, 10, C.red, C.roadShade)
  dot(g, 12, 10, C.blue, C.roadShade)
  return g
}

// ----- output -----
function scale(g, s) {
  const size = N * s
  const px = new Uint8Array(size * size * 4)
  for (let y = 0; y < size; y++)
    for (let x = 0; x < size; x++) {
      const c = g[Math.floor(y / s) * N + Math.floor(x / s)]
      const i = (y * size + x) * 4
      if (c === null) continue
      px[i] = (c >> 16) & 255; px[i + 1] = (c >> 8) & 255; px[i + 2] = c & 255; px[i + 3] = 255
    }
  return { px, size }
}

// Nearest-neighbour resample of the 16x16 art to any size (for 24 px, which is not a whole multiple).
function resample(g, size) {
  const px = new Uint8Array(size * size * 4)
  for (let y = 0; y < size; y++)
    for (let x = 0; x < size; x++) {
      const c = g[Math.min(N - 1, Math.floor((y * N) / size)) * N + Math.min(N - 1, Math.floor((x * N) / size))]
      const i = (y * size + x) * 4
      if (c === null) continue
      px[i] = (c >> 16) & 255; px[i + 1] = (c >> 8) & 255; px[i + 2] = c & 255; px[i + 3] = 255
    }
  return { px, size }
}

const crcTable = Array.from({ length: 256 }, (_, n) => { let c = n; for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1; return c >>> 0 })
const crc = buf => { let c = 0xffffffff; for (const b of buf) c = crcTable[(c ^ b) & 255] ^ (c >>> 8); return (c ^ 0xffffffff) >>> 0 }
function chunk(type, data) {
  const len = Buffer.alloc(4); len.writeUInt32BE(data.length)
  const td = Buffer.concat([Buffer.from(type), data])
  const c = Buffer.alloc(4); c.writeUInt32BE(crc(td))
  return Buffer.concat([len, td, c])
}
function png({ px, size, w = size, h = size }) {
  const raw = Buffer.alloc(h * (w * 4 + 1))
  for (let y = 0; y < h; y++) {
    raw[y * (w * 4 + 1)] = 0
    Buffer.from(px.buffer, y * w * 4, w * 4).copy(raw, y * (w * 4 + 1) + 1)
  }
  const ihdr = Buffer.alloc(13); ihdr.writeUInt32BE(w, 0); ihdr.writeUInt32BE(h, 4); ihdr[8] = 8; ihdr[9] = 6
  return Buffer.concat([Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]), chunk('IHDR', ihdr), chunk('IDAT', deflateSync(raw)), chunk('IEND', Buffer.alloc(0))])
}

// A Windows .ico holding PNG images of several sizes.
function ico(images) {
  const head = Buffer.alloc(6); head.writeUInt16LE(1, 2); head.writeUInt16LE(images.length, 4)
  let offset = 6 + images.length * 16
  const dir = [], data = []
  for (const { size, buf } of images) {
    const e = Buffer.alloc(16)
    e[0] = size >= 256 ? 0 : size; e[1] = size >= 256 ? 0 : size
    e.writeUInt16LE(1, 4); e.writeUInt16LE(32, 6); e.writeUInt32LE(buf.length, 8); e.writeUInt32LE(offset, 12)
    dir.push(e); data.push(buf); offset += buf.length
  }
  return Buffer.concat([head, ...dir, ...data])
}

const mode = process.argv[2]
const variants = { a: variantA(), b: variantB() }
if (mode === 'preview') {
  writeFileSync(join(here, 'icon-a.png'), png(scale(variants.a, 16)))
  writeFileSync(join(here, 'icon-b.png'), png(scale(variants.b, 16)))
  // Side by side on a dark strip: big (16x) and then small (1x, 2x) so the real taskbar sizes can be judged.
  const pad = 24, big = N * 8
  const W = pad * 3 + big * 2, H = pad * 2 + big + 80
  const px = new Uint8Array(W * H * 4)
  for (let i = 0; i < W * H; i++) { px[i * 4] = 40; px[i * 4 + 1] = 38; px[i * 4 + 2] = 34; px[i * 4 + 3] = 255 }
  const blit = (img, ox, oy) => { for (let y = 0; y < img.size; y++) for (let x = 0; x < img.size; x++) { const s = (y * img.size + x) * 4, d = ((oy + y) * W + ox + x) * 4; if (img.px[s + 3]) { px[d] = img.px[s]; px[d + 1] = img.px[s + 1]; px[d + 2] = img.px[s + 2] } } }
  ;['a', 'b'].forEach((k, i) => {
    const x0 = pad + i * (big + pad)
    blit(scale(variants[k], 8), x0, pad)
    blit(resample(variants[k], 48), x0, pad + big + 16)
    blit(resample(variants[k], 32), x0 + 64, pad + big + 24)
    blit(resample(variants[k], 16), x0 + 112, pad + big + 32)
  })
  writeFileSync(join(here, 'preview.png'), png({ px, w: W, h: H }))
} else if (mode === 'build') {
  const g = variants[process.argv[3] ?? 'a']
  const sizes = [16, 24, 32, 48, 64, 128, 256]
  const images = sizes.map(s => ({ size: s, buf: png(N % s === 0 || s % N === 0 ? scale(g, s / N) : resample(g, s)) }))
  writeFileSync(join(here, 'dotburg.ico'), ico(images))
  writeFileSync(join(here, 'dotburg.png'), png(scale(g, 16)))
  writeFileSync(join(here, 'dotburg-512.png'), png(scale(g, 32)))
  console.log('wrote dotburg.ico and dotburg.png')
}
