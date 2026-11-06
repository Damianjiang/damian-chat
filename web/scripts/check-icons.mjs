#!/usr/bin/env node
/**
 * 图标自检（构建期硬失败，不是运行时警告）
 *
 * 背景：项目把 MDI 图标从 webfont 改成「@mdi/js 路径 + 内联 SVG」后，
 * 只有登记在 src/lib/mdi-svg.ts 映射表里的 `mdi-*` 名字才会渲染。
 * 漏登记时运行时只 console.warn + 输出一个空 <i> —— 构建照样成功，
 * 于是「关于」弹窗里 GitHub / Releases / Issues / 证书 四个按钮前面
 * 一个图标都没有，谁也没发现。
 *
 * 这个脚本扫描 src 下全部 .vue/.ts 里用到的 mdi-* 名字，与映射表比对：
 *   • 有名字没登记     → 报错退出（构建失败）
 *   • 映射表的 key 找不到对应的 @mdi/js 导出 → 报错退出（名字拼错）
 *   • 映射表里有但源码没用到 → 仅提示（Vuetify 内部别名等会走到）
 */
import { readFileSync, readdirSync, statSync } from 'node:fs'
import { join, relative } from 'node:path'
import { fileURLToPath } from 'node:url'
import * as mdi from '@mdi/js'

const webRoot = fileURLToPath(new URL('..', import.meta.url))
const srcDir = join(webRoot, 'src')

/** kebab-case 名 → @mdi/js 的 PascalCase 导出名（mdi-arrow-left → mdiArrowLeft）。 */
function toExportName (name) {
  return 'mdi' + name.slice(4).split('-').map(p => p.charAt(0).toUpperCase() + p.slice(1)).join('')
}

/** 递归收集源码文件。 */
const files = []
;(function walk (dir) {
  for (const entry of readdirSync(dir)) {
    const p = join(dir, entry)
    if (statSync(p).isDirectory()) walk(p)
    else if (/\.(vue|ts)$/.test(entry)) files.push(p)
  }
})(srcDir)

/** 源码里出现过的图标名 → 出现位置（便于报错定位）。 */
const used = new Map()
for (const file of files) {
  const text = readFileSync(file, 'utf8')
  for (const m of text.matchAll(/\bmdi-[a-z0-9-]+\b/g)) {
    const name = m[0]
    if (name === 'mdi-svg') continue            // 文件名 lib/mdi-svg 不是图标
    if (!used.has(name)) used.set(name, new Set())
    used.get(name).add(relative(webRoot, file))
  }
}

/** 映射表里登记过的名字。 */
const mapPath = join(srcDir, 'lib/mdi-svg.ts')
const mapped = new Set(
  [...readFileSync(mapPath, 'utf8').matchAll(/'(mdi-[a-z0-9-]+)'\s*:/g)].map(m => m[1]),
)

let failed = false

const missing = [...used.keys()].filter(n => !mapped.has(n)).sort()
if (missing.length > 0) {
  failed = true
  console.error(`\n✗ 有 ${missing.length} 个图标没有登记到 src/lib/mdi-svg.ts：\n`)
  for (const name of missing) {
    const where = [...used.get(name)].join(', ')
    console.error(`    ${name}   ← ${where}`)
  }
  console.error('\n  修法：在 mdi-svg.ts 的 import 里加导出，并在 mdiIconPaths 里加一行映射。\n')
}

const invalid = [...mapped].filter(n => typeof mdi[toExportName(n)] !== 'string').sort()
if (invalid.length > 0) {
  failed = true
  console.error(`\n✗ 映射表里有 ${invalid.length} 个名字在 @mdi/js 中不存在（拼写错误）：\n`)
  for (const name of invalid) console.error(`    ${name} → ${toExportName(name)}`)
  console.error('')
}

if (failed) process.exit(1)

const unused = [...mapped].filter(n => !used.has(n)).sort()
console.log(`✓ 图标自检通过：${used.size} 个在用图标全部已登记（映射表共 ${mapped.size} 条）`)
if (unused.length > 0) {
  console.log(`  · 另有 ${unused.length} 条映射源码里没直接引用（Vuetify 内部别名可能用到，保留即可）`)
}
