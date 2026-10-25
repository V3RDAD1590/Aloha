using System;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace Aloha
{

    public class PrimarchWindow : Form
    {
        private WebView2 web;

        public PrimarchWindow()
        {
            Text = "Primarch - Network Map";
            Size = new Size(1024, 640);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.Black;

            try
            {
                string dir = Path.GetDirectoryName(Application.ExecutablePath);
                string ico = Path.Combine(dir, "OptIcon.ico");
                if (File.Exists(ico)) this.Icon = new Icon(ico);
            }
            catch { }

            web = new WebView2 { Dock = DockStyle.Fill };
            Controls.Add(web);

            this.Load += async (s, e) => await InitAsync();
        }

        private async Task InitAsync()
        {
            try
            {
                string udf = Path.Combine(
                    Path.GetDirectoryName(Application.ExecutablePath),
                    "PrimarchProfile");
                Directory.CreateDirectory(udf);

                var env = await CoreWebView2Environment.CreateAsync(null, udf);
                await web.EnsureCoreWebView2Async(env);

                web.CoreWebView2.NavigateToString(Html);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Primarch failed to start WebView2:\n" + ex.Message,
                    "Primarch", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private const string Html = @"<!DOCTYPE html>
<html>
<head>
<meta charset='utf-8'>
<style>
  * { margin:0; padding:0; box-sizing:border-box; }
  html,body { width:100%; height:100%; background:#000; overflow:hidden;
    font-family:'Consolas','Courier New',monospace; }
  #top { position:absolute; top:0; left:0; right:0; height:18px; color:#0f0;
    font-size:12px; line-height:18px; white-space:nowrap; padding:0 6px;
    border-bottom:1px solid #060; }
  #top .v { color:#ff3030; }
  #top .y { color:#ffd000; }
  canvas { position:absolute; top:18px; left:0; display:block; background:#000; }
  #pop { position:absolute; display:none; background:#000; border:1px solid #0f0;
    color:#0f0; font-size:11px; padding:6px 8px; pointer-events:none; z-index:50;
    box-shadow:0 0 8px rgba(0,255,0,.4); white-space:pre; }
</style>
</head>
<body>
<div id='top'>Network Map &nbsp; <span class='v'>X=-0107</span> &nbsp; <span class='y'>Y=+0078</span> &nbsp; Zoom=01 &nbsp; 01/20/93 &nbsp; 12:23 &nbsp; Events 2643 &nbsp; Alerts 0</div>
<canvas id='c'></canvas>
<div id='pop'></div>
<script>
const cv = document.getElementById('c');
const ctx = cv.getContext('2d');
const pop = document.getElementById('pop');

const GREEN='#19e019', DGREEN='#0a6a0a', RED='#ff3030', YELLOW='#ffd000', AMBER='#d0a000';

function size(){ cv.width=innerWidth; cv.height=innerHeight-18; }
size(); addEventListener('resize', ()=>{ size(); draw(); });

const NAMES=['NORAD','ICOT','MAINS','GCGO','OXFORD','AERO','NIAGARA','LERC',
'NASNET','ARCLAN','KR AUS','ASF','RAL','COMSAT','CASI','LORAL',
'ARCNAS','NASARC','NZ','ART','OMM','ULCC','NGS','CITADEL','GISS','STSCI',
'ENSS144','HK','UHA','MCMURDO','UMT','CNES','NRWCH','PSU','CIW','VLPU',
'TMB','ARC3','SJSU','SWA','GAC','GSFC2','SRA','SAI','HSC',
'MB','NCAP24','ARC5','SAIC','ORST','GSFC7','GSI','ENSS145','GSFC4','LUE',
'BR8','LPARL','ARC4','MOBLAS8','SURA','EAST','GSFC8','GSFC5','NRL',
'ES','ICM','MTWLSN','VIRGD','SUSJA','ACDURS','UARS','NCAP6','LARC',
'EI','DFRF','SPRLJ','GTIABS','MESSAC','GSFC3','CRSR',
'DSS14','GDSCC','CLAES','HALOIE','UARSGW','COF','WFF',
'DSS13','NCAP5','PEM','HQS','AGU','ECO','URI','NET',
'MSSS','JPL3','JPL1','JSC','GSFC6','CEPS','USRA','IBC','USNO',
'PMO','CRC','OVRO','MSFC3','KSC','UMIAM','CTIO',
'ASU','NCAR','NSO','LPI','ESU','MSFC1','ARECI',
'TMF','USGS','BBSO','WSMR','UNICH','UTSI','VANS',
'RSI','SEL','MOBLAS4','FORCE','NWSU','RST','UMINN',
'EDC','ESI','UXCL','LOWELL','UAZ','PSI','SWRI','USF','HDNG','UMIAH'];

let nodes=[], edges=[];
function build(){
  nodes=[]; edges=[];
  const cols=14, rows=11;
  const mx=70, my=24;
  const cw=(cv.width-mx*2)/(cols-1);
  const ch=(cv.height-my*2)/(rows-1);
  let i=0;
  for(let r=0;r<rows && i<NAMES.length;r++){
    for(let c=0;c<cols && i<NAMES.length;c++){
      const jx=(Math.random()-0.5)*cw*0.5;
      const jy=(Math.random()-0.5)*ch*0.4;
      const t=Math.random();
      let type='g';
      if(t>0.93) type='r';
      else if(t>0.82) type='y';
      else if(t>0.72) type='a';
      nodes.push({
        name:NAMES[i],
        x:mx+c*cw+jx, y:my+r*ch+jy, type,
        ip:'10.'+ (r) +'.'+ (c) +'.'+ (1+i%200),
        status: type==='r'?'ALERT':type==='y'?'WARN':type==='a'?'LIVE':'OK',
        load: Math.floor(Math.random()*100)
      });
      i++;
    }
  }
  for(let n=0;n<nodes.length;n++){
    const a=nodes[n];
    let best=[];
    for(let m=0;m<nodes.length;m++){
      if(m===n) continue;
      const b=nodes[m];
      const d=(a.x-b.x)**2+(a.y-b.y)**2;
      best.push({m,d});
    }
    best.sort((p,q)=>p.d-q.d);
    const k=1+Math.floor(Math.random()*2);
    for(let j=0;j<k && j<best.length;j++){
      if(Math.random()>0.35) edges.push([n,best[j].m]);
    }
  }
}

function boxColor(t){
  if(t==='r') return RED;
  if(t==='y') return YELLOW;
  if(t==='a') return AMBER;
  return GREEN;
}

function draw(){
  ctx.fillStyle='#000'; ctx.fillRect(0,0,cv.width,cv.height);
  ctx.lineWidth=1;
  for(const e of edges){
    const a=nodes[e[0]], b=nodes[e[1]];
    ctx.strokeStyle=DGREEN;
    ctx.beginPath();
    ctx.moveTo(a.x|0, a.y|0);
    ctx.lineTo(b.x|0, b.y|0);
    ctx.stroke();
  }
  ctx.font='11px Consolas, monospace';
  ctx.textBaseline='middle';
  for(const nd of nodes){
    const col=boxColor(nd.type);
    ctx.fillStyle=col;
    ctx.fillRect((nd.x-4)|0, (nd.y-4)|0, 8, 8);
    if(sel===nd){
      ctx.strokeStyle='#fff';
      ctx.strokeRect((nd.x-5)|0, (nd.y-5)|0, 10, 10);
    }
    ctx.fillStyle = nd.type==='g'? GREEN : col;
    ctx.fillText(nd.name, (nd.x+7)|0, nd.y|0);
  }
}

let sel=null;
function hit(mx,my){
  for(const nd of nodes){
    if(Math.abs(nd.x-mx)<7 && Math.abs(nd.y-my)<7) return nd;
  }
  return null;
}

cv.addEventListener('mousemove', function(ev){
  const r=cv.getBoundingClientRect();
  const mx=ev.clientX-r.left, my=ev.clientY-r.top;
  const nd=hit(mx,my);
  cv.style.cursor = nd? 'pointer':'crosshair';
});

cv.addEventListener('click', function(ev){
  const r=cv.getBoundingClientRect();
  const mx=ev.clientX-r.left, my=ev.clientY-r.top;
  const nd=hit(mx,my);
  sel=nd;
  if(nd){
    pop.textContent =
      nd.name+'\n'+
      '------------\n'+
      'addr  '+nd.ip+'\n'+
      'state '+nd.status+'\n'+
      'load  '+nd.load+'%';
    pop.style.display='block';
    let px=mx+18, py=my+36;
    if(px+160>cv.width) px=mx-150;
    pop.style.left=px+'px';
    pop.style.top=py+'px';
  } else {
    pop.style.display='none';
  }
  draw();
});

build();
draw();
</script>
</body>
</html>";
    }
}
