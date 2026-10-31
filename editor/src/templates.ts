// Starting points offered when making a new comparison.

interface Template {
  title: string;
  description: string;
  /** Null for an empty comparison. */
  build: (goalSeconds: number) => AstNode | null;
}

const completedHistory = (): AstNode => ({ type: "completedRunsOnly", source: { type: "segmentHistory" } });
const pb = (): AstNode => ({ type: "comparison", name: "Personal Best" });

const TEMPLATES: Template[] = [
  {
    title: "Goal pace",
    description: "Your typical runs, stretched to finish exactly in a goal time.",
    build: (goal) => ({
      type: "stretchToTotal",
      totalSeconds: goal,
      source: {
        type: "fallback",
        primary: { type: "aggregate", kind: "median", source: { type: "lastN", count: 10, source: completedHistory() } },
        fallback: pb(),
      },
    }),
  },
  {
    title: "Realistic pace",
    description: "Percentile 70 of the last 30 days: times you beat about 70 % of the time.",
    build: () => ({
      type: "fallback",
      primary: {
        type: "percentile",
        percent: 70,
        source: { type: "atLeast", count: 5, source: { type: "recentDays", days: 30, source: completedHistory() } },
      },
      fallback: pb(),
    }),
  },
  {
    title: "Recent form",
    description: "The median of your last 10 completed runs, ignoring the best and worst fluke.",
    build: () => ({
      type: "fallback",
      primary: {
        type: "aggregate",
        kind: "median",
        source: { type: "trimOutliers", percent: 10, source: { type: "lastN", count: 10, source: completedHistory() } },
      },
      fallback: pb(),
    }),
  },
  {
    title: "Safe pace",
    description: "The slower of your golds + 2 s and your median, so it's never unrealistically fast.",
    build: () => ({
      type: "pick",
      kind: "slower",
      first: { type: "addTime", seconds: 2, source: { type: "comparison", name: "Best Segments" } },
      second: { type: "aggregate", kind: "median", source: { type: "lastN", count: 20, source: completedHistory() } },
    }),
  },
  {
    title: "Blank",
    description: "Start from scratch.",
    build: () => null,
  },
];
