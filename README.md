# LiveSplit Custom Comparisons

[![CI](https://github.com/rodrigosiqueiradev/LiveSplit.CustomComparisons/actions/workflows/ci.yml/badge.svg)](https://github.com/rodrigosiqueiradev/LiveSplit.CustomComparisons/actions/workflows/ci.yml)

A LiveSplit component that lets runners build their own comparisons from blocks:
filters, aggregates, segment ranges and fallbacks over their own run history.
The results show up in LiveSplit's **Compare Against** menu like any built-in comparison.

**New here? Read the [User Guide](docs/USER-GUIDE.md)**: installing, building your first
comparison, every block explained, recipes and troubleshooting. No coding needed.

**Status: in development, no release yet.** Build comparisons in a block editor with a live preview of your
own splits. Comparisons are saved in your layout and can be shared as `.json` files
(see [`examples/`](examples)).

## What it does today

- Adding the component to a layout gives you one starter comparison,
  **Median Last 10 +1s**: for each segment, the median of your last 10 times
  from completed runs, plus one second.
- Double-click the component in the Layout Editor, then **Open block editor…** to build
  comparisons from blocks. Hexagon blocks give a list of times per segment and fit hexagon
  slots; round blocks give one time per segment and fit round slots. The preview updates
  as you build.
- From the same page you can **Import**, **Export**, **Rename** or **Delete** comparisons. Each one shows whether it's working, and why not if it isn't.
- Comparisons are saved inside the layout (`.lsl`), so save the layout after changing them.

## Build

Requirements: Windows, the [.NET SDK](https://dotnet.microsoft.com/download) (8 or later),
[Node.js](https://nodejs.org) for the editor, and a LiveSplit install (1.8.37 or later,
which ships the WebView2 libraries the editor uses).

```powershell
git clone https://github.com/rodrigosiqueiradev/LiveSplit.CustomComparisons.git
cd LiveSplit.CustomComparisons
```

Tell the build where LiveSplit is (the folder with `LiveSplit.exe`) by creating `local.props`
in the repository root. It's git-ignored, so it stays on your machine:

```xml
<Project>
  <PropertyGroup>
    <LiveSplitDir>C:\Path\To\LiveSplit</LiveSplitDir>
  </PropertyGroup>
</Project>
```

(Or set the `LIVESPLIT_DIR` environment variable, or pass `-p:LiveSplitDir=...` to `dotnet build`.)

```powershell
# Everything at once: editor, tests, component. Output also goes to build-log.txt
./build-and-test.ps1

# Engine tests only (also run on macOS/Linux)
dotnet test tests/CustomComparisons.Engine.Tests

# Component, optionally copied straight into LiveSplit\Components
dotnet build src/LiveSplit.CustomComparisons -c Release -p:DeployToLiveSplit=true
```

CI runs on every push (and on demand from the Actions tab): the engine tests on Linux, and
on Windows the editor plus the component, compiled against the official LiveSplit release it
downloads. Each run's summary page has a package to download.

### Releasing

Bump `<Version>` in `src/LiveSplit.CustomComparisons/LiveSplit.CustomComparisons.csproj`, commit,
then push a tag with the same version:

```powershell
git tag v0.2.0
git push origin v0.2.0
```

CI builds that version and, once the tests pass, publishes a GitHub release with
`LiveSplit.CustomComparisons-0.2.0.zip` attached. Unzip it into the LiveSplit folder: the DLL
lands in `Components`, and the user guide, readme, licence and example comparisons in
`CustomComparisons`.

Building the component also rebuilds the block editor when its sources have changed
(it needs `npm` on your PATH; pass `-p:BuildEditor=false` to skip that).

### Visual Studio: build, deploy and start LiveSplit with F5

1. Open `LiveSplit.CustomComparisons.sln` (Visual Studio 2022 or later, with the
   ".NET desktop development" workload) and create `local.props` as above.
2. Right-click **LiveSplit.CustomComparisons** → **Set as Startup Project**.
3. Pick **LiveSplit** in the run-button dropdown next to the green ▶ (it's the only profile).
4. Close LiveSplit if it's running, then press **F5**.

F5 rebuilds what changed (the editor too), copies the DLL and its `.pdb` into
`LiveSplit\Components`, starts LiveSplit and attaches the debugger, so breakpoints in the
component work. Stopping the debugger closes LiveSplit. In Debug builds, **F12** in the block
editor opens the browser developer tools for the editor page.

The copy into `Components` happens on every Visual Studio build. To turn it off, add
`<DeployToLiveSplit>false</DeployToLiveSplit>` to `local.props`. To keep your normal LiveSplit
untouched, point `LiveSplitDir` at a second, unzipped copy of LiveSplit just for development.

The component builds into a single `LiveSplit.CustomComparisons.dll`.
Copy it into `LiveSplit\Components` if you didn't use `DeployToLiveSplit`.

## Try it

1. Close LiveSplit, copy the DLL in, start LiveSplit.
2. Right-click → **Edit Layout…** → **+** → **Other** → **Custom Comparisons**.
3. Right-click → **Compare Against** → **Median Last 10 +1s**.
4. Double-click the component in the layout editor to see whether the comparison is working.

You need a few completed runs in your splits file for it to show times.

## File format

A comparison file is plain JSON describing blocks. It can never contain code.

```json
{
  "format": "livesplit-custom-comparison",
  "version": 1,
  "name": "Golds +3%",
  "root": {
    "type": "scale",
    "percent": 103,
    "source": { "type": "comparison", "name": "Best Segments" }
  }
}
```

Blocks give either **one time per segment** or **a list of times per segment**:

| Block | Fields | Gives |
| --- | --- | --- |
| `segmentHistory` | | list |
| `lastN` | `count` (1–10000), `source`: list | list |
| `completedRunsOnly` | `source`: list | list |
| `recentDays` ² | `days` (1–36500), `source`: list | list |
| `trimOutliers` ³ | `percent` (0–45, from each end), `source`: list | list |
| `finishedUnder` ³ | `totalSeconds`, `source`: list | list |
| `atLeast` ³ | `count` (1–10000), `source`: list | list |
| `reachedSegment` ⁴ | `segment` (1-based), `source`: list | list |
| `aggregate` | `kind` (`min` `max` `average` `median`), `source`: list | one |
| `percentile` ² | `percent` (0–100), `source`: list | one |
| `weightedAverage` ⁴ | `halfLife` (1–10000 times), `source`: list | one |
| `comparison` | `name` (any comparison, including your own) | one |
| `attempt` ³ | `number` (as in LiveSplit's attempt history) | one |
| `bestRecentRun` ⁴ | `days` (1–36500) | one |
| `addTime` | `seconds` (±3600), `source`: one | one |
| `scale` | `percent` (1–1000), `source`: one | one |
| `stretchToTotal` ² | `totalSeconds` (goal time), `source`: one | one |
| `blend` ² | `percent` (0–100), `from`: one, `to`: one | one |
| `pick` ² | `kind` (`faster` `slower`), `first`: one, `second`: one | one |
| `clamp` ⁴ | `source`: one, `low`: one, `high`: one | one |
| `round` ⁴ | `step` (seconds, e.g. 0.1), `source`: one | one |
| `segmentRange` | `cases`: [`{from, to, times}`], `otherwise`: one | one |
| `fallback` | `primary`: one, `fallback`: one | one |

² Format version 2. ³ Format version 3. ⁴ Format version 4. Files are written with the lowest version that
holds their blocks, so comparisons that don't use newer blocks still open in older releases.

`root` must give one time per segment. Mistakes are reported with their location,
e.g. `root.source.count: must be a whole number from 1 to 10000.`

## How it works

```
src/
  CustomComparisons.Engine/      Pure C#, no LiveSplit dependency. Testable anywhere.
    Ast/Nodes.cs                 Typed AST, one class per block.
    Model/RunData.cs             The engine's view of a run (one timing method).
    Evaluation/                  Evaluator: per-segment maths, cycle detection.
    Serialization/               Versioned JSON format (dependency-free reader/writer).
    Library/                     The saved list of comparisons and its naming rules.
    ComparisonNames.cs           Name validation against built-ins and existing comparisons.
    SamplePrograms.cs            Milestone-1 hardcoded program.
  LiveSplit.CustomComparisons/   The LiveSplit component (net481). Compiles the engine in.
    CustomComparisonsComponent   Keeps our generators registered on the loaded run.
    ProgramComparisonGenerator   One IComparisonGenerator per program.
    RunAdapter                   IRun → RunData.
    ComparisonSource             Existing comparisons for programs, incl. switched-off built-ins.
    Editor/                      WebView2 window and the bridge that answers the editor.
editor/                          The block editor (TypeScript + Blockly), built into EditorDist/.
tests/
  CustomComparisons.Engine.Tests xUnit tests for the engine.
```

### Semantics

- **Per segment.** Every block works on segment times. Splits are summed only at the end.
  Like LiveSplit's Average/Median Segments, the result may not match any real run.
- **Both timing methods.** Every program is evaluated for real time and game time.
- **Skipped splits.** A skipped segment and the combined segment after it are ignored, matching LiveSplit.
- **"Last N"** counts the last N times *that segment* was completed, not the last N attempts.
- **Median** is the plain median. LiveSplit's built-in Median Segments weights recent runs more heavily, so the numbers can differ.
- **Empty stays empty.** A segment with no data stays empty, and so does every split after it,
  unless the program uses a **Fallback** block.
- **Add time** applies per segment. Results never go below zero.
- **Programs can use other programs** as inputs. Loops are reported as errors.
- **Percentile** interpolates between neighbouring times; 50 equals the median.
- **Last N days** goes by when each attempt ended. Attempts without a date are left out.
- **Stretch to a goal** scales all segments by one factor so they add up to the goal exactly.
  If any segment is empty, the whole result is empty: fill gaps first with a fallback.
- **Blend** is empty where either side is empty. **Faster/slower** uses the other side
  where one is empty.
- **Trim outliers** drops the given share from each end per segment, rounded down, so
  small lists are left alone.
- **At least N** empties a segment's list when it has fewer than N times. Pair it with
  "if empty, use" for "use my median, unless there's too little data".
- **Attempt #N** is empty where that attempt skipped a split or reset. An attempt number
  that doesn't exist is reported as an error.
- **Reached segment X** keeps attempts with a recorded or skipped time for segment X.
- **Recent-weighted average**: the newest time weighs 1, and the weight halves every
  `halfLife` older times of that segment.
- **Keep between** (`clamp`) applies "never slower than" then "never faster than", so if
  the limits cross the lower limit wins. An empty limit doesn't apply to that segment.
- **Round** rounds each segment to the nearest step (halves up); split times follow.
- **Best run from the last N days** is the fastest finished attempt that ended in that
  window, with the attempt's skip rules; empty if none.

## Roadmap

- [x] Milestone 1: engine, tests, component with a hardcoded program
- [x] Milestone 2: versioned JSON format, saved in the layout, import/export
- [x] Milestone 3: Blockly editor in WebView2 with a live preview
- [ ] Fixture `.lss` integration tests
- [ ] Listing in LiveSplit's component update list

## License

MIT
