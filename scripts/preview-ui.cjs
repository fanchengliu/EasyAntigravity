// A standalone preview of the real UI. All API calls stay inside the preview.
const fs = require('node:fs');
const path = require('node:path');

const root = path.resolve(__dirname, '..');
const output = path.resolve(process.argv[2] || path.join(root, '.ui-preview', 'index.html'));
const panel = fs.readFileSync(path.join(root, 'index.html'), 'utf8');
const capsule = fs.readFileSync(path.join(root, 'src', 'capsule.html'), 'utf8');
const literal = value => JSON.stringify(value).replace(/</g, '\\u003c');

const html = `<!doctype html>
<html lang="zh-CN">
<head>
<meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
<title>EasyAG · 外观预览</title>
<style>
  * { box-sizing: border-box; }
  body { margin: 0; padding: 42px 32px; background: #f3f3f3; color: #202020; font: 14px/1.6 "Segoe UI", "Microsoft YaHei UI", sans-serif; }
  main { max-width: 980px; margin: auto; }
  .eyebrow { color: #737373; font-size: 11px; letter-spacing: 1.4px; }
  h1 { margin: 8px 0; font-size: 29px; font-weight: 600; letter-spacing: -.5px; }
  .intro { color: #666; margin: 0 0 28px; }
  .layout { display: grid; grid-template-columns: 490px 400px; gap: 36px; align-items: start; }
  .section-head { display: flex; align-items: center; justify-content: space-between; margin-bottom: 12px; }
  h2 { margin: 0; font-size: 13px; font-weight: 600; }
  .caption { margin: 9px 0 20px; color: #737373; font-size: 12px; }
  .panel { width: 490px; height: 740px; border: 1px solid #d4d4d4; border-radius: 9px; background: #f3f3f3; box-shadow: 0 8px 24px #0000000a; overflow: hidden; }
  iframe { display: block; width: 100%; height: 100%; border: 0; }
  .notification { width: 400px; height: 200px; border-radius: 10px; overflow: hidden; box-shadow: 0 4px 14px #0000000d; }
  .states { display: flex; gap: 6px; flex-wrap: wrap; margin-bottom: 20px; }
  button { border: 1px solid #d3d3d3; background: #fafafa; color: #4b4b4b; border-radius: 5px; padding: 5px 11px; font-size: 12px; line-height: 1.5; font-family: inherit; cursor: pointer; }
  button:hover { background: #e8e8e8; }
  button[aria-pressed="true"] { background: #e8f1fa; border-color: #0f6cbd; color: #0f6cbd; }
  button:focus-visible { outline: 2px solid #0f6cbd; outline-offset: 2px; }
  .notes { border-top: 1px solid #ddd; margin-top: 26px; padding-top: 18px; color: #666; font-size: 12px; line-height: 1.9; }
  @media (max-width: 1000px) { .layout { grid-template-columns: 1fr; } .panel { max-width: 100%; } }
</style>
</head>
<body><main>
<div class="eyebrow">EASYAG / WINDOWS STYLE</div>
<h1>更安静的工作提醒</h1>
<p class="intro">清晰的内容层级、柔和的状态色，以及随系统切换的明暗主题。</p>
<div class="layout">
  <section><div class="section-head"><h2>控制面板</h2><button id="theme" aria-pressed="false">切换为深色</button></div>
    <div class="panel"><iframe id="panel" title="控制面板预览"></iframe></div>
    <p class="caption">独立演示数据，按钮不会启动 Antigravity 或修改实际设置。</p>
  </section>
  <section><div class="section-head"><h2>通知</h2></div>
    <div class="states" role="group" aria-label="通知状态">
      <button data-state="danger" aria-pressed="true">需要确认</button><button data-state="interaction" aria-pressed="false">等待选择</button><button data-state="ready" aria-pressed="false">任务完成</button>
    </div>
    <div class="notification"><iframe id="light" title="浅色通知预览"></iframe></div><p class="caption">浅色 · 400 × 200</p>
    <div class="notification"><iframe id="dark" title="深色通知预览"></iframe></div><p class="caption">深色 · 400 × 200</p>
    <div class="notes">小面积状态色，减少视觉干扰。<br>标题和详情可换行，长命令不会挤出按钮。<br>保留关闭、前往查看和悬停暂停。</div>
  </section>
</div>
</main>
<script>
  const panelSource = ${literal(panel)};
  const capsuleSource = ${literal(capsule)};
  const samples = {
    danger: ['命令需要人工确认', '这个操作会删除目录中的文件。请先确认路径，再决定是否继续。'],
    interaction: ['有一个方案需要你选择', 'Antigravity 已暂停，等待你确认下一步的实现方式。'],
    ready: ['本轮任务已完成', '更改已整理完毕，可以回到 Antigravity 检查结果。']
  };
  let selected = 'danger';
  let panelTheme = 'light';
  const mock = '(' + function () {
    window.__previewRequests = [];
    const state = { port: 7890, launchMode: 'pilot', blockDangerous: true, riskAdvisor: true, enableI18n: true, dictEntries: 248, dangerRulesOn: 19, dangerRulesTotal: 19, kbRules: 230, clientRunning: false, riskHits: 0, approveCount: 0, blockCount: 0 };
    window.fetch = async function (url, options) {
      window.__previewRequests.push({ url: String(url), method: options && options.method || 'GET' });
      const value = String(url).includes('/api/logs') ? [
        { category: 'SYSTEM', message: '通知外观预览已就绪。', cls: '' },
        { category: 'SECURITY', message: '高危规则已加载：19/19 条生效', cls: 'tag-proxy' }
      ] : state;
      return { ok: true, status: 200, json: async () => value, text: async () => 'ok' };
    };
    window.EventSource = class { close() {} };
    navigator.sendBeacon = function () { return true; };
    window.__TAURI__ = { window: { getCurrentWindow: () => ({ hide: () => {} }) } };
  }.toString() + ')();';
  function frameHtml(source, theme, state) {
    let result = source.replace(/@media \\(prefers-color-scheme: light\\)/g, theme === 'light' ? '@media all' : '@media not all');
    const csp = ${literal("<meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; script-src 'unsafe-inline'; style-src 'unsafe-inline'; img-src data:;\">")};
    result = result.replace('<head>', '<head>' + csp + '<script>' + mock + '<' + '/script>');
    if (state) {
      const sample = samples[state];
      const call = 'window.__ea_capsule(' + JSON.stringify(state) + ',' + JSON.stringify(sample[0]) + ',' + JSON.stringify(sample[1]) + ');pauseTimer();';
      result = result.replace('</body>', '<script>' + call + '<' + '/script></body>');
    }
    return result;
  }
  function renderNotices() {
    document.getElementById('light').srcdoc = frameHtml(capsuleSource, 'light', selected);
    document.getElementById('dark').srcdoc = frameHtml(capsuleSource, 'dark', selected);
  }
  document.querySelectorAll('[data-state]').forEach(button => button.addEventListener('click', () => {
    selected = button.dataset.state;
    document.querySelectorAll('[data-state]').forEach(item => item.setAttribute('aria-pressed', String(item === button)));
    renderNotices();
  }));
  document.getElementById('theme').addEventListener('click', function () {
    panelTheme = panelTheme === 'light' ? 'dark' : 'light';
    this.textContent = panelTheme === 'light' ? '切换为深色' : '切换为浅色';
    this.setAttribute('aria-pressed', String(panelTheme === 'dark'));
    document.getElementById('panel').srcdoc = frameHtml(panelSource, panelTheme);
  });
  document.getElementById('panel').srcdoc = frameHtml(panelSource, panelTheme);
  renderNotices();
</script></body></html>`;

fs.mkdirSync(path.dirname(output), { recursive: true });
fs.writeFileSync(output, html, 'utf8');
console.log(output);
