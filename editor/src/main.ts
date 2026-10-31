// The editor page: tabs of programs, the Blockly workspace, live name checks and preview.

interface Tab {
  /** The name this program had when the editor opened; null for programs made here. */
  originalName: string | null;
  name: string;
  /** Blockly workspace JSON. Kept current for inactive tabs; the active tab lives in the workspace. */
  state: object;
  /** Each tab keeps its own undo history across tab switches. */
  undo?: Blockly.Events.Abstract[];
  redo?: Blockly.Events.Abstract[];
}

const NAME_MAX = 64;

const ui = {
  tabs: document.getElementById("tabs") as HTMLDivElement,
  newTab: document.getElementById("new-tab") as HTMLButtonElement,
  name: document.getElementById("name") as HTMLInputElement,
  nameError: document.getElementById("name-error") as HTMLDivElement,
  duplicate: document.getElementById("duplicate") as HTMLButtonElement,
  remove: document.getElementById("delete") as HTMLButtonElement,
  save: document.getElementById("save") as HTMLButtonElement,
  cancel: document.getElementById("cancel") as HTMLButtonElement,
  banner: document.getElementById("banner") as HTMLDivElement,
  blockly: document.getElementById("blockly") as HTMLDivElement,
  empty: document.getElementById("empty") as HTMLDivElement,
  previewRows: document.getElementById("preview-rows") as HTMLDivElement,
  previewStatus: document.getElementById("preview-status") as HTMLDivElement,
  timingButtons: Array.from(document.querySelectorAll<HTMLButtonElement>("[data-timing]")),
  undo: document.getElementById("undo") as HTMLButtonElement,
  redo: document.getElementById("redo") as HTMLButtonElement,
  summary: document.getElementById("summary") as HTMLDivElement,
  templates: document.getElementById("templates") as HTMLDialogElement,
  templateList: document.getElementById("template-list") as HTMLDivElement,
  emptyStart: document.getElementById("empty-start") as HTMLButtonElement,
  compareTo: document.getElementById("compare-to") as HTMLSelectElement,
  totals: document.getElementById("totals") as HTMLDivElement,
};

const host = connectHost();
let init: InitMessage | null = null;
let tabs: Tab[] = [];
let active = -1;
let workspace: Blockly.WorkspaceSvg;
let loading = false;
let dirty = false;
let saving = false;
let timingMethod: TimingMethod = "RealTime";
let previewTimer = 0;
let previewRequestId = 0;
/** The program last sent for preview, to explain warnings about it. */
let previewedRoot: AstNode | null = null;
/** What preview differences are measured against. */
let compareTo = "Personal Best";
/** The PB's final time and the sum of best, once a preview has shown them; used for "Goal pace". */
let pbTotalMs: number | null = null;
let sumOfBestMs: number | null = null;
/** Which preview rows are expanded to show where their time came from. */
const expandedRows = new Set<number>();

// ---------------------------------------------------------------- Startup

