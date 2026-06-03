// ============================================================
// Blazor IJSRuntime ile cagrilir; node div'lerini Blazor render eder,
// bu modul konum/transform/kablo cizimini ve etkilesimi yonetir.
// ============================================================

const instances = new Map();

const NS = "http://www.w3.org/2000/svg";
const DRAG_THRESHOLD = 4;

function getWorldPoint(state, clientX, clientY) {
    const rect = state.canvas.getBoundingClientRect();
    return {
        x: (clientX - rect.left - state.tx) / state.scale,
        y: (clientY - rect.top - state.ty) / state.scale
    };
}

function applyTransform(state) {
    state.world.style.transform = `translate(${state.tx}px, ${state.ty}px) scale(${state.scale})`;
}

function styleEdge(path, conn) {
    path.style.fill = "none";
    path.style.stroke = conn.ai ? "#a78bfa" : conn.active ? "#56d8ff" : "rgba(148,163,184,.78)";
    path.style.strokeWidth = conn.active ? "3.5px" : "2.5px";
    path.style.strokeLinecap = "round";
    path.style.strokeLinejoin = "round";
    path.style.pointerEvents = "stroke";
    path.style.cursor = "pointer";
    path.style.filter = conn.active ? "drop-shadow(0 0 10px rgba(86,216,255,.45))" : "";
    if (conn.ai || conn.active) {
        path.style.strokeDasharray = conn.ai ? "7 6" : "10 7";
    }
}

function styleTempEdge(path) {
    path.style.fill = "none";
    path.style.stroke = "#ff6b5f";
    path.style.strokeWidth = "3";
    path.style.strokeLinecap = "round";
    path.style.strokeDasharray = "7 5";
    path.style.filter = "drop-shadow(0 0 10px rgba(255,107,95,.45))";
}

function setPortState(el, stateName) {
    if (stateName === "compatible") {
        el.style.opacity = "1";
        el.style.borderColor = "#34d399";
        el.style.background = "#34d399";
        el.style.boxShadow = "0 0 0 6px rgba(52,211,153,.22)";
        return;
    }
    if (stateName === "incompatible") {
        el.style.opacity = ".32";
        el.style.pointerEvents = "none";
        return;
    }
    el.style.opacity = "";
    el.style.pointerEvents = "";
    const ai = el.dataset.portType === "sub";
    const out = el.classList.contains("out");
    const color = ai ? "#a78bfa" : out ? "#56d8ff" : "#ff6b5f";
    el.style.borderColor = color;
    el.style.background = "#101827";
    el.style.boxShadow = `0 0 0 3px ${ai ? "rgba(167,139,250,.14)" : "rgba(86,216,255,.12)"}`;
}

function alignVisible(state, force = false) {
    if (!force && state.didInitialAlign) return;
    if (state.nodes.size === 0) return;

    let minX = Infinity, minY = Infinity;
    for (const n of state.nodes.values()) {
        minX = Math.min(minX, n.x);
        minY = Math.min(minY, n.y);
    }

    const pad = 80;
    let changed = false;

    if (minX + state.tx < pad) {
        state.tx = pad - minX;
        changed = true;
    }

    if (minY + state.ty < pad) {
        state.ty = pad - minY;
        changed = true;
    }

    if (changed) applyTransform(state);
    state.didInitialAlign = true;
}

function portCenter(state, nodeId, kind, index) {
    const el = document.getElementById(`port-${nodeId}-${kind}-${index}`);
    const node = state.nodes.get(nodeId);
    if (!el || !node) return null;
    return {
        x: node.x + el.offsetLeft + el.offsetWidth / 2,
        y: node.y + el.offsetTop + el.offsetHeight / 2
    };
}

function bezier(a, b) {
    const dx = Math.max(40, Math.abs(b.x - a.x) * 0.5);
    return `M ${a.x},${a.y} C ${a.x + dx},${a.y} ${b.x - dx},${b.y} ${b.x},${b.y}`;
}

