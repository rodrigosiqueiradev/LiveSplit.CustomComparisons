// Plain-English descriptions of programs, for the summary line and "where did this time
// come from". Segment numbers are shown as names when the splits are known.

/** Segment names from the loaded splits; main.ts fills this on init. */
const segmentNames: string[] = [];

function segmentLabel(n: number): string {
  const name = segmentNames[n - 1];
  return name ? name : `segment ${n}`;
}

function rangeLabel(from: number, to: number): string {
  return from === to ? segmentLabel(from) : `${segmentLabel(from)}–${segmentLabel(to)}`;
}

function seconds(n: number): string {
  return `${Number.isInteger(n) ? n : Number(n.toFixed(3))} s`;
}

/** A list of times, as a noun phrase: "your last 10 times from completed runs". */
function describeSamples(node: AstNode): string {
  let lastN: number | null = null;
  const qualifiers: string[] = [];
  let current: AstNode | undefined = node;

  // Walk the filter chain down to the source, collecting what each filter does.
  while (current && current.type !== "segmentHistory") {
    switch (current.type) {
      case "lastN":
        lastN = lastN ?? Number(current.count);
        break;
      case "completedRunsOnly":
        qualifiers.push("from completed runs");
        break;
      case "recentDays":
        qualifiers.push(`from the last ${current.days} days`);
        break;
      case "finishedUnder":
        qualifiers.push(`from runs under ${formatDuration(Number(current.totalSeconds))}`);
        break;
      case "trimOutliers":
        qualifiers.push(`without the fastest and slowest ${current.percent} %`);
        break;
      case "atLeast":
        qualifiers.push(`when there are at least ${current.count}`);
        break;
      case "reachedSegment":
        qualifiers.push(`from runs that reached ${segmentLabel(Number(current.segment))}`);
        break;
    }

    current = current.source as AstNode | undefined;
  }

  const head = lastN !== null ? `your last ${lastN} times` : "all your times";
  return [head, ...qualifiers].join(" ");
}

/** One time per segment, as a noun phrase: "the median of your last 10 times". */
function describeTimes(node: AstNode): string {
  switch (node.type) {
    case "comparison":
      return node.name === "Best Segments" ? "your golds" : node.name === "Personal Best" ? "your PB" : `"${node.name}"`;
    case "attempt":
      return `attempt #${node.number}`;
    case "bestRecentRun":
      return `your best run from the last ${node.days} days`;
    case "weightedAverage":
      return `the recent-weighted average of ${describeSamples(node.source as AstNode)}`;
    case "clamp":
      return `${describeTimes(node.source as AstNode)}, kept between ${describeTimes(node.low as AstNode)} and ${describeTimes(node.high as AstNode)}`;
    case "round":
      return `${describeTimes(node.source as AstNode)}, rounded to ${seconds(Number(node.step))}`;
    case "aggregate": {
      const kind = ({ median: "the median", average: "the average", min: "the best", max: "the worst" } as Record<string, string>)[String(node.kind)];
      return `${kind} of ${describeSamples(node.source as AstNode)}`;
    }
    case "percentile":
      return `percentile ${node.percent} of ${describeSamples(node.source as AstNode)}`;
    case "addTime": {
      const s = Number(node.seconds);
      return `${describeTimes(node.source as AstNode)} ${s < 0 ? "−" : "+"} ${seconds(Math.abs(s))}`;
    }
    case "scale":
      return `${node.percent} % of ${describeTimes(node.source as AstNode)}`;
    case "stretchToTotal":
      return `${describeTimes(node.source as AstNode)}, stretched to finish in ${formatDuration(Number(node.totalSeconds))}`;
    case "blend":
      return `${node.percent} % of the way from ${describeTimes(node.from as AstNode)} to ${describeTimes(node.to as AstNode)}`;
    case "pick":
      return `the ${node.kind} of ${describeTimes(node.first as AstNode)} and ${describeTimes(node.second as AstNode)}`;
    case "fallback":
      return `${describeTimes(node.primary as AstNode)} (or ${describeTimes(node.fallback as AstNode)} where that's empty)`;
    case "segmentRange": {
      const cases = node.cases as { from: number; to: number; times: AstNode }[];
      const arms = cases.map((c) => `${describeTimes(c.times)} for ${rangeLabel(c.from, c.to)}`);
      return `${arms.join(", ")}, otherwise ${describeTimes(node.otherwise as AstNode)}`;
    }
    default:
      return "something";
  }
}

/** The summary line under the name: whole sentences, top-level structure spelled out. */
function summarize(root: AstNode): string {
  const sentence = (text: string) => text.charAt(0).toUpperCase() + text.slice(1) + ".";

  if (root.type === "fallback") {
    return `${summarize(root.primary as AstNode)} Gaps filled from ${describeTimes(root.fallback as AstNode)}.`;
  }

  if (root.type === "segmentRange") {
    const cases = root.cases as { from: number; to: number; times: AstNode }[];
    const parts = cases.map((c) => sentence(`${rangeLabel(c.from, c.to)}: ${describeTimes(c.times)}`));
    return [...parts, sentence(`Other segments: ${describeTimes(root.otherwise as AstNode)}`)].join(" ");
  }

  return sentence(describeTimes(root));
}

/**
 * Where a segment's time came from, given the path the engine reported
 * (e.g. "primary.cases.0.times.source"). Notes the choices on the way, like a gap
 * filled by a fallback or the side faster/slower picked.
 */
function describeOrigin(root: AstNode, path: string): string {
  let node: unknown = root;
  const notes: string[] = [];
  const steps = path ? path.split(".") : [];

  for (const step of steps) {
    const parent = node as AstNode;
    if (parent.type === "fallback" && step === "fallback") notes.push("filling a gap");
    if (parent.type === "pick") notes.push(`the ${parent.kind} of the two`);
    if (parent.type === "clamp" && step === "low") notes.push("held at the \"never faster than\" limit");
    if (parent.type === "clamp" && step === "high") notes.push("held at the \"never slower than\" limit");
    node = Array.isArray(node) ? (node as unknown[])[Number(step)] : (node as Record<string, unknown>)[step];
    if (node === undefined) return "";
  }

  // "source" steps walk into add / scale / stretch; describe the whole adjusted value instead.
  let target = root;
  let walked: unknown = root;
  for (const step of steps) {
    walked = Array.isArray(walked) ? (walked as unknown[])[Number(step)] : (walked as Record<string, unknown>)[step];
    const candidate = walked as AstNode;
    if (candidate && typeof candidate === "object" && "type" in candidate && step !== "source") {
      target = candidate;
    }
  }

  const text = describeTimes(target);
  const sentence = text.charAt(0).toUpperCase() + text.slice(1);
  return notes.length ? `${sentence} (${notes.join(", ")})` : sentence;
}
