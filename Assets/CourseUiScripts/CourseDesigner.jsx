import { useState, useRef, useCallback, useMemo } from "react";

const PADDING = 32;
const UNITY_SCALE = 10;

// ─── FEI path / obstacle helpers ───────────────────────────────────────
function parseObstacleLabel(label) {
  const m = /^(\d+)([abc])?$/i.exec(String(label ?? "").trim());
  if (!m) return null;
  return { num: parseInt(m[1], 10), suffix: (m[2] || "").toLowerCase() };
}

function groupHurdlesIntoObstacles(hurdles) {
  const groups = [];
  let i = 0;
  while (i < hurdles.length) {
    const p = parseObstacleLabel(hurdles[i].label);
    if (!p) {
      groups.push([hurdles[i]]);
      i++;
      continue;
    }
    const base = p.num;
    let j = i;
    while (j + 1 < hurdles.length) {
      const p2 = parseObstacleLabel(hurdles[j + 1].label);
      if (!p2 || p2.num !== base) break;
      j++;
    }
    groups.push(hurdles.slice(i, j + 1));
    i = j + 1;
  }
  return groups;
}

function getComboGroupFor(hurdles, id) {
  const target = hurdles.find(h => h.id === id);
  if (!target) return [];
  const groups = groupHurdlesIntoObstacles(hurdles);
  for (const g of groups) {
    if (g.some(h => h.id === id)) return g;
  }
  return [target];
}

function feiTotalsFromGroups(groups) {
  let singles = 0, doubles = 0, triples = 0, efforts = 0;
  let invalidCombo = false;
  for (const g of groups) {
    efforts += g.length;
    if (g.length === 1) singles++;
    else if (g.length === 2) doubles++;
    else if (g.length === 3) triples++;
    else invalidCombo = true;
  }
  return { obstacleCount: groups.length, efforts, singles, doubles, triples, invalidCombo };
}

function svgToUnityxz(cx, cy, canvasH) {
  return {
    x: (cx - PADDING) / UNITY_SCALE,
    z: (canvasH - PADDING - cy) / UNITY_SCALE,
  };
}

function buildPathPtsUnity(hurdles, startSensor, finishSensor, canvasH) {
  const pts = [];
  if (!hurdles?.length) return pts;
  if (startSensor) pts.push(svgToUnityxz(startSensor.x, startSensor.y, canvasH));
  hurdles.forEach((h) => pts.push(svgToUnityxz(h.x, h.y, canvasH)));
  if (finishSensor) pts.push(svgToUnityxz(finishSensor.x, finishSensor.y, canvasH));
  else if (hurdles.length > 1) pts.push(svgToUnityxz(hurdles[0].x, hurdles[0].y, canvasH));
  return pts;
}

function pathLengthMxz(pts) {
  let d = 0;
  for (let i = 1; i < pts.length; i++) {
    const dx = pts[i].x - pts[i - 1].x;
    const dz = pts[i].z - pts[i - 1].z;
    d += Math.hypot(dx, dz);
  }
  return d;
}

function comboSpacingIssues(groups, canvasH) {
  const out = [];
  groups.forEach((g, gi) => {
    if (g.length < 2) return;
    for (let k = 0; k < g.length - 1; k++) {
      const a = svgToUnityxz(g[k].x, g[k].y, canvasH);
      const b = svgToUnityxz(g[k + 1].x, g[k + 1].y, canvasH);
      const d = Math.hypot(b.x - a.x, b.z - a.z);
      if (d < 7 || d > 11) {
        out.push({
          level: "warn",
          code: "combo-dist",
          msg: `Combination ${gi + 1}: ~${d.toFixed(1)} m between numbered elements (typical schooling span ~7–11 m).`,
          fix: "Adjust spacing toward ~7–11 m between elements when possible.",
        });
      }
    }
  });
  return out;
}

function sharpTurnIssues(pts, minTurnDeg = 38) {
  const out = [];
  for (let i = 1; i < pts.length - 1; i++) {
    const ax = pts[i].x - pts[i - 1].x, az = pts[i].z - pts[i - 1].z;
    const bx = pts[i + 1].x - pts[i].x, bz = pts[i + 1].z - pts[i].z;
    const la = Math.hypot(ax, az), lb = Math.hypot(bx, bz);
    if (la < 0.5 || lb < 0.5) continue;
    const dot = (ax * bx + az * bz) / (la * lb);
    const ang = Math.acos(Math.max(-1, Math.min(1, dot))) * 180 / Math.PI;
    if (ang < minTurnDeg) {
      out.push({
        level: "warn",
        code: "sharp-turn",
        msg: `Line of travel bends sharply (~${ang.toFixed(0)}°) at point ${i + 1}.`,
        fix: "Soften corners and widen the arc so the horse can balance.",
      });
    }
  }
  return out;
}

function wallProximityIssuesxz(pts, arenaWm, arenaHm, minClearM = 2.8) {
  const out = [];
  pts.forEach((p, i) => {
    const clear = Math.min(p.x, arenaWm - p.x, p.z, arenaHm - p.z);
    if (clear < minClearM) {
      out.push({
        level: "warn",
        code: "wall",
        msg: `Path point ${i + 1} is ~${clear.toFixed(1)} m inside the arena boundary.`,
        fix: "Keep gallop tracks farther from rails for safety margins.",
      });
    }
  });
  return out;
}

