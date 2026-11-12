#!/usr/bin/env node
// 共享 skill 自检（辅助工具，**默认不拦人**）
//
// 用法：
//   node check-skill.mjs <skills目录>            # 只报告，永远返回 0
//   node check-skill.mjs <skills目录> --strict   # 有问题返回 1（给 CI / pre-commit 用）
//
// 查七项结构性问题；查不出「内容对不对」——那只能人看。
// 公司私有目录/项目名写在同目录的 check-skill.config.json（示例见 .example.json）。

import { readdirSync, readFileSync, statSync, existsSync } from 'node:fs';
import { join } from 'node:path';

const argv = process.argv.slice(2);
const strict = argv.includes('--strict');
const root = argv.find(a => !a.startsWith('--'));

if (!root) {
  console.error('用法: node check-skill.mjs <skills目录> [--strict]');
  process.exit(2);
}
if (!existsSync(root) || !statSync(root).isDirectory()) {
  console.error(`目录不存在或不是目录：${root}`);
  process.exit(2);
}

// ---- 公司私有标识符：从配置文件读，不写死 ----
let CFG = {};
const cfgPath = new URL('./check-skill.config.json', import.meta.url);
try { CFG = JSON.parse(readFileSync(cfgPath, 'utf8')); } catch { }
const docsRoots = (CFG.docsRoots || []).map(p => p.toLowerCase());

const esc = s => s.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');

