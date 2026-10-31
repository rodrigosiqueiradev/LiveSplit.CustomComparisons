// Block definitions, toolbox and theme.
//
// Two connection types keep programs valid by construction:
//   "Samples" - per segment, a list of past times (sources and filters)
//   "Times"   - per segment, one time (aggregates, comparisons, math, control)
// They match SamplesNode / TimesNode in the C# engine.

const COLOURS = {
  source: "#0f766e",
  filter: "#1d4ed8",
  aggregate: "#b45309",
  math: "#7e22ce",
  control: "#15803d",
  result: "#334155",
};

const BLOCK = {
  result: "cc_result",
  history: "cc_segment_history",
  comparison: "cc_comparison",
  lastN: "cc_last_n",
  completed: "cc_completed_only",
  aggregate: "cc_aggregate",
  addTime: "cc_add_time",
  scale: "cc_scale",
  range: "cc_segment_range",
  fallback: "cc_fallback",
  recentDays: "cc_recent_days",
  percentile: "cc_percentile",
  stretch: "cc_stretch",
  blend: "cc_blend",
  pick: "cc_pick",
  trim: "cc_trim",
  finishedUnder: "cc_finished_under",
  atLeast: "cc_at_least",
  attempt: "cc_attempt",
};

/** Names offered by the comparison dropdown. main.ts keeps this current. */
const comparisonChoices = {
  /** PB, built-ins and the run's own comparisons, from LiveSplit. */
  fromHost: [] as string[],
  /** The editor's other programs. */
  fromEditor: [] as string[],
  /** Names used by loaded programs that aren't available, so they still load and show as missing. */
  extra: new Set<string>(),
};

function comparisonMenu(this: unknown): [string, string][] {
  const available = new Set([...comparisonChoices.fromHost, ...comparisonChoices.fromEditor]);
  const options: [string, string][] = [...available].map((name) => [name, name]);
  for (const name of comparisonChoices.extra) {
    if (!available.has(name)) {
      options.push([`${name} (not found)`, name]);
    }
  }

  if (options.length === 0) {
    options.push(["Personal Best", "Personal Best"]);
  }

  return options;
}

/**
 * Zelos (Scratch-like) blocks, with one shape per connection type, so the shape of an
 * empty slot shows what fits in it: hexagons for lists of times, round for one time per segment.
 */
function registerRenderer(): void {
  class Constants extends Blockly.zelos.ConstantProvider {
    override shapeFor(connection: import("blockly/core").RenderedConnection) {
      const check = connection.getCheck();
      if (check?.includes("Samples")) return this.HEXAGONAL!;
      if (check?.includes("Times")) return this.ROUNDED!;
      return super.shapeFor(connection);
    }
  }

  class Renderer extends Blockly.zelos.Renderer {
    protected override makeConstants_() {
      return new Constants();
    }
  }

  Blockly.blockRendering.register("cc", Renderer);
}

/** Segment numbers used by loaded programs, so they load even if the splits are shorter. */
const extraSegmentNumbers = new Set<number>();

/** Dropdown options "1. Intro", "2. Forest"…; the stored value is the segment number. */
function segmentMenu(): [string, string][] {
  const options: [string, string][] = segmentNames.length > 0
    ? segmentNames.map((name, i) => [`${i + 1}. ${name}`, String(i + 1)])
    : Array.from({ length: 20 }, (_, i) => [String(i + 1), String(i + 1)]);
  const known = new Set(options.map((o) => o[1]));
  for (const n of [...extraSegmentNumbers].sort((a, b) => a - b)) {
    if (!known.has(String(n))) options.push([`${n}. (not in your splits)`, String(n)]);
  }

  return options;
}

/** A segment picker for the range block, shown by name. */
function registerSegmentField(): void {
  class FieldSegment extends Blockly.FieldDropdown {
    constructor(value?: string) {
      super(segmentMenu);
      if (value !== undefined) {
        // Fall back to the last segment if the default is past the end of short splits.
        const values = segmentMenu().map((o) => o[1]);
        this.setValue(values.includes(value) ? value : values[values.length - 1]);
      }
    }

    static override fromJson(options: Parameters<typeof Blockly.FieldDropdown.fromJson>[0]): FieldSegment {
      return new FieldSegment((options as { value?: string }).value);
    }
  }

  Blockly.fieldRegistry.register("field_segment", FieldSegment as unknown as import("blockly/core").fieldRegistry.RegistrableField);
}