function start(): void {
  registerRenderer();
  registerFlyout();
  registerSegmentField();
  registerPointerSnapping();
  defineBlocks();

  // Snapping. Blockly measures from the dragged block's connector (its left edge), not the
  // mouse, and its defaults are tight for nested slots this close together:
  // - snapRadius: how close a connector must get to a slot to start snapping (default 28).
  // - connectingSnapRadius: once a slot is highlighted, how far you can drift before it lets
  //   go, so the preview doesn't flicker on and off near the edge (default 28).
  // - currentConnectionPreference: how much closer another slot must be to steal the
  //   highlight, so it doesn't jump between neighbouring slots (default 8).
  Blockly.config.snapRadius = 48;
  Blockly.config.connectingSnapRadius = 72;
  Blockly.config.currentConnectionPreference = 20;
  workspace = Blockly.inject(ui.blockly, {
    toolbox: makeToolbox(),
    theme: makeTheme(),
    renderer: "cc",
    plugins: { flyoutsVerticalToolbox: "cc-fixed-scale" },
    media: "media/",
    trashcan: true,
    sounds: false,
    grid: { spacing: 24, length: 2, colour: "#262a31", snap: true },
    zoom: { controls: true, wheel: true, startScale: 0.85, maxScale: 2, minScale: 0.4, scaleSpeed: 1.15 },
    move: { scrollbars: true, drag: true, wheel: false },
  });
  workspace.addChangeListener(onWorkspaceChange);

  // Keep the block selector open while working with blocks (like Scratch), starting on
  // the first category. Clicking the selected category again still closes it.
  const toolbox = workspace.getToolbox() as import("blockly/core").Toolbox | null;
  (toolbox?.getFlyout() as import("blockly/core").Flyout | null)?.setAutoClose(false);
  toolbox?.selectItemByPosition(0);

  host.onMessage(onHostMessage);
  ui.newTab.addEventListener("click", openTemplates);
  ui.emptyStart.addEventListener("click", openTemplates);
  ui.undo.addEventListener("click", () => workspace.undo(false));
  ui.redo.addEventListener("click", () => workspace.undo(true));
  ui.compareTo.addEventListener("change", () => {
    compareTo = ui.compareTo.value;
    schedulePreview(0);
  });
  buildTemplateList();
  ui.duplicate.addEventListener("click", duplicateTab);
  ui.remove.addEventListener("click", deleteTab);
  ui.name.addEventListener("input", onNameInput);
  ui.name.addEventListener("change", onNameCommitted);
  ui.save.addEventListener("click", save);
  ui.cancel.addEventListener("click", cancel);
  for (const button of ui.timingButtons) {
    button.addEventListener("click", () => setTimingMethod(button.dataset.timing as TimingMethod));
  }

  document.addEventListener("keydown", (e) => {
    if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === "s") {
      e.preventDefault();
      save();
    }
  });

  host.send({ type: "ready" });
}

function onHostMessage(message: HostMessage): void {
  switch (message.type) {
    case "init":
      init = message;
      segmentNames.splice(0, segmentNames.length, ...message.segments);
      comparisonChoices.fromHost = message.comparisons;
      tabs = message.programs.map((p) => ({ originalName: p.name, name: p.name, state: stateFor(p.doc) }));
      setTimingMethod(message.timingMethod, false);
      renderTabs();
      selectTab(Math.max(0, tabs.findIndex((t) => t.name === message.initialProgram)));
      break;
    case "previewResult":
      if (message.requestId === previewRequestId) {
        renderPreview(message);
      }
      break;
    case "saveResult":
      saving = false;
      updateButtons();
      if (!message.ok) {
        showBanner(message.error ?? "Couldn't save.");
      }
      break;
  }
}

/** A program's saved block layout if it has a usable one, otherwise blocks rebuilt from its AST. */
function stateFor(doc: ProgramDoc): object {
  const saved = doc.workspace as { blocks?: { blocks?: { type?: string }[] } } | undefined;
  if (saved?.blocks?.blocks?.some((b) => b.type === BLOCK.result)) {
    // The AST is what actually runs. Only trust the saved layout if it still compiles to it.
    if (compileState(saved) === JSON.stringify(doc.root)) {
      return saved;
    }
  }

  return workspaceFromAst(doc.root);
}

// ---------------------------------------------------------------- Tabs

function renderTabs(): void {
  ui.tabs.replaceChildren(
    ...tabs.map((tab, index) => {
      const button = document.createElement("button");
      button.type = "button";
      button.className = "tab" + (index === active ? " active" : "") + (nameProblem(tab.name, index) ? " invalid" : "");
      button.textContent = tab.name.trim() || "(unnamed)";
      button.setAttribute("role", "tab");
      button.setAttribute("aria-selected", String(index === active));
      button.addEventListener("click", () => selectTab(index));
      return button;
    }),
  );

  const hasTabs = tabs.length > 0;
  ui.empty.hidden = hasTabs;
  ui.blockly.style.visibility = hasTabs ? "visible" : "hidden";
  ui.name.disabled = !hasTabs;
  ui.duplicate.disabled = !hasTabs;
  ui.remove.disabled = !hasTabs;
  updateButtons();
}

function selectTab(index: number): void {
  storeActiveTab();
  active = tabs.length === 0 ? -1 : Math.min(index, tabs.length - 1);
  if (active >= 0) {
    loadIntoWorkspace(tabs[active].state, tabs[active].undo, tabs[active].redo);
    ui.name.value = tabs[active].name;
  } else {
    loading = true;
    workspace.clear();
    loading = false;
    ui.name.value = "";
  }

  refreshEditorChoices();
  refreshCompareOptions();
  expandedRows.clear();
  showNameError();
  renderTabs();
  schedulePreview(0);
}

