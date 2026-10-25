using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace Aloha
{

    public class CockpitWindow : Form
    {
        private WebView2 web;
        private System.Windows.Forms.Timer feedTimer;
        private bool ready;
        private bool tracing;
        private int sampleRate = 10;
        private int counter;
        private readonly Random rng = new Random();

        public CockpitWindow()
        {
            Text = "Instruction Cockpit - Topological Surface";
            Size = new Size(1100, 720);
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

            feedTimer = new System.Windows.Forms.Timer { Interval = 60 };
            feedTimer.Tick += (s, e) => FeedTick();

            this.Load += async (s, e) => await InitAsync();
            this.FormClosed += (s, e) => { feedTimer.Stop(); };
        }

        private async Task InitAsync()
        {
            try
            {
                string udf = Path.Combine(
                    Path.GetDirectoryName(Application.ExecutablePath),
                    "CockpitProfile");
                Directory.CreateDirectory(udf);

                var env = await CoreWebView2Environment.CreateAsync(null, udf);
                await web.EnsureCoreWebView2Async(env);

                web.CoreWebView2.NavigateToString(Html);
                web.CoreWebView2.NavigationCompleted += (s, e) => { ready = true; };
            }
            catch (Exception ex)
            {
                MessageBox.Show("Cockpit failed to start WebView2:\n" + ex.Message,
                    "Cockpit", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        public void StartTracing(int rate)
        {
            sampleRate = Math.Max(1, rate);
            tracing = true;
            feedTimer.Start();
        }

        public void StopTracing()
        {
            tracing = false;
            feedTimer.Stop();
        }

        private void FeedTick()
        {
            if (!ready || !tracing || web?.CoreWebView2 == null) return;

            int batch = Math.Max(1, 12 / sampleRate + 1);
            var sb = new System.Text.StringBuilder();
            sb.Append("[");
            for (int k = 0; k < batch; k++)
            {
                if (k > 0) sb.Append(",");
                int op = rng.Next(6);
                int depth = (counter / 40) % 6;
                int rax = rng.Next(256);
                int isJump = (op >= 4) ? 1 : 0;
                int target = counter - rng.Next(30);
                sb.Append("{");
                sb.Append("\"ip\":").Append(counter).Append(",");
                sb.Append("\"op\":").Append(op).Append(",");
                sb.Append("\"jump\":").Append(isJump).Append(",");
                sb.Append("\"target\":").Append(target).Append(",");
                sb.Append("\"rax\":").Append(rax).Append(",");
                sb.Append("\"depth\":").Append(depth);
                sb.Append("}");
                counter++;
            }
            sb.Append("]");

            string js = "if(window.cockpit&&window.cockpit.add){window.cockpit.add(" + sb.ToString() + ");}";
            try { web.CoreWebView2.ExecuteScriptAsync(js); } catch { }
        }

        private const string Html = @"<!DOCTYPE html>
<html>
<head>
<meta charset='utf-8'>
<style>
  *{margin:0;padding:0;box-sizing:border-box;}
  html,body{width:100%;height:100%;background:#000;overflow:hidden;}
  #c{display:block;}
  #hud{position:absolute;top:10px;left:10px;color:#b4dcba;
    font-family:Consolas,monospace;font-size:12px;background:rgba(0,0,0,.65);
    border:1px solid #b4dcba;padding:8px 10px;z-index:10;min-width:170px;}
  #hud b{font-weight:bold;}
  #hud small{color:#19a040;}
</style>
</head>
<body>
<canvas id='c'></canvas>
<div id='hud'>
  <b>INSTRUCTION COCKPIT</b><br>
  instr&nbsp;&nbsp;<span id='n'>0</span><br>
  loops&nbsp;&nbsp;<span id='l'>0</span><br>
  branch&nbsp;<span id='b'>0</span><br>
  <small>drag rotate &middot; wheel zoom</small>
</div>
<script src='https://cdnjs.cloudflare.com/ajax/libs/three.js/r128/three.min.js'></script>
<script>
let scene,camera,renderer,surface;
const instr=[]; const loops=new Map(); const branches=new Map();
let rotX=0.5, rotY=0.4, dist=180, dragging=false, px=0, py=0;

window.cockpit={
  add:function(batch){
    for(const it of batch){
      instr.push(it);
      if(it.jump===1){
        if(it.target<it.ip){
          // backward jump -> loop
          if(!loops.has(it.target)) loops.set(it.target, instr.length-1);
        }
        const key=it.ip;
        if(!branches.has(key)) branches.set(key,{t:0,n:0});
        const s=branches.get(key);
        if(it.target<it.ip) s.t++; else s.n++;
      }
    }
    if(instr.length>600) instr.splice(0, instr.length-600);
    document.getElementById('n').textContent=instr.length;
    document.getElementById('l').textContent=loops.size;
    document.getElementById('b').textContent=branches.size;
    rebuild();
  }
};

function init(){
  scene=new THREE.Scene();
  scene.background=new THREE.Color(0x000000);
  camera=new THREE.PerspectiveCamera(70, innerWidth/innerHeight, 0.1, 5000);
  renderer=new THREE.WebGLRenderer({canvas:document.getElementById('c'),antialias:true});
  renderer.setSize(innerWidth,innerHeight);
  renderer.setPixelRatio(devicePixelRatio);
  const l=new THREE.PointLight(0x33ff66,1.1,1000); l.position.set(120,140,160); scene.add(l);
  scene.add(new THREE.AmbientLight(0x114022,0.6));
  addEventListener('resize',()=>{
    camera.aspect=innerWidth/innerHeight; camera.updateProjectionMatrix();
    renderer.setSize(innerWidth,innerHeight);
  });
  setupControls();
  rebuild();
  animate();
}

function surfPoint(u,v){
  // u: instruction index, v in [-1,1]
  const idx=Math.min(instr.length-1, Math.max(0, Math.floor(u)));
  const it=instr[idx]||{rax:0,depth:0,ip:0};
  let f=(it.rax/256)*60 + v*8 + it.depth*4;   // height
  let g=it.depth*4;                            // twist baseline
  // loop ripple
  for(const [tgt,start] of loops){
    if(idx>=start && idx<start+40){
      const p=((idx-start)%10)/10;
      g+=18*Math.sin(p*Math.PI*2);
    }
  }
  // branch bias
  const s=branches.get(it.ip);
  if(s){ const tot=s.t+s.n; if(tot>0){ g+=((s.t/tot)-0.5)*v*16; } }
  return [u, f, g];
}

function rebuild(){
  if(surface){ scene.remove(surface); surface.geometry.dispose(); surface.material.dispose(); }
  const uN=Math.max(2, Math.min(instr.length,180));
  const vN=24;
  const span=120, half=span/2;
  const verts=[]; const idxs=[];
  for(let ui=0;ui<uN;ui++){
    for(let vi=0;vi<vN;vi++){
      const u=instr.length*(ui/(uN-1));
      const v=(vi/(vN-1))*2-1;
      const p=surfPoint(u,v);
      const x=(ui/(uN-1))*span-half;
      const y=p[1];
      const z=v*half*0.7 + p[2];
      verts.push(x,y,z);
    }
  }
  for(let ui=0;ui<uN-1;ui++){
    for(let vi=0;vi<vN-1;vi++){
      const a=ui*vN+vi, b=(ui+1)*vN+vi, c=ui*vN+vi+1, d=(ui+1)*vN+vi+1;
      idxs.push(a,b,c, b,d,c);
    }
  }
  const geo=new THREE.BufferGeometry();
  geo.setAttribute('position', new THREE.Float32BufferAttribute(verts,3));
  geo.setIndex(idxs);
  geo.computeVertexNormals();
  const mat=new THREE.MeshPhongMaterial({color:0x33ff66, emissive:0x0c3a1c,
    side:THREE.DoubleSide, flatShading:false, wireframe:false});
  surface=new THREE.Mesh(geo,mat);
  scene.add(surface);
}

function setupControls(){
  const c=renderer.domElement;
  c.addEventListener('mousedown',e=>{dragging=true;px=e.clientX;py=e.clientY;});
  addEventListener('mouseup',()=>dragging=false);
  addEventListener('mousemove',e=>{
    if(!dragging)return;
    rotY+=(e.clientX-px)*0.01; rotX+=(e.clientY-py)*0.01;
    px=e.clientX; py=e.clientY;
  });
  c.addEventListener('wheel',e=>{e.preventDefault(); dist*=(1+e.deltaY*0.001);
    dist=Math.max(50,Math.min(dist,800));},{passive:false});
}

function animate(){
  requestAnimationFrame(animate);
  const cx=Math.cos(rotX);
  camera.position.x=Math.sin(rotY)*cx*dist;
  camera.position.y=Math.sin(rotX)*dist;
  camera.position.z=Math.cos(rotY)*cx*dist;
  camera.lookAt(0,0,0);
  renderer.render(scene,camera);
}

init();
</script>
</body>
</html>";
    }
}
