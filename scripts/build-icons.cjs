// Export the editable vector master with a local browser. No npm packages required.
const fs = require('node:fs');
const path = require('node:path');
const os = require('node:os');
const { spawn } = require('node:child_process');
const { pathToFileURL } = require('node:url');
const assert = require('node:assert/strict');

const root = path.resolve(__dirname, '..');
const candidates = [process.env.EASYAG_ICON_BROWSER,
  'C:/Program Files/Google/Chrome/Application/chrome.exe',
  'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',
  '/Applications/Google Chrome.app/Contents/MacOS/Google Chrome',
  '/usr/bin/google-chrome', '/usr/bin/chromium'].filter(Boolean);
const executable = candidates.find(file => fs.existsSync(file));
if (!executable) throw new Error('Set EASYAG_ICON_BROWSER to a local Chrome or Edge executable');
const profile = fs.mkdtempSync(path.join(os.tmpdir(), 'easyag-icon-export-'));
const delay = ms => new Promise(resolve => setTimeout(resolve, ms));
const browser = spawn(executable, ['--headless=new', '--disable-gpu', '--no-first-run',
  '--no-default-browser-check', '--remote-debugging-port=0', '--user-data-dir=' + profile, 'about:blank'],
  { windowsHide: true, stdio: ['ignore', 'ignore', 'ignore'] });
let socket;
let sequence = 0;
const pending = new Map();
function send(method, params = {}) {
  const id = ++sequence;
  return new Promise((resolve, reject) => {
    const timeout = setTimeout(() => { pending.delete(id); reject(new Error('Browser timeout: ' + method)); }, 10000);
    pending.set(id, { resolve: result => { clearTimeout(timeout); resolve(result); }, reject: error => { clearTimeout(timeout); reject(error); } });
    socket.send(JSON.stringify({ id, method, params }));
  });
}
async function evaluate(expression) {
  const result = await send('Runtime.evaluate', { expression, returnByValue: true, awaitPromise: true });
  if (result.exceptionDetails) throw new Error(JSON.stringify(result.exceptionDetails));
  return result.result.value;
}

(async () => {
  try {
    let port;
    for (let attempt = 0; attempt < 100; attempt++) {
      const active = path.join(profile, 'DevToolsActivePort');
      if (fs.existsSync(active)) { port = Number(fs.readFileSync(active, 'utf8').split('\n')[0]); break; }
      await delay(100);
    }
    assert.ok(port, 'Browser did not start');
    const pages = await (await fetch('http://127.0.0.1:' + port + '/json/list')).json();
    socket = new WebSocket(pages.find(page => page.type === 'page').webSocketDebuggerUrl);
    await new Promise((resolve, reject) => { socket.addEventListener('open', resolve, { once: true }); socket.addEventListener('error', reject, { once: true }); });
    socket.addEventListener('message', event => {
      const message = JSON.parse(event.data);
      const callback = pending.get(message.id);
      if (!callback) return;
      pending.delete(message.id);
      if (message.error) callback.reject(new Error(JSON.stringify(message.error))); else callback.resolve(message.result);
    });
    await send('Page.enable');
    await send('Emulation.setDefaultBackgroundColorOverride', { color: { r: 0, g: 0, b: 0, a: 0 } });
    await send('Page.navigate', { url: pathToFileURL(path.join(root, 'assets', 'logo.svg')).href });
    for (let attempt = 0; attempt < 60; attempt++) {
      if (await evaluate("document.readyState === 'complete' && document.documentElement.tagName === 'svg'")) break;
      await delay(100);
    }
    assert.equal(await evaluate('document.documentElement.tagName'), 'svg');
    const images = new Map();
    for (const size of [16, 20, 24, 32, 40, 48, 64, 128, 256, 1024]) {
      await send('Emulation.setDeviceMetricsOverride', { width: size, height: size, deviceScaleFactor: 1, mobile: false });
      await evaluate(`document.documentElement.setAttribute('width', '${size}'); document.documentElement.setAttribute('height', '${size}');`);
      await delay(60);
      const { data } = await send('Page.captureScreenshot', { format: 'png', captureBeyondViewport: false, omitBackground: true });
      const png = Buffer.from(data, 'base64');
      assert.equal(png.readUInt32BE(16), size); assert.equal(png.readUInt32BE(20), size);
      images.set(size, png);
    }
    for (const [size, name] of [[32, 'logo-32.png'], [48, 'logo-48.png'], [64, 'logo-64.png'], [128, 'logo-128.png'], [256, 'logo.png'], [1024, 'logo-master.png']]) {
      fs.writeFileSync(path.join(root, 'assets', name), images.get(size));
    }
    for (const size of [32, 64]) fs.writeFileSync(path.join(root, 'assets', `logo-${size}.b64.txt`), images.get(size).toString('base64'));
    const sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256];
    const header = Buffer.alloc(6 + sizes.length * 16);
    header.writeUInt16LE(1, 2); header.writeUInt16LE(sizes.length, 4);
    let offset = header.length;
    sizes.forEach((size, index) => {
      const png = images.get(size), entry = 6 + index * 16;
      header[entry] = size === 256 ? 0 : size; header[entry + 1] = size === 256 ? 0 : size;
      header.writeUInt16LE(1, entry + 4); header.writeUInt16LE(32, entry + 6);
      header.writeUInt32LE(png.length, entry + 8); header.writeUInt32LE(offset, entry + 12);
      offset += png.length;
    });
    const ico = Buffer.concat([header, ...sizes.map(size => images.get(size))]);
    for (const file of ['assets/icon.ico', 'assets/logo.ico', 'src-tauri/icons/icon.ico']) fs.writeFileSync(path.join(root, file), ico);
    fs.writeFileSync(path.join(root, 'src-tauri/icons/icon.png'), images.get(256));
    for (const relative of ['index.html', 'src/capsule.html', 'src/index.html']) {
      const file = path.join(root, relative);
      let html = fs.readFileSync(file, 'utf8');
      html = html.replace(/<link\b[^>]*rel="(icon|apple-touch-icon)"[^>]*>/g, (_, kind) =>
        `<link rel="${kind}" type="image/png" href="data:image/png;base64,${images.get(kind === 'icon' ? 32 : 128).toString('base64')}">`);
      html = html.replace(/<img\b[^>]*data-easyag-logo="(\d+)"[^>]*>/g, (tag, size) =>
        tag.replace(/src="[^"]*"/, 'src="data:image/png;base64,' + images.get(Number(size)).toString('base64') + '"'));
      fs.writeFileSync(file, html);
    }
    console.log(JSON.stringify({ master: 'assets/logo.svg', icoSizes: sizes, pngSizes: [32, 48, 64, 128, 256, 1024] }));
  } finally {
    if (socket && socket.readyState === WebSocket.OPEN) { try { await send('Browser.close'); } catch {} socket.close(); }
    browser.kill();
  }
})().catch(error => { console.error(error); process.exitCode = 1; });