/**
 * Snap to where the mouse is, not just to where the block's connector is.
 *
 * Blockly looks for slots near the dragged block's connector (its left edge), so grabbing a
 * wide block by its right end made snapping feel random. This also searches around the mouse
 * pointer and keeps whichever finds the closer slot. Blockly's own type checks still decide
 * what fits, so hexagons still never go into round slots.
 *
 * BlockDragStrategy's search is private in Blockly's typings, so it's patched at runtime. The
 * method names are stable in Blockly 12's build (checked against blockly_compressed.js).
 */
function registerPointerSnapping(): void {
  type Coordinate = import("blockly/core").utils.Coordinate;
  type Candidate = { local: import("blockly/core").RenderedConnection; neighbour: unknown; distance: number } | null;
  type Strategy = {
    workspace: Blockly.WorkspaceSvg;
    startLoc: Coordinate | null;
    ccGrab?: Coordinate | null;
    getLocalConnections(block: Blockly.BlockSvg): import("blockly/core").RenderedConnection[];
    getSearchRadius(): number;
  };

  const proto = Blockly.dragging.BlockDragStrategy.prototype as unknown as Record<string, unknown>;
  const originalStart = proto.startDrag as (this: Strategy, e?: PointerEvent) => void;
  const originalSearch = proto.getConnectionCandidate as (this: Strategy, block: Blockly.BlockSvg, delta: Coordinate) => Candidate;
  if (typeof originalStart !== "function" || typeof originalSearch !== "function") {
    return; // Blockly changed its internals: keep its default snapping rather than break dragging.
  }

  proto.startDrag = function (this: Strategy, e?: PointerEvent) {
    originalStart.call(this, e);
    this.ccGrab = null;
    if (e && this.startLoc) {
      // Where on the block the mouse grabbed it, in workspace units.
      const pointer = Blockly.utils.svgMath.screenToWsCoordinates(this.workspace, new Blockly.utils.Coordinate(e.clientX, e.clientY));
      this.ccGrab = new Blockly.utils.Coordinate(pointer.x - this.startLoc.x, pointer.y - this.startLoc.y);
    }
  };

  proto.getConnectionCandidate = function (this: Strategy, block: Blockly.BlockSvg, delta: Coordinate): Candidate {
    let best = originalSearch.call(this, block, delta);
    if (!this.ccGrab || !this.startLoc) {
      return best;
    }

    // The pointer's position now, in the same units as the slots' positions.
    const pointerX = this.startLoc.x + delta.x + this.ccGrab.x;
    const pointerY = this.startLoc.y + delta.y + this.ccGrab.y;
    let radius = best ? best.distance : this.getSearchRadius();
    for (const local of this.getLocalConnections(block)) {
      const offset = new Blockly.utils.Coordinate(pointerX - local.x, pointerY - local.y);
      const { connection, radius: distance } = local.closest(radius, offset);
      if (connection) {
        best = { local, neighbour: connection, distance };
        radius = distance;
      }
    }

    return best;
  };
}

/** The toolbox flyout keeps one size, so zooming only affects the program being built. */
const FLYOUT_SCALE = 0.75;

function registerFlyout(): void {
  class FixedScaleFlyout extends Blockly.VerticalFlyout {
    override getFlyoutScale(): number {
      return FLYOUT_SCALE;
    }
  }

  Blockly.registry.register(Blockly.registry.Type.FLYOUTS_VERTICAL_TOOLBOX, "cc-fixed-scale", FixedScaleFlyout);
}