function storeActiveTab(): void {
  if (active >= 0 && active < tabs.length) {
    tabs[active].state = Blockly.serialization.workspaces.save(workspace);
    tabs[active].undo = [...workspace.getUndoStack()];
    tabs[active].redo = [...workspace.getRedoStack()];
  }
}

function loadIntoWorkspace(state: object, undo?: Blockly.Events.Abstract[], redo?: Blockly.Events.Abstract[]): void {
  loading = true;
  try {
    rememberComparisonNames(state);
    workspace.clear();
    Blockly.serialization.workspaces.load(state, workspace);
    if (!findResultBlock(workspace)) {
      // Keep whatever loaded, but there must always be a result block to snap into.
      Blockly.serialization.blocks.append({ type: BLOCK.result, x: 40, y: 40, deletable: false }, workspace);
    }

    workspace.clearUndo();
    // Blocks reload with the same ids, so the tab's own history still applies.
    if (undo) workspace.getUndoStack().push(...undo);
    if (redo) workspace.getRedoStack().push(...redo);
    showResultBlock();
  } finally {
    loading = false;
  }
}

/** Scrolls so the program's start, the "this comparison is" block, is near the top left. */
function showResultBlock(): void {
  const block = findResultBlock(workspace) as Blockly.BlockSvg | null;
  if (!block) return;
  const xy = block.getRelativeToSurfaceXY();
  const scale = workspace.scale;
  workspace.scroll(-(xy.x * scale - 32), -(xy.y * scale - 32));
}

function addTab(fromState: object | null, baseName = "New Comparison"): void {
  storeActiveTab();
  tabs.push({ originalName: null, name: freeName(baseName), state: fromState ?? emptyWorkspaceState() });
  markDirty();
  selectTab(tabs.length - 1);
  ui.name.focus();
  ui.name.select();
}

function duplicateTab(): void {
  if (active < 0) {
    return;
  }

  storeActiveTab();
  const copy = JSON.parse(JSON.stringify(tabs[active].state));
  addTab(copy, tabs[active].name + " copy");
}

function deleteTab(): void {
  if (active < 0) {
    return;
  }

  if (!confirm(`Delete "${tabs[active].name}"? You can still cancel the editor to undo this.`)) {
    return;
  }

  tabs.splice(active, 1);
  active = -1; // nothing to store
  markDirty();
  selectTab(Math.max(0, tabs.length - 1));
}

/** Other programs in the editor can be used in comparison blocks. */
function refreshEditorChoices(): void {
  comparisonChoices.fromEditor = tabs.filter((_, i) => i !== active).map((t) => t.name.trim()).filter(Boolean);
}

// ---------------------------------------------------------------- Names

function nameProblem(name: string, index: number): string | null {
  const trimmed = name.trim();
  const same = (a: string) => a.trim().toLowerCase() === trimmed.toLowerCase();

  if (trimmed.length === 0) return "Give the comparison a name.";
  if (trimmed.length > NAME_MAX) return `Keep the name under ${NAME_MAX} characters.`;
  if (trimmed.toLowerCase().startsWith("[race]")) return "Names starting with [Race] are used by LiveSplit's race features.";
  if (init?.reservedNames.some(same)) return `"${trimmed}" is one of LiveSplit's own comparisons.`;
  if (init?.takenNames.some(same)) return `There's already a comparison called "${trimmed}" in your splits.`;
  if (tabs.some((t, i) => i !== index && same(t.name))) return `Another tab is already called "${trimmed}".`;
  return null;
}

function freeName(base: string): string {
  const free = (name: string) => nameProblem(name, -1) === null;
  if (free(base)) return base;
  for (let i = 2; i < 1000; i++) {
    if (free(`${base} (${i})`)) return `${base} (${i})`;
  }

  return base + " " + Date.now();
}

function onNameInput(): void {
  if (active < 0) return;
  tabs[active].name = ui.name.value;
  markDirty();
  showNameError();
  renderTabs();
}

