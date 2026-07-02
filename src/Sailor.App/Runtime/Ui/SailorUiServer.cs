using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace Sailor.App.Runtime.Ui;

public sealed class SailorUiServer
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    private readonly SailorUiSnapshotProvider _snapshotProvider;
    private readonly string _host;
    private readonly int _port;
    private readonly Action<string> _log;

    public SailorUiServer(
        SailorUiSnapshotProvider snapshotProvider,
        string host,
        int port,
        Action<string> log)
    {
        _snapshotProvider = snapshotProvider;
        _host = string.IsNullOrWhiteSpace(host) ? "127.0.0.1" : host.Trim();
        _port = Math.Max(1, port);
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
        _log($"SAILOR-066 SailorUI read-only monitor is listening at http://localhost:{_port}/");
        _log("SAILOR-066 uses a lightweight loopback HTTP server, updates the browser every second, and sends no broker orders.");
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
        using var reader = new StreamReader(stream, Encoding.ASCII, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        string? requestLine = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(requestLine))
        {
            return;
        }

        while (!cancellationToken.IsCancellationRequested)
        {
            string? header = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrEmpty(header))
            {
                break;
            }
        }

        string[] parts = requestLine.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        string path = parts.Length >= 2 ? parts[1] : "/";
        path = path.Split('?', 2)[0];

        if (path.Equals("/api/snapshot", StringComparison.OrdinalIgnoreCase))
        {
            SailorUiSnapshot snapshot = _snapshotProvider.ReadSnapshot();
            string json = JsonSerializer.Serialize(snapshot, JsonOptions);
            await WriteResponseAsync(stream, "200 OK", "application/json; charset=utf-8", json, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (path.Equals("/api/health", StringComparison.OrdinalIgnoreCase))
        {
            await WriteResponseAsync(stream, "200 OK", "application/json; charset=utf-8", "{\"status\":\"ok\",\"readOnly\":true}", cancellationToken).ConfigureAwait(false);
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
<title>SailorUI Read-only Monitor</title>
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
tr:hover td { background:#151515; }
.num { text-align:right; font-variant-numeric:tabular-nums; }
.center { text-align:center; }
select { width:100%; background:#161616; color:#eee; border:1px solid #444; height:16px; font-family:inherit; font-size:11px; }
input[type=checkbox] { width:13px; height:13px; margin:0; vertical-align:middle; accent-color:#00b050; }
.readonly { opacity:.72; }
.small { color:var(--muted); font-size:11px; }
.badge { display:inline-block; border:1px solid #555; padding:0 4px; margin-left:4px; color:#ccc; }
.col-pnl{width:74px}.col-rank{width:58px}.col-symbol{width:72px}.col-pos{width:76px}.col-val{width:82px}.col-buy{width:86px}.col-open{width:76px}.col-price{width:76px}.col-trade{width:48px}.col-strategy{width:210px}.col-volume{width:92px}.col-side{width:60px}.col-score{width:70px}.col-status{width:82px}
</style>
</head>
<body>
<div class="header">
  <div class="pnlBox"><span class="pnlTitle">P&amp;L DAILY</span><span id="dailyPnl" class="pnlValue">0.00</span><span class="pnlParts"><span>Unrealized</span><span id="unrealized" class="num">0.00</span><span>Realized</span><span id="realized" class="num">0.00</span></span></div>
  <div class="statusBox"><span id="status" class="statusWarn">loading</span> <span id="source" class="small"></span><span class="badge">read-only</span><span class="badge">1s refresh</span><span class="badge">max 2 strategies</span></div>
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
function cls(v){ return v > 0 ? 'pos' : v < 0 ? 'neg' : 'zero'; }
function n2(v){ return fmt2.format(Number(v || 0)); }
function n4(v){ return fmt4.format(Number(v || 0)); }
function n0(v){ return fmt0.format(Number(v || 0)); }
function esc(s){ return String(s ?? '').replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c])); }
function strategySelect(selected, options){
  const normalized = String(selected || '').toLowerCase();
  const opts = (options || []).map(o => {
    const label = `${o.strategy} | PnL ${n2(o.totalPnl)} | T ${o.trades}`;
    const value = o.profileName || o.strategy;
    const sel = String(value).toLowerCase() === normalized || String(o.strategy).toLowerCase() === normalized ? ' selected' : '';
    return `<option value="${esc(value)}"${sel}>${esc(label)}</option>`;
  }).join('');
  return `<select disabled class="readonly" title="read-only in SAILOR-066">${opts}</select>`;
}
async function load(){
  try {
    const r = await fetch('/api/snapshot?ts=' + Date.now(), {cache:'no-store'});
    const s = await r.json();
    document.getElementById('dailyPnl').textContent = n2(s.pnl.dailyPnl);
    document.getElementById('dailyPnl').className = 'pnlValue ' + cls(Number(s.pnl.dailyPnl || 0));
    document.getElementById('unrealized').textContent = n2(s.pnl.unrealized);
    document.getElementById('realized').textContent = n2(s.pnl.realized);
    document.getElementById('status').textContent = `${s.mode.toUpperCase()} ${s.status} ${new Date(s.observedUtc).toLocaleTimeString()}`;
    document.getElementById('status').className = s.status === 'OK' ? 'statusOk' : 'statusWarn';
    document.getElementById('source').textContent = `${s.sourceSummary} ${s.pnl.stale ? '| STALE: ' + s.pnl.staleReason : ''}`;
    document.getElementById('activeRows').innerHTML = (s.activeRows || []).map(row => `
      <tr><td class="num ${cls(row.dailyPnl)}">${n2(row.dailyPnl)}</td><td class="num">${row.scanRanking ?? '-'}</td><td>${esc(row.symbol)}</td><td class="num">${n0(row.position)}</td><td class="num ${cls(row.marketValue)}">${n2(row.marketValue)}</td><td class="num">${n2(row.buyValue)}</td><td class="num">${n4(row.open)}</td><td class="num ${row.priceStale ? 'stale' : ''}" title="${esc(row.priceSource)}">${n4(row.price)}${row.priceStale ? ' *' : ''}</td><td class="center"><input disabled class="readonly" type="checkbox" ${row.tradeEnabled ? 'checked' : ''}></td><td>${strategySelect(row.strategy,row.strategyOptions)}</td><td class="num">${n0(row.volume)}</td><td>${esc(row.reason)}</td></tr>`).join('') || '<tr><td colspan="12" class="small">No active/today trade rows found yet.</td></tr>';
    document.getElementById('scannerRows').innerHTML = (s.scannerRows || []).map(row => `
      <tr><td class="num">${row.scanRanking}</td><td>${esc(row.symbol)}</td><td class="center"><input disabled class="readonly" type="checkbox" ${row.tradeEnabled ? 'checked' : ''}></td><td>${strategySelect(row.strategy,row.strategyOptions)}</td><td class="num">${n0(row.volume)}</td><td class="num ${row.priceStale ? 'stale' : ''}">${n4(row.price)}${row.priceStale ? ' *' : ''}</td><td>${esc(row.selectedSide)}</td><td class="num">${n2(row.finalScore)}</td><td>${esc(row.status)}</td><td>${esc(row.reason)}</td></tr>`).join('') || '<tr><td colspan="10" class="small">No scanner rows found yet.</td></tr>';
    document.getElementById('warnings').innerHTML = (s.warnings || []).map(w => `<div class="stale">WARN: ${esc(w)}</div>`).join('');
  } catch(e) {
    document.getElementById('status').textContent = 'UI fetch failed: ' + e;
    document.getElementById('status').className = 'statusWarn';
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
