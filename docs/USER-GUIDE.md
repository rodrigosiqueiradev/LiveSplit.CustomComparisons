# Custom Comparisons – User Guide

Custom Comparisons lets you make your own comparisons for LiveSplit (the times your splits
are measured against, like *Personal Best* or *Best Segments*) by snapping blocks together.
No coding needed.

For example, you could make:

- **"What I usually do lately"**: the middle time of your last 10 finished runs.
- **"Sub-20 pace"**: your typical runs, sped up evenly so they add up to exactly 20:00.
- **"Safe pace"**: never faster than your golds, never slower than your normal runs.

Once made, your comparison shows up in LiveSplit's **Compare Against** menu next to the
built-in ones, and works with all your usual layout components (Splits, Delta, Previous
Segment, and so on).

---

## Contents

1. [Install it](#1-install-it)
2. [Add it to your layout](#2-add-it-to-your-layout)
3. [Make your first comparison](#3-make-your-first-comparison)
4. [Save it (don't skip this)](#4-save-it-dont-skip-this)
5. [Use it](#5-use-it)
6. [A tour of the block editor](#6-a-tour-of-the-block-editor)
7. [How blocks fit together](#7-how-blocks-fit-together)
8. [Every block, explained](#8-every-block-explained)
9. [Recipes](#9-recipes)
10. [Sharing comparisons](#10-sharing-comparisons)
11. [Good to know](#11-good-to-know)
12. [Troubleshooting](#12-troubleshooting)

---

## 1. Install it

1. Download the latest `LiveSplit.CustomComparisons-x.y.z.zip` from the
   [Releases page](https://github.com/rodrigosiqueiradev/LiveSplit.CustomComparisons/releases).
2. **Close LiveSplit.**
3. Unzip the file **into your LiveSplit folder** (the folder that has `LiveSplit.exe` in it).
   - `LiveSplit.CustomComparisons.dll` lands in the `Components` folder. That's the component.
   - A `CustomComparisons` folder appears with this guide, the licence and some example
     comparisons you can import.
4. Start LiveSplit again.

You need Windows 10 or 11. The block editor uses Microsoft Edge WebView2, which Windows
already includes.

## 2. Add it to your layout

1. Right-click LiveSplit → **Edit Layout…**
2. Click the **+** button → **Other** → **Custom Comparisons**.
3. Click **OK**, then right-click LiveSplit → **Save Layout**.

It comes with one starter comparison, **Median Last 10 +1s** (the middle time of your last 10
finished runs, plus a second per segment), so you can try it straight away.

The component doesn't draw anything on your timer. It just adds your comparisons to LiveSplit.
Your comparisons are stored **inside your layout file**, so they come along wherever that
layout goes.

## 3. Make your first comparison

1. Right-click LiveSplit → **Edit Layout…**, then double-click **Custom Comparisons** in the
   list (or select it and click **Layout Settings**, then its tab).
2. Click **Open block editor…**
3. Pick a starting point. Each one is a finished comparison you can use as it is or change:

   | Starting point | What it gives you |
   |---|---|
   | **Goal pace** | Your typical runs, stretched to finish exactly in a goal time you choose. |
   | **Realistic pace** | Times you beat about 70 % of the time, from the last 30 days. |
   | **Recent form** | The middle time of your last 10 finished runs, ignoring flukes. |
   | **Safe pace** | The slower of "your golds + 2 seconds" and your normal runs. |
   | **Blank** | An empty canvas. |

4. Give it a name at the top (for example *Recent form*). This is the name you'll see in
   **Compare Against**.
5. Look at the **Preview** on the right. It shows what the comparison would be for the
   splits you have open, segment by segment. Change a block and the preview updates straight
   away.

## 4. Save it (don't skip this)

Saving takes **three clicks in three places**, because LiveSplit keeps the comparison in your
layout:

1. In the block editor, click **Save** (or press **Ctrl+S**). The editor closes.
2. In Layout Settings, click **OK**. (A yellow note reminds you. **Cancel** would undo
   your changes.)
3. Right-click LiveSplit → **Save Layout**.

If you skip step 3, your comparison is gone next time you start LiveSplit.

## 5. Use it

Right-click LiveSplit → **Compare Against** → pick your comparison.

It works for both **Real Time** and **Game Time**: whichever timing method you use,
the comparison is worked out from your times in that method.

Comparisons update by themselves. As you do more runs, "your last 10 runs" really is
your latest 10.

---

## 6. A tour of the block editor

```
┌──────────────────────────────────────────────────────────────────────────────────┐
│ Custom Comparisons · Block editor                               [Cancel] [Save]  │
├─[ Recent form ]─[ Goal pace ]─[ + ]──────────────────────────────────────────────┤
│ Name: [Recent form      ]  ↶ ↷  [Duplicate] [Delete]  │ Preview  [Real][Game]   │
│ ┌──────────┐                                           │ Segment   Seg.   Split   │
│ │ Sources  │   ┌─────────────────────────┐             │ Forest    1:02   1:02    │
│ │ Filters  │   │ this comparison is ...  │             │ Castle    2:11   3:13    │
│ │Aggregates│   └─────────────────────────┘             │ ...                      │
│ │ Math     │                                           │                          │
│ │ Control  │                                           │ Coloured numbers: vs PB  │
│ └──────────┘                                           │                          │
└──────────────────────────────────────────────────────────────────────────────────┘
```

**Tabs (top).** Each tab is one comparison. Click **+** to add another. All tabs are saved
together when you click **Save**.

**Name, Undo/Redo, Duplicate, Delete.** Undo and redo work per tab (**Ctrl+Z** / **Ctrl+Y**).
*Duplicate* copies the current comparison into a new tab, handy for trying a variation.

**Block drawer (left).** Five groups of blocks: *Sources*, *Filters*, *Aggregates*, *Math*
and *Control*. Click a group to open it, then drag a block out onto the canvas. Each group
has short headings explaining what its blocks are for. Hover over any block to see a tip.

**Canvas (middle).** Where you build. Drag blocks near each other and they snap together.
Zoom with the mouse wheel (or the **+ / −** buttons in the corner); drag an empty spot to move
around. To delete a block, drag it back onto
the drawer or select it and press **Delete**.

**Preview (right).**

- **Segment time** is how long each segment should take. **Split time** is the running
  total at the end of each segment (what your Splits component shows).
- The small coloured number under each time is the difference from another comparison
  (Personal Best unless you change it with **Coloured numbers: difference from**).
  Green means faster, red means slower.
- **Real time / Game time** switches which of your times the preview uses.
- **Click a row** to see *why* it has that time: which block it came from and, for example,
  which comparison was used to fill a gap.
- A **highlighted row** means that segment is faster than your best ever time for it (your
  gold). That's allowed, but it's a warning that the pace may not be realistic. There's a
  note under the table listing them.
- The line under the table compares the finish time with your PB and your sum of best.

---

## 7. How blocks fit together

Every comparison is one chain of blocks plugged into the **this comparison is** block.
Blocks plug into each other like puzzle pieces, and their **shape** tells you what fits where:

| Shape | What it carries | Example |
|---|---|---|
| **⬡ Pointy ends (hexagon)** | **A list of times** for each segment: all the times you've done it. | *all my times for each segment* |
| **⬭ Round ends** | **One time** for each segment. | *comparison "Personal Best"* |

- **this comparison is** needs a **round** block: a comparison is one time per segment.
- To get from a list (pointy) to one time (round), you use a block from **Aggregates**,
  like *median of* or *percentile 70 of*.
- Blocks only snap where they fit. If a block won't snap, it's the wrong shape for that spot.

Read a chain from the inside out. For example:

```
this comparison is
  median of
    last 10 times of
      from completed runs only
        all my times for each segment
```

means: *take all my times for each segment → keep only those from finished runs → keep the
latest 10 → take the middle one.*

---

## 8. Every block, explained

### Sources: where the times come from

| Block | What it does |
|---|---|
| **all my times for each segment** ⬡ | Every time you've recorded for each segment. Skipped segments (and the combined segment right after a skip) are left out. |
| **comparison** ⬭ | An existing comparison, like *Personal Best*, *Best Segments* or one of your own custom comparisons. LiveSplit's built-in ones work even if you've switched them off. |
| **attempt #** ⬭ | One specific past attempt, by its number in LiveSplit's attempt history. Empty where that run skipped a split or reset. |

### Filters: choose which runs count (list in, list out)

| Block | What it does |
|---|---|
| **last N times of** | Keeps only the N most recent times for each segment. It counts times you completed that segment, not attempts. |
| **from completed runs only** | Keeps only times from attempts where you finished the whole run. |
| **from the last N days** | Keeps only times from the last N days, so old times stop counting. |
| **from runs that finished under** | Keeps only times from runs that finished faster than a time you type (like `20:00`), so only your good runs count. |
| **drop the fastest and slowest N % of** | Removes flukes: drops that share of each segment's fastest and slowest times. With only a few times, nothing is dropped. |
| **only if there are at least N times in** | If a segment has fewer than N times, it's left empty, so you're not trusting too little data. Pair it with **if empty, use** to fill those gaps. |

### Aggregates: turn a list into one time (list in, one time out)

| Block | What it does |
|---|---|
| **median of** | The middle time: half your times are faster, half slower. Not thrown off by one awful run. Usually the best choice. |
| **average of** | The mean time. One very slow run pulls it up a lot. |
| **best (min) of** | Your fastest time in the list. |
| **worst (max) of** | Your slowest time in the list. |
| **percentile N of** | 0 = your fastest, 100 = your slowest, 50 = the median. **70** means "a time I beat about 70 % of the time." |

### Math: adjust one time per segment (one time in, one time out)

| Block | What it does |
|---|---|
| **add N s per segment to** | Adds that many seconds to every segment. Use a negative number to take time off. Never goes below zero. |
| **N % of** | Scales every segment. **103 %** is 3 % slower, **97 %** is 3 % faster. |
| **stretch to finish in** | Speeds up or slows down every segment by the same amount so the whole run adds up to exactly the goal time you type (like `19:59.9`). |
| **N % of the way from … to …** | Mixes two comparisons. 0 % is all the first, 100 % all the second, 75 % is three quarters of the way. Great for "somewhere between my golds and my PB". |

### Control: combine rules

| Block | What it does |
|---|---|
| **use … if empty, use …** | Uses the first rule, and fills only the segments it leaves empty with the second. Useful when there aren't enough runs yet, or when you've added a new split. |
| **for segments X to Y use … otherwise …** | One rule for a range of segments, another for the rest. Put another one of these in *otherwise* for more ranges. |
| **the slower / faster of … and …** | For each segment, whichever of the two is slower (or faster). If one has no time for a segment, the other is used. |

**Typing times.** Where a block asks for a time, you can type `20:00`, `1:05:30`,
`59.5` (seconds) or `19:59.9`.

---

## 9. Recipes

**Recent form, but safe for new splits.** The median of your last 10 finished runs. Segments
with fewer than 5 times use your PB instead.

```
this comparison is
  use
    median of
      only if there are at least 5 times in
        last 10 times of
          from completed runs only
            all my times for each segment
  if empty, use
    comparison "Personal Best"
```

**Sub-X pace.** Your recent typical runs, spread evenly to hit a goal.

```
this comparison is
  stretch to finish in 19:59.9
    use  median of (last 20 times of (all my times for each segment))
    if empty, use  comparison "Personal Best"
```

> **stretch to finish in** needs every segment to have a time, so fill gaps *before* it,
> as above.

**Halfway between golds and PB.**

```
this comparison is
  50 % of the way from  comparison "Best Segments"
                    to  comparison "Personal Best"
```

**Only my good runs from this month.**

```
this comparison is
  median of
    from runs that finished under 21:00
      from the last 30 days
        all my times for each segment
```

**Different rules for the hard part.** Be cautious on segments 5 to 8, aggressive elsewhere.

```
this comparison is
  for segments 5 to 8 use  percentile 80 of (all my times for each segment)
  otherwise                percentile 40 of (all my times for each segment)
```

**Never faster than my golds + a bit.**

```
this comparison is
  the slower of  add 1 s per segment to (comparison "Best Segments")
          and    median of (all my times for each segment)
```

---

## 10. Sharing comparisons

Comparisons are small files (`.json`) that anyone can import. They only describe blocks.
**They can never contain code**, so they're safe to share and open.

In **Layout Settings → Custom Comparisons**, select a comparison in the list and use:

| Button | What it does |
|---|---|
| **Export…** | Saves it as a `.json` file to send to someone. |
| **Import…** | Adds a `.json` file someone sent you. Try the ones in the `CustomComparisons\examples` folder. |
| **Copy as text** | Copies it to the clipboard, ready to paste into Discord or a forum post. |
| **Paste** | Adds a comparison from text you've copied. |

If the name is already taken, you'll be asked for another one.

Imported comparisons use **your** times, not the sender's. You're sharing the recipe, not
the results.

The same list also has **Rename…**, **Delete** and **Move up / Move down**. Move up/down
changes the order they appear in **Compare Against**.

Remember to click **OK** and **Save Layout** afterwards (see [step 4](#4-save-it-dont-skip-this)).

---

## 11. Good to know

**Empty segments.** If a rule has no time for a segment (no runs yet, not enough runs, a
segment you just added), that segment stays **empty** and so do the split times after it.
Nothing is made up. To fill gaps, wrap your blocks in **use … if empty, use …** with a
backup, usually *Personal Best*.

**Names.** A comparison can't use the name of a LiveSplit built-in (like *Personal Best* or
*Best Segments*), can't start with `[Race]` (LiveSplit uses those for races), and must be
different from your other comparisons.

**Using one comparison inside another.** You can use your own custom comparisons in the
**comparison** block. Just don't make a loop (A uses B, B uses A): the preview will tell you
if you do, and that comparison stays empty until you fix it.

**Real time and game time.** Each comparison is worked out separately for each timing method
from your times in that method. If you never use game time, those times are simply empty.

**Faster than gold.** A highlighted row in the preview means the comparison expects you to
beat your best ever time for that segment. That's allowed (maybe you want a stretch goal),
it's just pointed out.

**Changing splits.** *for segments X to Y* remembers segment **positions** (1st, 2nd, …),
not names. If you add or remove segments, open the editor and check those ranges.

---

## 12. Troubleshooting

**"Custom Comparisons" isn't in the Other menu.**
Check that `LiveSplit.CustomComparisons.dll` is in LiveSplit's `Components` folder, then
restart LiveSplit. If Windows blocked the download, right-click the zip → **Properties** →
tick **Unblock** before unzipping.

**My comparison disappeared after restarting LiveSplit.**
The layout wasn't saved. After saving in the editor, click **OK** in Layout Settings and then
right-click → **Save Layout**. Also check you're using the layout that has the component.

**My comparison is all blank (or blank from some point on).**
A segment has no time, so everything after it is blank too. Click a blank row in the
preview to see why, then add **use … if empty, use** with a backup like *Personal Best*.
Also check that your splits file has enough runs for the rules you chose, and that you're
looking at the timing method (Real / Game) you actually use.

**A block won't snap.**
It's the wrong shape for that spot (see [How blocks fit together](#7-how-blocks-fit-together)).
Usually you need an **Aggregates** block between a list (⬡) and a spot that wants one time (⬭).

**"Can't use that name."**
See *Names* in [Good to know](#11-good-to-know).

**Import says the file was made with a newer version.**
Someone used a block your version doesn't have yet. Update Custom Comparisons from the
Releases page.

**The block editor window is blank.**
Make sure Microsoft Edge WebView2 Runtime is installed (it comes with Windows 10 and 11, but
can be reinstalled from Microsoft's website), then restart LiveSplit.

**Still stuck?** Open an issue on the
[GitHub page](https://github.com/rodrigosiqueiradev/LiveSplit.CustomComparisons/issues) and
attach the comparison (use **Export…**).
