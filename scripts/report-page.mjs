// Copyright 2026 Andre Kaufmann
// SPDX-License-Identifier: Apache-2.0
//
// Usage: node scripts/report-page.mjs docs/report-v1.md <out.html>
// Builds the artifact page from docs/report-v1.md (small Markdown subset: headings, paragraphs, lists, tables,
// blockquotes, inline code, bold, italic, links rendered as plain text).
import fs from "node:fs";
const [src, out] = process.argv.slice(2);
const md = fs.readFileSync(src, "utf8").replace(/\r\n/g, "\n");
const esc = s => s.replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;");
const inline = s => {
  const parts = s.split(/(`[^`]*`)/);
  return parts.map(p => p.startsWith("`") && p.endsWith("`") && p.length > 1
    ? `<code>${esc(p.slice(1, -1))}</code>`
    : esc(p)
        .replace(/\[([^\]]+)\]\([^)]+\)/g, "$1")
        .replace(/\*\*([^*]+)\*\*/g, "<strong>$1</strong>")
        .replace(/(^|[\s(])\*([^*\s][^*]*)\*/g, "$1<em>$2</em>")).join("");
};
const lines = md.split("\n");
let html = "", i = 0, title = "";
const cells = l => l.trim().replace(/^\|/, "").replace(/\|$/, "").split("|").map(c => c.trim());
while (i < lines.length) {
  const l = lines[i];
  if (!l.trim()) { i++; continue; }
  let m;
  if ((m = /^(#{1,3}) (.*)$/.exec(l))) {
    const level = m[1].length;
    if (level === 1) { title = m[2]; i++; continue; }
    const id = m[2].toLowerCase().replace(/[^a-z0-9]+/g, "-").replace(/^-|-$/g, "");
    html += `<h${level} id="${id}">${inline(m[2])}</h${level}>\n`; i++; continue;
  }
  if (l.startsWith("|")) {
    const head = cells(l), align = cells(lines[i + 1]).map(a => a.endsWith(":") ? "num" : "");
    i += 2;
    let t = `<div class="tw"><table><thead><tr>${head.map((h, k) => `<th class="${align[k]}">${inline(h)}</th>`).join("")}</tr></thead><tbody>`;
    while (i < lines.length && lines[i].startsWith("|")) {
      t += `<tr>${cells(lines[i]).map((c, k) => `<td class="${align[k]}">${inline(c)}</td>`).join("")}</tr>`; i++;
    }
    html += t + "</tbody></table></div>\n"; continue;
  }
  if (l.startsWith("> ")) {
    let q = "";
    while (i < lines.length && lines[i].startsWith(">")) { q += lines[i].replace(/^> ?/, "") + " "; i++; }
    html += `<blockquote>${inline(q.trim())}</blockquote>\n`; continue;
  }
  if (/^\s*- /.test(l)) {
    // nested lists by indentation (two spaces per level)
    const root = { kids: [] }, path = [root];
    while (i < lines.length && /^\s*- /.test(lines[i])) {
      const depth = Math.floor(lines[i].match(/^\s*/)[0].length / 2);
      const node = { text: lines[i].replace(/^\s*- /, ""), kids: [] };
      path.length = Math.min(path.length, depth + 1);
      path[path.length - 1].kids.push(node);
      path.push(node);
      i++;
    }
    const render = n => `<ul>${n.kids.map(k => `<li>${inline(k.text)}${k.kids.length ? render(k) : ""}</li>`).join("")}</ul>`;
    html += render(root) + "\n"; continue;
  }
  let p = "";
  while (i < lines.length && lines[i].trim() && !/^(#|\||> |\s*- )/.test(lines[i])) { p += lines[i] + " "; i++; }
  html += `<p>${inline(p.trim())}</p>\n`;
}

// Ratio scale: G with its interval on a log axis, the 1.00 and 1.20 lines of the decision rule.
const rows = [
  ["Parsing, faithful (Track F)", "endpoint", 6.31, 6.20, 6.41],
  ["Parsing, optimized", "endpoint", 0.60, 0.59, 0.61],
  ["Parsing, optimized", "last additional", 0.40, 0.39, 0.40],
  ["Parse+bind", "endpoint", 1.207, 1.191, 1.219],
  ["Parse+bind", "last additional", 0.425, 0.419, 0.431],
  ["Parse+bind+check, 4 checkers", "endpoint", 1.680, 1.675, 1.689],
  ["Parse+bind+check, 4 checkers", "additional after CO6", 0.970, 0.965, 0.976],
  ["Parse+bind+check, 4 checkers", "additional after MM1 (tuned on corpus)", 0.756, 0.753, 0.759],
  ["12 checkers, Workstation GC", "§18", 1.305, 1.292, 1.315],
  ["12 checkers, Server GC", "§18", 0.890, 0.878, 0.900],
  ["12 checkers, Workstation GC", "additional after CO6", 1.236, 1.226, 1.244],
  ["Parse+bind+check+emit, 4 checkers", "endpoint (§19)", 1.030, 1.026, 1.035],
  ["Parse+bind+check+emit, 4 checkers", "additional after MM1 (tuned on corpus)", 0.782, 0.779, 0.786],
];
const rowsRs = [
  ["Check, tsrs's kind of pool, Server GC", "§20, 16 checkers (98.75 %)", 1.832, 1.796, 1.866],
  ["Check, tsrs's kind of pool, Workstation GC", "§20, 12 checkers", 2.149, 2.122, 2.182],
  ["Check, 4 checkers, Workstation GC", "additional after MM1", 3.794, 3.764, 3.819],
  ["Check, single-threaded vs tsrs single", "additional after MM1", 1.466, 1.462, 1.469],
  ["typescript-go, 12 checkers", "same §20 run", 3.20, 3.16, 3.24],
  ["typescript-go, 4 checkers", "same §20 run", 5.10, 5.04, 5.14],
];
function chart(rows, label, ticks, loV, hiV) {
  const W = 720, left = 290, right = 30, top = 34, rowH = 30, H = top + rows.length * rowH + 12;
  const lo = Math.log(loV), hi = Math.log(hiV);
  const x = v => left + (Math.log(v) - lo) / (hi - lo) * (W - left - right);
  let svg = `<svg viewBox="0 0 ${W} ${H}" role="img" aria-label="${label}">`;
  for (const t of ticks) svg += `<line class="grid" x1="${x(t)}" x2="${x(t)}" y1="${top - 8}" y2="${H - 6}"/><text class="tick" x="${x(t)}" y="${top - 14}" text-anchor="middle">${t}</text>`;
  svg += `<rect class="band" x="${x(1)}" y="${top - 8}" width="${x(1.2) - x(1)}" height="${H - top + 2}"/>`;
  svg += `<line class="rule" x1="${x(1.2)}" x2="${x(1.2)}" y1="${top - 8}" y2="${H - 6}"/><line class="one" x1="${x(1)}" x2="${x(1)}" y1="${top - 8}" y2="${H - 6}"/>`;
  rows.forEach(([name, kind, g, a, b], k) => {
    const y = top + k * rowH + rowH / 2;
    const cls = b < 1 ? "fast" : b <= 1.2 ? "within" : a > 1.2 ? "no" : "inc";
    svg += `<text class="lab" x="0" y="${y - 2}">${esc(name)}</text><text class="sub" x="0" y="${y + 11}">${esc(kind)}</text>`;
    svg += `<line class="ci ${cls}" x1="${x(a)}" x2="${x(b)}" y1="${y}" y2="${y}"/><circle class="pt ${cls}" cx="${x(g)}" cy="${y}" r="5"/>`;
    const flip = x(g) > W - right - 60;
    svg += `<text class="val" x="${x(g) + (flip ? -10 : 10)}" y="${y + 4}" text-anchor="${flip ? "end" : "start"}">${g}</text>`;
  });
  return svg + `</svg>`;
}
const svg = chart(rows, "G against typescript-go on a log scale", [0.3, 0.5, 1, 2, 4, 8], 0.3, 8);
const svgRs = chart(rowsRs, "Factor against tsrs on a log scale", [0.8, 1, 2, 4, 6], 0.8, 6);

const page = `<!DOCTYPE html>
<html lang="en">
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1, viewport-fit=cover">
<title>NeverSharp Report v1.4</title>
<meta name="claude:viewport-fit-sides" content="cover">
<link rel="preconnect" href="https://fonts.googleapis.com">
<link rel="stylesheet" href="https://fonts.googleapis.com/css2?family=Spectral:wght@500;600&family=Public+Sans:ital,wght@0,400;0,600;1,400&family=JetBrains+Mono:wght@400;500&display=swap">
<style>
/* Layout: one reading column; the ratio scale opens the page as the report's thesis. */
:root {
  --bg: #f6f7f9; --surface: #ffffff; --fg: #1b2230; --muted: #5a6577; --line: #d9dee6;
  --accent: #3b5bdb; --fast: #1f8a5b; --within: #3b7fc4; --no: #c2452d; --inc: #b7791f; --band: rgba(59,91,219,.08);
  --display: "Spectral", Georgia, serif; --body: "Public Sans", system-ui, sans-serif; --mono: "JetBrains Mono", ui-monospace, Consolas, monospace;
}
@media (prefers-color-scheme: dark) { :root:not([data-theme="light"]) {
  --bg: #12161d; --surface: #1a2029; --fg: #e3e7ee; --muted: #9aa5b6; --line: #2c3442;
  --accent: #8ea6ff; --fast: #4cc38a; --within: #6fb0f0; --no: #f0785f; --inc: #e0aa4a; --band: rgba(142,166,255,.10); color-scheme: dark } }
:root[data-theme="dark"] {
  --bg: #12161d; --surface: #1a2029; --fg: #e3e7ee; --muted: #9aa5b6; --line: #2c3442;
  --accent: #8ea6ff; --fast: #4cc38a; --within: #6fb0f0; --no: #f0785f; --inc: #e0aa4a; --band: rgba(142,166,255,.10); color-scheme: dark }
body { background: var(--bg); color: var(--fg); font: 16px/1.6 var(--body); }
main { max-width: 52rem; margin: 0 auto; padding-block: 2.5rem 4rem; padding-left: max(16px, env(safe-area-inset-left, 0px)); padding-right: max(16px, env(safe-area-inset-right, 0px)); display: grid; gap: .25rem; }
main > * { min-width: 0; }
.eyebrow { font: 500 .75rem var(--mono); letter-spacing: .08em; text-transform: uppercase; color: var(--muted); margin: 0; }
h1 { font: 600 2.4rem/1.15 var(--display); margin: .2rem 0 .6rem; text-wrap: balance; }
h2 { font: 600 1.55rem/1.25 var(--display); margin: 2.4rem 0 .4rem; padding-top: 1rem; border-top: 1px solid var(--line); text-wrap: balance; }
h3 { font: 600 1.15rem/1.3 var(--body); margin: 1.6rem 0 .2rem; }
p, li { max-width: 68ch; }
p { margin: .5rem 0; }
ul { margin: .3rem 0 .6rem; padding-left: 1.25rem; }
li { margin: .15rem 0; }
code { font: .86em var(--mono); background: var(--surface); border: 1px solid var(--line); border-radius: 4px; padding: 0 .3em; overflow-wrap: anywhere; }
strong { font-weight: 600; }
blockquote { margin: .8rem 0; padding: .7rem 1rem; background: var(--surface); border-left: 3px solid var(--accent); border-radius: 0 6px 6px 0; }
.tw { overflow-x: auto; margin: .6rem 0 1rem; border: 1px solid var(--line); border-radius: 8px; background: var(--surface); }
table { border-collapse: collapse; width: 100%; font-size: .88rem; }
th, td { text-align: left; padding: .45rem .7rem; border-bottom: 1px solid var(--line); vertical-align: top; }
tr:last-child td { border-bottom: 0; }
th { font-weight: 600; color: var(--muted); font-size: .78rem; letter-spacing: .02em; }
td.num, th.num { text-align: right; font-variant-numeric: tabular-nums; }
td { font-variant-numeric: tabular-nums; }
figure { margin: .6rem 0 1.2rem; background: var(--surface); border: 1px solid var(--line); border-radius: 10px; padding: 1rem; overflow-x: auto; }
figure svg { min-width: 560px; width: 100%; height: auto; display: block; }
figcaption { font-size: .82rem; color: var(--muted); margin-top: .6rem; max-width: 68ch; }
.legend { display: flex; flex-wrap: wrap; gap: .4rem 1rem; font-size: .8rem; color: var(--muted); margin-top: .5rem; }
.legend span::before { content: ""; display: inline-block; width: .6rem; height: .6rem; border-radius: 50%; margin-right: .35rem; background: var(--c); vertical-align: 0; }
svg .grid { stroke: var(--line); stroke-width: 1; }
svg .band { fill: var(--band); }
svg .rule { stroke: var(--no); stroke-dasharray: 4 3; stroke-width: 1.2; }
svg .one { stroke: var(--muted); stroke-width: 1.2; }
svg .tick { fill: var(--muted); font: 11px var(--mono); }
svg .lab { fill: var(--fg); font: 600 12.5px var(--body); }
svg .sub { fill: var(--muted); font: 11px var(--body); }
svg .val { fill: var(--fg); font: 500 11.5px var(--mono); }
svg .ci { stroke-width: 6; stroke-linecap: round; opacity: .35; }
svg .pt { stroke: var(--surface); stroke-width: 1.5; }
svg .fast { stroke: var(--fast); fill: var(--fast); } svg .within { stroke: var(--within); fill: var(--within); }
svg .no { stroke: var(--no); fill: var(--no); } svg .inc { stroke: var(--inc); fill: var(--inc); }
svg .pt.fast, svg .pt.within, svg .pt.no, svg .pt.inc { stroke: var(--surface); }
</style>
<main>
<p class="eyebrow">NeverSharp · C# port of TypeScript 6.0.3 vs typescript-go 7.0.2</p>
<h1>${esc(title.replace(/ —.*$/, ""))}</h1>
<p class="eyebrow">Windows only · reference machine Ryzen 9 3900X</p>
<figure>${svg}
<div class="legend"><span style="--c:var(--fast)">faster (upper bound &lt; 1.00)</span><span style="--c:var(--within)">within 20 %</span><span style="--c:var(--inc)">inconclusive</span><span style="--c:var(--no)">no parity (lower bound &gt; 1.20)</span></div>
<figcaption>G = geometric mean over the five repositories of median(C#) / median(typescript-go), log scale. Dot: G; bar: its interval (95 %, Server GC 98.75 %). Shaded: 1.00 to 1.20; dashed: the 1.20 limit of the decision rule. Endpoints are the pre-registered results; additional runs never replace them.</figcaption>
</figure>
<figure>${svgRs}
<figcaption>Secondary: factor = median(C# or typescript-go) / median(tsrs 0.18.0), geometric mean over the five repositories, log scale; lower is better. The C# rows after MM1 were tuned on these repositories (pre-registration §20). With work stealing the parallel diagnostics vary between runs.</figcaption>
</figure>
${html}
</main>`;
fs.writeFileSync(out, page);
console.log("written", out, page.length);
