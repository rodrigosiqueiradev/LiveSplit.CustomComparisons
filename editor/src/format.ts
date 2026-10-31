// Time formatting in LiveSplit's style: 1:02.90, 12:52.10, 1:04:03.25.

function formatTime(ms: number | null): string {
  if (ms === null) {
    return "—";
  }

  const negative = ms < 0;
  let rest = Math.round(Math.abs(ms) / 10); // hundredths
  const hundredths = rest % 100;
  rest = Math.floor(rest / 100);
  const seconds = rest % 60;
  rest = Math.floor(rest / 60);
  const minutes = rest % 60;
  const hours = Math.floor(rest / 60);

  const hh = String(hundredths).padStart(2, "0");
  let text: string;
  if (hours > 0) {
    text = `${hours}:${String(minutes).padStart(2, "0")}:${String(seconds).padStart(2, "0")}.${hh}`;
  } else if (minutes > 0) {
    text = `${minutes}:${String(seconds).padStart(2, "0")}.${hh}`;
  } else {
    text = `${seconds}.${hh}`;
  }

  return negative ? "−" + text : text;
}

/** A delta with an explicit sign: +1.20, −0.85, ±0.00. */
function formatDelta(ms: number | null): string {
  if (ms === null) {
    return "—";
  }

  if (Math.round(ms / 10) === 0) {
    return "±0.00";
  }

  return ms > 0 ? "+" + formatTime(ms) : formatTime(ms);
}

/** Parses "1:05:30", "20:00", "59.5" or "20:00.25" into seconds. Null if it isn't a time. */
function parseDuration(text: string): number | null {
  const trimmed = text.trim();
  if (!/^\d+(:\d{1,2}){0,2}(\.\d+)?$/.test(trimmed)) {
    return null;
  }

  const [main, fraction = ""] = trimmed.split(".");
  const parts = main.split(":").map(Number);
  if (parts.slice(1).some((p) => p >= 60)) {
    return null;
  }

  const seconds = parts.reduce((total, part) => total * 60 + part, 0);
  return seconds + (fraction ? Number("0." + fraction) : 0);
}

/** Formats seconds for the goal-time field: 20:00, 1:05:30, 0:59.5. */
function formatDuration(totalSeconds: number): string {
  const whole = Math.floor(totalSeconds);
  const fraction = Math.round((totalSeconds - whole) * 1000) / 1000;
  const hours = Math.floor(whole / 3600);
  const minutes = Math.floor((whole % 3600) / 60);
  const seconds = whole % 60;
  const ss = String(seconds).padStart(2, "0") + (fraction > 0 ? String(fraction).slice(1) : "");
  return hours > 0 ? `${hours}:${String(minutes).padStart(2, "0")}:${ss}` : `${minutes}:${ss}`;
}
