// Blocks -> program JSON, and program JSON -> blocks.
// The output must match ProgramSerializer.cs exactly; C# re-validates everything anyway.

class CompileError extends Error {
  constructor(message: string, readonly blockId: string | null) {
    super(message);
  }
}

function findResultBlock(workspace: Blockly.Workspace): Blockly.Block | null {
  return workspace.getBlocksByType(BLOCK.result, false)[0] ?? null;
}

/** Compiles the workspace's result block. Throws CompileError pointing at the block to fix. */
function compileWorkspace(workspace: Blockly.Workspace): AstNode {
  const result = findResultBlock(workspace);
  if (!result) {
    throw new CompileError("The \"this comparison is\" block is missing.", null);
  }

  return compileTimes(result.getInputTargetBlock("TIMES"), result, "Snap a block into \"this comparison is\".");
}

function compileTimes(block: Blockly.Block | null, parent: Blockly.Block, emptyMessage: string): AstNode {
  if (!block) {
    throw new CompileError(emptyMessage, parent.id);
  }

  if (!block.isEnabled()) {
    throw new CompileError("This block is disabled. Enable it or replace it.", block.id);
  }

  switch (block.type) {
    case BLOCK.comparison:
      return { type: "comparison", name: String(block.getFieldValue("NAME") ?? "") };

    case BLOCK.aggregate:
      return {
        type: "aggregate",
        kind: String(block.getFieldValue("KIND")),
        source: compileSamples(block.getInputTargetBlock("SOURCE"), block),
      };

    case BLOCK.addTime:
      return {
        type: "addTime",
        seconds: Number(block.getFieldValue("SECONDS")),
        source: compileTimes(block.getInputTargetBlock("SOURCE"), block, "Snap the times to add to into this block."),
      };

    case BLOCK.scale:
      return {
        type: "scale",
        percent: Number(block.getFieldValue("PERCENT")),
        source: compileTimes(block.getInputTargetBlock("SOURCE"), block, "Snap the times to scale into this block."),
      };

    case BLOCK.fallback:
      return {
        type: "fallback",
        primary: compileTimes(block.getInputTargetBlock("PRIMARY"), block, "Fill the \"use\" slot."),
        fallback: compileTimes(block.getInputTargetBlock("FALLBACK"), block, "Fill the \"if empty, use\" slot."),
      };

    case BLOCK.attempt:
      return { type: "attempt", number: Number(block.getFieldValue("NUMBER")) };

    case BLOCK.bestRecent:
      return { type: "bestRecentRun", days: Number(block.getFieldValue("DAYS")) };

    case BLOCK.weighted:
      return {
        type: "weightedAverage",
        halfLife: Number(block.getFieldValue("HALF_LIFE")),
        source: compileSamples(block.getInputTargetBlock("SOURCE"), block),
      };

    case BLOCK.clamp:
      return {
        type: "clamp",
        source: compileTimes(block.getInputTargetBlock("SOURCE"), block, "Snap the times to keep into this block."),
        low: compileTimes(block.getInputTargetBlock("LOW"), block, "Fill the \"never faster than\" slot."),
        high: compileTimes(block.getInputTargetBlock("HIGH"), block, "Fill the \"never slower than\" slot."),
      };

    case BLOCK.round:
      return {
        type: "round",
        step: Number(block.getFieldValue("STEP")),
        source: compileTimes(block.getInputTargetBlock("SOURCE"), block, "Snap the times to round into this block."),
      };

    case BLOCK.percentile:
      return {
        type: "percentile",
        percent: Number(block.getFieldValue("PERCENT")),
        source: compileSamples(block.getInputTargetBlock("SOURCE"), block),
      };

    case BLOCK.stretch: {
      const goal = parseDuration(String(block.getFieldValue("GOAL")));
      if (goal === null || goal <= 0) {
        throw new CompileError("Type a goal time like 20:00 or 1:05:30.", block.id);
      }

      return {
        type: "stretchToTotal",
        totalSeconds: Math.round(goal * 1000) / 1000,
        source: compileTimes(block.getInputTargetBlock("SOURCE"), block, "Snap the times to stretch into this block."),
      };
    }

    case BLOCK.blend:
      return {
        type: "blend",
        percent: Number(block.getFieldValue("PERCENT")),
        from: compileTimes(block.getInputTargetBlock("FROM"), block, "Fill the \"from\" slot."),
        to: compileTimes(block.getInputTargetBlock("TO"), block, "Fill the \"to\" slot."),
      };

    case BLOCK.pick:
      return {
        type: "pick",
        kind: String(block.getFieldValue("KIND")),
        first: compileTimes(block.getInputTargetBlock("FIRST"), block, "Fill the first slot."),
        second: compileTimes(block.getInputTargetBlock("SECOND"), block, "Fill the \"and\" slot."),
      };

    case BLOCK.range: {
      const from = Number(block.getFieldValue("FROM"));
      const to = Number(block.getFieldValue("TO"));
      if (to < from) {
        throw new CompileError(`Segments ${from} to ${to} is backwards. Swap the numbers.`, block.id);
      }

      return {
        type: "segmentRange",
        cases: [{ from, to, times: compileTimes(block.getInputTargetBlock("TIMES"), block, "Fill the \"use\" slot.") }],
        otherwise: compileTimes(block.getInputTargetBlock("OTHERWISE"), block, "Fill the \"otherwise\" slot."),
      };
    }

    default:
      throw new CompileError("This block can't go here.", block.id);
  }
}