function defineBlocks(): void {
  // Goal times are typed like LiveSplit shows them: 20:00, 1:05:30, 59.5. Anything else is refused.
  Blockly.Extensions.register("cc_goal_time", function (this: Blockly.Block) {
    const field = this.getField("GOAL") as import("blockly/core").FieldTextInput;
    field.setValidator((text: string) => {
      const seconds = parseDuration(text);
      return seconds === null || seconds <= 0 || seconds > 360_000 ? null : formatDuration(seconds);
    });
  });

  Blockly.Extensions.register("cc_comparison_dropdown", function (this: Blockly.Block) {
    this.getInput("MAIN")!.appendField(new Blockly.FieldDropdown(comparisonMenu), "NAME");
  });

  Blockly.common.defineBlocksWithJsonArray([
    {
      type: BLOCK.result,
      message0: "this comparison is %1",
      args0: [{ type: "input_value", name: "TIMES", check: "Times" }],
      colour: COLOURS.result,
      tooltip: "The finished comparison. Snap the blocks that build it in here.",
      deletable: false,
      inputsInline: false,
    },
    {
      type: BLOCK.history,
      message0: "all my times for each segment",
      output: "Samples",
      colour: COLOURS.source,
      tooltip: "Every time you've recorded for each segment. Skipped segments, and the combined segment right after a skip, are left out.",
    },
    {
      type: BLOCK.comparison,
      message0: "comparison %1",
      args0: [{ type: "input_dummy", name: "MAIN" }],
      extensions: ["cc_comparison_dropdown"],
      output: "Times",
      colour: COLOURS.source,
      tooltip: "An existing comparison, segment by segment. Built-ins work even if they're switched off in LiveSplit.",
    },
    {
      type: BLOCK.lastN,
      message0: "last %1 times of",
      args0: [{ type: "field_number", name: "COUNT", value: 10, min: 1, max: 10000, precision: 1 }],
      message1: "%1",
      args1: [{ type: "input_value", name: "SOURCE", check: "Samples" }],
      inputsInline: false,
      output: "Samples",
      colour: COLOURS.filter,
      tooltip: "Keeps the most recent times for each segment. Counts times that segment was completed, not attempts.",
    },
    {
      type: BLOCK.completed,
      message0: "from completed runs only",
      message1: "%1",
      args1: [{ type: "input_value", name: "SOURCE", check: "Samples" }],
      inputsInline: false,
      output: "Samples",
      colour: COLOURS.filter,
      tooltip: "Keeps only times from attempts that finished the whole run.",
    },
    {
      type: BLOCK.aggregate,
      message0: "%1 of",
      args0: [
        {
          type: "field_dropdown",
          name: "KIND",
          options: [
            ["median", "median"],
            ["average", "average"],
            ["best (min)", "min"],
            ["worst (max)", "max"],
          ],
        },
      ],
      message1: "%1",
      args1: [{ type: "input_value", name: "SOURCE", check: "Samples" }],
      inputsInline: false,
      output: "Times",
      colour: COLOURS.aggregate,
      tooltip: "Turns each segment's list of times into one time. A segment with no times stays empty.",
    },
    {
      type: BLOCK.addTime,
      message0: "add %1 s per segment to",
      args0: [{ type: "field_number", name: "SECONDS", value: 0.5, min: -3600, max: 3600, precision: 0.001 }],
      message1: "%1",
      args1: [{ type: "input_value", name: "SOURCE", check: "Times" }],
      inputsInline: false,
      output: "Times",
      colour: COLOURS.math,
      tooltip: "Adds (or, if negative, removes) time from every segment. Never goes below zero.",
    },
    {
      type: BLOCK.scale,
      message0: "%1 % of",
      args0: [{ type: "field_number", name: "PERCENT", value: 103, min: 1, max: 1000, precision: 0.1 }],
      message1: "%1",
      args1: [{ type: "input_value", name: "SOURCE", check: "Times" }],
      inputsInline: false,
      output: "Times",
      colour: COLOURS.math,
      tooltip: "Scales every segment. 103 % is 3 % slower, 97 % is 3 % faster.",
    },
    {
      type: BLOCK.range,
      message0: "for segments %1 to %2 use %3 otherwise %4",
      args0: [
        { type: "field_segment", name: "FROM", value: "1" },
        { type: "field_segment", name: "TO", value: "3" },
        { type: "input_value", name: "TIMES", check: "Times" },
        { type: "input_value", name: "OTHERWISE", check: "Times" },
      ],
      inputsInline: false,
      output: "Times",
      colour: COLOURS.control,
      tooltip: "Uses one rule for a range of segments and another for the rest. Put another of these in \"otherwise\" for more ranges.",
    },
    {
      type: BLOCK.fallback,
      message0: "use %1 if empty, use %2",
      args0: [
        { type: "input_value", name: "PRIMARY", check: "Times" },
        { type: "input_value", name: "FALLBACK", check: "Times" },
      ],
      inputsInline: false,
      output: "Times",
      colour: COLOURS.control,
      tooltip: "Fills only the segments the first rule leaves empty, for example because there aren't enough runs yet.",
    },
    {
      type: BLOCK.recentDays,
      message0: "from the last %1 days",
      args0: [{ type: "field_number", name: "DAYS", value: 30, min: 1, max: 36500, precision: 1 }],
      message1: "%1",
      args1: [{ type: "input_value", name: "SOURCE", check: "Samples" }],
      inputsInline: false,
      output: "Samples",
      colour: COLOURS.filter,
      tooltip: "Keeps only times from attempts that ended in the last N days, so old times stop counting. Attempts without a date are left out.",
    },
    {
      type: BLOCK.percentile,
      message0: "percentile %1 of",
      args0: [{ type: "field_number", name: "PERCENT", value: 70, min: 0, max: 100, precision: 1 }],
      message1: "%1",
      args1: [{ type: "input_value", name: "SOURCE", check: "Samples" }],
      inputsInline: false,
      output: "Times",
      colour: COLOURS.aggregate,
      tooltip: "0 is your fastest time, 100 your slowest, 50 the median. 70 means: you beat this about 70% of the time.",
    },
    {
      type: BLOCK.stretch,
      message0: "stretch to finish in %1",
      args0: [{ type: "field_input", name: "GOAL", text: "20:00" }],
      message1: "%1",
      args1: [{ type: "input_value", name: "SOURCE", check: "Times" }],
      extensions: ["cc_goal_time"],
      inputsInline: false,
      output: "Times",
      colour: COLOURS.math,
      tooltip: "Scales every segment by the same amount so the whole run adds up to the goal time. If any segment is empty, the result is empty: fill gaps first with \"if empty, use\".",
    },
    {
      type: BLOCK.blend,
      message0: "%1 % of the way from %2",
      args0: [
        { type: "field_number", name: "PERCENT", value: 75, min: 0, max: 100, precision: 1 },
        { type: "input_value", name: "FROM", check: "Times" },
      ],
      message1: "to %1",
      args1: [{ type: "input_value", name: "TO", check: "Times" }],
      inputsInline: false,
      output: "Times",
      colour: COLOURS.math,
      tooltip: "Mixes two rules per segment. 0 % is all the first, 100 % all the second, 75 % is three quarters of the way.",
    },
    {
      type: BLOCK.pick,
      message0: "the %1 of",
      args0: [{ type: "field_dropdown", name: "KIND", options: [["slower", "slower"], ["faster", "faster"]] }],
      message1: "%1",
      args1: [{ type: "input_value", name: "FIRST", check: "Times" }],
      message2: "and %1",
      args2: [{ type: "input_value", name: "SECOND", check: "Times" }],
      inputsInline: false,
      output: "Times",
      colour: COLOURS.control,
      tooltip: "For each segment, whichever of the two rules is slower (or faster). If one has no time for a segment, the other is used.",
    },
    {
      type: BLOCK.trim,
      message0: "drop the fastest and slowest %1 % of",
      args0: [{ type: "field_number", name: "PERCENT", value: 10, min: 0, max: 45, precision: 1 }],
      message1: "%1",
      args1: [{ type: "input_value", name: "SOURCE", check: "Samples" }],
      inputsInline: false,
      output: "Samples",
      colour: COLOURS.filter,
      tooltip: "Removes flukes before averaging: drops that share of each segment's fastest and slowest times. With few times nothing is dropped.",
    },
    {
      type: BLOCK.finishedUnder,
      message0: "from runs that finished under %1",
      args0: [{ type: "field_input", name: "GOAL", text: "20:00" }],
      message1: "%1",
      args1: [{ type: "input_value", name: "SOURCE", check: "Samples" }],
      extensions: ["cc_goal_time"],
      inputsInline: false,
      output: "Samples",
      colour: COLOURS.filter,
      tooltip: "Keeps only times from runs that finished faster than this, so only your good runs count.",
    },
    {
      type: BLOCK.atLeast,
      message0: "only if there are at least %1 times in",
      args0: [{ type: "field_number", name: "COUNT", value: 5, min: 1, max: 10000, precision: 1 }],
      message1: "%1",
      args1: [{ type: "input_value", name: "SOURCE", check: "Samples" }],
      inputsInline: false,
      output: "Samples",
      colour: COLOURS.filter,
      tooltip: "Segments with fewer times become empty, so you can fill them with \"if empty, use\" instead of trusting too little data.",
    },
    {
      type: BLOCK.attempt,
      message0: "attempt # %1",
      args0: [{ type: "field_number", name: "NUMBER", value: 1, min: 1, precision: 1 }],
      output: "Times",
      colour: COLOURS.source,
      tooltip: "The segments of one past attempt, by its number in LiveSplit's attempt history. Empty where that run skipped a split or reset.",
    },
  ]);
}