function redrawEdges(state) {
    const svg = state.edges;
    while (svg.firstChild) svg.removeChild(svg.firstChild);

    for (const conn of state.connections) {
        const from = portCenter(state, conn.fromId, "out", conn.fromPort);
        const to = portCenter(state, conn.toId, "in", conn.toPort);
        if (!from || !to) continue;

        const path = document.createElementNS(NS, "path");
        path.setAttribute("class", "edge" + (conn.active ? " active" : "") + (conn.ai ? " ai" : ""));
        path.setAttribute("d", bezier(from, to));
        styleEdge(path, conn);
        path.addEventListener("pointerdown", (e) => {
            e.stopPropagation();
            if (state.readOnly) return;
            state.dotnet.invokeMethodAsync("OnEdgeClick", conn.fromId, conn.fromPort, conn.toId, conn.toPort);
        });
        svg.appendChild(path);
    }
}

function moveNodeDom(state, node) {
    const el = document.getElementById(`node-${node.id}`);
    if (el) { el.style.left = node.x + "px"; el.style.top = node.y + "px"; }
}

// ---------- Pointer handling ----------
function onPointerDown(state, e) {
    const portEl = e.target.closest(".nwf-port");
    const headEl = e.target.closest(".nwf-node-head") || e.target.closest(".nwf-sticky-head");
    const nodeEl = e.target.closest(".nwf-node");
    const resizeEl = e.target.closest(".nwf-sticky-resize-handle");

    if (state.readOnly) {
        // In read-only mode, only panning is allowed on canvas
        const canvasClick = !e.target.closest(".nwf-node") && !e.target.closest(".nwf-port") && !e.target.closest(".nwf-controls") && !e.target.closest(".nwf-add-fab");
        if (canvasClick || e.target === state.canvas) {
            state.panning = { startX: e.clientX, startY: e.clientY, tx: state.tx, ty: state.ty, moved: false };
            state.canvas.classList.add("panning");
        }
        return;
    }

    // Middle mouse button click (button === 1) pans from anywhere (even over nodes)
    if (e.button === 1) {
        state.panning = { startX: e.clientX, startY: e.clientY, tx: state.tx, ty: state.ty, moved: false };
        state.canvas.classList.add("panning");
        e.preventDefault();
        return;
    }

    // Shift + drag on empty space triggers selection area
    const canvasClick = !e.target.closest(".nwf-node") && !e.target.closest(".nwf-port") && !e.target.closest(".nwf-controls") && !e.target.closest(".nwf-add-fab") && !e.target.closest(".nwf-chat-fab") && !e.target.closest(".nwf-chat");
    if (canvasClick && e.shiftKey) {
        const pt = getWorldPoint(state, e.clientX, e.clientY);
        state.selecting = {
            startX: e.clientX,
            startY: e.clientY,
            x1: pt.x,
            y1: pt.y,
            current: null
        };
        state.selectionBox = document.createElement("div");
        state.selectionBox.className = "nwf-selection-box";
        state.selectionBox.style.position = "absolute";
        state.selectionBox.style.zIndex = "1000";
        state.selectionBox.style.border = "1.5px dashed #56d8ff";
        state.selectionBox.style.borderRadius = "6px";
        state.selectionBox.style.background = "rgba(86,216,255,.1)";
        state.selectionBox.style.pointerEvents = "none";
        state.world.appendChild(state.selectionBox);
        e.preventDefault();
        return;
    }

    if (resizeEl && nodeEl) {
        const nodeId = nodeEl.id.replace("node-", "");
        state.resizing = {
            id: nodeId,
            startX: e.clientX,
            startY: e.clientY,
            startWidth: nodeEl.offsetWidth,
            startHeight: nodeEl.offsetHeight,
            currentWidth: nodeEl.offsetWidth,
            currentHeight: nodeEl.offsetHeight
        };
        e.preventDefault();
        return;
    }

    if (portEl && portEl.classList.contains("out")) {
        // Baglanti cizimi baslat
        const nodeId = portEl.dataset.node;
        const port = parseInt(portEl.dataset.port, 10);
        state.connecting = { fromId: nodeId, fromPort: port };
        state.tempPath = document.createElementNS(NS, "path");
        state.tempPath.setAttribute("class", "temp");
        styleTempEdge(state.tempPath);
        state.edges.appendChild(state.tempPath);

        // Port uyumluluk vurgulari (ayni tipteki in portlari yesil, uyumsuzlar soluk)
        const pType = portEl.dataset.portType;
        state.canvas.querySelectorAll(".nwf-port.in").forEach(inEl => {
            if (inEl.dataset.portType === pType) {
                inEl.classList.add("nwf-port-compatible");
                setPortState(inEl, "compatible");
            } else {
                inEl.classList.add("nwf-port-incompatible");
                setPortState(inEl, "incompatible");
            }
        });

        e.preventDefault();
        return;
    }

    if (headEl && nodeEl) {
        const nodeId = nodeEl.id.replace("node-", "");
        const node = state.nodes.get(nodeId);
        if (!node) return;
        state.dragging = {
            id: nodeId, node, startX: e.clientX, startY: e.clientY,
            origX: node.x, origY: node.y, moved: false
        };
        headEl.classList.add("dragging");
        e.preventDefault();
        return;
    }

    // Bos alan -> pan
    state.panning = { startX: e.clientX, startY: e.clientY, tx: state.tx, ty: state.ty, moved: false };
    state.canvas.classList.add("panning");
}

