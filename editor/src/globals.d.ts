// Blockly is loaded with a <script> tag before editor.js, so it's a global here.
// Its package only ships module typings, so the global (value and the types we use)
// is declared from them.
declare const Blockly: typeof import("blockly/core");

declare namespace Blockly {
  type Block = import("blockly/core").Block;
  type BlockSvg = import("blockly/core").BlockSvg;
  type Workspace = import("blockly/core").Workspace;
  type WorkspaceSvg = import("blockly/core").WorkspaceSvg;
  type Theme = import("blockly/core").Theme;
  type FieldDropdown = import("blockly/core").FieldDropdown;
  namespace Events {
    type Abstract = import("blockly/core").Events.Abstract;
  }
  namespace utils {
    namespace toolbox {
      type ToolboxDefinition = import("blockly/core").utils.toolbox.ToolboxDefinition;
    }
  }
}

// The bridge WebView2 injects. Absent when index.html is opened in a normal browser.
interface WebView2Bridge {
  postMessage(message: unknown): void;
  addEventListener(type: "message", listener: (event: { data: unknown }) => void): void;
}

interface Window {
  chrome?: { webview?: WebView2Bridge };
}
