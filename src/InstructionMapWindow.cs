using System;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace Aloha
{

    public class InstructionMapWindow : DafyFrame
    {
        private WebView2 web;
        private readonly CoreWebView2Environment sharedEnv;
        private readonly ProcessTreeProbe probe = new ProcessTreeProbe();
        private System.Windows.Forms.Timer treeTimer;
        private bool webReady = false;

        public InstructionMapWindow(CoreWebView2Environment env = null) : base("OPT-IMAP", "Network Map")
        {
            sharedEnv = env;
            Size = new Size(1024, 640);
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = true;
            ClientArea.BackColor = Color.Black;

            web = new WebView2 { Dock = DockStyle.Fill };
            ClientArea.Controls.Add(web);

            this.Load += async (s, e) => await InitAsync();
            this.FormClosed += (s, e) => { treeTimer?.Stop(); treeTimer?.Dispose(); };
        }

        private async Task InitAsync()
        {
            try
            {

                if (sharedEnv != null)
                    await web.EnsureCoreWebView2Async(sharedEnv);
                else
                    await web.EnsureCoreWebView2Async();

                web.CoreWebView2.WebMessageReceived += (s, e) =>
                {
                    try
                    {
                        string m = e.TryGetWebMessageAsString();
                        if (m == null) return;
                        if (m.StartsWith("drill2|")) { HandleDrill(m); return; }
                    }
                    catch { }
                };

                web.CoreWebView2.NavigateToString(Html);
                webReady = true;

                treeTimer = new System.Windows.Forms.Timer { Interval = 1000 };
                treeTimer.Tick += (s, e) => PushProcessTree();
                treeTimer.Start();
                PushProcessTree();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Network Map failed to start WebView2:\n" + ex.Message,
                    "Network Map", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private readonly TcpConnectionProbe tcp = new TcpConnectionProbe();

        private void HandleDrill(string m)
        {
            var kv = new System.Collections.Generic.Dictionary<string, string>();
            foreach (var part in m.Split('|'))
            {
                int eq = part.IndexOf('=');
                if (eq > 0) kv[part.Substring(0, eq)] = part.Substring(eq + 1);
            }
            string Get(string k) { return kv.TryGetValue(k, out var v) ? v : ""; }

            int tier = 0; int.TryParse(Get("tier"), out tier);
            int pid = 0; int.TryParse(Get("pid"), out pid);
            string name = Get("name"), ip = Get("ip"), host = Get("host"),
                   proto = Get("proto"), tp = Get("tp"), adapter = Get("adapter"),
                   port = Get("port"), lport = Get("lport"), state = Get("state");

            if (tier == 0 && pid > 0)
            {

                var v = new PrimarchView(name, ip, state.Length > 0 ? state : "IDLE",
                                         "0", "proc", pid);
                v.Show(); v.BringToFront();
                return;
            }

            string scanTarget = (host.Length > 0) ? host : (ip.Length > 0 && ip != "*" ? ip : "");
            string[] lines = BuildInfoLines(tier, name, ip, host, proto, tp, adapter, port, lport, state);
            string title = name.Length > 0 ? name : "node";

            var con = new NodeInfoConsole(title, lines, scanTarget, scanTarget.Length > 0);
            con.OnSiteMap += (t) =>
            {
                try
                {
                    string url = t;
                    if (url.IndexOf("://", StringComparison.Ordinal) < 0) url = "https://" + url;
                    var w = new SiteMapPanel(() => new string[0], (u) => { try { System.Diagnostics.Process.Start(u); } catch { } });
                    w.Show(); w.BringToFront();
                }
                catch { }
            };
            con.Show(); con.BringToFront();
        }

        private static string[] BuildInfoLines(int tier, string name, string ip, string host,
            string proto, string tp, string adapter, string port, string lport, string state)
        {
            string[] tierName = { "process", "transport", "local socket", "adapter", "protocol", "remote IP", "host" };
            var L = new System.Collections.Generic.List<string>();
            L.Add(name);
            L.Add("tier      " + (tier >= 0 && tier < tierName.Length ? tierName[tier] : "?"));
            if (host.Length > 0)    L.Add("host      " + host);
            if (ip.Length > 0 && ip != "*") L.Add("ip        " + ip + (port.Length > 0 ? (":" + port) : ""));
            if (tp.Length > 0)      L.Add("transport " + tp);
            if (proto.Length > 0)   L.Add("protocol  " + proto);
            if (adapter.Length > 0) L.Add("adapter   " + adapter);
            if (lport.Length > 0)   L.Add("local     :" + lport);
            if (state.Length > 0)   L.Add("state     " + state);
            return L.ToArray();
        }

        private void PushProcessTree()
        {
            if (!webReady || web == null || web.CoreWebView2 == null) return;
            try
            {
                int bpid = (int)web.CoreWebView2.BrowserProcessId;
                var procs = probe.Snapshot(bpid);
                if (procs.Count == 0) return;
                web.CoreWebView2.PostWebMessageAsString(ProcessTreeProbe.ToJson(procs));

                var pids = new System.Collections.Generic.HashSet<int>();
                foreach (var p in procs) pids.Add(p.Pid);
                var conns = tcp.ForPids(pids);
                web.CoreWebView2.PostWebMessageAsString(tcp.ToJson(conns));
            }
            catch { }
        }

        private const string Html = @"<!DOCTYPE html>
<html>
<head>
<meta charset='utf-8'>
<style>
  * { margin:0; padding:0; box-sizing:border-box; }
  html,body { width:100%; height:100%; background:#000; overflow:hidden;
    font-family:'Lucida Console','Consolas','Courier New',monospace; }
  #top { position:absolute; top:0; left:0; right:0; height:24px; background:#ffffff;
    border-bottom:1px solid #808080; display:flex; align-items:center; gap:12px;
    padding:0 8px; font-weight:bold; font-size:13px; color:#000;
    white-space:nowrap; overflow:hidden; z-index:10; }
  #top .xyz { background:#000; color:#f7ff3a; padding:1px 8px; letter-spacing:1px; }
  #frame { position:absolute; top:25px; left:0; right:0; bottom:0;
    background:#000; overflow:scroll; }
  #c { position:absolute; top:0; left:0; display:block; background:#000; }
  /* white map border as a fixed overlay that frames the viewport just inside the bars */
  #vp { position:absolute; top:25px; left:0; right:18px; bottom:18px;
    border:1px solid #fcfcfc; pointer-events:none; z-index:6; }
  #pop { position:absolute; display:none; background:#000; border:1px solid #19e019;
    color:#19e019; font-size:11px; padding:6px 8px; pointer-events:none; z-index:50;
    box-shadow:0 0 8px rgba(25,224,25,.4); white-space:pre; }
  #frame::-webkit-scrollbar { width:18px; height:18px; background:#f0f0f0; }
  #frame::-webkit-scrollbar-track { background:#f4f4f4; box-shadow: inset 1px 1px 0 #c8c8c8; }
  #frame::-webkit-scrollbar-thumb { background:#f0f0f0;
    border-top:2px solid #ffffff; border-left:2px solid #ffffff;
    border-right:2px solid #b0b0b0; border-bottom:2px solid #b0b0b0; }
  #frame::-webkit-scrollbar-button { background:#f0f0f0;
    border-top:2px solid #ffffff; border-left:2px solid #ffffff;
    border-right:2px solid #b0b0b0; border-bottom:2px solid #b0b0b0;
    background-repeat:no-repeat; background-position:center; }
  #frame::-webkit-scrollbar-button:vertical:decrement { height:18px;
    background-image:url('data:image/svg+xml,%3Csvg xmlns=%27http://www.w3.org/2000/svg%27 width=%2718%27 height=%2718%27%3E%3Cpolygon points=%279,6 13,12 5,12%27 fill=%27%23303030%27/%3E%3C/svg%3E'); }
  #frame::-webkit-scrollbar-button:vertical:increment { height:18px;
    background-image:url('data:image/svg+xml,%3Csvg xmlns=%27http://www.w3.org/2000/svg%27 width=%2718%27 height=%2718%27%3E%3Cpolygon points=%275,6 13,6 9,12%27 fill=%27%23303030%27/%3E%3C/svg%3E'); }
  #frame::-webkit-scrollbar-button:horizontal:decrement { width:18px;
    background-image:url('data:image/svg+xml,%3Csvg xmlns=%27http://www.w3.org/2000/svg%27 width=%2718%27 height=%2718%27%3E%3Cpolygon points=%2712,5 12,13 6,9%27 fill=%27%23303030%27/%3E%3C/svg%3E'); }
  #frame::-webkit-scrollbar-button:horizontal:increment { width:18px;
    background-image:url('data:image/svg+xml,%3Csvg xmlns=%27http://www.w3.org/2000/svg%27 width=%2718%27 height=%2718%27%3E%3Cpolygon points=%276,5 6,13 12,9%27 fill=%27%23303030%27/%3E%3C/svg%3E'); }
  #frame::-webkit-scrollbar-button:start:increment,
  #frame::-webkit-scrollbar-button:end:decrement { display:none; }
  #frame::-webkit-scrollbar-corner { background:#f0f0f0; }
