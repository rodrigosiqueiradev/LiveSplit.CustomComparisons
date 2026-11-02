// Talks to the LiveSplit component through WebView2. Opened in a normal browser
// (no WebView2), it falls back to a stand-in with sample splits, so the editor can be
// developed and checked without LiveSplit.

interface Host {
  send(message: EditorMessage): void;
  onMessage(handler: (message: HostMessage) => void): void;
}

function connectHost(): Host {
  const webview = window.chrome?.webview;
  if (webview) {
    return {
      send: (message) => webview.postMessage(message),
      onMessage: (handler) => webview.addEventListener("message", (event) => handler(event.data as HostMessage)),
    };
  }

  return createSampleHost();
}

/** Sample data in the same shape LiveSplit sends. Times are made up. */
function createSampleHost(): Host {
  let handler: (message: HostMessage) => void = () => {};
  const segments = ["Intro", "Forest", "Caves", "Castle", "Tower", "Final Boss"];
  const pb = [63_800, 133_100, 113_500, 187_200, 163_100, 118_200];
  const best = [61_900, 129_400, 108_300, 181_600, 158_000, 112_700];
  const history = segments.map((_, i) => [0.99, 1.0, 1.01, 1.02, 1.04].map((f) => pb[i] * f));

  const samplePrograms: ProgramDoc[] = [
    {
      format: FORMAT_ID,
      version: FORMAT_VERSION,
      name: "Safe Pace",
      root: {
        type: "fallback",
        primary: {
          type: "segmentRange",
          cases: [{ from: 1, to: 3, times: { type: "addTime", seconds: 0.5, source: { type: "comparison", name: "Best Segments" } } }],
          otherwise: { type: "aggregate", kind: "median", source: { type: "lastN", count: 10, source: { type: "completedRunsOnly", source: { type: "segmentHistory" } } } },
        },
        fallback: { type: "comparison", name: "Personal Best" },
      },
    },
    {
      format: FORMAT_ID,
      version: FORMAT_VERSION,
      name: "Median Last 10 +1s",
      root: { type: "addTime", seconds: 1, source: { type: "aggregate", kind: "median", source: { type: "lastN", count: 10, source: { type: "completedRunsOnly", source: { type: "segmentHistory" } } } } },
    },
  ];

  // A tiny evaluator over the sample data, good enough to exercise the preview panel.
  // The last segment is left empty so the preview has a gap to show.
  const sampleLists = history.map((xs, i) => (i === segments.length - 1 ? [] : [...xs]));
  const medianOf = (xs: number[]): number | null => {
    if (xs.length === 0) return null;
    const sorted = [...xs].sort((a, b) => a - b);
    const mid = Math.floor(sorted.length / 2);
    return sorted.length % 2 === 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2;
  };
  const evalSamples = (node: AstNode): number[][] => {
    switch (node.type) {
      case "segmentHistory":
        return sampleLists.map((xs) => [...xs]);
      case "lastN": {
        const n = Number(node.count);
        return evalSamples(node.source as AstNode).map((xs) => xs.slice(Math.max(0, xs.length - n)));
      }
      case "atLeast": {
        const n = Number(node.count);
        return evalSamples(node.source as AstNode).map((xs) => (xs.length >= n ? xs : []));
      }
      case "trimOutliers": {
        const percent = Number(node.percent);
        return evalSamples(node.source as AstNode).map((xs) => {
          const drop = Math.floor((xs.length * percent) / 100);
          if (drop === 0) return xs;
          const order = xs.map((t, i) => ({ t, i })).sort((a, b) => a.t - b.t || a.i - b.i);
          const keep = new Set(order.slice(drop, xs.length - drop).map((x) => x.i));
          return xs.filter((_, i) => keep.has(i));
        });
      }
      case "reachedSegment":
        return Number(node.segment) > segments.length
          ? segments.map(() => [])
          : evalSamples(node.source as AstNode);
      case "completedRunsOnly":
      case "recentDays":
      case "finishedUnder":
        return evalSamples(node.source as AstNode);
      default:
        return segments.map(() => []);
    }
  };
  const evalTimes = (node: AstNode): (number | null)[] => {
    switch (node.type) {
      case "comparison":
        return node.name === "Best Segments" ? [...best] : node.name === "Personal Best" ? [...pb] : segments.map(() => null);
      case "aggregate": {
        return evalSamples(node.source as AstNode).map((xs) => {
          if (xs.length === 0) return null;
          const sorted = [...xs].sort((a, b) => a - b);
          switch (node.kind) {
            case "min": return sorted[0];
            case "max": return sorted[sorted.length - 1];
            case "average": return sorted.reduce((a, b) => a + b, 0) / sorted.length;
            default: return medianOf(xs);
          }
        });
      }
      case "attempt":
        return pb.map((t) => t * 1.01);
      case "percentile": {
        const p = Number(node.percent) / 100;
        return evalSamples(node.source as AstNode).map((xs) => {
          if (xs.length === 0) return null;
          const sorted = [...xs].sort((a, b) => a - b);
          const rank = p * (sorted.length - 1);
          const lo = Math.floor(rank);
          const hi = Math.ceil(rank);
          return sorted[lo] + (sorted[hi] - sorted[lo]) * (rank - lo);
        });
      }
      case "stretchToTotal": {
        const xs = evalTimes(node.source as AstNode);
        if (xs.some((t) => t === null)) return xs.map(() => null);
        const sum = (xs as number[]).reduce((a, b) => a + b, 0);
        return (xs as number[]).map((t) => (t * Number(node.totalSeconds) * 1000) / sum);
      }
      case "blend": {
        const a = evalTimes(node.from as AstNode);
        const b = evalTimes(node.to as AstNode);
        return a.map((t, i) => (t === null || b[i] === null ? null : t + ((b[i] as number) - t) * (Number(node.percent) / 100)));
      }
      case "pick": {
        const a = evalTimes(node.first as AstNode);
        const b = evalTimes(node.second as AstNode);
        const slower = node.kind === "slower";
        return a.map((t, i) => (t === null ? b[i] : b[i] === null ? t : slower ? Math.max(t, b[i] as number) : Math.min(t, b[i] as number)));
      }
      case "bestRecentRun":
        return [...pb];
      case "weightedAverage":
        return evalSamples(node.source as AstNode).map((xs) => {
          if (xs.length === 0) return null;
          let total = 0, weights = 0;
          [...xs].reverse().forEach((t, age) => {
            const w = Math.pow(0.5, age / Number(node.halfLife));
            total += t * w;
            weights += w;
          });
          return total / weights;
        });
      case "clamp": {
        const src = evalTimes(node.source as AstNode);
        const lo = evalTimes(node.low as AstNode);
        const hi = evalTimes(node.high as AstNode);
        return src.map((t, i) => {
          if (t === null) return null;
          let v = t;
          if (hi[i] !== null && v > (hi[i] as number)) v = hi[i] as number;
          if (lo[i] !== null && v < (lo[i] as number)) v = lo[i] as number;
          return v;
        });
      }
      case "round": {
        const step = Number(node.step) * 1000;
        return evalTimes(node.source as AstNode).map((t) => (t === null ? null : Math.floor((t + step / 2) / step) * step));
      }
      case "addTime":
        return evalTimes(node.source as AstNode).map((t) => (t === null ? null : Math.max(0, t + Number(node.seconds) * 1000)));
      case "scale":
        return evalTimes(node.source as AstNode).map((t) => (t === null ? null : (t * Number(node.percent)) / 100));
      case "fallback": {
        const a = evalTimes(node.primary as AstNode);
        const b = evalTimes(node.fallback as AstNode);
        return a.map((t, i) => t ?? b[i]);
      }
      case "segmentRange": {
        const cases = node.cases as { from: number; to: number; times: AstNode }[];
        const otherwise = evalTimes(node.otherwise as AstNode);
        const arms = cases.map((c) => evalTimes(c.times));
        return segments.map((_, i) => {
          const k = cases.findIndex((c) => i + 1 >= c.from && i + 1 <= c.to);
          return k >= 0 ? arms[k][i] : otherwise[i];
        });
      }
      default:
        return segments.map(() => null);
    }
  };

  const reply = (message: HostMessage) => setTimeout(() => handler(message), 30);
  const cumulative = (xs: (number | null)[]) => {
    let total: number | null = 0;
    return xs.map((x) => (total = total === null || x === null ? null : total + x));
  };

  return {
    onMessage: (h) => (handler = h),
    send: (message) => {
      switch (message.type) {
        case "ready":
          reply({
            type: "init",
            programs: samplePrograms.map((doc) => ({ name: doc.name, doc })),
            initialProgram: "Safe Pace",
            segments,
            comparisons: ["Personal Best", "Best Segments", "Best Split Times", "Average Segments", "Median Segments", "Worst Segments", "Balanced PB", "Latest Run"],
            takenNames: ["Personal Best"],
            reservedNames: ["Personal Best", "None", "Best Segments", "Best Split Times", "Average Segments", "Median Segments", "Worst Segments", "Balanced PB", "Latest Run", "Golf HCP"],
            timingMethod: "RealTime",
          });
          break;
        case "preview": {
          const of = message.of === "times" || message.of === "samples" ? message.of : "comparison";
          if (of === "samples") {
            const lists = evalSamples(message.program.root);
            reply({
              type: "previewResult",
              requestId: message.requestId,
              of: "samples",
              error: null,
              rows: [],
              samples: segments.map((segment, i) => {
                const xs = [...(lists[i] ?? [])].sort((a, b) => a - b);
                return {
                  segment,
                  count: xs.length,
                  minMs: xs.length ? xs[0] : null,
                  medianMs: medianOf(xs),
                  maxMs: xs.length ? xs[xs.length - 1] : null,
                };
              }),
            });
            break;
          }

          const seg = evalTimes(message.program.root);
          const splits = cumulative(seg);
          const other = message.others.find((d) => d.name === message.compareTo);
          const referenceSegments = message.compareTo === "Best Segments" ? best
            : other ? evalTimes(other.root)
            : message.compareTo === "Personal Best" ? pb
            : segments.map(() => null);
          const referenceSplits = cumulative(referenceSegments);
          // Rough origins: only fallback is traced here; the real engine traces every choice.
          const root = message.program.root;
          const primary = root.type === "fallback" ? evalTimes(root.primary as AstNode) : null;
          reply({
            type: "previewResult",
            requestId: message.requestId,
            of,
            error: null,
            compareTo: message.compareTo,
            rows: segments.map((segment, i) => ({
              segment,
              segmentMs: seg[i],
              splitMs: splits[i],
              referenceMs: referenceSplits[i],
              bestSegmentMs: best[i],
              origin: primary ? (primary[i] === null ? "fallback" : "primary") : "",
            })),
          });
          break;
        }
        case "save":
          console.log("save", message.programs);
          reply({ type: "saveResult", ok: true, error: null });
          break;
        default:
          break;
      }
    },
  };
}