/** When a program is renamed, comparison blocks in other tabs that used the old name follow it. */
let nameBeforeEdit = "";
ui.name.addEventListener("focus", () => (nameBeforeEdit = ui.name.value.trim()));

function onNameCommitted(): void {
  const oldName = nameBeforeEdit;
  const newName = ui.name.value.trim();
  nameBeforeEdit = newName;
  if (active < 0 || !oldName || oldName === newName || nameProblem(newName, active)) {
    return;
  }

  tabs.forEach((tab, i) => {
    if (i !== active) {
      tab.state = renameComparisonUses(tab.state, oldName, newName);
    }
  });
  comparisonChoices.extra.add(newName);
  refreshEditorChoices();
  refreshCompareOptions();
}

function renameComparisonUses(state: object, oldName: string, newName: string): object {
  const walk = (value: unknown): unknown => {
    if (Array.isArray(value)) return value.map(walk);
    if (value && typeof value === "object") {
      const obj = value as Record<string, unknown>;
      const copy: Record<string, unknown> = {};
      for (const [k, v] of Object.entries(obj)) copy[k] = walk(v);
      const fields = copy.fields as Record<string, unknown> | undefined;
      if (copy.type === BLOCK.comparison && fields?.NAME === oldName) {
        copy.fields = { ...fields, NAME: newName };
      }

      return copy;
    }

    return value;
  };
  return walk(state) as object;
}

function showNameError(): void {
  const problem = active >= 0 ? nameProblem(ui.name.value, active) : null;
  ui.nameError.textContent = problem ?? "";
  ui.name.setAttribute("aria-invalid", String(problem !== null));
  updateButtons();
}

// ---------------------------------------------------------------- Changes and preview

function onWorkspaceChange(event: Blockly.Events.Abstract): void {
  if (loading || event.isUiEvent) {
    return;
  }

  markDirty();
  schedulePreview(250);
}

function markDirty(): void {
  hideBanner();
  if (!dirty) {
    dirty = true;
    host.send({ type: "dirty", dirty: true });
  }
}

function schedulePreview(delay: number): void {
  clearTimeout(previewTimer);
  previewTimer = window.setTimeout(requestPreview, delay);
}

function requestPreview(): void {
  const requestId = ++previewRequestId;
  if (active < 0) {
    ui.summary.textContent = "";
    renderPreview({ type: "previewResult", requestId, rows: [], error: null });
    return;
  }

  let root: AstNode;
  try {
    root = compileWorkspace(workspace);
  } catch (e) {
    renderIncomplete(e as Error);
    return;
  }

  ui.summary.textContent = summarize(root);
  ui.summary.classList.remove("unfinished");

  storeActiveTab();
  const others: ProgramDoc[] = [];
  tabs.forEach((tab, i) => {
    if (i === active || nameProblem(tab.name, i)) return;
    const otherRoot = compileStateOrNull(tab.state);
    if (otherRoot) others.push(makeDoc(tab.name, otherRoot, tab.state));
  });

  previewedRoot = root;
  host.send({
    type: "preview",
    requestId,
    timingMethod,
    compareTo,
    program: makeDoc(tabs[active].name.trim() || "Preview", root, tabs[active].state),
    others,
  });
}

function setTimingMethod(method: TimingMethod, refresh = true): void {
  timingMethod = method;
  for (const button of ui.timingButtons) {
    button.setAttribute("aria-pressed", String(button.dataset.timing === method));
  }

  if (refresh) schedulePreview(0);
}

/** Gold slack: preview times are shown to the hundredth, so ignore differences smaller than that. */
const GOLD_TOLERANCE_MS = 5;

function isBelowGold(row: PreviewRow): boolean {
  return row.segmentMs !== null && row.bestSegmentMs !== null && row.segmentMs < row.bestSegmentMs - GOLD_TOLERANCE_MS;
}

