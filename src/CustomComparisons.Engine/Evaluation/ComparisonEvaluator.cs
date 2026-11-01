using System;
using System.Collections.Generic;
using System.Linq;
using CustomComparisons.Engine.Ast;
using CustomComparisons.Engine.Model;

namespace CustomComparisons.Engine.Evaluation;

/// <summary>The outcome of evaluating one program against one run, for one timing method.</summary>
public sealed class ComparisonResult
{
    /// <summary>Per-segment times. Empty segments are null.</summary>
    public IReadOnlyList<TimeSpan?> SegmentTimes { get; }

    /// <summary>
    /// Cumulative split times, which is what LiveSplit stores for a comparison.
    /// Once a segment is empty, every later split is empty too.
    /// </summary>
    public IReadOnlyList<TimeSpan?> SplitTimes { get; }

    /// <summary>Null on success. On failure every time is empty and this says why.</summary>
    public string Error { get; }

    public bool Succeeded => Error == null;

    /// <summary>
    /// Only from <see cref="ComparisonEvaluator.EvaluateWithOrigins"/>: per segment, the path from
    /// the program's root to the block that produced its time, through the choices made by
    /// fallback, segment range and faster/slower blocks. Steps use the file format's keys,
    /// e.g. "primary.cases.0.times" or "fallback". Empty means the root block itself.
    /// </summary>
    public IReadOnlyList<string> Origins { get; }

    internal ComparisonResult(IReadOnlyList<TimeSpan?> segmentTimes, IReadOnlyList<TimeSpan?> splitTimes, string error, IReadOnlyList<string> origins = null)
    {
        SegmentTimes = segmentTimes;
        SplitTimes = splitTimes;
        Error = error;
        Origins = origins;
    }
}

/// <summary>Raised for problems in a program itself (unknown comparison, cycle).</summary>
public sealed class ProgramException : Exception
{
    public ProgramException(string message) : base(message)
    {
    }
}

/// <summary>
/// Evaluates a set of user programs. Programs may refer to each other by name
/// through <see cref="ComparisonSourceNode"/>; cycles are reported as errors.
/// </summary>
public sealed class ComparisonEvaluator
{
    private readonly Dictionary<string, ComparisonProgram> programs;

    public ComparisonEvaluator(IEnumerable<ComparisonProgram> programs)
    {
        this.programs = new Dictionary<string, ComparisonProgram>(StringComparer.Ordinal);
        foreach (ComparisonProgram program in programs ?? Enumerable.Empty<ComparisonProgram>())
        {
            if (this.programs.ContainsKey(program.Name))
            {
                throw new ArgumentException($"Two comparisons are both named '{program.Name}'.", nameof(programs));
            }

            this.programs.Add(program.Name, program);
        }
    }

    public IEnumerable<string> ProgramNames => programs.Keys;

    /// <summary>Evaluates every program against the run. Errors are captured per program.</summary>
    public IReadOnlyDictionary<string, ComparisonResult> EvaluateAll(RunData run)
    {
        var session = new Session(this, run);
        return programs.Keys.ToDictionary(name => name, session.Evaluate, StringComparer.Ordinal);
    }

    public ComparisonResult Evaluate(string programName, RunData run)
    {
        if (!programs.ContainsKey(programName))
        {
            throw new ArgumentException($"No comparison named '{programName}'.", nameof(programName));
        }

        return new Session(this, run).Evaluate(programName);
    }

    /// <summary>Like <see cref="Evaluate"/>, and also records where each segment's time came from.</summary>
    public ComparisonResult EvaluateWithOrigins(string programName, RunData run)
    {
        if (!programs.ContainsKey(programName))
        {
            throw new ArgumentException($"No comparison named '{programName}'.", nameof(programName));
        }

        var session = new Session(this, run);
        ComparisonResult result = session.Evaluate(programName);
        if (!result.Succeeded)
        {
            return result;
        }

        string[] origins = session.Trace(programs[programName].Root).paths;
        return new ComparisonResult(result.SegmentTimes, result.SplitTimes, null, origins);
    }

    /// <summary>One evaluation pass over one run. Caches results so shared sub-programs run once.</summary>
    private sealed class Session
    {
        private readonly ComparisonEvaluator owner;
        private readonly RunData run;
        private readonly Dictionary<string, TimeSpan?[]> finished = new(StringComparer.Ordinal);
        private readonly List<string> inProgress = new();
        private List<Sample>[] segmentHistoryCache;

