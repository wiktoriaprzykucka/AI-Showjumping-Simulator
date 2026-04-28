import { useState, useRef, useCallback } from "react";

const PADDING = 32;
const UNITY_SCALE = 10;

function HurdleSymbol({ hurdle, isSelected, onPointerDown }) {
  const col = isSelected ? "#b8860b" : "#1a1a1a";
  return (
    <g transform={`translate(${hurdle.x},${hurdle.y}) rotate(${hurdle.rotation})`}
      onPointerDown={onPointerDown} style={{ cursor: "pointer", userSelect: "none" }}>
      {isSelected && <circle r={30} fill="rgba(184,134,11,0.08)" stroke="#b8860b" strokeWidth={1.5} strokeDasharray="5,3" />}
      <line x1={0} y1={-26} x2={0} y2={-38} stroke={col} strokeWidth={1.5} markerEnd="url(#arr)" opacity={0.5} />
      <line x1={-16} y1={-6} x2={-16} y2={8} stroke={col} strokeWidth={3.5} strokeLinecap="round" />
      <line x1={16} y1={-6} x2={16} y2={8} stroke={col} strokeWidth={3.5} strokeLinecap="round" />
      <line x1={-16} y1={-3} x2={16} y2={-3} stroke={col} strokeWidth={4} strokeLinecap="round" />
      <line x1={-16} y1={4} x2={16} y2={4} stroke={isSelected ? "#b8860b" : "#555"} strokeWidth={2.5} strokeLinecap="round" />
      {[-8, 0, 8].map(cx => <line key={cx} x1={cx} y1={-3} x2={cx} y2={4} stroke="#999" strokeWidth={1} opacity={0.4} />)}
      <circle cx={0} cy={-21} r={12} fill={isSelected ? "#b8860b" : "white"} stroke={isSelected ? "#b8860b" : "#1a1a1a"} strokeWidth={1.5} />
      <text x={0} y={-17} textAnchor="middle" fontSize={hurdle.label.length > 2 ? 8 : 10}
        fontWeight="700" fill={isSelected ? "white" : "#1a1a1a"} fontFamily="Georgia, serif">
        {hurdle.label}
      </text>
    </g>
  );
}

function PathSeg({ x1, y1, x2, y2, color = "#999", dashed = true }) {
  const mx = (x1+x2)/2, my = (y1+y2)/2;
  const angle = Math.atan2(y2-y1, x2-x1)*180/Math.PI;
  return (
    <g>
      <line x1={x1} y1={y1} x2={x2} y2={y2} stroke={color} strokeWidth={1.2}
        strokeDasharray={dashed ? "10,6" : "none"} opacity={dashed ? 1 : 0.4} />
      <g transform={`translate(${mx},${my}) rotate(${angle})`}>
        <polygon points="0,-3.5 7,0 0,3.5" fill={color} opacity={dashed ? 0.7 : 0.3} />
      </g>
    </g>
  );
}