function renderPreview(result: PreviewResultMessage): void {
  const reference = describeTimes({ type: "comparison", name: result.compareTo ?? compareTo });
  if ((result.compareTo ?? compareTo) === "Personal Best" && result.rows.length > 0) {
    pbTotalMs = result.rows[result.rows.length - 1].referenceMs;
  }

  if (result.rows.length > 0 && result.rows.every((r) => r.bestSegmentMs !== null)) {
    sumOfBestMs = result.rows.reduce((total, r) => total + (r.bestSegmentMs as number), 0);
  }

  ui.previewRows.replaceChildren(
    ...result.rows.map((row, i) => {
      // Reference segment = this reference split minus the previous one.
      const previousRef = i === 0 ? 0 : result.rows[i - 1].referenceMs;
      const refSegment = row.referenceMs !== null && previousRef !== null ? row.referenceMs - previousRef : null;
      const segmentDelta = diff(row.segmentMs, refSegment);
      const splitDelta = diff(row.splitMs, row.referenceMs);
      const belowGold = isBelowGold(row);
      const origin = previewedRoot && row.segmentMs !== null ? describeOrigin(previewedRoot, row.origin) : "";

      const line = document.createElement("div");
      line.className = "row clickable" + (belowGold ? " below-gold" : "") + (expandedRows.has(i) ? " expanded" : "");
      line.tabIndex = 0;
      line.setAttribute("role", "button");
      line.setAttribute("aria-expanded", String(expandedRows.has(i)));
      const tips = [origin ? `From: ${origin}` : ""];
      if (belowGold) {
        // Just the row colour here; the status line below explains it.
        const gold = formatTime(row.bestSegmentMs);
        const by = formatTime((row.bestSegmentMs as number) - (row.segmentMs as number));
        tips.push(`${by} faster than your best ever ${row.segment} (${gold}).`);
      }

      line.title = tips.filter(Boolean).join("\n");
      line.append(
        cell(row.segment, "name"),
        timeWithDelta(row.segmentMs, segmentDelta, `${reference} for ${row.segment}`),
        timeWithDelta(row.splitMs, splitDelta, `${reference} at this split`),
      );

      // Click a row to see where its time comes from.
      const detail = cell(origin ? `From: ${origin}` : "No time for this segment.", "origin");
      line.append(detail);
      const toggle = () => {
        if (expandedRows.has(i)) expandedRows.delete(i);
        else expandedRows.add(i);
        line.classList.toggle("expanded");
        line.setAttribute("aria-expanded", String(expandedRows.has(i)));
      };
      line.addEventListener("click", toggle);
      line.addEventListener("keydown", (e) => {
        if (e.key === "Enter" || e.key === " ") {
          e.preventDefault();
          toggle();
        }
      });
      return line;
    }),
  );

  renderTotals(result, reference);

  if (result.error) {
    setStatus("error", result.error);
  } else if (active < 0) {
    setStatus("info", "Add a comparison to get started.");
  } else if (result.rows.length === 0) {
    setStatus("info", "Open a splits file in LiveSplit to see a preview.");
  } else if (result.rows.some(isBelowGold)) {
    setStatus("warn", belowGoldMessage(result.rows));
  } else if (result.rows.some((r) => r.segmentMs === null)) {
    setStatus("warn", "Some segments are empty, so the splits after them are too. Wrap your blocks in \"use … if empty, use\" to fill the gaps.");
  } else {
    setStatus("ok", "No problems");
  }
}

/** Says which segments beat their gold and, when it can tell, why. */
function belowGoldMessage(rows: PreviewRow[]): string {
  const names = rows.filter(isBelowGold).map((r) => r.segment);
  const list = names.length <= 3 ? names.join(", ") : `${names.slice(0, 3).join(", ")} and ${names.length - 3} more`;
  const count = names.length === 1 ? "1 segment is" : `${names.length} segments are`;
  let message = `${count} faster than your gold (${list}). `;

  const sumOfBest = rows.every((r) => r.bestSegmentMs !== null)
    ? rows.reduce((total, r) => total + (r.bestSegmentMs as number), 0)
    : null;
  const tooFastGoal = findNodes(previewedRoot, "stretchToTotal")
    .map((n) => Number(n.totalSeconds) * 1000)
    .find((goalMs) => sumOfBest !== null && goalMs < sumOfBest - GOLD_TOLERANCE_MS);

  if (tooFastGoal !== undefined && sumOfBest !== null) {
    message += `Your goal ${formatTime(tooFastGoal)} is faster than your sum of best (${formatTime(sumOfBest)}), so it can't be reached without beating golds.`;
  } else if (findNodes(previewedRoot, "addTime").some((n) => Number(n.seconds) < 0)) {
    message += "A negative \"add … s\" can take segments below gold.";
  } else if (findNodes(previewedRoot, "scale").some((n) => Number(n.percent) < 100)) {
    message += "A \"… % of\" under 100 % can take segments below gold.";
  } else if (findNodes(previewedRoot, "comparison").length > 0) {
    message += "One of the comparisons used has segments faster than your golds.";
  }

  return message.trim();
}