function approachLandingIssuesxz(pts, hasStart, hasFinish) {
  if (pts.length < 2) return [];
  const segs = [];
  for (let i = 1; i < pts.length; i++) {
    const dx = pts[i].x - pts[i - 1].x, dz = pts[i].z - pts[i - 1].z;
    segs.push(Math.hypot(dx, dz));
  }
  const n = segs.length;
  const out = [];
  if (hasStart && n >= 1 && segs[0] < 12) {
    out.push({
      level: "warn",
      code: "approach-first",
      msg: `Approach toward the first obstacle is ~${segs[0].toFixed(1)} m.`,
      fix: "Extend the start placement on the straight line toward fence 1 (often ~≥12 m).",
    });
  }
  for (let i = 1; i < n - 1; i++) {
    if (segs[i] < 10) {
      out.push({
        level: "warn",
        code: "leg-short",
        msg: `Riding leg ${i + 1} is ~${segs[i].toFixed(1)} m—short for balanced landing and departure.`,
        fix: "Move obstacles along the path to lengthen this section.",
      });
    }
  }
  if (hasFinish && n >= 1) {
    const last = segs[n - 1];
    if (last < 8) {
      out.push({
        level: "warn",
        code: "gallop-out",
        msg: `Finish is ~${last.toFixed(1)} m past the final obstacle.`,
        fix: "Place the finish farther out on the getaway line.",
      });
    }
  }
  return out;
}