function onPointerMove(state, e) {
    if (state.selecting) {
        const s = state.selecting;
        const pt = getWorldPoint(state, e.clientX, e.clientY);
        const x = Math.min(s.x1, pt.x);
        const y = Math.min(s.y1, pt.y);
        const w = Math.abs(s.x1 - pt.x);
        const h = Math.abs(s.y1 - pt.y);

        if (state.selectionBox) {
            state.selectionBox.style.left = x + "px";
            state.selectionBox.style.top = y + "px";
            state.selectionBox.style.width = w + "px";
            state.selectionBox.style.height = h + "px";
        }
        s.current = { x, y, w, h };
        return;
    }

    if (state.resizing) {
        const r = state.resizing;
        const dx = (e.clientX - r.startX) / state.scale;
        const dy = (e.clientY - r.startY) / state.scale;
        r.currentWidth = Math.max(150, Math.round((r.startWidth + dx) / 10) * 10);
        r.currentHeight = Math.max(80, Math.round((r.startHeight + dy) / 10) * 10);

        const el = document.getElementById(`node-${r.id}`);
        if (el) {
            el.style.width = r.currentWidth + "px";
            el.style.height = r.currentHeight + "px";
        }
        return;
    }

    if (state.connecting) {
        const from = portCenter(state, state.connecting.fromId, "out", state.connecting.fromPort);
        const to = getWorldPoint(state, e.clientX, e.clientY);
        if (from) state.tempPath.setAttribute("d", bezier(from, to));
        return;
    }

    if (state.dragging) {
        const d = state.dragging;
        const dx = (e.clientX - d.startX) / state.scale;
        const dy = (e.clientY - d.startY) / state.scale;
        if (!d.moved && Math.hypot(e.clientX - d.startX, e.clientY - d.startY) > DRAG_THRESHOLD) d.moved = true;
        // 20px grid snapping
        d.node.x = Math.round((d.origX + dx) / 20) * 20;
        d.node.y = Math.round((d.origY + dy) / 20) * 20;
        moveNodeDom(state, d.node);
        redrawEdges(state);
        return;
    }

    if (state.panning) {
        const p = state.panning;
        const dx = e.clientX - p.startX, dy = e.clientY - p.startY;
        if (!p.moved && Math.hypot(dx, dy) > DRAG_THRESHOLD) p.moved = true;
        state.tx = p.tx + dx;
        state.ty = p.ty + dy;
        applyTransform(state);
    }
}

