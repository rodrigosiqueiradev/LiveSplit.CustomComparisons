// Copies the static page and the parts of Blockly the editor needs next to editor.js.
// The C# project embeds everything in EditorDist/ into the component DLL.
// copyFileSync overwrites in place (cpSync deletes first, which some file systems refuse).
import { copyFileSync, existsSync, mkdirSync, readdirSync, statSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const root = join(dirname(fileURLToPath(import.meta.url)), "..");
const out = join(root, "..", "src", "LiveSplit.CustomComparisons", "EditorDist");
const blockly = join(root, "node_modules", "blockly");

function copyDir(from, to) {
  mkdirSync(to, { recursive: true });
  for (const name of readdirSync(from)) {
    const source = join(from, name);
    if (statSync(source).isDirectory()) {
      copyDir(source, join(to, name));
    } else {
      copyFileSync(source, join(to, name));
    }
  }
}

copyDir(join(root, "static"), out);

for (const [from, to] of [
  ["blockly_compressed.js", "blockly_compressed.js"],
  [join("msg", "en.js"), "msg_en.js"],
]) {
  const source = join(blockly, from);
  if (!existsSync(source)) {
    throw new Error(`Blockly file not found: ${source}. Run npm install in editor/.`);
  }

  copyFileSync(source, join(out, to));
}

copyDir(join(blockly, "media"), join(out, "media"));
console.log(`Editor assets copied to ${out}`);
