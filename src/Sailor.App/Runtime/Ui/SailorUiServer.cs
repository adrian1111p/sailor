using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Sailor.App.Runtime.Common;

namespace Sailor.App.Runtime.Ui;

public sealed class SailorUiServer
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    private readonly SailorRuntimeMode _mode;
    private readonly SailorUiSnapshotProvider _snapshotProvider;
    private readonly SailorUiDesiredStateStore _desiredStateStore;
    private readonly string _host;
    private readonly int _port;
    private readonly bool _controlsEnabled;
    private readonly Action<string> _log;

    public SailorUiServer(
        SailorRuntimeMode mode,
        SailorUiSnapshotProvider snapshotProvider,
        SailorUiDesiredStateStore desiredStateStore,
        string host,
        int port,
        bool controlsEnabled,
        Action<string> log)
    {
        _mode = mode;
        _snapshotProvider = snapshotProvider;
        _desiredStateStore = desiredStateStore;
        _host = string.IsNullOrWhiteSpace(host) ? "127.0.0.1" : host.Trim();
        _port = Math.Max(1, port);
        _controlsEnabled = controlsEnabled && mode == SailorRuntimeMode.Paper;
        _log = log;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        IPAddress address = _host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            ? IPAddress.Loopback
            : IPAddress.TryParse(_host, out IPAddress? parsed)
                ? parsed
                : IPAddress.Loopback;

        var listener = new TcpListener(address, _port);
        listener.Start();
        string milestone = _controlsEnabled ? "067" : _mode == SailorRuntimeMode.Live ? "069" : "066";
        _log($"SAILOR-{milestone} SailorUI monitor is listening at http://localhost:{_port}/");
        if (_controlsEnabled)
        {
            _log("SAILOR-067 paper desired-state controls are enabled. Browser actions persist desired state only; paper runtime safety gates remain server-side.");
            _log($"SAILOR-067 desired state JSON: {_desiredStateStore.LatestStatePath}");
            _log($"SAILOR-067 action CSV: {_desiredStateStore.ActionCsvPath}");
        }
        else if (_mode == SailorRuntimeMode.Live)
        {
            _log("SAILOR-069 live SailorUI hardening is active: UI is read-only, loopback-only, and cannot persist desired-state actions.");
            _log(SailorUiLiveHardening.LiveControlsForbiddenReason);
        }
        else
        {
            _log("SAILOR-066 uses a lightweight loopback HTTP server, updates the browser every second, and sends no broker orders.");
        }
        _log("Press Ctrl+C to stop SailorUI.");

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                TcpClient client = await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
                _ = Task.Run(() => HandleClientAsync(client, cancellationToken), cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal Ctrl+C shutdown.
        }
        finally
        {
            listener.Stop();
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        await using NetworkStream stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        string? requestLine = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(requestLine))
        {
            return;
        }

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        while (!cancellationToken.IsCancellationRequested)
        {
            string? header = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrEmpty(header))
            {
                break;
            }

            int separator = header.IndexOf(':', StringComparison.Ordinal);
            if (separator > 0)
            {
                headers[header[..separator].Trim()] = header[(separator + 1)..].Trim();
            }
        }

        string[] parts = requestLine.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        string method = parts.Length >= 1 ? parts[0] : "GET";
        string path = parts.Length >= 2 ? parts[1] : "/";
        path = path.Split('?', 2)[0];

        if (path.Equals(SailorUiContract.SnapshotEndpoint, StringComparison.OrdinalIgnoreCase))
        {
            SailorUiSnapshot snapshot = _snapshotProvider.ReadSnapshot();
            string json = JsonSerializer.Serialize(snapshot, JsonOptions);
            await WriteResponseAsync(stream, "200 OK", "application/json; charset=utf-8", json, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (path.Equals(SailorUiContract.HealthEndpoint, StringComparison.OrdinalIgnoreCase))
        {
            string health = JsonSerializer.Serialize(new
            {
                status = "ok",
                mode = _mode.ToDisplayName(),
                readOnly = !_controlsEnabled,
                controlsEnabled = _controlsEnabled,
                controlMode = SailorUiLiveHardening.ResolveControlMode(_mode, _controlsEnabled),
                liveUiLocked = _mode == SailorRuntimeMode.Live,
                desiredStatePath = _mode == SailorRuntimeMode.Live ? "n/a" : _desiredStateStore.LatestStatePath,
                exportEndpoint = SailorUiContract.ExportEndpoint
            }, JsonOptions);
            await WriteResponseAsync(stream, "200 OK", "application/json; charset=utf-8", health, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (path.Equals(SailorUiContract.ExportEndpoint, StringComparison.OrdinalIgnoreCase))
        {
            if (!method.Equals("GET", StringComparison.OrdinalIgnoreCase) && !method.Equals("POST", StringComparison.OrdinalIgnoreCase))
            {
                string error = JsonSerializer.Serialize(new { accepted = false, rejectedReason = "GET or POST required." }, JsonOptions);
                await WriteResponseAsync(stream, "405 Method Not Allowed", "application/json; charset=utf-8", error, cancellationToken).ConfigureAwait(false);
                return;
            }

            SailorUiSnapshot snapshot = _snapshotProvider.ReadSnapshot();
            SailorUiReportExportResult result = new SailorUiReportExporter(_mode).Write(snapshot);
            _log(result.ToSummaryString());
            string json = JsonSerializer.Serialize(new
            {
                accepted = true,
                exportedUtc = result.ExportedUtc,
                mode = result.Mode,
                csvPath = result.CsvPath,
                htmlPath = result.HtmlPath,
                activeRows = result.ActiveRows,
                scannerRows = result.ScannerRows,
                dailyPnl = result.DailyPnl,
                unrealized = result.Unrealized,
                realized = result.Realized
            }, JsonOptions);
            await WriteResponseAsync(stream, "200 OK", "application/json; charset=utf-8", json, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (path.Equals(SailorUiContract.DesiredStateEndpoint, StringComparison.OrdinalIgnoreCase))
        {
            if (!method.Equals("POST", StringComparison.OrdinalIgnoreCase))
            {
                await WriteResponseAsync(stream, "405 Method Not Allowed", "application/json; charset=utf-8", "{\"accepted\":false,\"rejectedReason\":\"POST required.\"}", cancellationToken).ConfigureAwait(false);
                return;
            }

            if (!_controlsEnabled || _mode != SailorRuntimeMode.Paper)
            {
                await WriteResponseAsync(stream, "403 Forbidden", "application/json; charset=utf-8", "{\"accepted\":false,\"rejectedReason\":\"SAILOR-067 desired-state controls are available only for paper mode with --ui-controls true.\"}", cancellationToken).ConfigureAwait(false);
                return;
            }

            string body = await ReadBodyAsync(reader, headers, cancellationToken).ConfigureAwait(false);
            SailorUiDesiredStateUpdate? update = null;
            try
            {
                update = JsonSerializer.Deserialize<SailorUiDesiredStateUpdate>(body, JsonOptions);
            }
            catch (Exception ex)
            {
                string error = JsonSerializer.Serialize(new { accepted = false, rejectedReason = $"Invalid JSON: {ex.Message}" }, JsonOptions);
                await WriteResponseAsync(stream, "400 Bad Request", "application/json; charset=utf-8", error, cancellationToken).ConfigureAwait(false);
                return;
            }

            if (update is null)
            {
                await WriteResponseAsync(stream, "400 Bad Request", "application/json; charset=utf-8", "{\"accepted\":false,\"rejectedReason\":\"Empty desired-state update.\"}", cancellationToken).ConfigureAwait(false);
                return;
            }

            string userAgent = headers.TryGetValue("User-Agent", out string? agent) ? agent : string.Empty;
            SailorUiDesiredStateUpdateResult result = _desiredStateStore.TryUpdate(update, "SailorUI", userAgent);
            string json = JsonSerializer.Serialize(result, JsonOptions);
            await WriteResponseAsync(stream, result.Accepted ? "200 OK" : "409 Conflict", "application/json; charset=utf-8", json, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (path.Equals("/favicon.ico", StringComparison.OrdinalIgnoreCase))
        {
            await WriteResponseAsync(stream, "204 No Content", "text/plain; charset=utf-8", string.Empty, cancellationToken).ConfigureAwait(false);
            return;
        }

        string html = BuildHtml();
        await WriteResponseAsync(stream, "200 OK", "text/html; charset=utf-8", html, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<string> ReadBodyAsync(
        StreamReader reader,
        IReadOnlyDictionary<string, string> headers,
        CancellationToken cancellationToken)
    {
        if (!headers.TryGetValue("Content-Length", out string? contentLengthText)
            || !int.TryParse(contentLengthText, out int contentLength)
            || contentLength <= 0)
        {
            return string.Empty;
        }

        char[] buffer = new char[contentLength];
        int totalRead = 0;
        while (totalRead < contentLength)
        {
            int read = await reader.ReadAsync(buffer.AsMemory(totalRead, contentLength - totalRead), cancellationToken).ConfigureAwait(false);
            if (read <= 0)
            {
                break;
            }

            totalRead += read;
        }

        return new string(buffer, 0, totalRead);
    }

    private static async Task WriteResponseAsync(
        NetworkStream stream,
        string status,
        string contentType,
        string body,
        CancellationToken cancellationToken)
    {
        byte[] payload = Encoding.UTF8.GetBytes(body);
        string header =
            $"HTTP/1.1 {status}\r\n" +
            $"Content-Type: {contentType}\r\n" +
            "Cache-Control: no-store, no-cache, must-revalidate, max-age=0\r\n" +
            "Pragma: no-cache\r\n" +
            $"Content-Length: {payload.Length}\r\n" +
            "Connection: close\r\n" +
            "\r\n";
        byte[] headerBytes = Encoding.ASCII.GetBytes(header);
        await stream.WriteAsync(headerBytes, cancellationToken).ConfigureAwait(false);
        if (payload.Length > 0)
        {
            await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
        }
    }

    private static string BuildHtml()
    {
        return """
<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8" />
<meta name="viewport" content="width=device-width, initial-scale=1" />
<title>SailorUI Monitor</title>
<style>
:root { color-scheme: dark; --bg:#050505; --panel:#111; --grid:#333; --text:#e7e7e7; --muted:#a0a0a0; --green:#00ff5a; --red:#ff4040; --yellow:#ffd24a; --blue:#79b8ff; }
* { box-sizing:border-box; }
body { margin:0; background:var(--bg); color:var(--text); font-family:Consolas, 'Segoe UI Mono', monospace; font-size:12px; line-height:1.15; overflow:hidden; }
.header { display:flex; align-items:stretch; gap:8px; padding:5px 7px; border-bottom:1px solid var(--grid); background:#000; }
.pnlBox { min-width:360px; background:#052d05; color:white; padding:3px 8px; border-left:4px solid #0aff0a; }
.pnlTitle { font-size:14px; color:#ddffdd; }
.pnlValue { font-size:19px; font-weight:700; color:#eaffea; padding-left:20px; }
.pnlParts { display:inline-grid; grid-template-columns:auto auto; column-gap:10px; margin-left:10px; color:#c9d5e8; }
.statusBox { flex:1; padding:3px 6px; color:var(--muted); white-space:nowrap; overflow:hidden; text-overflow:ellipsis; }
.statusOk { color:var(--green); } .statusWarn { color:var(--yellow); } .stale { color:var(--yellow); } .pos { color:var(--green); } .neg { color:var(--red); } .zero { color:var(--text); }
.main { height:calc(100vh - 48px); overflow:auto; padding:4px 6px 16px; }
.sectionTitle { color:#f5f5f5; background:#181818; padding:2px 4px; border-top:1px solid var(--grid); border-bottom:1px solid var(--grid); margin-top:4px; font-weight:700; }
table { width:100%; border-collapse:collapse; table-layout:fixed; }
th,td { border-bottom:1px solid #202020; padding:1px 4px; white-space:nowrap; overflow:hidden; text-overflow:ellipsis; height:18px; }
th { position:sticky; top:0; background:#0f0f0f; color:#bcbcbc; font-weight:400; z-index:2; }
tr:hover td { filter:brightness(1.18); }
tr.desired td { background:#081408; }
tr.rowLong td, tr.rowWin td { background:#04230c; }
tr.rowShort td, tr.rowLoss td { background:#340707; }
tr.rowShort.rowWin td { background:#251407; }
tr.rowLong.rowLoss td { background:#241707; }
tr.rowFlat td { background:#070707; }
.num { text-align:right; font-variant-numeric:tabular-nums; }
.center { text-align:center; }
select { width:100%; background:#161616; color:#eee; border:1px solid #444; height:16px; font-family:inherit; font-size:11px; }
input[type=checkbox] { width:13px; height:13px; margin:0; vertical-align:middle; accent-color:#00b050; }
.readonly { opacity:.72; }
.small { color:var(--muted); font-size:11px; }
.badge { display:inline-block; border:1px solid #555; padding:0 4px; margin-left:4px; color:#ccc; }
.badgeActive { border-color:#0a6; color:#00ff5a; }
.badgeWarn { border-color:#aa6; color:#ffd24a; }
.exportBtn { height:17px; margin-left:4px; padding:0 6px; border:1px solid #777; background:#151515; color:#ddd; font-family:inherit; font-size:11px; cursor:pointer; }
.exportBtn:hover { background:#222; color:white; }
.actionStatus { margin-left:6px; color:#ffd24a; }
.col-pnl{width:74px}.col-rank{width:58px}.col-symbol{width:72px}.col-pos{width:76px}.col-val{width:82px}.col-buy{width:86px}.col-open{width:76px}.col-price{width:76px}.col-trade{width:48px}.col-strategy{width:210px}.col-volume{width:92px}.col-side{width:60px}.col-score{width:70px}.col-status{width:82px}
</style>
</head>
<body>
<div class="header">
  <div class="pnlBox"><span class="pnlTitle">P&amp;L DAILY</span><span id="dailyPnl" class="pnlValue">0.00</span><span class="pnlParts"><span>Unrealized</span><span id="unrealized" class="num">0.00</span><span>Realized</span><span id="realized" class="num">0.00</span></span></div>
  <div class="statusBox"><span id="status" class="statusWarn">loading</span> <span id="source" class="small"></span><span id="controlBadge" class="badge">read-only</span><span class="badge">1s refresh</span><span class="badge">max 2 strategies</span><button class="exportBtn" onclick="exportReport()" title="SAILOR-070 export current SailorUI report to logs/{Mode}/SailorUI">export</button><span id="activeStrategies" class="small"></span><span id="actionStatus" class="actionStatus"></span></div>
</div>
<div class="main">
  <div class="sectionTitle">Section 2 — Active / today trades</div>
  <table><thead><tr><th class="col-pnl">DLY P&amp;L</th><th class="col-rank">Ranking</th><th class="col-symbol">Symbol</th><th class="col-pos">Position</th><th class="col-val">MKT VAL</th><th class="col-buy">Buy value</th><th class="col-open">Open</th><th class="col-price">Price</th><th class="col-trade">Trade</th><th class="col-strategy">Strategy</th><th class="col-volume">Volume</th><th>Reason</th></tr></thead><tbody id="activeRows"></tbody></table>
  <div class="sectionTitle">Section 3 — Rest scanner symbols</div>
  <table><thead><tr><th class="col-rank">Ranking</th><th class="col-symbol">Symbol</th><th class="col-trade">Trade</th><th class="col-strategy">Strategy</th><th class="col-volume">Volume</th><th class="col-price">Price</th><th class="col-side">Side</th><th class="col-score">Score</th><th class="col-status">Status</th><th>Reason</th></tr></thead><tbody id="scannerRows"></tbody></table>
  <div id="warnings" class="small"></div>
</div>
<script>
const fmt2 = new Intl.NumberFormat(undefined,{minimumFractionDigits:2,maximumFractionDigits:2});
const fmt4 = new Intl.NumberFormat(undefined,{minimumFractionDigits:2,maximumFractionDigits:4});
const fmt0 = new Intl.NumberFormat(undefined,{maximumFractionDigits:0});
let controlsEnabled = false;
let currentMode = 'paper';
let loading = false;
function cls(v){ return v > 0 ? 'pos' : v < 0 ? 'neg' : 'zero'; }
function activeRowClass(row){
  const pos = Number(row.position || 0);
  const pnl = Number(row.dailyPnl || 0);
  const classes = [];
  if(pos < 0) classes.push('rowShort');
  else if(pos > 0) classes.push('rowLong');
  else classes.push('rowFlat');
  if(pnl < 0) classes.push('rowLoss');
  else if(pnl > 0) classes.push('rowWin');
  return classes.join(' ');
}
function scannerRowClass(row){
  const side = String(row.selectedSide || '').toUpperCase();
  if(side === 'SHORT') return 'rowShort';
  if(side === 'LONG') return 'rowLong';
  return 'rowFlat';
}
function n2(v){ return fmt2.format(Number(v || 0)); }
function n4(v){ return fmt4.format(Number(v || 0)); }
function n0(v){ return fmt0.format(Number(v || 0)); }
function esc(s){ return String(s ?? '').replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c])); }
function action(msg, bad=false){ const el=document.getElementById('actionStatus'); el.textContent=msg || ''; el.className = bad ? 'actionStatus neg' : 'actionStatus'; if(msg) setTimeout(()=>{ if(el.textContent===msg) el.textContent=''; }, 6000); }
async function exportReport(){
  try {
    action('exporting report...');
    const r = await fetch('/api/export', { method:'POST', cache:'no-store' });
    const j = await r.json();
    if(!j.accepted){ action(j.rejectedReason || 'export rejected', true); return; }
    action(`exported CSV ${j.csvPath} | HTML ${j.htmlPath}`);
  } catch(e){ action('export failed: ' + e, true); }
}
function strategySelect(symbol, selected, options){
  const normalized = String(selected || '').toLowerCase();
  const opts = (options || []).map(o => {
    const label = `${o.strategy} | PnL ${n2(o.totalPnl)} | T ${o.trades}`;
    const value = o.profileName || o.strategy;
    const sel = String(value).toLowerCase() === normalized || String(o.strategy).toLowerCase() === normalized ? ' selected' : '';
    return `<option value="${esc(value)}"${sel}>${esc(label)}</option>`;
  }).join('');
  const disabled = controlsEnabled ? '' : ' disabled';
  const title = controlsEnabled
    ? 'SAILOR-067 desired-state strategy control'
    : String(currentMode).toLowerCase() === 'live'
      ? 'SAILOR-069 live SailorUI is read-only locked; strategy changes are disabled'
      : 'read-only; start paper sailor-ui with --ui-controls true';
  return `<select ${disabled} data-symbol="${esc(symbol)}" onchange="setStrategy(this)" title="${esc(title)}">${opts}</select>`;
}
function tradeBox(symbol, enabled){
  const disabled = controlsEnabled ? '' : ' disabled';
  const cls = controlsEnabled ? '' : ' readonly';
  return `<input ${disabled} class="${cls}" type="checkbox" data-symbol="${esc(symbol)}" onchange="setTrade(this)" ${enabled ? 'checked' : ''}>`;
}
async function postDesired(symbol, desiredTradeEnabled, selectedStrategy){
  try {
    action('saving ' + symbol + '...');
    const payload = { symbol, desiredTradeEnabled, selectedStrategy, source:'SailorUI' };
    const r = await fetch('/api/desired-state', { method:'POST', headers:{'Content-Type':'application/json'}, body:JSON.stringify(payload), cache:'no-store' });
    const j = await r.json();
    if(!j.accepted){ action(j.rejectedReason || 'desired state rejected', true); await load(true); return; }
    action('saved ' + symbol);
    await load(true);
  } catch(e){ action('save failed: ' + e, true); }
}
function setTrade(cb){
  const symbol = cb.dataset.symbol;
  const select = document.querySelector(`select[data-symbol="${CSS.escape(symbol)}"]`);
  postDesired(symbol, cb.checked, select ? select.value : '');
}
function setStrategy(sel){
  const symbol = sel.dataset.symbol;
  const cb = document.querySelector(`input[type=checkbox][data-symbol="${CSS.escape(symbol)}"]`);
  postDesired(symbol, cb ? cb.checked : false, sel.value);
}
async function load(force=false){
  if(loading && !force) return;
  loading = true;
  try {
    const r = await fetch('/api/snapshot?ts=' + Date.now(), {cache:'no-store'});
    const s = await r.json();
    controlsEnabled = !!s.controlsEnabled;
    currentMode = s.mode || 'paper';
    document.getElementById('dailyPnl').textContent = n2(s.pnl.dailyPnl);
    document.getElementById('dailyPnl').className = 'pnlValue ' + cls(Number(s.pnl.dailyPnl || 0));
    document.getElementById('unrealized').textContent = n2(s.pnl.unrealized);
    document.getElementById('realized').textContent = n2(s.pnl.realized);
    document.getElementById('status').textContent = `${s.mode.toUpperCase()} ${s.status} ${new Date(s.observedUtc).toLocaleTimeString()}`;
    document.getElementById('status').className = s.status === 'OK' ? 'statusOk' : 'statusWarn';
    document.getElementById('source').textContent = `${s.sourceSummary} ${s.pnl.stale ? '| STALE: ' + s.pnl.staleReason : ''}`;
    const badge = document.getElementById('controlBadge');
    const liveLocked = String(s.controlMode || '').toLowerCase() === 'live-read-only-locked' || String(s.mode || '').toLowerCase() === 'live';
    badge.textContent = controlsEnabled ? 'paper controls' : liveLocked ? 'LIVE read-only lock' : 'read-only';
    badge.className = controlsEnabled ? 'badge badgeActive' : 'badge badgeWarn';
    document.getElementById('activeStrategies').textContent = (s.activeDesiredStrategies || []).length ? ` activeStrategies=${(s.activeDesiredStrategies || []).join(',')}` : '';
    document.getElementById('activeRows').innerHTML = (s.activeRows || []).map(row => `
      <tr class="${activeRowClass(row)} ${row.tradeEnabled ? 'desired' : ''}"><td class="num ${cls(row.dailyPnl)}">${n2(row.dailyPnl)}</td><td class="num">${row.scanRanking ?? '-'}</td><td>${esc(row.symbol)}</td><td class="num">${n0(row.position)}</td><td class="num ${cls(row.marketValue)}">${n2(row.marketValue)}</td><td class="num">${n2(row.buyValue)}</td><td class="num">${n4(row.open)}</td><td class="num ${row.priceStale ? 'stale' : ''}" title="${esc(row.priceSource)}">${n4(row.price)}${row.priceStale ? ' *' : ''}</td><td class="center">${tradeBox(row.symbol,row.tradeEnabled)}</td><td>${strategySelect(row.symbol,row.strategy,row.strategyOptions)}</td><td class="num">${n0(row.volume)}</td><td>${esc(row.reason)}</td></tr>`).join('') || '<tr><td colspan="12" class="small">No active/today trade rows found yet.</td></tr>';
    document.getElementById('scannerRows').innerHTML = (s.scannerRows || []).map(row => `
      <tr class="${scannerRowClass(row)} ${row.tradeEnabled ? 'desired' : ''}"><td class="num">${row.scanRanking}</td><td>${esc(row.symbol)}</td><td class="center">${tradeBox(row.symbol,row.tradeEnabled)}</td><td>${strategySelect(row.symbol,row.strategy,row.strategyOptions)}</td><td class="num">${n0(row.volume)}</td><td class="num ${row.priceStale ? 'stale' : ''}">${n4(row.price)}${row.priceStale ? ' *' : ''}</td><td>${esc(row.selectedSide)}</td><td class="num">${n2(row.finalScore)}</td><td>${esc(row.status)}</td><td>${esc(row.reason)}</td></tr>`).join('') || '<tr><td colspan="10" class="small">No scanner rows found yet.</td></tr>';
    document.getElementById('warnings').innerHTML = (s.warnings || []).map(w => `<div class="stale">WARN: ${esc(w)}</div>`).join('');
  } catch(e) {
    document.getElementById('status').textContent = 'UI fetch failed: ' + e;
    document.getElementById('status').className = 'statusWarn';
  } finally {
    loading = false;
  }
}
load();
setInterval(load, 1000);
</script>
</body>
</html>
""";
    }
}