</style>
</head>
<body>
<div id='top'>
  <span>Network Map</span>
  <span class='xyz'>X=<span id='xv'>+0000</span> Y=<span id='yv'>+0000</span> Z=<span id='zv'>01.0</span></span>
  <span><span id='clock'>--:--:--</span>&nbsp;&nbsp;Nodes <span id='ev'>0</span>&nbsp;&nbsp;Links <span id='al'>0</span></span>
</div>
<div id='frame'>
<canvas id='c'></canvas>
<div id='pop'></div>
</div>
<div id='vp'></div>
<script>
const frame=document.getElementById('frame'),cv=document.getElementById('c'),ctx=cv.getContext('2d'),pop=document.getElementById('pop');
const GREEN='#19e019',DGREEN='#0a6a0a',MGREEN='#10b010',WHITE='#fcfcfc';
const FONT='\'Lucida Console\',\'Consolas\',monospace';
let LOCAL=[['BROWSER','browser'],['GPU','gpu'],['NET','net'],['STORAGE','stor'],['UTILITY','util'],['RENDER1','rend'],['RENDER2','rend'],['RENDER3','rend'],['AUDIO','aud'],['PLUGIN','plug']];
let LOCAL_PIDS=[];   // real pids parallel to LOCAL, filled from C# proctree
let LOCAL_CPU=[];    // real per-process cpu%, filled from C# proctree
let nodes=[],edges=[],sel=null,hov=null,dirty=true,CW=0,CH=0,Z=1;
let CONNS=[];   // real per-PID TCP/UDP endpoints, pushed from C#
let PROTO_NODES=[];   // middle-tier protocol chips
let GRID=[];          // right-tier endpoint cells (row-major)
let HUB={x:130,y:300};
let FOCUS=0;          // index into GRID for the crosshair
let GRID_ROWS=10;     // endpoint rows per column (for arrow nav)
let KEYNAV=false;     // crosshair visible once arrows are used
function fmtZ(z){ return (z<10?'0':'')+z.toFixed(1); }
function applyZoom(){
  cv.width=Math.round(CW*Z); cv.height=Math.round(CH*Z);
  cv.style.width=(CW*Z)+'px'; cv.style.height=(CH*Z)+'px';
  document.getElementById('zv').textContent=fmtZ(Z); dirty=true;
}
function size(){
  const vw=frame.clientWidth, vh=frame.clientHeight;
  const vp=document.getElementById('vp');
  vp.style.right=(frame.offsetWidth-frame.clientWidth)+'px';
  vp.style.bottom=(frame.offsetHeight-frame.clientHeight)+'px';
  CW=Math.max(vw,800); CH=Math.max(vh,540);
  applyZoom();
}
function build(){
  nodes=[]; edges=[];
  const lx=50, ly0=34, lstep=16;
  // left column: the REAL WebView2 subprocesses (from the proctree push)
  LOCAL.forEach((l,i)=>nodes.push({
    name:l[0], role:l[1], x:lx, y:ly0+i*lstep,
    act:(LOCAL_CPU[i]||0)>5,
    ip:(LOCAL_PIDS[i]!=null?('pid '+LOCAL_PIDS[i]):'127.0.0.'+(1+i)),
    pid:(LOCAL_PIDS[i]!=null?LOCAL_PIDS[i]:-1),
    load:(LOCAL_CPU[i]||0), local:true
  }));
  const localCount=nodes.length;

  // ===================================================================
  // LAYERED STACK GRAPH. Each Conn is projected onto 7 technically-ordered
  // tiers, each deduplicated to its distinct real entities. Edges join
  // consecutive tiers, so you see the true fan: many procs -> one TCP ->
  // local sockets -> the adapter -> protocol -> many IPs -> their hosts.
  //   T0 process   T1 transport(L4)   T2 local socket   T3 adapter(NIC)
  //   T4 protocol(L7)   T5 remote IP(L3)   T6 host(DNS)
  // ===================================================================
  // tier registries: key -> node
  const tiers=[{},{},{},{},{},{},{}];
  const TIER_X=[lx, 250, 360, 470, 590, 710, 850];   // column x per tier
  const tierLabel=['','TCP/UDP','socket','adapter','proto','ip','host'];

  // process nodes already live in nodes[0..localCount-1]; register them in T0 by pid
  for(let i=0;i<localCount;i++){ if(nodes[i].pid>=0) tiers[0]['pid'+nodes[i].pid]=nodes[i]; }

  function tierNode(t, key, label, extra){
    let nd=tiers[t][key];
    if(!nd){
      nd=Object.assign({ tier:t, name:label, x:TIER_X[t], y:0, act:false,
                         idx:nodes.length, local:(t===0), role:'tier'+t }, extra||{});
      tiers[t][key]=nd; nodes.push(nd);
    }
    return nd;
  }
  // ensure process nodes have an idx for fast linking
  for(let i=0;i<localCount;i++) nodes[i].idx=i;
  function link(a,b,lit){ if(a&&b) edges.push([a.idx,b.idx,!!lit]); }

  for(const c of CONNS){
    const est=(c.state==='ESTABLISHED');
    const proc = tiers[0]['pid'+c.pid];                 // may be undefined if pid not in tree
    const tp   = tierNode(1, c.tp||'TCP', c.tp||'TCP', {transport:c.tp});
    const sock = tierNode(2, 'L'+(c.lport!=null?c.lport:'?'), (c.lport!=null?(':'+c.lport):'socket'),
                          {lport:c.lport});
    const adp  = tierNode(3, c.adapter||'?', (c.adapter||'?').split(':')[0], {adapter:c.adapter});
    const pr   = tierNode(4, c.proto||'?', c.proto||'?', {proto:c.proto});
    let ipNode=null, hostNode=null;
    if(c.ip && c.ip!=='*'){
      ipNode  = tierNode(5, c.ip, c.ip, {ip:c.ip, port:c.port});
      const hk = (c.host&&c.host.length)?c.host:c.ip;
      hostNode = tierNode(6, hk, hk, {host:c.host||null, ip:c.ip, port:c.port,
                                      tp:c.tp, proto:c.proto, adapter:c.adapter,
                                      lport:c.lport, state:c.state});
    }
    if(est){ tp.act=adp.act=pr.act=true; if(ipNode)ipNode.act=true; if(hostNode)hostNode.act=true; }
    if(proc) link(proc, tp, est);
    link(tp, sock, est); link(sock, adp, est); link(adp, pr, est);
    if(ipNode){ link(pr, ipNode, est); link(ipNode, hostNode, est); }
  }

  // ---- lay out each tier as an evenly spaced vertical column ----
  const topY=44, botPad=24, vh=(frame.clientHeight||CH)-topY-botPad;
  for(let t=1;t<tiers.length;t++){
    const arr=Object.keys(tiers[t]).map(k=>tiers[t][k]);
    const n=arr.length; if(n===0) continue;
    const step=Math.min(26, vh/Math.max(1,n));
    arr.forEach((nd,i)=>{ nd.x=TIER_X[t]; nd.y=topY+step*(i+0.5); });
  }
  // (process column keeps its own y from the LOCAL build above)

  // crosshair navigates the host tier (T6) top-to-bottom
  GRID=Object.keys(tiers[6]).map(k=>tiers[6][k]).sort((a,b)=>a.y-b.y);
  GRID_ROWS=GRID.length||1;
  if(FOCUS>=GRID.length) FOCUS=GRID.length-1;
  if(FOCUS<0) FOCUS=0;

  if(CONNS.length===0){
    const nd={tier:6,name:'(no active connections)',x:TIER_X[6],y:topY,act:false,ip:'-',role:'tier6',local:false};
    nodes.push(nd);
  }

  const maxX=TIER_X[TIER_X.length-1]+180;
  CW=Math.max(frame.clientWidth||CW, maxX);
  applyZoom();
}
function draw(){
  ctx.setTransform(1,0,0,1,0,0);
  ctx.fillStyle='#000'; ctx.fillRect(0,0,cv.width,cv.height);
  ctx.setTransform(Z,0,0,Z,0,0); ctx.lineWidth=1/Z;

  // straight edges between tiers; established paths bright, others dim. when a
  // node is selected we still draw everything, just brighten its incident edges.
  for(const e of edges){ const a=nodes[e[0]], b=nodes[e[1]]; if(!a||!b) continue;
    const incident = sel && (a===sel||b===sel);
    ctx.strokeStyle = incident ? WHITE : (e[2]?MGREEN:DGREEN);
    ctx.beginPath(); ctx.moveTo(a.x|0,a.y|0); ctx.lineTo(b.x|0,b.y|0); ctx.stroke(); }

  // nodes: small square + label, no boxes/borders. process tier bold.
  ctx.textBaseline='middle'; ctx.textAlign='left';
  for(const nd of nodes){
    const closing = nd.state && /WAIT|CLOSE|FIN|LAST/.test(nd.state);
    const c = closing ? '#c0282d' : (nd.act?WHITE:GREEN);
    const s = nd.local?7:5;
    // floor the square's position ONCE so the fill and the highlight share it exactly
    const sx=(nd.x-s)|0, sy=(nd.y-s)|0, sw=s*2, sh=s*2;
    ctx.fillStyle=c; ctx.fillRect(sx,sy,sw,sh);
    if(sel===nd||hov===nd){
      ctx.strokeStyle=WHITE; ctx.lineWidth=1/Z;
      // stroke 2px outside the fill on every side; +0.5 so a 1px line lands crisp
      ctx.strokeRect(sx-2+0.5, sy-2+0.5, sw+4-1, sh+4-1);
    }
    ctx.fillStyle=c; ctx.font=(nd.local?'bold 12px ':'11px ')+FONT;
    let lbl=nd.name||''; const maxw=(nd.tier>=5?150:96);
    while(lbl.length>4 && ctx.measureText(lbl).width>maxw) lbl=lbl.slice(0,-1);
    if(lbl!==(nd.name||'')) lbl=lbl.slice(0,-1)+'…';
    ctx.fillText(lbl,(nd.x+s+5)|0,nd.y|0);
  }

  // crosshair reticle over the focused host cell (keyboard nav targets T6 hosts)
  if(KEYNAV && GRID.length && GRID[FOCUS]){ const f=GRID[FOCUS];
    const r=12; ctx.strokeStyle=WHITE; ctx.lineWidth=1/Z; ctx.beginPath();
    ctx.moveTo((f.x-r)|0,f.y|0); ctx.lineTo((f.x-4)|0,f.y|0);
    ctx.moveTo((f.x+4)|0,f.y|0); ctx.lineTo((f.x+r)|0,f.y|0);
    ctx.moveTo(f.x|0,(f.y-r)|0); ctx.lineTo(f.x|0,(f.y-4)|0);
    ctx.moveTo(f.x|0,(f.y+4)|0); ctx.lineTo(f.x|0,(f.y+r)|0);
    ctx.stroke();
  }
}
function sample(){
  dirty=true;
  document.getElementById('ev').textContent=nodes.length;   // real node count
  document.getElementById('al').textContent=edges.length;   // real link count
  const d=new Date();
  document.getElementById('clock').textContent=
    String(d.getHours()).padStart(2,'0')+':'+String(d.getMinutes()).padStart(2,'0')+':'+String(d.getSeconds()).padStart(2,'0');
}
function worldXY(clientX,clientY){ const r=cv.getBoundingClientRect(); return [ (clientX-r.left)/Z, (clientY-r.top)/Z ]; }
function hit(mx,my){ let best=null,bd=120; for(const nd of nodes){ const d=(nd.x-mx)*(nd.x-mx)+(nd.y-my)*(nd.y-my); if(d<bd){bd=d;best=nd;} } return best; }
cv.addEventListener('mousemove', function(ev){ const p=worldXY(ev.clientX,ev.clientY), mx=p[0], my=p[1];
  hov=hit(mx,my); cv.style.cursor=hov?'pointer':'crosshair';
  const px=Math.round((mx/CW-0.5)*400), py=Math.round((0.5-my/CH)*200);
  document.getElementById('xv').textContent=(px<0?'-':'+')+String(Math.abs(px)).padStart(4,'0');
  document.getElementById('yv').textContent=(py<0?'-':'+')+String(Math.abs(py)).padStart(4,'0');
  dirty=true; });
