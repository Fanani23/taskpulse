'use strict';

const $ = (id) => document.getElementById(id);
const now = () => new Date().toISOString().slice(11, 23);
const fmtDuration = (s) => {
  if (!Number.isFinite(s)) return '—';
  const h = Math.floor(s / 3600), m = Math.floor((s % 3600) / 60), sec = Math.floor(s % 60);
  return h ? `${h}h ${m}m` : m ? `${m}m ${sec}s` : `${sec}s`;
};
const CLOSE_REASONS = {
  1000: 'normal closure', 1001: 'endpoint going away (server shutdown / navigation)',
  1005: 'no status received', 1006: 'connection dropped without a close frame (abnormal)',
  1008: 'policy violation', 1009: 'message too big', 1011: 'server error', 1012: 'service restart',
};

(() => {
  const root = document.documentElement;
  let stored = null;
  try { stored = localStorage.getItem('theme'); } catch { }
  if (stored) root.dataset.theme = stored;
  $('theme').addEventListener('click', () => {
    const dark = matchMedia('(prefers-color-scheme: dark)').matches;
    const current = root.dataset.theme || (dark ? 'dark' : 'light');
    const next = current === 'dark' ? 'light' : 'dark';
    root.dataset.theme = next;
    try { localStorage.setItem('theme', next); } catch { }
  });
})();

const url = `${location.protocol === 'https:' ? 'wss' : 'ws'}://${location.host}/ws`;
$('endpoint').textContent = url;

const state = {
  ws: null,
  myId: null,
  sent: 0,
  received: 0,
  wantOpen: false,
  reconnectAttempt: 0,
  reconnectTimer: null,
  pingSentAt: null,
  filters: { in: true, out: true, sys: true },
  count: 0,
};

const log = $('log');

function render(kind, badge, body) {
  const row = document.createElement('div');
  row.className = `msg ${kind}`;
  row.hidden = !state.filters[kind === 'err' ? 'in' : kind];

  const t = document.createElement('time');
  t.textContent = now();

  const b = document.createElement('span');
  b.className = 'badge';
  b.textContent = badge;

  const c = document.createElement('div');
  c.className = 'body';
  if (typeof body === 'string') c.textContent = body;
  else c.append(...body);

  row.append(t, b, c);
  log.appendChild(row);
  state.count++;
  $('count').textContent = `${state.count} message${state.count === 1 ? '' : 's'}`;
  if ($('autoScroll').checked) log.scrollTop = log.scrollHeight;
}

const kv = (key, value) => {
  const k = document.createElement('span'); k.className = 'k'; k.textContent = `${key}=`;
  const v = document.createElement('span'); v.textContent = value;
  return [k, v, document.createTextNode('  ')];
};

function renderIncoming(raw) {
  let m;
  try { m = JSON.parse(raw); } catch { render('in', 'text', raw); return; }

  switch (m.type) {
    case 'welcome':
      state.myId = m.connectionId;
      $('myId').textContent = m.connectionId;
      $('connCount').textContent = m.connections;
      render('sys', 'welcome', [...kv('you', m.connectionId), ...kv('connections', m.connections)]);
      break;
    case 'system':
      $('connCount').textContent = m.connections;
      render('sys', m.event, [...kv('client', m.connectionId), ...kv('connections', m.connections)]);
      break;
    case 'pong':
      if (state.pingSentAt) { $('rtt').textContent = `${(performance.now() - state.pingSentAt).toFixed(1)} ms`; state.pingSentAt = null; }
      render('in', 'pong', kv('from', m.from));
      break;
    case 'echo':
    case 'broadcast': {
      const from = document.createElement('span');
      from.className = 'from';
      from.textContent = m.from === state.myId ? 'you' : (m.actor ?? m.from);
      render('in', m.type, [from, document.createTextNode(': '), document.createTextNode(m.data ?? '')]);
      break;
    }
    case 'authed':
      state.user = m.user;
      render('sys', 'authed', kv('signed in as', m.user));
      break;
    case 'changed':
      render('in', 'changed', [...kv('who', m.actor ?? 'anonymous'), ...kv('did', `${m.action} ${m.resource}${m.kind ? '/' + m.kind : ''}`), ...kv('id', String(m.id).slice(0, 8))]);
      break;
    case 'error':
      render('err', 'error', m.error ?? raw);
      break;
    default:
      render('in', m.type ?? 'message', raw);
  }
}

function setStatus(stateName, label) {
  const pill = $('status');
  pill.dataset.state = stateName;
  pill.textContent = label;
  const open = stateName === 'open';
  $('connect').disabled = open || stateName === 'connecting';
  $('disconnect').disabled = !open && stateName !== 'connecting';
  $('send').disabled = !open;
  if (!open) { $('myId').textContent = '—'; }
}