function HurdleSymbol({ hurdle, isSelected, onPointerDown }) {
  const col = isSelected ? "#b8860b" : "#1a1a1a";
  return (
    <g transform={`translate(${hurdle.x},${hurdle.y}) rotate(${hurdle.rotation})`}
      onPointerDown={onPointerDown}
      style={{ cursor: "pointer", userSelect: "none", pointerEvents: "all" }}>
      {/* invisible but paintable hit area so the whole hurdle footprint is always grabbable */}
      <circle r={32} fill="#000" fillOpacity={0.001} pointerEvents="all" />
      {isSelected && <circle r={30} fill="rgba(184,134,11,0.08)" stroke="#b8860b" strokeWidth={1.5} strokeDasharray="5,3" pointerEvents="none" />}
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

function SensorMarker({ kind, pt, isSelected, onPointerDown }) {
  const isStart = kind === "start";
  const stroke = isStart ? "#2e7d4f" : "#c0392b";
  const fill = isStart ? "#e8f5ec" : "#fdecea";
  const label = isStart ? "START" : "FIN";
  return (
    <g transform={`translate(${pt.x},${pt.y}) rotate(${pt.rotation})`}
      onPointerDown={onPointerDown} style={{ cursor: "pointer", userSelect: "none" }}>
      {isSelected && <circle r={28} fill="rgba(184,134,11,0.08)" stroke="#b8860b" strokeWidth={1.5} strokeDasharray="5,3" />}
      <line x1={0} y1={-8} x2={0} y2={-32} stroke={stroke} strokeWidth={1.5} markerEnd="url(#arr)" opacity={0.55} />
      <rect x={-22} y={-14} width={44} height={22} rx={2} fill={fill} stroke={stroke} strokeWidth={1.5} />
      <text x={0} y={3} textAnchor="middle" fontSize={9} fontWeight="700"
        fill={stroke} fontFamily="Georgia, serif">{label}</text>
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
  const [startSensor, setStartSensor] = useState(null); // { x, y, rotation }
  const [finishSensor, setFinishSensor] = useState(null);
  const [selectedId, setSelectedId] = useState(null);
  const [selectedSensor, setSelectedSensor] = useState(null); // "start" | "finish" | null
  const [tool, setTool] = useState("add");
  const [nextNum, setNextNum] = useState(1);
  const [comboMode, setComboMode] = useState(null);
  const [showExport, setShowExport] = useState(false);
  const [exportText, setExportText] = useState("");
  const [copied, setCopied] = useState(false);
  const [arenaW, setArenaW] = useState(60);
  const [arenaH, setArenaH] = useState(40);
  const [params, setParams] = useState({ name: "ZO 12", speed: 350, height: 1.4 });
  const [speedPreset, setSpeedPreset] = useState("indoor");

  const svgRef = useRef(null);
  const dragRef = useRef(null);
  const rotRef = useRef(null);

  const canvasW = Math.max(400, arenaW * UNITY_SCALE + PADDING * 2);
  const canvasH = Math.max(280, arenaH * UNITY_SCALE + PADDING * 2);

  const fei = useMemo(() => {
    const px = typeof params.speed === "string" ? parseFloat(String(params.speed).replace(",", ".")) : Number(params.speed);
    const speed = Number.isFinite(px) && px > 0 ? px : 0;
    const ph = typeof params.height === "string" ? parseFloat(String(params.height).replace(",", ".")) : Number(params.height);
    const heightM = Number.isFinite(ph) ? ph : NaN;

    const groups = groupHurdlesIntoObstacles(hurdles);
    const totals = feiTotalsFromGroups(groups);
    const pts = buildPathPtsUnity(hurdles, startSensor, finishSensor, canvasH);
    const lengthM = pathLengthMxz(pts);
    const timeAllowedSec = speed > 0 && lengthM > 0 ? Math.round((lengthM / speed) * 60) : 0;
    const timeLimitSec = timeAllowedSec > 0 ? Math.round(timeAllowedSec * 2) : 0;

    const issues = [];
    const hasCourse = hurdles.length > 0;

    if (!hasCourse) {
      return {
        empty: true,
        hasCourse: false,
        obstacleCount: 0,
        efforts: 0,
        singles: 0,
        doubles: 0,
        triples: 0,
        invalidCombo: false,
        groups,
        lengthM,
        speed,
        heightM,
        timeAllowedSec: 0,
        timeLimitSec: 0,
        issues,
        overall: null,
      };
    }

    if (!speed) {
      issues.push({
        level: "invalid",
        code: "speed",
        msg: "Speed must be entered and greater than zero (m/min).",
        fix: "Choose a preset speed or enter a custom value.",
      });
    }

    if (lengthM <= 0) {
      issues.push({
        level: "invalid",
        code: "length",
        msg: "Course length along the riding line is zero.",
        fix: "Place start, obstacles, and finish so consecutive path points span distance.",
      });
    }

    if (!Number.isFinite(heightM)) {
      issues.push({
        level: "invalid",
        code: "height-num",
        msg: "Fence height must be set to a numeric value.",
        fix: "Enter obstacle height (meters) in parameters.",
      });
    } else if (heightM < 0.3 || heightM > 1.6) {
      issues.push({
        level: "invalid",
        code: "height",
        msg: `Height ${heightM.toFixed(2)} m is outside the simulator range (0.30–1.60 m).`,
        fix: "Adjust Height to remain within bounds for compatibility with training assets.",
      });
    }

    if (totals.invalidCombo) {
      issues.push({
        level: "invalid",
        code: "combo-too-big",
        msg: "A combination exceeds three elements.",
        fix: "Use only double (two) or triple (three) combinations for this tooling.",
      });
    }

    hurdles.forEach((h) => {
      if (!parseObstacleLabel(h.label)) {
        issues.push({
          level: "warn",
          code: `label-${h.id}`,
          msg: `"${h.label}" is not standard FEI-style numbering.`,
          fix: "Use digits with optional suffix a/b/c (e.g., 11, 15a).",
        });
      }
    });

    issues.push(...comboSpacingIssues(groups, canvasH));
    if (pts.length >= 3) {
      issues.push(...sharpTurnIssues(pts));
      issues.push(...wallProximityIssuesxz(pts, arenaW, arenaH));
      issues.push(...approachLandingIssuesxz(pts, !!startSensor, !!finishSensor));
    }

    let overall = "valid";
    if (issues.some((x) => x.level === "invalid")) overall = "invalid";
    else if (issues.some((x) => x.level === "warn")) overall = "risky";

    return {
      empty: false,
      hasCourse: true,
      ...totals,
      groups,
      lengthM,
      speed,
      heightM,
      timeAllowedSec,
      timeLimitSec,
      issues,
      overall,
      pathPts: pts,
    };
  }, [hurdles, startSensor, finishSensor, canvasH, arenaW, arenaH, params.speed, params.height]);

  const toUnityPos = useCallback((cx, cy) => ({
    x: parseFloat(((cx - PADDING) / UNITY_SCALE).toFixed(2)),
    y: 0.0,
    z: parseFloat(((canvasH - PADDING - cy) / UNITY_SCALE).toFixed(2)),
  }), [canvasH]);

  const clampArena = useCallback((x, y) => ({
    x: Math.max(PADDING+5, Math.min(canvasW-PADDING-5, x)),
    y: Math.max(PADDING+5, Math.min(canvasH-PADDING-5, y)),
  }), [canvasW, canvasH]);

  const getSVGPos = useCallback((e) => {
    const r = svgRef.current.getBoundingClientRect();
    return { x: e.clientX - r.left, y: e.clientY - r.top };
  }, []);

  const onCanvasPointerDown = useCallback((e) => {
    if (tool === "select" || tool === "rotate") return;
    e.preventDefault();
    const raw = getSVGPos(e);
    const { x: cx, y: cy } = clampArena(raw.x, raw.y);

    if (tool === "place-start") {
      setStartSensor(prev => ({
        x: cx, y: cy,
        rotation: prev?.rotation ?? 0,
      }));
      setSelectedSensor("start");
      setSelectedId(null);
      return;
    }

    if (tool === "place-finish") {
      setFinishSensor(prev => ({
        x: cx, y: cy,
        rotation: prev?.rotation ?? 0,
      }));
      setSelectedSensor("finish");
      setSelectedId(null);
      return;
    }

    if (tool !== "add") return;

    let label;
    if (comboMode) {
      label = `${comboMode.num}${comboMode.suffix}`;
      const triple = !!comboMode.triple;
      if (comboMode.suffix === "a") setComboMode({ num: comboMode.num, suffix: "b", triple });
      else if (comboMode.suffix === "b") {
        if (triple) setComboMode({ num: comboMode.num, suffix: "c", triple });
        else { setComboMode(null); setNextNum((n) => n + 1); }
      } else {
        setComboMode(null);
        setNextNum((n) => n + 1);
      }
    } else {
      label = `${nextNum}`;
      setNextNum(n => n+1);
    }
    const id = Date.now();
    setHurdles(prev => [...prev, { id, x: cx, y: cy, rotation: 0, label }]);
    setSelectedId(id);
    setSelectedSensor(null);
  }, [tool, getSVGPos, comboMode, nextNum, clampArena]);

  const onHurdlePointerDown = useCallback((e, id) => {
    e.stopPropagation();
    e.preventDefault();
    setSelectedSensor(null);
    setSelectedId(id);
    if (tool !== "select" && tool !== "rotate") return;

    const group = getComboGroupFor(hurdles, id);
    const groupIds = new Set(group.map(h => h.id));
    const start = getSVGPos(e);

    // AbortController guarantees listeners are torn down on pointerup OR pointercancel,
    // even if the user releases outside the window — fixes "sometimes drag stops working".
    const ac = new AbortController();
    const signal = ac.signal;

    if (tool === "select") {
      const origs = new Map(group.map(h => [h.id, { x: h.x, y: h.y }]));
      const onMove = (ev) => {
        const cur = getSVGPos(ev);
        let dx = cur.x - start.x;
        let dy = cur.y - start.y;
        // clamp delta so no group member leaves the arena
        for (const h of group) {
          const o = origs.get(h.id);
          const c = clampArena(o.x + dx, o.y + dy);
          dx = c.x - o.x;
          dy = c.y - o.y;
        }
        setHurdles(prev => prev.map(h => {
          if (!groupIds.has(h.id)) return h;
          const o = origs.get(h.id);
          return { ...h, x: o.x + dx, y: o.y + dy };
        }));
      };
      window.addEventListener("pointermove", onMove, { signal });
      window.addEventListener("pointerup", () => ac.abort(), { signal });
      window.addEventListener("pointercancel", () => ac.abort(), { signal });
      return;
    }

    if (tool === "rotate") {
      // pivot = centroid of the entire combination so all efforts swing as one rigid obstacle
      const pivotX = group.reduce((s, h) => s + h.x, 0) / group.length;
      const pivotY = group.reduce((s, h) => s + h.y, 0) / group.length;
      const startAngle = Math.atan2(start.y - pivotY, start.x - pivotX) * 180 / Math.PI;
      const origs = new Map(group.map(h => [h.id, { x: h.x, y: h.y, rotation: h.rotation }]));
      const onMove = (ev) => {
        const cur = getSVGPos(ev);
        const angle = Math.atan2(cur.y - pivotY, cur.x - pivotX) * 180 / Math.PI;
        const deltaDeg = angle - startAngle;
        const rad = deltaDeg * Math.PI / 180;
        const cos = Math.cos(rad), sin = Math.sin(rad);
        setHurdles(prev => prev.map(h => {
          const o = origs.get(h.id);
          if (!o) return h;
          const rx = o.x - pivotX;
          const ry = o.y - pivotY;
          const nx = rx * cos - ry * sin + pivotX;
          const ny = rx * sin + ry * cos + pivotY;
          const c = clampArena(nx, ny);
          return {
            ...h,
            x: c.x,
            y: c.y,
            rotation: (o.rotation + deltaDeg + 360) % 360,
          };
        }));
      };
      window.addEventListener("pointermove", onMove, { signal });
      window.addEventListener("pointerup", () => ac.abort(), { signal });
      window.addEventListener("pointercancel", () => ac.abort(), { signal });
      return;
    }
  }, [tool, getSVGPos, hurdles, clampArena]);

  const onSensorPointerDown = useCallback((e, sensorKind) => {
    e.stopPropagation();
    e.preventDefault();
    setSelectedId(null);
    setSelectedSensor(sensorKind);
    if (tool !== "select" && tool !== "rotate") return;

    const pt = sensorKind === "start" ? startSensor : finishSensor;
    if (!pt) return;
    const setFn = sensorKind === "start" ? setStartSensor : setFinishSensor;
    const start = getSVGPos(e);
    const ac = new AbortController();
    const signal = ac.signal;

    if (tool === "select") {
      const orig = { x: pt.x, y: pt.y };
      const onMove = (ev) => {
        const cur = getSVGPos(ev);
        const c = clampArena(orig.x + cur.x - start.x, orig.y + cur.y - start.y);
        setFn(prev => ({ ...prev, x: c.x, y: c.y }));
      };
      window.addEventListener("pointermove", onMove, { signal });
      window.addEventListener("pointerup", () => ac.abort(), { signal });
      window.addEventListener("pointercancel", () => ac.abort(), { signal });
      return;
    }

    if (tool === "rotate") {
      const cx = pt.x, cy = pt.y, origRot = pt.rotation;
      const startAngle = Math.atan2(start.y - cy, start.x - cx) * 180 / Math.PI;
      const onMove = (ev) => {
        const cur = getSVGPos(ev);
        const angle = Math.atan2(cur.y - cy, cur.x - cx) * 180 / Math.PI;
        setFn(prev => ({
          ...prev,
          rotation: (origRot + angle - startAngle + 360) % 360,
        }));
      };
      window.addEventListener("pointermove", onMove, { signal });
      window.addEventListener("pointerup", () => ac.abort(), { signal });
      window.addEventListener("pointercancel", () => ac.abort(), { signal });
      return;
    }
  }, [tool, getSVGPos, startSensor, finishSensor, clampArena]);

  const stepRotate = (deg) => {
    if (selectedId != null) {
      const group = getComboGroupFor(hurdles, selectedId);
      if (group.length <= 1) {
        setHurdles(prev => prev.map(h =>
          h.id === selectedId ? { ...h, rotation: (h.rotation + deg + 360) % 360 } : h
        ));
        return;
      }
      const pivotX = group.reduce((s, h) => s + h.x, 0) / group.length;
      const pivotY = group.reduce((s, h) => s + h.y, 0) / group.length;
      const groupIds = new Set(group.map(h => h.id));
      const rad = deg * Math.PI / 180;
      const cos = Math.cos(rad), sin = Math.sin(rad);
      setHurdles(prev => prev.map(h => {
        if (!groupIds.has(h.id)) return h;
        const rx = h.x - pivotX;
        const ry = h.y - pivotY;
        const nx = rx * cos - ry * sin + pivotX;
        const ny = rx * sin + ry * cos + pivotY;
        const c = clampArena(nx, ny);
        return { ...h, x: c.x, y: c.y, rotation: (h.rotation + deg + 360) % 360 };
      }));
      return;
    }
    if (selectedSensor === "start" && startSensor)
      setStartSensor({ ...startSensor, rotation: (startSensor.rotation + deg + 360) % 360 });
    else if (selectedSensor === "finish" && finishSensor)
      setFinishSensor({ ...finishSensor, rotation: (finishSensor.rotation + deg + 360) % 360 });
  };

  const deleteSelected = () => {
    if (selectedSensor === "start") { setStartSensor(null); setSelectedSensor(null); return; }
    if (selectedSensor === "finish") { setFinishSensor(null); setSelectedSensor(null); return; }
    setHurdles(prev => prev.filter(h => h.id !== selectedId));
    setSelectedId(null);
  };

  const undo = () => {
    if (!hurdles.length) return;
    const last = hurdles[hurdles.length - 1];
    setHurdles(prev => prev.slice(0, -1));
    const num = parseInt(last.label);
    if (!isNaN(num)) { setNextNum(num); setComboMode(null); }
    if (selectedId === last.id) setSelectedId(null);
  };

  const handleExport = () => {
    const speedExport = typeof params.speed === "string" ? parseFloat(params.speed) : Number(params.speed);
    const heightExport = typeof params.height === "string" ? parseFloat(params.height) : Number(params.height);
    const lengthOut = Number.isFinite(fei.lengthM) ? +fei.lengthM.toFixed(2) : 0;
    const timeAllowedOut = Number.isFinite(fei.timeAllowedSec) ? fei.timeAllowedSec : 0;
    const data = {
      courseInfo: {
        name: params.name,
        arenaSize: `${arenaW}x${arenaH}m`,
        speed_m_per_min: Number.isFinite(speedExport) ? speedExport : 0,
        length_m: lengthOut,
        timeAllowed_sec: timeAllowedOut,
        obstacleHeight_m: Number.isFinite(heightExport) ? heightExport : 0,
        totalObstacles: fei.obstacleCount ?? hurdles.length,
      },
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
    if (startSensor) {
      data.startSensor = {
        unityPosition: toUnityPos(startSensor.x, startSensor.y),
        rotationY: Math.round(startSensor.rotation),
      };
    }
    if (finishSensor) {
      data.finishSensor = {
        unityPosition: toUnityPos(finishSensor.x, finishSensor.y),
        rotationY: Math.round(finishSensor.rotation),
      };
    }
    setExportText(JSON.stringify(data, null, 2));
    setShowExport(true);
  };

  const sel = hurdles.find(h => h.id === selectedId);
  const selSensorPt = selectedSensor === "start" ? startSensor : selectedSensor === "finish" ? finishSensor : null;

  const svgCursor = (tool === "add" || tool === "place-start" || tool === "place-finish") ? "crosshair" : "default";

  const TB = ({ id, icon, label }) => (
    <button onClick={() => {
      if (id !== "add") setComboMode(null);
      setTool(id);
    }} style={{
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

  const Chip = ({ active, label, onClick }) => (
    <button type="button" onClick={onClick} style={{
      padding:"4px 8px", fontSize:"10px", fontFamily:"Georgia, serif",
      border:`1.5px solid ${active ? "#b8860b" : "#d0c9b8"}`,
      background: active ? "#b8860b" : "transparent",
      color: active ? "white" : "#3a3020",
      cursor:"pointer", borderRadius:"2px",
    }}>{label}</button>
  );

  const FeiCard = ({ title, children }) => (
    <div style={{ background:"white", border:"2px solid #1a1a1a", marginBottom:"12px", padding:"12px 14px",
      boxShadow:"4px 4px 0 #c8bfa8", boxSizing:"border-box" }}>
      <div style={{ fontSize:"9px", letterSpacing:"2px", textTransform:"uppercase", marginBottom:"8px",
        color:"#b8860b", fontFamily:"Georgia, serif", fontWeight:"700" }}>{title}</div>
      {children}
    </div>
  );

  const MC = ({ label, value, unit, helper }) => (
    <div style={{ borderBottom:"1px solid #eae4d8", padding:"10px 0" }}>
      <div style={{ fontSize:"8px", letterSpacing:"1.2px", textTransform:"uppercase", color:"#8a7a5a",
        fontFamily:"Georgia, serif" }}>{label}</div>
      <div style={{ fontSize:"21px", fontWeight:"900", color:"#1a1a1a", lineHeight:1.25, fontFamily:"Georgia, serif" }}>
        {value} <span style={{ fontSize:"12px", fontWeight:"600", color:"#8a7a5a" }}>{unit}</span>
      </div>
      {helper && <div style={{ fontSize:"9px", color:"#9a9078", marginTop:"4px", fontStyle:"italic", lineHeight:1.35 }}>{helper}</div>}
    </div>
  );

  const inputStyleSmall = {
    padding:"4px 6px", textAlign:"center", fontFamily:"Georgia, serif", fontSize:"13px",
    fontWeight:"bold", border:"1px solid #d0c9b8", background:"#fdfbf7", borderRadius:"1px", outline:"none",
  };

  const pathSegs = [];
  if (hurdles.length > 0) {
    let prev = hurdles[0];
    let prevPt = startSensor ?? null;
    if (prevPt) {
      pathSegs.push(<PathSeg key="p-start" x1={prevPt.x} y1={prevPt.y} x2={prev.x} y2={prev.y} />);
    }
    for (let i = 1; i < hurdles.length; i++) {
      const h = hurdles[i];
      pathSegs.push(<PathSeg key={`p${h.id}`} x1={prev.x} y1={prev.y} x2={h.x} y2={h.y} />);
      prev = h;
    }
    if (finishSensor) {
      pathSegs.push(<PathSeg key="p-fin" x1={hurdles[hurdles.length - 1].x} y1={hurdles[hurdles.length - 1].y}
        x2={finishSensor.x} y2={finishSensor.y} color="#b8860b" dashed={false} />);
    } else if (hurdles.length > 1) {
      pathSegs.push(<PathSeg key="loop" x1={hurdles[hurdles.length - 1].x} y1={hurdles[hurdles.length - 1].y}
        x2={hurdles[0].x} y2={hurdles[0].y} color="#b8860b" dashed={false} />);
    }
  }

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
          <div>{fei.hasCourse
            ? `${fei.obstacleCount} obstacle number${fei.obstacleCount !== 1 ? "s" : ""} · ${fei.efforts} effort${fei.efforts !== 1 ? "s" : ""} · ${[startSensor && "start", finishSensor && "finish"].filter(Boolean).join(" · ") || "no sensors"}`
            : `No course · ${[startSensor && "start", finishSensor && "finish"].filter(Boolean).join(" · ") || "no sensors"}`
          }</div>
        </div>
      </div>

      {/* Params bar */}
      <div style={{ background:"white", borderBottom:"2px solid #1a1a1a", padding:"8px 20px", display:"flex", flexWrap:"wrap", gap:"18px", alignItems:"flex-end" }}>
        <label style={{ display:"flex", flexDirection:"column", gap:"2px" }}>
          <span style={{ fontSize:"8px", letterSpacing:"1.5px", textTransform:"uppercase", color:"#8a7a5a" }}>Name</span>
          <input type="text" value={params.name} onChange={(e)=>setParams((p)=>({ ...p, name: e.target.value }))}
            style={{ width:"120px", ...inputStyleSmall, textAlign:"left", fontWeight:"600" }} />
        </label>
        <div style={{ display:"flex", flexDirection:"column", gap:"4px" }}>
          <span style={{ fontSize:"8px", letterSpacing:"1.5px", textTransform:"uppercase", color:"#8a7a5a" }}>Speed m/min</span>
          <div style={{ display:"flex", flexWrap:"wrap", gap:"6px", alignItems:"center" }}>
            <Chip active={speedPreset === "indoor"} label="Indoor 350" onClick={() => { setSpeedPreset("indoor"); setParams((p) => ({ ...p, speed: 350 })); }} />
            <Chip active={speedPreset === "out375"} label="Outdoor 375" onClick={() => { setSpeedPreset("out375"); setParams((p) => ({ ...p, speed: 375 })); }} />
            <Chip active={speedPreset === "out400"} label="Outdoor 400" onClick={() => { setSpeedPreset("out400"); setParams((p) => ({ ...p, speed: 400 })); }} />
            <label style={{ display:"flex", alignItems:"center", gap:"4px", fontSize:"10px", color:"#8a7a5a" }}>
              Custom
              <input type="number" min={1} step={1} value={params.speed}
                onChange={(e) => { setSpeedPreset("custom"); setParams((p) => ({ ...p, speed: e.target.value })); }}
                style={{ width:"72px", ...inputStyleSmall }} />
            </label>
          </div>
        </div>
        <label style={{ display:"flex", flexDirection:"column", gap:"2px" }}>
          <span style={{ fontSize:"8px", letterSpacing:"1.5px", textTransform:"uppercase", color:"#8a7a5a" }}>Height m</span>
          <input type="number" value={params.height} step={0.05}
            onChange={(e)=>setParams((p)=>({ ...p, height: e.target.value }))}
            style={{ width:"70px", ...inputStyleSmall }} />
        </label>
        <div style={{ fontSize:"10px", color:"#9a9078", maxWidth:"260px", lineHeight:1.5, fontFamily:"Georgia, serif" }}>
          Course length and timing follow the dashed riding path (start → obstacles → finish, or lap to fence 1 if no finish). See FEI Course Summary →
        </div>
      </div>

      <div style={{ display:"flex" }}>
        {/* Sidebar */}
        <div style={{ width:"190px", flexShrink:0, background:"#fdfbf7", borderRight:"1px solid #d0c9b8", padding:"14px", overflowY:"auto", minHeight:"calc(100vh - 110px)" }}>

          <SL t="Tools" />
          <TB id="add" icon="＋" label="Add Hurdle" />
          <TB id="place-start" icon="◎" label="Place Start" />
          <TB id="place-finish" icon="◉" label="Place Finish" />
          <TB id="select" icon="↖" label="Move" />
          <TB id="rotate" icon="↻" label="Rotate (drag)" />

          <div style={{ height:"14px" }} />
          <SL t="Combination" />
          <button type="button" onClick={(e) => {
            setComboMode({ num: nextNum, suffix: "a", triple: e.shiftKey });
            setTool("add");
          }} style={{
            width:"100%", padding:"8px 12px", marginBottom:"4px",
            fontFamily:"Georgia, serif", fontSize:"12px",
            border:`1.5px solid ${comboMode?"#7c5c00":"#d0c9b8"}`,
            background: comboMode?"#7c5c00":"transparent",
            color: comboMode?"white":"#7c5c00", cursor:"pointer", borderRadius:"2px",
          }}>
            {comboMode
              ? `Placing ${comboMode.num}${comboMode.suffix} (${comboMode.triple ? "triple" : "double"})`
              : "Start Combo (Shift = triple)"}
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

          {selSensorPt && selectedSensor && !sel && (
            <>
              <div style={{ height:"14px" }} />
              <SL t={selectedSensor === "start" ? "Start sensor" : "Finish sensor"} />
              <div style={{ fontSize:"11px", color:"#8a7a5a", marginBottom:"8px" }}>Rotation: <strong>{Math.round(selSensorPt.rotation)}°</strong></div>
              <div style={{ display:"grid", gridTemplateColumns:"1fr 1fr", gap:"4px", marginBottom:"8px" }}>
                {[[-45,"−45°"],[-15,"−15°"],[15,"+15°"],[45,"+45°"]].map(([d,l])=>(
                  <button key={d} onClick={()=>stepRotate(d)} style={{ padding:"6px 2px", fontFamily:"Georgia,serif", fontSize:"11px", border:"1px solid #d0c9b8", background:"white", cursor:"pointer", borderRadius:"2px" }}>{l}</button>
                ))}
              </div>
              <div style={{ fontSize:"10px", color:"#aaa", marginBottom:"10px", lineHeight:"1.7" }}>
                X: {((selSensorPt.x - PADDING)/UNITY_SCALE).toFixed(1)}m &nbsp; Z: {((canvasH - PADDING - selSensorPt.y)/UNITY_SCALE).toFixed(1)}m
              </div>
              <button onClick={deleteSelected} style={{ width:"100%", padding:"7px 12px", fontFamily:"Georgia,serif", fontSize:"12px", border:"1.5px solid #c0392b", background:"transparent", color:"#c0392b", cursor:"pointer", borderRadius:"2px" }}>
                🗑 Remove sensor
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
          <button onClick={()=>{ setHurdles([]); setSelectedId(null); setNextNum(1); setComboMode(null); setStartSensor(null); setFinishSensor(null); setSelectedSensor(null); }}
            style={{ width:"100%", padding:"8px 12px", fontFamily:"Georgia,serif", fontSize:"12px", border:"1px solid #c0392b", background:"transparent", color:"#c0392b", cursor:"pointer", borderRadius:"2px" }}>
            Clear all
          </button>

          {/* Status hint */}
          <div style={{ marginTop:"16px", fontSize:"10px", color:"#aaa", lineHeight:"1.6", fontStyle:"italic" }}>
            {tool==="place-start" ? "Click arena to place or move start (one only)" :
             tool==="place-finish" ? "Click arena to place or move finish (one only)" :
             tool==="add" ? (comboMode ? `Next: ${comboMode.num}${comboMode.suffix} (${comboMode.triple ? "triple" : "double"})` : `Next: ${nextNum}`) :
             tool==="rotate" ? "Drag hurdle or sensor arrow to rotate" : "Drag hurdle or sensor to move"}
          </div>
        </div>

        {/* Canvas */}
        <div style={{ flex:1, padding:"16px", overflow:"auto", background:"#f5f0e8" }}>
          <svg ref={svgRef} width={canvasW} height={canvasH}
            onPointerDown={onCanvasPointerDown}
            style={{ background:"white", border:"2px solid #1a1a1a", cursor: svgCursor, display:"block", boxShadow:"5px 5px 0 #c8bfa8" }}>
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

            {pathSegs}

            {/* Hurdles (before sensors so markers stay easy to hit) */}
            {hurdles.map(h=>(
              <HurdleSymbol key={h.id} hurdle={h} isSelected={h.id===selectedId}
                onPointerDown={(e)=>onHurdlePointerDown(e,h.id)} />
            ))}

            {startSensor && (
              <SensorMarker kind="start" pt={startSensor} isSelected={selectedSensor==="start"}
                onPointerDown={(e)=>onSensorPointerDown(e, "start")} />
            )}
            {finishSensor && (
              <SensorMarker kind="finish" pt={finishSensor} isSelected={selectedSensor==="finish"}
                onPointerDown={(e)=>onSensorPointerDown(e, "finish")} />
            )}
          </svg>
        </div>

        {/* FEI summary rail */}
        <div style={{
          width: "294px",
          flexShrink: 0,
          background: "#fdfbf7",
          borderLeft: "1px solid #d0c9b8",
          padding: "14px",
          overflowY: "auto",
          minHeight: "calc(100vh - 110px)",
          boxSizing: "border-box",
        }}>
          {!fei.hasCourse ? (
            <FeiCard title="FEI Course Summary">
              <div style={{ fontSize: "12px", color: "#8a7a5a", textAlign: "center", padding: "20px 8px", fontStyle: "italic", lineHeight: 1.5 }}>
                Build a course to calculate FEI metrics.
              </div>
            </FeiCard>
          ) : (
            <>
              <FeiCard title="Course Metrics">
                <MC label="Speed" value={fei.speed > 0 ? fei.speed : "—"} unit="m/min" />
                <MC label="Course length (path)" value={fei.lengthM > 0 ? fei.lengthM.toFixed(1) : "0"} unit="m" />
                <MC label="Time allowed" value={fei.timeAllowedSec > 0 ? fei.timeAllowedSec : "—"} unit="s" />
                <MC label="Time limit (2× allowed)" value={fei.timeLimitSec > 0 ? fei.timeLimitSec : "—"} unit="s" />
                <MC label="Obstacles" value={fei.obstacleCount} unit="numbered" helper={`Efforts (${fei.efforts}) = actual jumps. Combinations count as one obstacle number but multiple efforts.`} />
                <MC label="Efforts" value={fei.efforts} unit="jumps"
                  helper="Combinations: one obstacle number, multiple jumping efforts." />
                <div style={{ paddingTop: "8px", fontSize: "9px", color: "#9a9078", lineHeight: 1.35, fontStyle: "italic", borderTop: "1px solid #eae4d8" }}>
                  Time allowed uses (course length ÷ speed) × 60; rounded to seconds. Limit is twice that figure.
                </div>
              </FeiCard>

              <FeiCard title="Course structure">
                <MC label="Single obstacles" value={fei.singles} unit="" />
                <MC label="Double combinations" value={fei.doubles} unit="" />
                <MC label="Triple combinations" value={fei.triples} unit="" />
                <MC label="Total obstacles" value={fei.obstacleCount} unit="numbers" />
                <div style={{ borderBottom: "none", padding: "10px 0 0" }}>
                  <div style={{ fontSize: "8px", letterSpacing: "1.2px", textTransform: "uppercase", color: "#8a7a5a" }}>Total efforts</div>
                  <div style={{ fontSize: "21px", fontWeight: "900", color: "#1a1a1a", fontFamily: "Georgia, serif" }}>{fei.efforts}</div>
                </div>
              </FeiCard>
            </>
          )}

          <FeiCard title="Course validation">
            {!fei.hasCourse ? (
              <div style={{ fontSize: "12px", color: "#8a7a5a", textAlign: "center", padding: "8px", fontStyle: "italic", lineHeight: 1.5 }}>
                Validation runs once hurdles exist along the riding line.
              </div>
            ) : (
              <>
                <div style={{ display: "flex", alignItems: "center", gap: "10px", marginBottom: "12px", flexWrap: "wrap" }}>
                  <span style={{
                    padding: "4px 12px",
                    borderRadius: "2px",
                    fontSize: "11px",
                    fontWeight: "800",
                    fontFamily: "Georgia, serif",
                    letterSpacing: "0.08em",
                    textTransform: "uppercase",
                    color: "#fff",
                    background: fei.overall === "invalid" ? "#c0392b" : fei.overall === "risky" ? "#c79300" : "#2e7d4f",
                  }}>
                    {fei.overall === "invalid" ? "Invalid" : fei.overall === "risky" ? "Risky" : "Valid"}
                  </span>
                  <span style={{ fontSize: "10px", color: "#8a7a5a", lineHeight: 1.4 }}>
                    Risky highlights layout concerns; Invalid blocks exporter-friendly ranges.
                  </span>
                </div>
                {fei.issues.length > 0 ? (
                  <ul style={{ margin: 0, padding: 0, listStyle: "none" }}>
                    {fei.issues.map((it, idx) => (
                      <li key={`${it.code}-${idx}`} style={{
                        marginBottom: "10px",
                        padding: "8px 10px",
                        borderLeft: `3px solid ${it.level === "invalid" ? "#c0392b" : "#e6a800"}`,
                        background: it.level === "invalid" ? "#fdecea" : "#fffbf0",
                        fontSize: "11px",
                        lineHeight: 1.45,
                        color: "#3a3020",
                      }}>
                        <div style={{ fontWeight: "700", marginBottom: "4px" }}>{it.msg}</div>
                        <div style={{ fontSize: "10px", color: "#6a5a40" }}><em>Fix:</em> {it.fix}</div>
                      </li>
                    ))}
                  </ul>
                ) : (
                  <div style={{ fontSize: "11px", color: "#2e7d4f", fontWeight: "600", fontFamily: "Georgia, serif" }}>✓ Baseline checks passed.</div>
                )}
              </>
            )}
          </FeiCard>
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