        public Session(ComparisonEvaluator owner, RunData run)
        {
            this.owner = owner;
            this.run = run ?? throw new ArgumentNullException(nameof(run));
        }

        public ComparisonResult Evaluate(string programName)
        {
            int count = run.SegmentCount;
            try
            {
                TimeSpan?[] segments = EvaluateProgram(programName);
                return new ComparisonResult(segments, ToSplits(segments), null);
            }
            catch (ProgramException e)
            {
                inProgress.Clear();
                var empty = new TimeSpan?[count];
                return new ComparisonResult(empty, empty, e.Message);
            }
        }

        private TimeSpan?[] EvaluateProgram(string name)
        {
            if (finished.TryGetValue(name, out TimeSpan?[] cached))
            {
                return cached;
            }

            int cycleStart = inProgress.IndexOf(name);
            if (cycleStart >= 0)
            {
                IEnumerable<string> loop = inProgress.Skip(cycleStart).Concat(new[] { name });
                throw new ProgramException("These comparisons depend on each other in a loop: " + string.Join(" → ", loop));
            }

            inProgress.Add(name);
            TimeSpan?[] result = EvalTimes(owner.programs[name].Root);
            inProgress.RemoveAt(inProgress.Count - 1);
            finished[name] = result;
            return result;
        }

        // ------------------------------------------------------------ Origins

        /// <summary>
        /// Values and, per segment, the path to the block that produced each value.
        /// Follows the branch actually taken by fallback / segment range / faster-slower, and
        /// looks through blocks that adjust a single input (add, scale, stretch).
        /// </summary>
        public (TimeSpan?[] values, string[] paths) Trace(TimesNode node)
        {
            switch (node)
            {
                case FallbackNode n:
                {
                    (TimeSpan?[] primary, string[] primaryPaths) = Trace(n.Primary);
                    (TimeSpan?[] fallback, string[] fallbackPaths) = Trace(n.Fallback);
                    var values = new TimeSpan?[primary.Length];
                    var paths = new string[primary.Length];
                    for (int i = 0; i < values.Length; i++)
                    {
                        bool usePrimary = primary[i].HasValue;
                        values[i] = usePrimary ? primary[i] : fallback[i];
                        paths[i] = usePrimary ? Join("primary", primaryPaths[i]) : Join("fallback", fallbackPaths[i]);
                    }

                    return (values, paths);
                }

                case SegmentRangeSwitchNode n:
                {
                    var arms = n.Cases.Select(c => Trace(c.Times)).ToArray();
                    (TimeSpan?[] otherwise, string[] otherwisePaths) = Trace(n.Otherwise);
                    var values = new TimeSpan?[run.SegmentCount];
                    var paths = new string[run.SegmentCount];
                    for (int i = 0; i < values.Length; i++)
                    {
                        int k = IndexOfFirst(n.Cases, c => c.Contains(i + 1));
                        values[i] = k >= 0 ? arms[k].values[i] : otherwise[i];
                        paths[i] = k >= 0 ? Join($"cases.{k}.times", arms[k].paths[i]) : Join("otherwise", otherwisePaths[i]);
                    }

                    return (values, paths);
                }

                case PickNode n:
                {
                    (TimeSpan?[] first, string[] firstPaths) = Trace(n.First);
                    (TimeSpan?[] second, string[] secondPaths) = Trace(n.Second);
                    TimeSpan?[] values = EvalTimes(n);
                    var paths = new string[values.Length];
                    for (int i = 0; i < values.Length; i++)
                    {
                        // Same rule as the evaluator: ties and a missing second side go to the first.
                        bool takeFirst = first[i].HasValue && (!second[i].HasValue ||
                            (n.Kind == PickKind.Faster ? first[i].Value <= second[i].Value : first[i].Value >= second[i].Value));
                        paths[i] = takeFirst ? Join("first", firstPaths[i]) : Join("second", secondPaths[i]);
                    }

                    return (values, paths);
                }

                case AddPerSegmentNode n:
                    return Through(n, Trace(n.Source).paths);
                case ScaleNode n:
                    return Through(n, Trace(n.Source).paths);
                case StretchToTotalNode n:
                    return Through(n, Trace(n.Source).paths);
                case RoundNode n:
                    return Through(n, Trace(n.Source).paths);

                case ClampNode n:
                {
                    // Points at whichever input the time ended up equal to: a limit, or the source.
                    (TimeSpan?[] source, string[] sourcePaths) = Trace(n.Source);
                    (TimeSpan?[] low, string[] lowPaths) = Trace(n.Low);
                    (TimeSpan?[] high, string[] highPaths) = Trace(n.High);
                    TimeSpan?[] values = EvalTimes(n);
                    var paths = new string[values.Length];
                    for (int i = 0; i < values.Length; i++)
                    {
                        paths[i] = !values[i].HasValue || values[i] == source[i] ? Join("source", sourcePaths[i])
                            : values[i] == low[i] ? Join("low", lowPaths[i])
                            : Join("high", highPaths[i]);
                    }

                    return (values, paths);
                }

                default:
                {
                    TimeSpan?[] values = EvalTimes(node);
                    return (values, values.Select(_ => "").ToArray());
                }
            }
        }