const GENERIC_PRIVATE = [
  { re: /\b(?:192\.168|10\.\d{1,3}|172\.(?:1[6-9]|2\d|3[01]))\.\d{1,3}\.\d{1,3}(?:\.\d{1,3})*\b/g, what: '内网 IP' },
  { re: /[A-Za-z]:\\Users\\[^\\\s]+/g, what: '本机用户目录' },
  { re: /\b[A-Za-z]:\\(?!Users\\|Windows\\|Program Files)[^\\\s"']+\\?/g, what: '本机盘符路径' },
];

const PRIVATE_PATTERNS = [
  ...GENERIC_PRIVATE,
  ...(CFG.privatePaths || []).map(p => ({
    re: new RegExp(esc(p), 'g'),
    what: '公司私有标识符',
  })),
];

const JUNK = ['.git', 'node_modules', '.DS_Store', 'Thumbs.db'];

const results = [];

for (const name of readdirSync(root)) {
  const dir = join(root, name);
  if (!statSync(dir).isDirectory()) continue;
  const skillMd = join(dir, 'SKILL.md');
  if (!existsSync(skillMd)) continue;

  const text = readFileSync(skillMd, 'utf8');
  const problems = [];
  const warnings = [];

  // ---- 1：目录 ----
  if (!existsSync(skillMd)) problems.push('缺少 SKILL.md');
  const entries = readdirSync(dir);
  const junk = entries.filter(e => JUNK.includes(e));
  const big = [];
  for (const e of entries) {
    try {
      const p = join(dir, e);
      if (statSync(p).isFile() && statSync(p).size > 5 * 1024 * 1024) big.push(`${e}(>5MB)`);
    } catch { }
  }
  if (junk.length || big.length) warnings.push(`目录里有不该共享的东西：${[...junk, ...big].join(', ')}`);

  // ---- 2：frontmatter ----
  const fm = text.match(/^---\r?\n([\s\S]*?)\r?\n---/);
  let descText = '';
  if (!fm) {
    problems.push('缺少 YAML frontmatter');
  } else {
    const body = fm[1];
    const mName = body.match(/^name:\s*(.+)$/m);
    if (!mName) problems.push('frontmatter 缺 name');
    else if (mName[1].trim().replace(/^["']|["']$/g, '') !== name)
      problems.push(`name「${mName[1].trim()}」与目录名「${name}」不一致`);

    // description：从 description: 起，到下一个顶层键为止（支持多行 / 块标量）
    const m = body.match(/^description:\s*([\s\S]*?)(?=^[A-Za-z_][\w-]*:\s|(?![\s\S]))/m);
    if (!m) problems.push('frontmatter 缺 description');
    else descText = m[1];
  }

  // ---- 3：description 里不许有 ** ----
  if (descText && /\*\*/.test(descText))
    problems.push('description 里有 **（YAML 会字面渲染；以 ** 开头还会直接解析失败）');

  // ---- 4：「适用环境」是个填过的块（不绑定任何领域）----
  const envSec = text.match(/^#{1,3}[^\n]*(?:适用环境|运行环境)[^\n]*\r?\n([\s\S]*?)(?=^#{1,3}\s|(?![\s\S]))/m);
  if (!envSec) {
    problems.push('缺少「适用环境」小节');
  } else {
    const b = envSec[1];
    const items = b.split(/\r?\n/).filter(l => /^\s*[-*]\s*\S/.test(l) || /^\s*\|[^|]+\|/.test(l)).length;
    if (items < 3) problems.push(`「适用环境」只有 ${items} 条，至少 3 条（平台/版本/运行条件/验证日期）`);
    if (!/\b\d{4}-\d{2}-\d{2}\b/.test(b)) problems.push('「适用环境」没有验证日期（YYYY-MM-DD）');
  }

  // ---- 5：实测数字要带出处（软检查，误报多，只提示）----
  const lines = text.split(/\r?\n/);
  const noSrc = [];
  lines.forEach((ln, i) => {
    if (!/\d/.test(ln)) return;
    if (!/(?:ms|MB|KB|s\/|毫秒|\d+\s*次|\d+\s*个变量)/.test(ln)) return;
    const hasDate = /\b\d{4}-\d{2}-\d{2}\b/.test(ln);
    const hasWhere = /(本机|现场|实测|未验证|未测|官方|手册|§|代码|复测)/.test(ln);
    if (hasDate && hasWhere) return;           // 日期 + 环境/动作 都写才算有出处
    noSrc.push(i + 1);
  });
  if (noSrc.length) warnings.push(`${noSrc.length} 行数字缺出处（第 ${noSrc.slice(0, 5).join(', ')} 行）—— 软检查，请人工核对`);

  // ---- 6：不含私有标识符（只提示）----
  for (const { re, what } of PRIVATE_PATTERNS) {
    for (const h of [...text.matchAll(re)].slice(0, 3)) {
      const hit = h[0];
      if (what === '本机盘符路径' && docsRoots.some(r => hit.toLowerCase().startsWith(r))) continue;
      const lineNo = text.slice(0, h.index).split(/\r?\n/).length;
      warnings.push(`第 ${lineNo} 行含${what}：${hit}`);
    }
  }

  // ---- 7：自包含 ----
  const memLinks = [...text.matchAll(/\[\[([^\]]+)\]\]/g)].map(m => m[1]);
  if (memLinks.length)
    problems.push(`含 ${memLinks.length} 处记忆双链（共享库里指不到）：${[...new Set(memLinks)].slice(0, 3).join(' / ')}；关键内容内联，否则删`);
  if (existsSync(join(dir, 'scripts')) && !/\bscripts[\/\\]/.test(text))
    problems.push('有 scripts/ 目录，但 SKILL.md 没写怎么调用');

  results.push({ name, problems, warnings });
}

let bad = 0;
for (const r of results) {
  if (r.problems.length) bad++;
  console.log(`${r.problems.length ? '⚠' : '✅'} ${r.name}`);
  for (const p of r.problems) console.log(`     · ${p}`);
  for (const w of r.warnings) console.log(`     ~ ${w}`);
}

console.log(`\n共 ${results.length} 个 skill，${bad} 个有结构问题。`);
if (bad && strict) {
  console.log('（--strict：按不合格退出）');
  process.exit(1);
}
console.log('（默认只报告，不拦人；要卡 CI 加 --strict）');
process.exit(0);