/** Every block of the given type in a program. */
function findNodes(node: unknown, type: string): AstNode[] {
  const found: AstNode[] = [];
  const walk = (value: unknown): void => {
    if (Array.isArray(value)) {
      value.forEach(walk);
    } else if (value && typeof value === "object") {
      const obj = value as AstNode;
      if (obj.type === type) found.push(obj);
      Object.values(obj).forEach(walk);
    }
  };
  walk(node);
  return found;
}

/** "This comparison 12:53.10 · your PB 13:01.90 (−8.80) · Sum of best 12:31.90" */
function renderTotals(result: PreviewResultMessage, reference: string): void {
  const last = result.rows[result.rows.length - 1];
  if (!last || result.error) {
    ui.totals.hidden = true;
    return;
  }

  const sumOfBest = result.rows.every((r) => r.bestSegmentMs !== null)
    ? result.rows.reduce((total, r) => total + (r.bestSegmentMs as number), 0)
    : null;
  const parts: [string, string, string][] = [["This comparison", formatTime(last.splitMs), ""]];
  const delta = diff(last.splitMs, last.referenceMs);
  parts.push([reference.charAt(0).toUpperCase() + reference.slice(1), formatTime(last.referenceMs), delta === null ? "" : formatDelta(delta)]);
  parts.push(["Sum of best", formatTime(sumOfBest), ""]);

  ui.totals.replaceChildren(
    ...parts.map(([label, value, extra]) => {
      const item = cell("", "total");
      item.append(cell(label, "total-label"), cell(value, "total-value"));
      if (extra) item.append(cell(extra, "total-delta " + ((delta ?? 0) <= 0 ? "ahead" : "behind")));
      return item;
    }),
  );
  ui.totals.hidden = false;
}

/** The comparisons the preview can measure against: built-ins, the run's, and other tabs. */
function refreshCompareOptions(): void {
  const names = [
    "Personal Best",
    ...(init?.comparisons ?? []),
    ...tabs.filter((t, i) => i !== active && !nameProblem(t.name, i)).map((t) => t.name.trim()),
  ];
  const unique = [...new Set(names)];
  if (!unique.includes(compareTo)) compareTo = "Personal Best";
  ui.compareTo.replaceChildren(
    ...unique.map((name) => {
      const option = document.createElement("option");
      option.value = name;
      option.textContent = name;
      option.selected = name === compareTo;
      return option;
    }),
  );
}

// ---------------------------------------------------------------- Templates

function buildTemplateList(): void {
  ui.templateList.replaceChildren(
    ...TEMPLATES.map((template) => {
      const button = document.createElement("button");
      button.type = "button";
      button.className = "template";
      button.append(cell(template.title, "template-title"), cell(template.description, "template-description"));
      button.addEventListener("click", () => {
        ui.templates.close();
        const goal = goalDefault();
        const root = template.build(goal);
        addTab(root ? workspaceFromAst(root) : null, template.title === "Blank" ? "New Comparison" : template.title);
      });
      return button;
    }),
  );
}

function openTemplates(): void {
  ui.templates.showModal();
}

/**
 * "Goal pace" starts just under the PB (rounded down to 10 s), but never faster than the
 * sum of best, so a fresh template doesn't open with segments below gold.
 */
function goalDefault(): number {
  if (!pbTotalMs) {
    return 20 * 60;
  }

  let goal = Math.floor(pbTotalMs / 10_000) * 10;
  if (sumOfBestMs !== null) {
    goal = Math.max(goal, Math.ceil(sumOfBestMs / 10_000) * 10);
  }

  return goal;
}