        private (TimeSpan?[] values, string[] paths) Through(TimesNode node, string[] sourcePaths) =>
            (EvalTimes(node), sourcePaths.Select(p => Join("source", p)).ToArray());

        private static string Join(string step, string rest) => rest.Length == 0 ? step : step + "." + rest;

        // ------------------------------------------------------------ Times

        private TimeSpan?[] EvalTimes(TimesNode node)
        {
            switch (node)
            {
                case AggregateNode n:
                    return EvalSamples(n.Source).Select(samples => AggregateSamples(n.Kind, samples)).ToArray();

                case PercentileNode n:
                    return EvalSamples(n.Source).Select(samples => Percentile(samples, n.Percent)).ToArray();

                case ComparisonSourceNode n:
                    return ComparisonSegments(n.ComparisonName);

                case AttemptNode n:
                    return AttemptSegments(n.Number);

                case BestRecentRunNode n:
                {
                    DateTime cutoff = run.NowUtc.AddDays(-n.Days);
                    AttemptData best = run.Attempts.Values
                        .Where(a => a.Completed && a.FinalTime.HasValue && a.WhenUtc.HasValue && a.WhenUtc.Value >= cutoff)
                        .OrderBy(a => a.FinalTime.Value)
                        .ThenBy(a => a.Index)
                        .FirstOrDefault();
                    return best == null ? new TimeSpan?[run.SegmentCount] : AttemptSegments(best.Index);
                }

                case WeightedAverageNode n:
                    return EvalSamples(n.Source).Select(samples => WeightedAverage(samples, n.HalfLife)).ToArray();

                case ClampNode n:
                {
                    TimeSpan?[] source = EvalTimes(n.Source);
                    TimeSpan?[] low = EvalTimes(n.Low);
                    TimeSpan?[] high = EvalTimes(n.High);
                    return source.Select((t, i) =>
                    {
                        if (!t.HasValue) return (TimeSpan?)null;
                        TimeSpan v = t.Value;
                        if (high[i].HasValue && v > high[i].Value) v = high[i].Value;
                        if (low[i].HasValue && v < low[i].Value) v = low[i].Value;
                        return v;
                    }).ToArray();
                }

                case RoundNode n:
                {
                    long step = n.Step.Ticks;
                    return EvalTimes(n.Source)
                        .Select(t => t.HasValue
                            ? TimeSpan.FromTicks((t.Value.Ticks + step / 2) / step * step)
                            : (TimeSpan?)null)
                        .ToArray();
                }

                case StretchToTotalNode n:
                    return StretchToTotal(EvalTimes(n.Source), n.Total);

                case BlendNode n:
                {
                    TimeSpan?[] from = EvalTimes(n.From);
                    TimeSpan?[] to = EvalTimes(n.To);
                    double share = n.Percent / 100.0;
                    return from.Select((a, i) => a.HasValue && to[i].HasValue
                            ? TimeSpan.FromTicks((long)Math.Round(a.Value.Ticks + (to[i].Value.Ticks - a.Value.Ticks) * share))
                            : (TimeSpan?)null)
                        .ToArray();
                }

                case PickNode n:
                {
                    TimeSpan?[] first = EvalTimes(n.First);
                    TimeSpan?[] second = EvalTimes(n.Second);
                    return first.Select((a, i) =>
                    {
                        TimeSpan? b = second[i];
                        if (!a.HasValue) return b;
                        if (!b.HasValue) return a;
                        bool takeFirst = n.Kind == PickKind.Faster ? a.Value <= b.Value : a.Value >= b.Value;
                        return takeFirst ? a : b;
                    }).ToArray();
                }

                case AddPerSegmentNode n:
                    return EvalTimes(n.Source).Select(t => t.HasValue ? ClampToZero(t.Value + n.Amount) : (TimeSpan?)null).ToArray();

                case ScaleNode n:
                    return EvalTimes(n.Source)
                        .Select(t => t.HasValue
                            ? TimeSpan.FromTicks((long)Math.Round(t.Value.Ticks * (n.Percent / 100.0)))
                            : (TimeSpan?)null)
                        .ToArray();

                case FallbackNode n:
                {
                    TimeSpan?[] primary = EvalTimes(n.Primary);
                    TimeSpan?[] fallback = EvalTimes(n.Fallback);
                    return primary.Select((t, i) => t ?? fallback[i]).ToArray();
                }

                case SegmentRangeSwitchNode n:
                {
                    // Evaluate each branch once, then pick per segment.
                    TimeSpan?[][] caseTimes = n.Cases.Select(c => EvalTimes(c.Times)).ToArray();
                    TimeSpan?[] otherwise = EvalTimes(n.Otherwise);
                    var result = new TimeSpan?[run.SegmentCount];
                    for (int i = 0; i < result.Length; i++)
                    {
                        int segmentNumber = i + 1;
                        int caseIndex = IndexOfFirst(n.Cases, c => c.Contains(segmentNumber));
                        result[i] = caseIndex >= 0 ? caseTimes[caseIndex][i] : otherwise[i];
                    }

                    return result;
                }

                default:
                    throw new ProgramException($"Unsupported block: {node?.GetType().Name ?? "(missing)"}.");
            }
        }

