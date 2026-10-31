using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace LiveSplit.CustomComparisons.Editor;

/// <summary>
/// The block editor window: a WebView2 showing the Blockly page embedded in this DLL.
/// All logic about programs lives in <see cref="EditorBridge"/>; this class only hosts.
/// </summary>
internal sealed class EditorForm : Form
{
    /// <summary>
    /// The page is served from this made-up origin. It never touches the network: every
    /// request to it is answered from embedded resources, and nothing else may load.
    /// </summary>
    private const string Origin = "https://custom-comparisons.editor";

    private readonly EditorBridge bridge;
    private readonly WebView2 webView;
    private bool closingConfirmed;

    public EditorForm(EditorBridge bridge)
    {
        this.bridge = bridge;
        // Deferred, so a reply the bridge is still sending isn't posted to a closed window.
        bridge.CloseRequested += (s, e) => BeginInvoke(new Action(() =>
        {
            closingConfirmed = true;
            Close();
        }));

        Text = "Custom Comparisons – Block editor";
        // Sized and centred in OnLoad, once we know which screen LiveSplit is on.
        StartPosition = FormStartPosition.Manual;
        Size = new Size(1280, 800);
        MinimumSize = new Size(900, 560);
        BackColor = Color.FromArgb(0x15, 0x17, 0x1c);
        ShowInTaskbar = false;

        webView = new WebView2
        {
            Dock = DockStyle.Fill,
            DefaultBackgroundColor = BackColor,
        };
        Controls.Add(webView);
        Load += async (s, e) => await InitializeAsync();
    }

    private async System.Threading.Tasks.Task InitializeAsync()
    {
        try
        {
            // Our own profile folder, so we never clash with other components' WebView2 settings.
            string userData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "LiveSplit.CustomComparisons",
                "WebView2");
            CoreWebView2Environment environment = await CoreWebView2Environment.CreateAsync(null, userData);
            await webView.EnsureCoreWebView2Async(environment);
        }
        catch (WebView2RuntimeNotFoundException)
        {
            ShowFatal("The block editor needs the Microsoft Edge WebView2 Runtime, which isn't installed.\n\n" +
                      "Install it from https://developer.microsoft.com/microsoft-edge/webview2/ and open the editor again.");
            return;
        }
        catch (Exception e)
        {
            ShowFatal("The block editor couldn't start: " + e.Message);
            return;
        }

        CoreWebView2 core = webView.CoreWebView2;
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.AreBrowserAcceleratorKeysEnabled = false;
        core.Settings.IsZoomControlEnabled = false;

        core.AddWebResourceRequestedFilter(Origin + "/*", CoreWebView2WebResourceContext.All);
        core.WebResourceRequested += (s, e) => e.Response = ServeEmbedded(core.Environment, e.Request.Uri);

        // Only our page, ever. Links and pop-ups go nowhere.
        core.NavigationStarting += (s, e) =>
        {
            if (!e.Uri.StartsWith(Origin + "/", StringComparison.OrdinalIgnoreCase))
            {
                e.Cancel = true;
            }
        };
        core.NewWindowRequested += (s, e) => e.Handled = true;

        core.WebMessageReceived += (s, e) =>
        {
            string reply = bridge.Handle(e.WebMessageAsJson);
            if (reply != null)
            {
                core.PostWebMessageAsJson(reply);
            }
        };

        core.Navigate(Origin + "/index.html");
    }

    /// <summary>The share of the screen's usable area the editor opens at.</summary>
    private const double ScreenShare = 0.75;

    protected override void OnLoad(EventArgs e)
    {
        // 75% of the screen LiveSplit is on (not the taskbar), centred, never below the minimum.
        Rectangle area = Screen.FromControl(Owner ?? (Control)this).WorkingArea;
        int width = Math.Max(MinimumSize.Width, (int)(area.Width * ScreenShare));
        int height = Math.Max(MinimumSize.Height, (int)(area.Height * ScreenShare));
        width = Math.Min(width, area.Width);
        height = Math.Min(height, area.Height);
        Bounds = new Rectangle(
            area.Left + (area.Width - width) / 2,
            area.Top + (area.Height - height) / 2,
            width,
            height);

        base.OnLoad(e);
    }

    private void ShowFatal(string message)
    {
        MessageBox.Show(this, message, "Block editor", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        closingConfirmed = true;
        Close();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!closingConfirmed && bridge.HasUnsavedChanges)
        {
            DialogResult answer = MessageBox.Show(
                this,
                "Discard your changes to the comparisons?",
                "Block editor",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2);
            if (answer != DialogResult.Yes)
            {
                e.Cancel = true;
                return;
            }
        }

        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            webView.Dispose();
        }

        base.Dispose(disposing);
    }

    // ------------------------------------------------------------ Embedded files

    private static CoreWebView2WebResourceResponse ServeEmbedded(CoreWebView2Environment environment, string uri)
    {
        string path = Uri.TryCreate(uri, UriKind.Absolute, out Uri parsed) ? parsed.AbsolutePath.TrimStart('/') : "";
        if (path.Length == 0)
        {
            path = "index.html";
        }

        Stream resource = path.Contains("..") ? null : OpenResource(path);
        if (resource == null)
        {
            return environment.CreateWebResourceResponse(null, 404, "Not Found", "");
        }

        var buffer = new MemoryStream();
        using (resource)
        {
            resource.CopyTo(buffer);
        }

        buffer.Position = 0;
        string headers = $"Content-Type: {ContentType(path)}\r\nCache-Control: no-store";
        return environment.CreateWebResourceResponse(buffer, 200, "OK", headers);
    }

    /// <summary>Embedded resources are named "editor/" + the file's path under EditorDist.</summary>
    private static Stream OpenResource(string path)
    {
        Assembly assembly = typeof(EditorForm).Assembly;
        return assembly.GetManifestResourceStream("editor/" + path)
            ?? assembly.GetManifestResourceStream("editor/" + path.Replace('/', '\\'));
    }

    private static string ContentType(string path)
    {
        switch (Path.GetExtension(path).ToLowerInvariant())
        {
            case ".html": return "text/html; charset=utf-8";
            case ".js": return "text/javascript; charset=utf-8";
            case ".css": return "text/css; charset=utf-8";
            case ".json": return "application/json";
            case ".png": return "image/png";
            case ".svg": return "image/svg+xml";
            case ".gif": return "image/gif";
            case ".cur": return "image/x-icon";
            case ".mp3": return "audio/mpeg";
            case ".wav": return "audio/wav";
            case ".ogg": return "audio/ogg";
            default: return "application/octet-stream";
        }
    }
}
