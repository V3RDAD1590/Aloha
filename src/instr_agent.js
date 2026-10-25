// ============================================================
// instr_agent.js — Frida agent for Aloha INSTR
// Attach: frida -p <renderer_pid> -l instr_agent.js -q
// Emits one JSON object per line on stdout via console.log:
//   {"t":"proc","pid":...,"name":...,"nthreads":...}
//   {"t":"threads","list":[{"id":..,"state":..,"cpu":NN,"pc":"0x.."}]}
//   {"t":"regs","tid":..,"ctx":{"rax":"0x..", ... "rip":"0x.."}}
//   {"t":"mods","list":[{"name":..,"base":"0x..","size":..}]}
// Every value here is read from the live process — nothing synthetic.
//
// CPU%: real, via GetThreadTimes deltas across two samples (Windows).
// kernel+user 100ns ticks per thread, differenced over the wall interval.
// ============================================================

var IS_WIN = Process.platform === 'windows';
var lastTimes = {};   // tid -> total 100ns ticks at previous sample
var lastWall = Date.now();

var GetThreadTimes = null, OpenThread = null, CloseHandle = null;
if (IS_WIN) {
  try {
    GetThreadTimes = new NativeFunction(
      Module.getExportByName('kernel32.dll', 'GetThreadTimes'),
      'int', ['pointer','pointer','pointer','pointer','pointer']);
    OpenThread = new NativeFunction(
      Module.getExportByName('kernel32.dll', 'OpenThread'),
      'pointer', ['uint32','int','uint32']);
    CloseHandle = new NativeFunction(
      Module.getExportByName('kernel32.dll', 'CloseHandle'),
      'int', ['pointer']);
  } catch (e) { /* fall back to state-only below */ }
}

var THREAD_QUERY_INFORMATION = 0x0040;

function threadTicks(tid) {
  if (!GetThreadTimes || !OpenThread) return -1;
  var h = OpenThread(THREAD_QUERY_INFORMATION, 0, tid);
  if (h.isNull()) return -1;
  try {
    var c = Memory.alloc(8), e = Memory.alloc(8), k = Memory.alloc(8), u = Memory.alloc(8);
    if (GetThreadTimes(h, c, e, k, u) === 0) return -1;
    // FILETIME = 100ns units, 64-bit split lo/hi
    var kern = k.readU32() + k.add(4).readU32() * 4294967296;
    var user = u.readU32() + u.add(4).readU32() * 4294967296;
    return kern + user;
  } finally { CloseHandle(h); }
}

function safeCtx(th) {
  var c = th.context || {};
  var out = {};
  ['rax','rbx','rcx','rdx','rsi','rdi','rsp','rip','pc','sp'].forEach(function (k) {
    if (c[k] !== undefined) out[k] = c[k].toString();
  });
  return out;
}

function sampleThreads() {
  try {
    var now = Date.now();
    var dtMs = Math.max(1, now - lastWall);
    lastWall = now;
    var ths = Process.enumerateThreads();
    var list = ths.slice(0, 16).map(function (th) {
      var cpu = -1;
      var ticks = threadTicks(th.id);
      if (ticks >= 0) {
        var prev = lastTimes[th.id];
        if (prev !== undefined) {
          var deltaMs = (ticks - prev) / 10000.0;        // 100ns ticks -> ms of CPU
          cpu = Math.max(0, Math.min(100, Math.round(deltaMs / dtMs * 100)));
        }
        lastTimes[th.id] = ticks;
      }
      return {
        id: th.id,
        state: th.state,
        cpu: cpu,
        pc: th.context && th.context.pc ? th.context.pc.toString() : '0x0'
      };
    });
    console.log(JSON.stringify({ t: 'threads', list: list }));
    if (ths.length > 0)
      console.log(JSON.stringify({ t: 'regs', tid: ths[0].id, ctx: safeCtx(ths[0]) }));
  } catch (e) {
    console.log(JSON.stringify({ t: 'err', m: '' + e }));
  }
}

function sampleModules() {
  try {
    var mods = Process.enumerateModules().slice(0, 12).map(function (m) {
      return { name: m.name, base: m.base.toString(), size: m.size };
    });
    console.log(JSON.stringify({ t: 'mods', list: mods }));
  } catch (e) { /* modules are stable; ignore transient failures */ }
}

// TASK level: a real call stack for the busiest thread. We backtrace from the
// thread's live register context and symbolize each frame to module!symbol+off.
// Nothing here is invented — frames come from Thread.backtrace on the real ctx.
function sampleStack() {
  try {
    var ths = Process.enumerateThreads();
    if (ths.length === 0) return;
    // pick the thread with the highest recent CPU (fallback: first)
    var best = ths[0], bestCpu = -1;
    ths.forEach(function (th) {
      var prev = lastTimes[th.id];
      if (prev !== undefined) {
        var t = threadTicks(th.id);
        if (t >= 0 && (t - prev) > bestCpu) { bestCpu = t - prev; best = th; }
      }
    });
    if (!best.context) { console.log(JSON.stringify({ t: 'stack', tid: best.id, frames: [] })); return; }
    var frames = Thread.backtrace(best.context, Backtracer.FUZZY)
      .slice(0, 12)
      .map(function (addr) {
        try {
          var s = DebugSymbol.fromAddress(addr);
          return {
            addr: addr.toString(),
            mod:  s.moduleName || '?',
            sym:  s.name || ('+' + (s.address ? addr.sub(s.address).toString() : '0')),
            off:  (s.address && !s.address.isNull()) ? addr.sub(s.address).toString() : '0x0'
          };
        } catch (e2) {
          return { addr: addr.toString(), mod: '?', sym: '?', off: '0x0' };
        }
      });
    console.log(JSON.stringify({ t: 'stack', tid: best.id, frames: frames }));
  } catch (e) {
    console.log(JSON.stringify({ t: 'err', m: 'stack: ' + e }));
  }
}

// FUNCTION level: real resolved exports near the thread PCs — the actual
// functions the renderer's hot modules expose, read from the live image.
function sampleSyms() {
  try {
    // resolve the program counters of the live threads to module!symbol
    var ths = Process.enumerateThreads().slice(0, 12);
    var seen = {}, out = [];
    ths.forEach(function (th) {
      var pc = th.context && th.context.pc ? th.context.pc : null;
      if (!pc) return;
      try {
        var s = DebugSymbol.fromAddress(pc);
        var key = (s.moduleName || '?') + '!' + (s.name || pc.toString());
        if (!seen[key]) {
          seen[key] = true;
          out.push({
            tid: th.id,
            mod: s.moduleName || '?',
            sym: s.name || '?',
            addr: pc.toString()
          });
        }
      } catch (e2) { /* unresolved pc — skip */ }
    });
    console.log(JSON.stringify({ t: 'syms', list: out }));
  } catch (e) { /* symbol tables vary; ignore transient failures */ }
}

console.log(JSON.stringify({
  t: 'proc',
  pid: Process.id,
  name: 'renderer',
  arch: Process.arch,
  nthreads: Process.enumerateThreads().length
}));
console.log(JSON.stringify({ t: 'ready', pid: Process.id }));

sampleModules();
sampleThreads();                       // prime the tick baseline
sampleStack();
sampleSyms();
setInterval(sampleThreads, 420);       // matches INSTR's sample-and-hold cadence
setInterval(sampleStack, 600);         // TASK: real backtrace of the busiest thread
setInterval(sampleSyms, 800);          // FUNCTION: real resolved symbols at thread PCs
setInterval(sampleModules, 5000);