        private TimeSpan?[] ComparisonSegments(string name)
        {
            // Other custom programs are resolved here (with cycle detection), not read
            // back from LiveSplit, so the order generators run in doesn't matter.
            if (owner.programs.ContainsKey(name))
            {
                return EvaluateProgram(name);
            }

            IReadOnlyList<TimeSpan?> splits = run.GetComparisonSplits(name)
                ?? throw new ProgramException($"There is no comparison named '{name}'.");

            var segments = new TimeSpan?[run.SegmentCount];
            for (int i = 0; i < segments.Length; i++)
            {
                TimeSpan? current = i < splits.Count ? splits[i] : null;
                TimeSpan? previous = i == 0 ? TimeSpan.Zero : (i - 1 < splits.Count ? splits[i - 1] : null);
                segments[i] = current.HasValue && previous.HasValue ? ClampToZero(current.Value - previous.Value) : null;
            }

            return segments;
        }

        /// <summary>One attempt's segment times, with LiveSplit's skip rules applied.</summary>
        private TimeSpan?[] AttemptSegments(int number)
        {
            bool known = run.Attempts.ContainsKey(number) || run.Segments.Any(s => s.History.ContainsKey(number));
            if (!known)
            {
                throw new ProgramException($"There's no attempt #{number} in these splits.");
            }

            var result = new TimeSpan?[run.SegmentCount];
            List<Sample>[] history = SegmentHistory();
            for (int i = 0; i < result.Length; i++)
            {
                Sample match = history[i].FirstOrDefault(s => s.AttemptIndex == number);
                result[i] = match.AttemptIndex == number ? match.Time : null;
            }

            return result;
        }

        // ------------------------------------------------------------ Samples