function compileSamples(block: Blockly.Block | null, parent: Blockly.Block): AstNode {
  if (!block) {
    throw new CompileError("Snap a list of times into this block, like \"all my times for each segment\".", parent.id);
  }

  if (!block.isEnabled()) {
    throw new CompileError("This block is disabled. Enable it or replace it.", block.id);
  }

  switch (block.type) {
    case BLOCK.history:
      return { type: "segmentHistory" };
    case BLOCK.lastN:
      return {
        type: "lastN",
        count: Number(block.getFieldValue("COUNT")),
        source: compileSamples(block.getInputTargetBlock("SOURCE"), block),
      };
    case BLOCK.completed:
      return { type: "completedRunsOnly", source: compileSamples(block.getInputTargetBlock("SOURCE"), block) };
    case BLOCK.trim:
      return {
        type: "trimOutliers",
        percent: Number(block.getFieldValue("PERCENT")),
        source: compileSamples(block.getInputTargetBlock("SOURCE"), block),
      };
    case BLOCK.finishedUnder: {
      const limit = parseDuration(String(block.getFieldValue("GOAL")));
      if (limit === null || limit <= 0) {
        throw new CompileError("Type a time like 20:00 or 1:05:30.", block.id);
      }

      return {
        type: "finishedUnder",
        totalSeconds: Math.round(limit * 1000) / 1000,
        source: compileSamples(block.getInputTargetBlock("SOURCE"), block),
      };
    }
    case BLOCK.reached:
      return {
        type: "reachedSegment",
        segment: Number(block.getFieldValue("SEGMENT")),
        source: compileSamples(block.getInputTargetBlock("SOURCE"), block),
      };
    case BLOCK.atLeast:
      return {
        type: "atLeast",
        count: Number(block.getFieldValue("COUNT")),
        source: compileSamples(block.getInputTargetBlock("SOURCE"), block),
      };
    case BLOCK.recentDays:
      return {
        type: "recentDays",
        days: Number(block.getFieldValue("DAYS")),
        source: compileSamples(block.getInputTargetBlock("SOURCE"), block),
      };
    default:
      throw new CompileError("This block can't go here.", block.id);
  }
}

// ---------------------------------------------------------------- AST -> blocks

type BlockState = {
  type: string;
  id?: string;
  x?: number;
  y?: number;
  deletable?: boolean;
  fields?: Record<string, unknown>;
  inputs?: Record<string, { block: BlockState }>;
};

/** Builds Blockly's workspace JSON for a program that has no saved block layout (e.g. hand-written). */
function workspaceFromAst(root: AstNode): object {
  const result: BlockState = {
    type: BLOCK.result,
    x: 40,
    y: 40,
    deletable: false,
    inputs: { TIMES: { block: timesToBlock(root) } },
  };
  return { blocks: { languageVersion: 0, blocks: [result] } };
}

function emptyWorkspaceState(): object {
  return { blocks: { languageVersion: 0, blocks: [{ type: BLOCK.result, x: 40, y: 40, deletable: false }] } };
}

