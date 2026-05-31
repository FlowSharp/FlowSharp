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
        state.edges.appendChild(state.tempPath);
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
    const edges = canvas.querySelector(".nwf-edges");

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
        dbl: (e) => onDoubleClick(state, e)
    };

    canvas.addEventListener("pointerdown", state.handlers.down);
    window.addEventListener("pointermove", state.handlers.move);
    window.addEventListener("pointerup", state.handlers.up);
    canvas.addEventListener("wheel", state.handlers.wheel, { passive: false });
    canvas.addEventListener("dblclick", state.handlers.dbl);

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
    instances.delete(canvasId);
}