export default function CourseDesigner() {
  const [hurdles, setHurdles] = useState([]);
  const [selectedId, setSelectedId] = useState(null);
  const [tool, setTool] = useState("add");
  const [nextNum, setNextNum] = useState(1);
  const [comboMode, setComboMode] = useState(null);
  const [showExport, setShowExport] = useState(false);
  const [exportText, setExportText] = useState("");
  const [copied, setCopied] = useState(false);
  const [arenaW, setArenaW] = useState(60);
  const [arenaH, setArenaH] = useState(40);
  const [params, setParams] = useState({ name:"ZO 12", speed:350, length:450, time:78, height:1.40 });

  const svgRef = useRef(null);
  const dragRef = useRef(null);
  const rotRef = useRef(null);

  const canvasW = Math.max(400, arenaW * UNITY_SCALE + PADDING * 2);
  const canvasH = Math.max(280, arenaH * UNITY_SCALE + PADDING * 2);

  const getSVGPos = useCallback((e) => {
    const r = svgRef.current.getBoundingClientRect();
    return { x: e.clientX - r.left, y: e.clientY - r.top };
  }, []);

  const onCanvasPointerDown = useCallback((e) => {
    if (tool !== "add") return;
    e.preventDefault();
    const { x, y } = getSVGPos(e);
    // Clamp to arena
    const cx = Math.max(PADDING+5, Math.min(canvasW-PADDING-5, x));
    const cy = Math.max(PADDING+5, Math.min(canvasH-PADDING-5, y));
    let label;
    if (comboMode) {
      label = `${comboMode.num}${comboMode.suffix}`;
      if (comboMode.suffix === "a") setComboMode({ num: comboMode.num, suffix: "b" });
      else if (comboMode.suffix === "b") setComboMode({ num: comboMode.num, suffix: "c" });
      else { setComboMode(null); setNextNum(n => n+1); }
    } else {
      label = `${nextNum}`;
      setNextNum(n => n+1);
    }
    const id = Date.now();
    setHurdles(prev => [...prev, { id, x: cx, y: cy, rotation: 0, label }]);
    setSelectedId(id);
  }, [tool, getSVGPos, comboMode, nextNum, canvasW, canvasH]);

  const onHurdlePointerDown = useCallback((e, id) => {
    e.stopPropagation();
    setSelectedId(id);
    if (tool === "add") return;

    if (tool === "select") {
      const start = getSVGPos(e);
      const h = hurdles.find(h => h.id === id);
      dragRef.current = { origX: h.x, origY: h.y, startX: start.x, startY: start.y };
      const onMove = (ev) => {
        const cur = getSVGPos(ev);
        setHurdles(prev => prev.map(h => h.id === id
          ? { ...h, x: dragRef.current.origX + cur.x - dragRef.current.startX,
                   y: dragRef.current.origY + cur.y - dragRef.current.startY } : h));
      };
      const onUp = () => { window.removeEventListener("pointermove", onMove); window.removeEventListener("pointerup", onUp); };
      window.addEventListener("pointermove", onMove);
      window.addEventListener("pointerup", onUp);
    }

    if (tool === "rotate") {
      const h = hurdles.find(h => h.id === id);
      const sp = getSVGPos(e);
      rotRef.current = {
        startAngle: Math.atan2(sp.y - h.y, sp.x - h.x) * 180 / Math.PI,
        origRot: h.rotation, cx: h.x, cy: h.y,
      };
      const onMove = (ev) => {
        const cur = getSVGPos(ev);
        const angle = Math.atan2(cur.y - rotRef.current.cy, cur.x - rotRef.current.cx) * 180 / Math.PI;
        setHurdles(prev => prev.map(h => h.id === id
          ? { ...h, rotation: (rotRef.current.origRot + angle - rotRef.current.startAngle + 360) % 360 } : h));
      };
      const onUp = () => { window.removeEventListener("pointermove", onMove); window.removeEventListener("pointerup", onUp); };
      window.addEventListener("pointermove", onMove);
      window.addEventListener("pointerup", onUp);
    }
  }, [tool, getSVGPos, hurdles]);

  const stepRotate = (deg) =>
    setHurdles(prev => prev.map(h => h.id === selectedId ? { ...h, rotation: (h.rotation + deg + 360) % 360 } : h));

  const deleteSelected = () => { setHurdles(prev => prev.filter(h => h.id !== selectedId)); setSelectedId(null); };

  const undo = () => {
    if (!hurdles.length) return;
    const last = hurdles[hurdles.length - 1];
    setHurdles(prev => prev.slice(0, -1));
    const num = parseInt(last.label);
    if (!isNaN(num)) { setNextNum(num); setComboMode(null); }
    if (selectedId === last.id) setSelectedId(null);
  };

  const handleExport = () => {
    const data = {
      courseInfo: { name: params.name, arenaSize: `${arenaW}x${arenaH}m`, speed_m_per_min: +params.speed, length_m: +params.length, timeAllowed_sec: +params.time, obstacleHeight_m: +params.height },
      hurdles: hurdles.map((h, i) => ({
        order: i + 1, label: h.label,
        unityPosition: {
          x: parseFloat(((h.x - PADDING) / UNITY_SCALE).toFixed(2)),
          y: 0.0,
          z: parseFloat(((canvasH - PADDING - h.y) / UNITY_SCALE).toFixed(2)),
        },
        rotationY: Math.round(h.rotation),
      })),
    };
    setExportText(JSON.stringify(data, null, 2));
    setShowExport(true);
  };

  const sel = hurdles.find(h => h.id === selectedId);

  const TB = ({ id, icon, label }) => (
    <button onClick={() => setTool(id)} style={{
      display:"flex", alignItems:"center", gap:"8px", width:"100%",
      padding:"9px 12px", marginBottom:"5px", fontFamily:"Georgia, serif",
      fontSize:"13px", border:`1.5px solid ${tool===id?"#b8860b":"#d0c9b8"}`,
      background: tool===id?"#b8860b":"transparent", color: tool===id?"white":"#3a3020",
      cursor:"pointer", borderRadius:"2px", transition:"all 0.12s",
    }}>
      <span style={{fontSize:"15px", opacity: tool===id?1:0.55}}>{icon}</span>{label}
    </button>
  );

  const SL = ({ t }) => (
    <div style={{ fontSize:"9px", letterSpacing:"2px", textTransform:"uppercase", color:"#8a7a5a",
      fontFamily:"Georgia, serif", marginBottom:"10px", borderBottom:"1px solid #d0c9b8", paddingBottom:"6px" }}>
      {t}
    </div>
  );

  return (
    <div style={{ background:"#f5f0e8", minHeight:"100vh", fontFamily:"Georgia, serif" }}>
      {/* Header */}
      <div style={{ background:"#1a1a1a", color:"white", padding:"12px 20px", display:"flex", alignItems:"center", justifyContent:"space-between" }}>
        <div>
          <div style={{ fontSize:"9px", letterSpacing:"3px", textTransform:"uppercase", color:"#b8860b", marginBottom:"2px" }}>Show Jumping</div>
          <h1 style={{ margin:0, fontFamily:"Georgia, serif", fontSize:"20px", fontWeight:"900", letterSpacing:"1px" }}>Course Designer</h1>
        </div>
        <div style={{ textAlign:"right", fontSize:"11px", color:"#888", lineHeight:"1.7" }}>
          <div style={{ color:"#b8860b", fontWeight:"bold", fontStyle:"italic" }}>{params.name || "—"}</div>
          <div>{hurdles.length} obstacle{hurdles.length!==1?"s":""}</div>
        </div>
      </div>

      {/* Params bar */}
      <div style={{ background:"white", borderBottom:"2px solid #1a1a1a", padding:"8px 20px", display:"flex", flexWrap:"wrap", gap:"20px", alignItems:"center" }}>
        {[{k:"name",l:"Name",t:"text",w:"100px"},{k:"speed",l:"Speed m/min",t:"number",w:"70px"},{k:"length",l:"Length m",t:"number",w:"70px"},{k:"time",l:"Time sec",t:"number",w:"60px"},{k:"height",l:"Height m",t:"number",w:"60px",s:"0.05"}].map(({k,l,t,w,s})=>(
          <label key={k} style={{ display:"flex", flexDirection:"column", gap:"2px" }}>
            <span style={{ fontSize:"8px", letterSpacing:"1.5px", textTransform:"uppercase", color:"#8a7a5a" }}>{l}</span>
            <input type={t} value={params[k]} step={s} onChange={e=>setParams(p=>({...p,[k]:e.target.value}))}
              style={{ width:w, padding:"4px 6px", textAlign:"center", fontFamily:"Georgia, serif", fontSize:"13px", fontWeight:"bold", border:"1px solid #d0c9b8", background:"#fdfbf7", borderRadius:"1px", outline:"none" }} />
          </label>
        ))}
      </div>

      <div style={{ display:"flex" }}>
        {/* Sidebar */}
        <div style={{ width:"190px", flexShrink:0, background:"#fdfbf7", borderRight:"1px solid #d0c9b8", padding:"14px", overflowY:"auto", minHeight:"calc(100vh - 110px)" }}>

          <SL t="Tools" />
          <TB id="add" icon="＋" label="Add Hurdle" />
          <TB id="select" icon="↖" label="Move" />
          <TB id="rotate" icon="↻" label="Rotate (drag)" />

          <div style={{ height:"14px" }} />
          <SL t="Combination" />
          <button onClick={()=>{ setComboMode({num:nextNum,suffix:"a"}); setTool("add"); }} style={{
            width:"100%", padding:"8px 12px", marginBottom:"4px",
            fontFamily:"Georgia, serif", fontSize:"12px",
            border:`1.5px solid ${comboMode?"#7c5c00":"#d0c9b8"}`,
            background: comboMode?"#7c5c00":"transparent",
            color: comboMode?"white":"#7c5c00", cursor:"pointer", borderRadius:"2px",
          }}>
            {comboMode ? `Placing ${comboMode.num}${comboMode.suffix}…` : "Start Combo"}
          </button>
          {comboMode && <button onClick={()=>setComboMode(null)} style={{ width:"100%", padding:"5px 12px", fontFamily:"Georgia, serif", fontSize:"11px", border:"1px solid #d0c9b8", background:"transparent", color:"#aaa", cursor:"pointer", borderRadius:"2px" }}>Cancel</button>}

          <div style={{ height:"14px" }} />
          <SL t="Arena Size (m)" />
          <div style={{ display:"flex", gap:"6px", marginBottom:"6px" }}>
            {[["W", arenaW, setArenaW],["D", arenaH, setArenaH]].map(([l,v,s])=>(
              <label key={l} style={{ flex:1, display:"flex", flexDirection:"column", gap:"2px" }}>
                <span style={{ fontSize:"8px", letterSpacing:"1px", textTransform:"uppercase", color:"#8a7a5a" }}>{l==="W"?"Width":"Depth"}</span>
                <input type="number" value={v} min={10} max={200} onChange={e=>s(Math.max(10,+e.target.value))}
                  style={{ width:"100%", boxSizing:"border-box", padding:"5px 4px", textAlign:"center", fontFamily:"Georgia,serif", fontSize:"12px", border:"1px solid #d0c9b8", background:"#fdfbf7", borderRadius:"1px", outline:"none" }} />
              </label>
            ))}
          </div>
          <div style={{ fontSize:"10px", color:"#aaa", marginBottom:"4px" }}>{arenaW} × {arenaH} m</div>

          {sel && (
            <>
              <div style={{ height:"14px" }} />
              <SL t={`Hurdle ${sel.label}`} />
              <div style={{ fontSize:"11px", color:"#8a7a5a", marginBottom:"8px" }}>Rotation: <strong>{Math.round(sel.rotation)}°</strong></div>
              <div style={{ display:"grid", gridTemplateColumns:"1fr 1fr", gap:"4px", marginBottom:"8px" }}>
                {[[-45,"−45°"],[-15,"−15°"],[15,"+15°"],[45,"+45°"]].map(([d,l])=>(
                  <button key={d} onClick={()=>stepRotate(d)} style={{ padding:"6px 2px", fontFamily:"Georgia,serif", fontSize:"11px", border:"1px solid #d0c9b8", background:"white", cursor:"pointer", borderRadius:"2px" }}>{l}</button>
                ))}
              </div>
              <div style={{ fontSize:"10px", color:"#aaa", marginBottom:"10px", lineHeight:"1.7" }}>
                X: {((sel.x - PADDING)/UNITY_SCALE).toFixed(1)}m &nbsp; Z: {((canvasH - PADDING - sel.y)/UNITY_SCALE).toFixed(1)}m
              </div>
              <button onClick={deleteSelected} style={{ width:"100%", padding:"7px 12px", fontFamily:"Georgia,serif", fontSize:"12px", border:"1.5px solid #c0392b", background:"transparent", color:"#c0392b", cursor:"pointer", borderRadius:"2px" }}>
                🗑 Delete
              </button>
            </>
          )}

          <div style={{ height:"14px" }} />
          <SL t="Actions" />
          <button onClick={handleExport} style={{ width:"100%", padding:"10px 12px", marginBottom:"6px", fontFamily:"Georgia, serif", fontSize:"13px", fontWeight:"700", border:"none", background:"#1a1a1a", color:"white", cursor:"pointer", borderRadius:"2px" }}>
            Export JSON →
          </button>
          <button onClick={undo} style={{ width:"100%", padding:"8px 12px", marginBottom:"5px", fontFamily:"Georgia,serif", fontSize:"12px", border:"1px solid #d0c9b8", background:"transparent", color:"#3a3020", cursor:"pointer", borderRadius:"2px" }}>
            ↩ Undo
          </button>
          <button onClick={()=>{ setHurdles([]); setSelectedId(null); setNextNum(1); setComboMode(null); }}
            style={{ width:"100%", padding:"8px 12px", fontFamily:"Georgia,serif", fontSize:"12px", border:"1px solid #c0392b", background:"transparent", color:"#c0392b", cursor:"pointer", borderRadius:"2px" }}>
            Clear all
          </button>

          {/* Status hint */}
          <div style={{ marginTop:"16px", fontSize:"10px", color:"#aaa", lineHeight:"1.6", fontStyle:"italic" }}>
            {tool==="add" ? (comboMode ? `Next: ${comboMode.num}${comboMode.suffix}` : `Next: ${nextNum}`) : tool==="rotate" ? "Drag hurdle to rotate" : "Drag hurdle to move"}
          </div>
        </div>

        {/* Canvas */}
        <div style={{ flex:1, padding:"16px", overflow:"auto", background:"#f5f0e8" }}>
          <svg ref={svgRef} width={canvasW} height={canvasH}
            onPointerDown={onCanvasPointerDown}
            style={{ background:"white", border:"2px solid #1a1a1a", cursor: tool==="add"?"crosshair":"default", display:"block", boxShadow:"5px 5px 0 #c8bfa8" }}>
            <defs>
              <marker id="arr" markerWidth="8" markerHeight="8" refX="6" refY="4" orient="auto">
                <path d="M0,0 L0,8 L8,4 z" fill="#555" />
              </marker>
              <pattern id="dots" x="0" y="0" width={UNITY_SCALE*10} height={UNITY_SCALE*10} patternUnits="userSpaceOnUse">
                <rect width={UNITY_SCALE*10} height={UNITY_SCALE*10} fill="#fdfbf7" />
              </pattern>
            </defs>

            {/* Arena */}
            <rect x={PADDING} y={PADDING} width={canvasW-PADDING*2} height={canvasH-PADDING*2} fill="#fdfbf7" />
            <rect x={PADDING} y={PADDING} width={canvasW-PADDING*2} height={canvasH-PADDING*2} fill="none" stroke="#1a1a1a" strokeWidth={2} />
            <rect x={PADDING+5} y={PADDING+5} width={canvasW-PADDING*2-10} height={canvasH-PADDING*2-10} fill="none" stroke="#1a1a1a" strokeWidth={0.6} opacity={0.35} />

            {/* Grid lines */}
            {Array.from({length: Math.floor(arenaW/10)-1},(_,i)=>{
              const gx = PADDING+(i+1)*10*UNITY_SCALE;
              return <g key={`gv${i}`}>
                <line x1={gx} y1={PADDING} x2={gx} y2={canvasH-PADDING} stroke="#ddd" strokeWidth={0.8} strokeDasharray="4,4" />
                <text x={gx} y={PADDING-6} textAnchor="middle" fontSize="8" fill="#bbb" fontFamily="Georgia,serif">{(i+1)*10}m</text>
              </g>;
            })}
            {Array.from({length: Math.floor(arenaH/10)-1},(_,i)=>{
              const gy = PADDING+(i+1)*10*UNITY_SCALE;
              return <g key={`gh${i}`}>
                <line x1={PADDING} y1={gy} x2={canvasW-PADDING} y2={gy} stroke="#ddd" strokeWidth={0.8} strokeDasharray="4,4" />
                <text x={PADDING-5} y={gy+3} textAnchor="end" fontSize="8" fill="#bbb" fontFamily="Georgia,serif">{(i+1)*10}m</text>
              </g>;
            })}

            {/* Scale bar */}
            <g transform={`translate(${PADDING}, ${canvasH-14})`}>
              <line x1={0} y1={0} x2={UNITY_SCALE*10} y2={0} stroke="#aaa" strokeWidth={1.5} />
              <line x1={0} y1={-4} x2={0} y2={3} stroke="#aaa" strokeWidth={1.5} />
              <line x1={UNITY_SCALE*10} y1={-4} x2={UNITY_SCALE*10} y2={3} stroke="#aaa" strokeWidth={1.5} />
              <text x={UNITY_SCALE*5} y={-7} textAnchor="middle" fontSize="8" fill="#aaa" fontFamily="Georgia,serif">10m</text>
            </g>

            {/* Watermark */}
            <text x={canvasW-PADDING-6} y={canvasH-PADDING-6} textAnchor="end" fontSize="11" fill="#e0d8c8" fontFamily="Georgia,serif" fontStyle="italic">{params.name}</text>

            {/* Path lines */}
            {hurdles.map((h,i)=>{
              if(i===0) return null;
              return <PathSeg key={`p${h.id}`} x1={hurdles[i-1].x} y1={hurdles[i-1].y} x2={h.x} y2={h.y} />;
            })}

            {/* Loop line last → first */}
            {hurdles.length>1 && <PathSeg x1={hurdles[hurdles.length-1].x} y1={hurdles[hurdles.length-1].y} x2={hurdles[0].x} y2={hurdles[0].y} color="#b8860b" dashed={false} />}

            {/* START / FINISH */}
            {hurdles.length>0 && (
              <g transform={`translate(${hurdles[0].x+18},${hurdles[0].y+20})`}>
                <rect x={-2} y={-11} width={36} height={14} rx={1} fill="#e8f5ec" stroke="#2e7d4f" strokeWidth={1} />
                <text fontSize="9" fill="#2e7d4f" fontFamily="Georgia,serif" fontWeight="bold" y={0}>START</text>
              </g>
            )}
            {hurdles.length>1 && (
              <g transform={`translate(${hurdles[hurdles.length-1].x+18},${hurdles[hurdles.length-1].y-22})`}>
                <rect x={-2} y={-11} width={40} height={14} rx={1} fill="#fdecea" stroke="#c0392b" strokeWidth={1} />
                <text fontSize="9" fill="#c0392b" fontFamily="Georgia,serif" fontWeight="bold" y={0}>FINISH</text>
              </g>
            )}

            {/* Hurdles */}
            {hurdles.map(h=>(
              <HurdleSymbol key={h.id} hurdle={h} isSelected={h.id===selectedId}
                onPointerDown={(e)=>onHurdlePointerDown(e,h.id)} />
            ))}
          </svg>
        </div>
      </div>

      {/* Export modal */}
      {showExport && (
        <div style={{ position:"fixed", inset:0, background:"rgba(0,0,0,0.6)", display:"flex", alignItems:"center", justifyContent:"center", zIndex:999 }}>
          <div style={{ background:"white", border:"2px solid #1a1a1a", padding:"24px", maxWidth:"600px", width:"92%", maxHeight:"85vh", overflow:"auto" }}>
            <div style={{ display:"flex", justifyContent:"space-between", alignItems:"flex-start", marginBottom:"14px" }}>
              <div>
                <h2 style={{ margin:0, fontFamily:"Georgia,serif", fontSize:"18px" }}>Unity Export</h2>
                <p style={{ margin:"4px 0 0", fontSize:"11px", color:"#888" }}>Save as <code style={{ background:"#f3f0e8", padding:"1px 4px" }}>course.json</code> in your Unity Assets folder</p>
              </div>
              <button onClick={()=>setShowExport(false)} style={{ border:"none", background:"none", fontSize:"22px", cursor:"pointer", color:"#555", lineHeight:1 }}>✕</button>
            </div>
            <pre style={{ background:"#fdfbf7", border:"1px solid #d0c9b8", padding:"14px", fontSize:"11px", overflow:"auto", maxHeight:"380px", lineHeight:"1.6", borderRadius:"2px", fontFamily:"monospace" }}>
              {exportText}
            </pre>
            <div style={{ display:"flex", gap:"10px", marginTop:"14px" }}>
              <button onClick={()=>{ navigator.clipboard?.writeText(exportText); setCopied(true); setTimeout(()=>setCopied(false),2000); }}
                style={{ padding:"9px 18px", fontFamily:"Georgia,serif", fontSize:"13px", border:"none", background: copied?"#2e7d4f":"#1a1a1a", color:"white", cursor:"pointer", borderRadius:"2px", transition:"background 0.2s" }}>
                {copied?"✓ Copied!":"Copy to clipboard"}
              </button>
              <button onClick={()=>setShowExport(false)} style={{ padding:"9px 18px", fontFamily:"Georgia,serif", fontSize:"13px", border:"1px solid #d0c9b8", background:"transparent", cursor:"pointer", borderRadius:"2px" }}>
                Close
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