function timesToBlock(node: AstNode): BlockState {
  switch (node.type) {
    case "comparison":
      comparisonChoices.extra.add(String(node.name));
      return { type: BLOCK.comparison, fields: { NAME: String(node.name) } };
    case "aggregate":
      return { type: BLOCK.aggregate, fields: { KIND: node.kind }, inputs: { SOURCE: { block: samplesToBlock(node.source as AstNode) } } };
    case "addTime":
      return { type: BLOCK.addTime, fields: { SECONDS: node.seconds }, inputs: { SOURCE: { block: timesToBlock(node.source as AstNode) } } };
    case "scale":
      return { type: BLOCK.scale, fields: { PERCENT: node.percent }, inputs: { SOURCE: { block: timesToBlock(node.source as AstNode) } } };
    case "fallback":
      return {
        type: BLOCK.fallback,
        inputs: {
          PRIMARY: { block: timesToBlock(node.primary as AstNode) },
          FALLBACK: { block: timesToBlock(node.fallback as AstNode) },
        },
      };
    case "attempt":
      return { type: BLOCK.attempt, fields: { NUMBER: node.number } };
    case "bestRecentRun":
      return { type: BLOCK.bestRecent, fields: { DAYS: node.days } };
    case "weightedAverage":
      return { type: BLOCK.weighted, fields: { HALF_LIFE: node.halfLife }, inputs: { SOURCE: { block: samplesToBlock(node.source as AstNode) } } };
    case "clamp":
      return {
        type: BLOCK.clamp,
        inputs: {
          SOURCE: { block: timesToBlock(node.source as AstNode) },
          LOW: { block: timesToBlock(node.low as AstNode) },
          HIGH: { block: timesToBlock(node.high as AstNode) },
        },
      };
    case "round":
      return { type: BLOCK.round, fields: { STEP: String(node.step) }, inputs: { SOURCE: { block: timesToBlock(node.source as AstNode) } } };
    case "percentile":
      return { type: BLOCK.percentile, fields: { PERCENT: node.percent }, inputs: { SOURCE: { block: samplesToBlock(node.source as AstNode) } } };
    case "stretchToTotal":
      return { type: BLOCK.stretch, fields: { GOAL: formatDuration(Number(node.totalSeconds)) }, inputs: { SOURCE: { block: timesToBlock(node.source as AstNode) } } };
    case "blend":
      return {
        type: BLOCK.blend,
        fields: { PERCENT: node.percent },
        inputs: { FROM: { block: timesToBlock(node.from as AstNode) }, TO: { block: timesToBlock(node.to as AstNode) } },
      };
    case "pick":
      return {
        type: BLOCK.pick,
        fields: { KIND: node.kind },
        inputs: { FIRST: { block: timesToBlock(node.first as AstNode) }, SECOND: { block: timesToBlock(node.second as AstNode) } },
      };
    case "segmentRange": {
      // Several ranges become nested range blocks; "first range that matches wins" is preserved.
      const cases = node.cases as { from: number; to: number; times: AstNode }[];
      for (const c of cases) {
        extraSegmentNumbers.add(c.from);
        extraSegmentNumbers.add(c.to);
      }
      let block: BlockState = timesToBlock(node.otherwise as AstNode);
      for (let i = cases.length - 1; i >= 0; i--) {
        block = {
          type: BLOCK.range,
          fields: { FROM: String(cases[i].from), TO: String(cases[i].to) },
          inputs: { TIMES: { block: timesToBlock(cases[i].times) }, OTHERWISE: { block } },
        };
      }

      return block;
    }
    default:
      throw new Error(`Unknown block "${node.type}".`);
  }
}

function samplesToBlock(node: AstNode): BlockState {
  switch (node.type) {
    case "segmentHistory":
      return { type: BLOCK.history };
    case "lastN":
      return { type: BLOCK.lastN, fields: { COUNT: node.count }, inputs: { SOURCE: { block: samplesToBlock(node.source as AstNode) } } };
    case "completedRunsOnly":
      return { type: BLOCK.completed, inputs: { SOURCE: { block: samplesToBlock(node.source as AstNode) } } };
    case "trimOutliers":
      return { type: BLOCK.trim, fields: { PERCENT: node.percent }, inputs: { SOURCE: { block: samplesToBlock(node.source as AstNode) } } };
    case "finishedUnder":
      return { type: BLOCK.finishedUnder, fields: { GOAL: formatDuration(Number(node.totalSeconds)) }, inputs: { SOURCE: { block: samplesToBlock(node.source as AstNode) } } };
    case "reachedSegment":
      extraSegmentNumbers.add(Number(node.segment));
      return { type: BLOCK.reached, fields: { SEGMENT: String(node.segment) }, inputs: { SOURCE: { block: samplesToBlock(node.source as AstNode) } } };
    case "atLeast":
      return { type: BLOCK.atLeast, fields: { COUNT: node.count }, inputs: { SOURCE: { block: samplesToBlock(node.source as AstNode) } } };
    case "recentDays":
      return { type: BLOCK.recentDays, fields: { DAYS: node.days }, inputs: { SOURCE: { block: samplesToBlock(node.source as AstNode) } } };
    default:
      throw new Error(`Unknown block "${node.type}".`);
  }
}

/** Collects every comparison name a saved workspace uses, so the dropdown accepts them on load. */
function rememberComparisonNames(state: unknown): void {
  const walk = (value: unknown): void => {
    if (Array.isArray(value)) {
      value.forEach(walk);
    } else if (value && typeof value === "object") {
      const obj = value as Record<string, unknown>;
      if (obj.type === BLOCK.comparison && obj.fields && typeof (obj.fields as Record<string, unknown>).NAME === "string") {
        comparisonChoices.extra.add((obj.fields as Record<string, string>).NAME);
      }

      if ((obj.type === BLOCK.range || obj.type === BLOCK.reached) && obj.fields) {
        const fields = obj.fields as Record<string, unknown>;
        for (const key of ["FROM", "TO", "SEGMENT"]) {
          if (!(key in fields)) continue;
          const n = Number(fields[key]);
          if (Number.isInteger(n) && n >= 1) extraSegmentNumbers.add(n);
        }
      }

      Object.values(obj).forEach(walk);
    }
  };
  walk(state);
}
