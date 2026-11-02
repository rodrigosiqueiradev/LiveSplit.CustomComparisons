// Messages between the editor page and the LiveSplit component (EditorBridge.cs).
// Keep the two sides in sync.

/** One block in the saved program format (see README "File format"). */
type AstNode = { type: string; [field: string]: unknown };

/** A whole program file, exactly as ProgramSerializer reads and writes it. */
interface ProgramDoc {
  format: "livesplit-custom-comparison";
  version: number;
  name: string;
  root: AstNode;
  /** Blockly's own state, so blocks reopen where they were. Optional. */
  workspace?: unknown;
}

type TimingMethod = "RealTime" | "GameTime";

// ---- LiveSplit -> editor

interface InitMessage {
  type: "init";
  programs: { name: string; doc: ProgramDoc }[];
  /** Open this program's tab first, if given. */
  initialProgram: string | null;
  /** Segment names of the loaded splits, for the preview. */
  segments: string[];
  /** Comparisons a "comparison" block can use, other than the editor's own programs. */
  comparisons: string[];
  /** Names a program can't take: comparisons in the run, unreadable saved entries. */
  takenNames: string[];
  /** LiveSplit's built-in names, which are always reserved. */
  reservedNames: string[];
  timingMethod: TimingMethod;
}

interface PreviewRow {
  segment: string;
  /** Milliseconds, or null when empty. */
  segmentMs: number | null;
  splitMs: number | null;
  /** The reference comparison's split (PB unless another was picked), or null if none. */
  referenceMs: number | null;
  /** The runner's gold for this segment (best ever segment time), or null if none. */
  bestSegmentMs: number | null;
  /** Path to the block this segment's time came from, e.g. "primary.cases.0.times". */
  origin: string;
}

/** One segment of a hexagon block: how many times it kept, and the fastest, middle and slowest. */
interface SampleSummary {
  segment: string;
  count: number;
  /** Milliseconds, or null when this segment has no times. */
  minMs: number | null;
  medianMs: number | null;
  maxMs: number | null;
}

/**
 * What a preview is of. Omitted means the finished comparison.
 * "times" is one round block. "samples" is one hexagon block.
 */
type PreviewOf = "comparison" | "times" | "samples";

interface PreviewResultMessage {
  type: "previewResult";
  requestId: number;
  rows: PreviewRow[];
  error: string | null;
  /** The comparison referenceMs comes from. */
  compareTo?: string;
  /**
   * Same sentence as the settings list when this timing method left segments empty.
   * Null when every segment has a time. Omitted by the browser stand-in, which
   * the page then derives from the rows.
   */
  emptyStatus?: string | null;
  /**
   * Same sentence as the settings list when a range or reached segment names a
   * segment the splits don't have. Null when every named segment exists.
   * Omitted by the browser stand-in, which the page then derives from the program.
   */
  segmentWarning?: string | null;
  /** Echoed from the request so a late reply cannot be drawn as the other mode. */
  of?: PreviewOf;
  /** Set when `of` is "samples". `rows` is empty in that case. */
  samples?: SampleSummary[];
}

interface SaveResultMessage {
  type: "saveResult";
  ok: boolean;
  error: string | null;
}

type HostMessage = InitMessage | PreviewResultMessage | SaveResultMessage;

// ---- editor -> LiveSplit

interface ReadyMessage {
  type: "ready";
}

interface PreviewRequest {
  type: "preview";
  requestId: number;
  timingMethod: TimingMethod;
  /** What the differences are measured against. */
  compareTo: string;
  /**
   * What to preview. Omitted means the finished comparison.
   * "times": program.root is one round block, evaluated like any comparison root.
   * "samples": program.root is one hexagon block. The host reads it with
   * ProgramSerializer.ReadSamplesRoot and replies with sample summaries.
   */
  of?: PreviewOf;
  /**
   * The program to preview. For "times" and "samples" this root is the clicked
   * block's subtree, not the whole comparison.
   */
  program: ProgramDoc;
  /** The editor's other complete programs, which the previewed one may use by name. */
  others: ProgramDoc[];
}

interface SaveRequest {
  type: "save";
  /** Every program in the editor, with the name each had when the editor opened (null if new). */
  programs: { originalName: string | null; doc: ProgramDoc }[];
}

interface DirtyMessage {
  type: "dirty";
  dirty: boolean;
}

interface CloseRequest {
  type: "close";
}

type EditorMessage = ReadyMessage | PreviewRequest | SaveRequest | DirtyMessage | CloseRequest;

const FORMAT_ID = "livesplit-custom-comparison";
const FORMAT_VERSION = 1;