function drillNode(nd){
  if(!nd) return;
  // rich, structured drill: tier decides process(INSTR) vs endpoint(console)
  const pid=(nd.local&&nd.ip&&(''+nd.ip).indexOf('pid ')===0)?(''+nd.ip).slice(4):'0';
  const parts=[
    'drill2',
    'tier='+(nd.tier!=null?nd.tier:(nd.local?0:6)),
    'name='+(nd.name||''),
    'ip='+(nd.ip||''),
    'host='+(nd.host||''),
    'proto='+(nd.proto||''),
    'tp='+(nd.tp||nd.transport||''),
    'adapter='+(nd.adapter||''),
    'port='+(nd.port!=null?nd.port:''),
    'lport='+(nd.lport!=null?nd.lport:''),
    'state='+(nd.state||''),
    'pid='+pid
  ];
  try{ if(window.chrome&&window.chrome.webview) window.chrome.webview.postMessage(parts.join('|')); }catch(e){}
}
cv.addEventListener('click', function(ev){ const p=worldXY(ev.clientX,ev.clientY), mx=p[0], my=p[1]; sel=hit(mx,my);
  pop.style.display='none';
  if(sel && sel.tier===6){ const gi=GRID.indexOf(sel); if(gi>=0){ FOCUS=gi; KEYNAV=true; } }   // sync crosshair to click
  if(sel) drillNode(sel);
  dirty=true; });