function connect() {
  clearTimeout(state.reconnectTimer);
  state.wantOpen = true;
  setStatus('connecting', state.reconnectAttempt ? `reconnecting (try ${state.reconnectAttempt})` : 'connecting…');

  const ws = new WebSocket(url);
  state.ws = ws;

  ws.addEventListener('open', () => {
    state.reconnectAttempt = 0;
    setStatus('open', 'connected');
    render('sys', 'open', url);
    $('text').focus();
  });

  ws.addEventListener('message', (e) => {
    state.received++; $('received').textContent = state.received;
    renderIncoming(e.data);
  });

  ws.addEventListener('error', () => render('err', 'error', 'socket error — see browser console'));

  ws.addEventListener('close', (e) => {
    state.ws = null;
    setStatus('closed', 'disconnected');
    const why = CLOSE_REASONS[e.code] ?? 'unknown code';
    render(e.wasClean ? 'sys' : 'err', 'closed',
      [...kv('code', `${e.code} (${why})`), ...kv('reason', e.reason || '—'), ...kv('clean', e.wasClean)]);
    scheduleReconnect();
  });
}

function disconnect() {
  state.wantOpen = false;
  state.reconnectAttempt = 0;
  clearTimeout(state.reconnectTimer);
  state.ws?.close(1000, 'user clicked disconnect');
}

function scheduleReconnect() {
  if (!state.wantOpen || !$('autoReconnect').checked) return;
  state.reconnectAttempt++;
  const delay = Math.min(30_000, 500 * 2 ** (state.reconnectAttempt - 1)) * (0.8 + Math.random() * 0.4);
  render('sys', 'retry', `reconnecting in ${(delay / 1000).toFixed(1)}s (attempt ${state.reconnectAttempt})`);
  state.reconnectTimer = setTimeout(connect, delay);
}

function send(type, text) {
  if (state.ws?.readyState !== WebSocket.OPEN) return;
  const payload = type === 'raw' ? text : JSON.stringify(type === 'ping' ? { type } : type === 'auth' ? { type, token: text.trim() } : { type, data: text });
  if (type === 'ping') state.pingSentAt = performance.now();
  state.ws.send(payload);
  state.sent++; $('sent').textContent = state.sent;
  // Never echo a token into the log.
  render('out', type, type === 'auth' ? '{"type":"auth","token":"…"}' : payload);
}

async function pollStats() {
  try {
    const r = await fetch('/stats', { cache: 'no-store' });
    if (!r.ok) throw new Error(r.status);
    const s = await r.json();
    $('uptime').textContent = fmtDuration(s.uptimeSeconds);
    $('connCount').textContent = s.connections;
    const ul = $('clients');
    ul.replaceChildren(...(s.clients.length ? s.clients.map((c) => {
      const li = document.createElement('li');
      if (c.id === state.myId) li.classList.add('me');
      const id = document.createElement('span'); id.className = 'id'; id.textContent = (c.id === state.myId ? `${c.id} (you)` : c.id) + (c.user ? ` · ${c.user}` : '');
      const n = document.createElement('span'); n.className = 'muted'; n.textContent = `↓${c.messagesReceived} ↑${c.messagesSent}`;
      li.append(id, n);
      return li;
    }) : [Object.assign(document.createElement('li'), { className: 'muted', textContent: 'none' })]));
    $('clientsNote').textContent = '(from /stats)';
  } catch {
    $('clientsNote').textContent = '(/stats unreachable)';
  }
}

$('connect').addEventListener('click', () => { state.reconnectAttempt = 0; connect(); });
$('disconnect').addEventListener('click', disconnect);
$('composer').addEventListener('submit', (e) => { e.preventDefault(); send($('type').value, $('text').value); });
$('clear').addEventListener('click', () => { log.replaceChildren(); state.count = 0; $('count').textContent = '0 messages'; });
document.addEventListener('keydown', (e) => { if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'k') { e.preventDefault(); $('clear').click(); } });
document.querySelectorAll('.chip[data-filter]').forEach((chip) => chip.addEventListener('click', () => {
  const f = chip.dataset.filter;
  state.filters[f] = !state.filters[f];
  chip.setAttribute('aria-pressed', String(state.filters[f]));
  log.querySelectorAll(`.msg.${f}${f === 'in' ? ', .msg.err' : ''}`).forEach((row) => { row.hidden = !state.filters[f]; });
}));
window.addEventListener('beforeunload', () => { state.wantOpen = false; state.ws?.close(1000, 'page unload'); });

pollStats();
setInterval(pollStats, 3000);
connect();