        private List<Sample>[] EvalSamples(SamplesNode node)
        {
            switch (node)
            {
                case SegmentHistoryNode:
                    return SegmentHistory();

                case LastNNode n:
                    return EvalSamples(n.Source)
                        .Select(samples => samples.OrderBy(s => s.AttemptIndex).Skip(Math.Max(0, samples.Count - n.Count)).ToList())
                        .ToArray();

                case CompletedRunsOnlyNode n:
                    return EvalSamples(n.Source)
                        .Select(samples => samples
                            .Where(s => run.Attempts.TryGetValue(s.AttemptIndex, out AttemptData a) && a.Completed)
                            .ToList())
                        .ToArray();

                case TrimOutliersNode n:
                    return EvalSamples(n.Source)
                        .Select(samples =>
                        {
                            int drop = (int)Math.Floor(samples.Count * n.Percent / 100.0);
                            if (drop == 0)
                            {
                                return samples;
                            }

                            var kept = new HashSet<int>(samples
                                .Select((sample, i) => (sample, i))
                                .OrderBy(x => x.sample.Time)
                                .ThenBy(x => x.i)
                                .Skip(drop)
                                .Take(samples.Count - 2 * drop)
                                .Select(x => x.i));
                            return samples.Where((_, i) => kept.Contains(i)).ToList();
                        })
                        .ToArray();

                case FinishedUnderNode n:
                    return EvalSamples(n.Source)
                        .Select(samples => samples
                            .Where(s => run.Attempts.TryGetValue(s.AttemptIndex, out AttemptData a)
                                        && a.FinalTime.HasValue
                                        && a.FinalTime.Value < n.Limit)
                            .ToList())
                        .ToArray();

                case ReachedSegmentNode n:
                {
                    if (n.Segment > run.SegmentCount)
                    {
                        return EvalSamples(n.Source).Select(_ => new List<Sample>()).ToArray();
                    }

                    IReadOnlyDictionary<int, TimeSpan?> reached = run.Segments[n.Segment - 1].History;
                    return EvalSamples(n.Source)
                        .Select(samples => samples.Where(s => reached.ContainsKey(s.AttemptIndex)).ToList())
                        .ToArray();
                }

                case AtLeastNode n:
                    return EvalSamples(n.Source)
                        .Select(samples => samples.Count >= n.Count ? samples : new List<Sample>())
                        .ToArray();

                case RecentDaysNode n:
                {
                    DateTime cutoff = run.NowUtc.AddDays(-n.Days);
                    return EvalSamples(n.Source)
                        .Select(samples => samples
                            .Where(s => run.Attempts.TryGetValue(s.AttemptIndex, out AttemptData a)
                                        && a.WhenUtc.HasValue
                                        && a.WhenUtc.Value >= cutoff)
                            .ToList())
                        .ToArray();
                }

                default:
                    throw new ProgramException($"Unsupported block: {node?.GetType().Name ?? "(missing)"}.");
            }
        }

        /// <summary>
        /// Builds per-segment samples from segment history, following LiveSplit's own
        /// rules: within an attempt, a skipped segment (null) means the next recorded
        /// segment time covers two segments, so it is left out too; and an attempt
        /// stops contributing at the first segment it never reached.
        /// </summary>
        private List<Sample>[] SegmentHistory()
        {
            if (segmentHistoryCache != null)
            {
                return segmentHistoryCache.Select(list => list.ToList()).ToArray();
            }

            var perSegment = Enumerable.Range(0, run.SegmentCount).Select(_ => new List<Sample>()).ToArray();
            IEnumerable<int> attemptIndices = run.Segments
                .SelectMany(s => s.History.Keys)
                .Distinct()
                .OrderBy(i => i);

            foreach (int attempt in attemptIndices)
            {
                bool previousWasSkipped = false;
                for (int i = 0; i < run.SegmentCount; i++)
                {
                    if (!run.Segments[i].History.TryGetValue(attempt, out TimeSpan? time))
                    {
                        break;
                    }

                    if (time == null)
                    {
                        previousWasSkipped = true;
                    }
                    else if (previousWasSkipped)
                    {
                        previousWasSkipped = false;
                    }
                    else
                    {
                        perSegment[i].Add(new Sample(attempt, time.Value));
                    }
                }
            }

            segmentHistoryCache = perSegment;
            return perSegment.Select(list => list.ToList()).ToArray();
        }

        // ------------------------------------------------------------ Helpers

        private TimeSpan?[] ToSplits(TimeSpan?[] segments)
        {
            var splits = new TimeSpan?[segments.Length];
            TimeSpan? total = TimeSpan.Zero;
            for (int i = 0; i < segments.Length; i++)
            {
                total = total.HasValue && segments[i].HasValue ? total + segments[i] : null;
                splits[i] = total;
            }

            return splits;
        }