cv.addEventListener('wheel', function(ev){ ev.preventDefault();
  const fr=frame.getBoundingClientRect(), vx=ev.clientX-fr.left, vy=ev.clientY-fr.top;
  const wx=(frame.scrollLeft+vx)/Z, wy=(frame.scrollTop+vy)/Z;
  const nz=Math.min(6, Math.max(1, Z*(ev.deltaY<0?1.15:1/1.15)));
  if(nz===Z) return;
  Z=nz; applyZoom(); build();
  frame.scrollLeft=wx*Z-vx; frame.scrollTop=wy*Z-vy;
}, {passive:false});
// arrow-key crosshair navigation over the endpoint grid; Enter drills the focused cell
window.addEventListener('keydown', function(ev){
  if(!GRID.length) return;
  let used=true;
  if(ev.key==='ArrowDown')      FOCUS=Math.min(GRID.length-1, FOCUS+1);
  else if(ev.key==='ArrowUp')   FOCUS=Math.max(0, FOCUS-1);
  else if(ev.key==='ArrowRight')FOCUS=Math.min(GRID.length-1, FOCUS+1);
  else if(ev.key==='ArrowLeft') FOCUS=Math.max(0, FOCUS-1);
  else if(ev.key==='Enter'){ sel=GRID[FOCUS]; drillNode(sel); }
  else used=false;
  if(used){ KEYNAV=true; ev.preventDefault();
    const f=GRID[FOCUS]; if(f){ // scroll focused host into view
      const fx=f.x, fy=f.y, m=30;
      if(fx*Z<frame.scrollLeft) frame.scrollLeft=fx*Z-m;
      if(fx*Z>frame.scrollLeft+frame.clientWidth) frame.scrollLeft=fx*Z-frame.clientWidth+m;
      if(fy*Z<frame.scrollTop) frame.scrollTop=fy*Z-m;
      if(fy*Z>frame.scrollTop+frame.clientHeight) frame.scrollTop=fy*Z-frame.clientHeight+m;
    }
    dirty=true; }
});
function loop(){ requestAnimationFrame(loop); if(dirty){ draw(); dirty=false; } }
// real Chromium process tree + real TCP endpoints pushed from C#
if(window.chrome&&window.chrome.webview){
  window.chrome.webview.addEventListener('message', function(ev){
    let d=ev.data; if(typeof d!=='string') return;
    try{
      if(d.indexOf('proctree')>=0){
        const o=JSON.parse(d); if(!o.list||!o.list.length) return;
        LOCAL=o.list.map(p=>[p.name+'-'+p.pid, p.role]);
        LOCAL_PIDS=o.list.map(p=>p.pid);
        LOCAL_CPU=o.list.map(p=>p.cpu);
        build(); dirty=true;
      } else if(d.indexOf('""t"":""conns""')>=0){
        const o=JSON.parse(d); CONNS=o.list||[];
        build(); dirty=true;
      }
    }catch(e){}
  });
}
size(); build(); addEventListener('resize', function(){ size(); build(); dirty=true; });
sample(); setInterval(sample,420); loop();
</script>
</body>
</html>";
    }
}