function onPointerUp(state, e) {
    if (state.selecting) {
        const s = state.selecting;
        if (state.selectionBox) {
            state.selectionBox.remove();
            state.selectionBox = null;
        }
        state.selecting = null;
        if (s.current && s.current.w > 30 && s.current.h > 30) {
            state.dotnet.invokeMethodAsync("OnSelectionAreaFinished", s.current.x, s.current.y, s.current.w, s.current.h);
        }
        return;
    }

    if (state.resizing) {
        const r = state.resizing;
        state.dotnet.invokeMethodAsync("OnNodeResized", r.id, r.currentWidth, r.currentHeight);
        state.resizing = null;
        return;
    }

    if (state.connecting) {
        // Port uyumluluk vurgularini temizle
        state.canvas.querySelectorAll(".nwf-port.in").forEach(inEl => {
            inEl.classList.remove("nwf-port-compatible", "nwf-port-incompatible");
            setPortState(inEl, null);
        });

        const portEl = e.target.closest(".nwf-port.in");
        if (portEl) {
            const toId = portEl.dataset.node;
            const toPort = parseInt(portEl.dataset.port, 10);
            if (toId !== state.connecting.fromId) {
                state.dotnet.invokeMethodAsync("OnConnect", state.connecting.fromId, state.connecting.fromPort, toId, toPort);
            }
        }
        if (state.tempPath) state.tempPath.remove();
        state.tempPath = null;
        state.connecting = null;
        return;
    }

    if (state.dragging) {
        const d = state.dragging;
        document.querySelectorAll(".nwf-node-head.dragging, .nwf-sticky-head.dragging").forEach(el => el.classList.remove("dragging"));
        if (d.moved) {
            state.dotnet.invokeMethodAsync("OnNodeMoved", d.id, Math.round(d.node.x), Math.round(d.node.y));
        } else {
            state.dotnet.invokeMethodAsync("OnNodeSelected", d.id);
        }
        state.dragging = null;
        return;
    }

    if (state.panning) {
        const wasClick = !state.panning.moved;
        state.panning = null;
        state.canvas.classList.remove("panning");
        if (wasClick) state.dotnet.invokeMethodAsync("OnCanvasClick");
    }
}

function onWheel(state, e) {
    e.preventDefault();
    const rect = state.canvas.getBoundingClientRect();
    const mx = e.clientX - rect.left, my = e.clientY - rect.top;
    const delta = -e.deltaY * 0.0015;
    const newScale = Math.min(2.5, Math.max(0.25, state.scale * (1 + delta)));
    // imlec etrafinda zoom
    state.tx = mx - (mx - state.tx) * (newScale / state.scale);
    state.ty = my - (my - state.ty) * (newScale / state.scale);
    state.scale = newScale;
    applyTransform(state);
}

function onDoubleClick(state, e) {
    const nodeEl = e.target.closest(".nwf-node");
    if (nodeEl) {
        state.dotnet.invokeMethodAsync("OnNodeOpen", nodeEl.id.replace("node-", ""));
    }
}