/** Shorthand for toolbox presets: a block with blocks already snapped into its inputs. */
function preset(type: string, inputs: Record<string, object> = {}, fields: Record<string, unknown> = {}): object {
  const block: Record<string, unknown> = { type, fields };
  const wrapped: Record<string, object> = {};
  for (const [name, child] of Object.entries(inputs)) {
    wrapped[name] = { block: child };
  }

  block.inputs = wrapped;
  return block;
}

function makeToolbox(): Blockly.utils.toolbox.ToolboxDefinition {
  const history = { type: BLOCK.history };
  const completedHistory = preset(BLOCK.completed, { SOURCE: history });
  const block = (b: object) => ({ kind: "block", ...b });
  const label = (text: string) => ({ kind: "label", text });

  return {
    kind: "categoryToolbox",
    contents: [
      {
        kind: "category",
        name: "Sources",
        colour: COLOURS.source,
        contents: [
          label("⬡  A list of times for each segment"),
          block(history),
          label("⬭  One time for each segment"),
          block({ type: BLOCK.comparison }),
          block({ type: BLOCK.attempt }),
        ],
      },
      {
        kind: "category",
        name: "Filters",
        colour: COLOURS.filter,
        contents: [
          label("⬡  Choose which runs count"),
          block(preset(BLOCK.lastN, { SOURCE: completedHistory })),
          block({ type: BLOCK.lastN }),
          block({ type: BLOCK.completed }),
          block({ type: BLOCK.recentDays }),
          block({ type: BLOCK.finishedUnder }),
          label("⬡  Make a list more reliable"),
          block({ type: BLOCK.trim }),
          block({ type: BLOCK.atLeast }),
        ],
      },
      {
        kind: "category",
        name: "Aggregates",
        colour: COLOURS.aggregate,
        contents: [
          label("⬡ → ⬭  Turn a list of times into one time"),
          block(preset(BLOCK.aggregate, { SOURCE: preset(BLOCK.lastN, { SOURCE: completedHistory }) }, { KIND: "median" })),
          block(preset(BLOCK.aggregate, {}, { KIND: "median" })),
          block(preset(BLOCK.aggregate, {}, { KIND: "average" })),
          block(preset(BLOCK.aggregate, {}, { KIND: "min" })),
          block(preset(BLOCK.aggregate, {}, { KIND: "max" })),
          block(preset(BLOCK.percentile, { SOURCE: preset(BLOCK.lastN, { SOURCE: completedHistory }) }, { PERCENT: 70 })),
          block({ type: BLOCK.percentile }),
        ],
      },
      {
        kind: "category",
        name: "Math",
        colour: COLOURS.math,
        contents: [
          label("⬭  Adjust one time per segment"),
          block({ type: BLOCK.addTime }),
          block({ type: BLOCK.scale }),
          label("⬭  Aim for a goal time"),
          block(preset(BLOCK.stretch, { SOURCE: preset(BLOCK.comparison, {}, { NAME: "Best Segments" }) })),
          block(preset(BLOCK.blend, {
            FROM: preset(BLOCK.comparison, {}, { NAME: "Best Segments" }),
            TO: preset(BLOCK.comparison, {}, { NAME: "Personal Best" }),
          })),
        ],
      },
      {
        kind: "category",
        name: "Control",
        colour: COLOURS.control,
        contents: [
          label("⬭  Combine rules"),
          block(preset(BLOCK.fallback, { FALLBACK: preset(BLOCK.comparison, {}, { NAME: "Personal Best" }) })),
          block(preset(BLOCK.fallback, {
            PRIMARY: preset(BLOCK.aggregate, { SOURCE: preset(BLOCK.atLeast, { SOURCE: preset(BLOCK.lastN, { SOURCE: completedHistory }) }) }, { KIND: "median" }),
            FALLBACK: preset(BLOCK.comparison, {}, { NAME: "Personal Best" }),
          })),
          block({ type: BLOCK.range }),
          block({ type: BLOCK.pick }),
        ],
      },
    ],
  } as Blockly.utils.toolbox.ToolboxDefinition;
}

function makeTheme(): Blockly.Theme {
  return Blockly.Theme.defineTheme("cc-dark", {
    name: "cc-dark",
    base: Blockly.Themes.Classic,
    componentStyles: {
      workspaceBackgroundColour: "#15171c",
      toolboxBackgroundColour: "#1b1e24",
      toolboxForegroundColour: "#e8eaed",
      flyoutBackgroundColour: "#22262d",
      flyoutForegroundColour: "#e8eaed",
      flyoutOpacity: 1,
      scrollbarColour: "#4a505b",
      scrollbarOpacity: 0.6,
      insertionMarkerColour: "#ffffff",
      insertionMarkerOpacity: 0.3,
      cursorColour: "#d0d0d0",
    },
    fontStyle: { family: "'Segoe UI', system-ui, sans-serif", weight: "600", size: 11 },
  });
}