function renderIncomplete(error: Error): void {
  ui.previewRows.replaceChildren();
  ui.totals.hidden = true;
  ui.summary.textContent = "Not finished yet.";
  ui.summary.classList.add("unfinished");
  const blockId = error instanceof CompileError ? error.blockId : null;
  setStatus("warn", error.message, blockId ? () => showBlock(blockId) : undefined);
}

function showBlock(blockId: string): void {
  const block = workspace.getBlockById(blockId);
  if (block) {
    workspace.centerOnBlock(blockId);
    Blockly.common.setSelected(block as Blockly.BlockSvg);
  }
}

function diff(a: number | null, b: number | null): number | null {
  return a !== null && b !== null ? a - b : null;
}

/** A time with its difference from PB right underneath, so it's clear what's compared. */
function timeWithDelta(ms: number | null, delta: number | null, against: string): HTMLSpanElement {
  const wrap = cell("", "time stacked");
  wrap.append(cell(formatTime(ms), "value"));
  const d = cell(formatDelta(delta), "delta " + (delta === null ? "" : delta <= 0 ? "ahead" : "behind"));
  if (delta !== null) {
    const amount = formatTime(Math.abs(delta));
    d.title = Math.round(delta / 10) === 0
      ? `Same as ${against}`
      : `${amount} ${delta < 0 ? "faster" : "slower"} than ${against}`;
  }

  wrap.append(d);
  return wrap;
}

function cell(text: string, className: string): HTMLSpanElement {
  const span = document.createElement("span");
  span.className = className;
  span.textContent = text;
  return span;
}

function setStatus(kind: "ok" | "info" | "warn" | "error", text: string, action?: () => void): void {
  ui.previewStatus.className = "status " + kind;
  const message = document.createElement("span");
  message.textContent = text;
  ui.previewStatus.replaceChildren(message);
  if (action) {
    const button = document.createElement("button");
    button.type = "button";
    button.className = "link";
    button.textContent = "Show me";
    button.addEventListener("click", action);
    ui.previewStatus.append(button);
  }
}

// ---------------------------------------------------------------- Compile helpers

/** Compiles a stored workspace without showing it. Returns the AST as JSON text, or null. */
function compileState(state: object): string | null {
  const root = compileStateOrNull(state);
  return root ? JSON.stringify(root) : null;
}

function compileStateOrNull(state: object): AstNode | null {
  const headless = new Blockly.Workspace();
  try {
    rememberComparisonNames(state);
    Blockly.serialization.workspaces.load(state, headless);
    return compileWorkspace(headless);
  } catch {
    return null;
  } finally {
    headless.dispose();
  }
}

function makeDoc(name: string, root: AstNode, state: object): ProgramDoc {
  return { format: FORMAT_ID, version: FORMAT_VERSION, name: name.trim(), root, workspace: state };
}

// ---------------------------------------------------------------- Save and close

function updateButtons(): void {
  const namesOk = tabs.every((t, i) => nameProblem(t.name, i) === null);
  ui.save.disabled = saving || !init || !namesOk;
  ui.save.title = namesOk ? "Save (Ctrl+S)" : "Fix the highlighted names first.";
}

function save(): void {
  if (ui.save.disabled) return;
  storeActiveTab();

  const programs: SaveRequest["programs"] = [];
  for (let i = 0; i < tabs.length; i++) {
    const tab = tabs[i];
    const headless = new Blockly.Workspace();
    try {
      rememberComparisonNames(tab.state);
      Blockly.serialization.workspaces.load(tab.state, headless);
      programs.push({ originalName: tab.originalName, doc: makeDoc(tab.name, compileWorkspace(headless), tab.state) });
    } catch (e) {
      selectTab(i);
      const message = e instanceof Error ? e.message : String(e);
      showBanner(`"${tab.name.trim()}" isn't finished: ${message}`);
      schedulePreview(0);
      return;
    } finally {
      headless.dispose();
    }
  }

  saving = true;
  updateButtons();
  host.send({ type: "save", programs });
}

function cancel(): void {
  if (dirty && !confirm("Discard your changes?")) {
    return;
  }

  host.send({ type: "close" });
}

function showBanner(text: string): void {
  ui.banner.textContent = text;
  ui.banner.hidden = false;
}

function hideBanner(): void {
  ui.banner.hidden = true;
}

start();