// ---------- Public API ----------
export function init(canvasId, dotnet, readOnly = false) {
    const canvas = document.getElementById(canvasId);
    if (!canvas) return;
    const world = canvas.querySelector(".nwf-world");
    let edges = canvas.querySelector(".nwf-edges");
    if (!edges && world) {
        edges = document.createElementNS(NS, "svg");
        edges.setAttribute("class", "nwf-edges");
        edges.style.position = "absolute";
        edges.style.top = "0";
        edges.style.left = "0";
        edges.style.overflow = "visible";
        edges.style.pointerEvents = "none";
        edges.style.zIndex = "2";
        world.prepend(edges);
    }
    if (!world || !edges) return;

    const state = {
        canvas, world, edges, dotnet, readOnly,
        nodes: new Map(), connections: [],
        tx: 40, ty: 40, scale: 1,
        didInitialAlign: false,
        dragging: null, panning: null, connecting: null, tempPath: null, resizing: null
    };

    state.handlers = {
        down: (e) => onPointerDown(state, e),
        move: (e) => onPointerMove(state, e),
        up: (e) => onPointerUp(state, e),
        wheel: (e) => onWheel(state, e),
        dbl: (e) => onDoubleClick(state, e),
        keydown: (e) => {
            if (e.key === "Delete" || e.key === "Backspace") {
                state.dotnet.invokeMethodAsync("OnKeyDown", e.key);
            }
        }
    };

    canvas.addEventListener("pointerdown", state.handlers.down);
    window.addEventListener("pointermove", state.handlers.move);
    window.addEventListener("pointerup", state.handlers.up);
    canvas.addEventListener("wheel", state.handlers.wheel, { passive: false });
    canvas.addEventListener("dblclick", state.handlers.dbl);
    canvas.addEventListener("keydown", state.handlers.keydown);

    applyTransform(state);
    instances.set(canvasId, state);
}

export function sync(canvasId, graphJson) {
    const state = instances.get(canvasId);
    if (!state) return;
    const graph = typeof graphJson === "string" ? JSON.parse(graphJson) : graphJson;
    state.nodes = new Map(graph.nodes.map(n => [n.id, { id: n.id, x: n.x, y: n.y }]));
    state.connections = graph.connections || [];
    alignVisible(state);
    // DOM guncellemesinin tamamlanmasi icin bir sonraki frame'de ciz
    requestAnimationFrame(() => redrawEdges(state));
}

export function zoomIn(canvasId) { zoomBy(canvasId, 1.2); }
export function zoomOut(canvasId) { zoomBy(canvasId, 1 / 1.2); }

function zoomBy(canvasId, factor) {
    const state = instances.get(canvasId);
    if (!state) return;
    const rect = state.canvas.getBoundingClientRect();
    const cx = rect.width / 2, cy = rect.height / 2;
    const newScale = Math.min(2.5, Math.max(0.25, state.scale * factor));
    state.tx = cx - (cx - state.tx) * (newScale / state.scale);
    state.ty = cy - (cy - state.ty) * (newScale / state.scale);
    state.scale = newScale;
    applyTransform(state);
}

export function resetView(canvasId) {
    const state = instances.get(canvasId);
    if (!state) return;
    state.tx = 40; state.ty = 40; state.scale = 1;
    applyTransform(state);
    alignVisible(state, true);
}

export function fitView(canvasId) {
    const state = instances.get(canvasId);
    if (!state || state.nodes.size === 0) return;
    let minX = Infinity, minY = Infinity, maxX = -Infinity, maxY = -Infinity;
    for (const n of state.nodes.values()) {
        minX = Math.min(minX, n.x); minY = Math.min(minY, n.y);
        maxX = Math.max(maxX, n.x + 200); maxY = Math.max(maxY, n.y + 110);
    }
    const rect = state.canvas.getBoundingClientRect();
    const pad = 60;
    const scale = Math.min(1.4, Math.min((rect.width - pad * 2) / (maxX - minX), (rect.height - pad * 2) / (maxY - minY)));
    state.scale = Math.max(0.25, scale);
    state.tx = pad - minX * state.scale;
    state.ty = pad - minY * state.scale;
    applyTransform(state);
}

export function dispose(canvasId) {
    const state = instances.get(canvasId);
    if (!state) return;
    state.canvas.removeEventListener("pointerdown", state.handlers.down);
    window.removeEventListener("pointermove", state.handlers.move);
    window.removeEventListener("pointerup", state.handlers.up);
    state.canvas.removeEventListener("wheel", state.handlers.wheel);
    state.canvas.removeEventListener("dblclick", state.handlers.dbl);
    state.canvas.removeEventListener("keydown", state.handlers.keydown);
    instances.delete(canvasId);
}