        private static TimeSpan? AggregateSamples(AggregateKind kind, List<Sample> samples)
        {
            if (samples.Count == 0)
            {
                return null;
            }

            List<long> ticks = samples.Select(s => s.Time.Ticks).OrderBy(t => t).ToList();
            switch (kind)
            {
                case AggregateKind.Min:
                    return TimeSpan.FromTicks(ticks[0]);
                case AggregateKind.Max:
                    return TimeSpan.FromTicks(ticks[ticks.Count - 1]);
                case AggregateKind.Average:
                    return TimeSpan.FromTicks((long)Math.Round(ticks.Average()));
                case AggregateKind.Median:
                {
                    int mid = ticks.Count / 2;
                    return ticks.Count % 2 == 1
                        ? TimeSpan.FromTicks(ticks[mid])
                        : TimeSpan.FromTicks((ticks[mid - 1] + ticks[mid]) / 2);
                }
                default:
                    throw new ProgramException($"Unsupported aggregate: {kind}.");
            }
        }

        /// <summary>Newest time weighs 1; weight halves every <paramref name="halfLife"/> older times.</summary>
        private static TimeSpan? WeightedAverage(List<Sample> samples, int halfLife)
        {
            if (samples.Count == 0)
            {
                return null;
            }

            List<Sample> newestFirst = samples.OrderByDescending(s => s.AttemptIndex).ToList();
            double total = 0, weights = 0;
            for (int age = 0; age < newestFirst.Count; age++)
            {
                double weight = Math.Pow(0.5, (double)age / halfLife);
                total += newestFirst[age].Time.Ticks * weight;
                weights += weight;
            }

            return TimeSpan.FromTicks((long)Math.Round(total / weights));
        }

        /// <summary>Linear interpolation between closest ranks (so 50 is exactly the median).</summary>
        private static TimeSpan? Percentile(List<Sample> samples, double percent)
        {
            if (samples.Count == 0)
            {
                return null;
            }

            List<long> ticks = samples.Select(s => s.Time.Ticks).OrderBy(t => t).ToList();
            double rank = percent / 100.0 * (ticks.Count - 1);
            int lower = (int)Math.Floor(rank);
            int upper = (int)Math.Ceiling(rank);
            double fraction = rank - lower;
            return TimeSpan.FromTicks((long)Math.Round(ticks[lower] + (ticks[upper] - ticks[lower]) * fraction));
        }

        /// <summary>
        /// Scales every segment by one factor so the total is exactly <paramref name="total"/>.
        /// Rounding leftovers go to the last segment so the sum is exact to the tick.
        /// </summary>
        private static TimeSpan?[] StretchToTotal(TimeSpan?[] segments, TimeSpan total)
        {
            if (segments.Length == 0 || segments.Any(t => !t.HasValue))
            {
                return new TimeSpan?[segments.Length];
            }

            long sum = segments.Sum(t => t.Value.Ticks);
            if (sum <= 0)
            {
                return new TimeSpan?[segments.Length];
            }

            double factor = (double)total.Ticks / sum;
            var result = new TimeSpan?[segments.Length];
            long assigned = 0;
            for (int i = 0; i < segments.Length - 1; i++)
            {
                long ticks = (long)Math.Round(segments[i].Value.Ticks * factor);
                result[i] = TimeSpan.FromTicks(ticks);
                assigned += ticks;
            }

            result[segments.Length - 1] = TimeSpan.FromTicks(Math.Max(0, total.Ticks - assigned));
            return result;
        }

        private static TimeSpan ClampToZero(TimeSpan t) => t < TimeSpan.Zero ? TimeSpan.Zero : t;

        private static int IndexOfFirst<T>(IReadOnlyList<T> items, Func<T, bool> predicate)
        {
            for (int i = 0; i < items.Count; i++)
            {
                if (predicate(items[i]))
                {
                    return i;
                }
            }

            return -1;
        }
    }

    private readonly struct Sample
    {
        public int AttemptIndex { get; }
        public TimeSpan Time { get; }

        public Sample(int attemptIndex, TimeSpan time)
        {
            AttemptIndex = attemptIndex;
            Time = time;
        }
    }
}
