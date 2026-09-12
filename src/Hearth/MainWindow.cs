using System.ClientModel;
using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Speech.Recognition;
using System.Text;
using System.Text.Json;
using Markdig;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace Hearth;

public sealed class MainWindow : Form
{
    readonly WebView2 web = new() { Dock = DockStyle.Fill };
    readonly LocalStore store;
    readonly string? uiReport;
    readonly MarkdownPipeline markdown = new MarkdownPipelineBuilder().DisableHtml().UsePipeTables().UseSoftlineBreakAsHardlineBreak().Build();
    readonly List<Conversation> chats;
    Preferences prefs;
    List<ModelInfo> models = [];
    string? key, selected, folder;
    bool ready, busy, computerAccess, connecting;
    CancellationTokenSource? run;
    SpeechRecognitionEngine? speech;
    string notice = "";
    string initializationStage = "created";
    string trustedDocument = "about:blank";
    System.Windows.Forms.Timer? watchdog;
    public MainWindow(string? uiReport = null)
    {
        this.uiReport = uiReport;
        store = new(uiReport == null ? null : Path.Combine(Path.GetTempPath(), "Hearth-UI-" + Guid.NewGuid().ToString("N")));
        Text = "Hearth · Corvex"; Width = 1180; Height = 800; MinimumSize = new Size(780, 560);
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        StartPosition = FormStartPosition.CenterScreen; BackColor = Color.FromArgb(250, 249, 246);
        if (uiReport != null) { StartPosition = FormStartPosition.Manual; Location = new Point(-20000, -20000); ShowInTaskbar = false; }
        Controls.Add(web); prefs = store.LoadPreferences(); key = store.LoadKey(); chats = store.LoadChats(); selected = chats.FirstOrDefault()?.Id;
        notice = string.Join(" ", store.Warnings); Shown += async (_, _) => await Initialize();
        if (uiReport != null)
        {
            watchdog = new System.Windows.Forms.Timer { Interval = 30000 };
            watchdog.Tick += (_, _) => { watchdog.Stop(); if (!File.Exists(uiReport)) { Directory.CreateDirectory(Path.GetDirectoryName(uiReport)!); File.WriteAllText(uiReport, JsonSerializer.Serialize(new { passed = false, initializationStage, error = "UI initialization timeout" })); Close(); } };
            watchdog.Start();
        }
        FormClosing += (_, e) =>
        {
            if (busy && MessageBox.Show(this, "A reply is still running. Stop it and close Hearth? Saved messages will remain.", "Close Hearth?", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) { e.Cancel = true; return; }
            run?.Cancel(); speech?.Dispose();
        };
    }
    async Task Initialize()
    {
        try
        {
            initializationStage = "creating WebView2 environment";
            var environment = await CoreWebView2Environment.CreateAsync(null, Path.Combine(store.Root, "webview"));
            initializationStage = "initializing WebView2 control";
            await web.EnsureCoreWebView2Async(environment);
            initializationStage = "configuring WebView2";
            web.CoreWebView2.Settings.AreDevToolsEnabled = false;
            web.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            web.CoreWebView2.Settings.IsStatusBarEnabled = false;
            web.CoreWebView2.Settings.AreHostObjectsAllowed = false;
            web.CoreWebView2.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;
            web.CoreWebView2.NewWindowRequested += (_, e) => { e.Handled = true; };
            web.CoreWebView2.NavigationStarting += (_, e) => { if (e.Uri != "about:blank" && e.Uri != trustedDocument) e.Cancel = true; };
            web.CoreWebView2.DownloadStarting += (_, e) => e.Cancel = true;
            web.CoreWebView2.WebMessageReceived += async (_, e) =>
            {
                initializationStage = "bridge message received";
                if (e.Source != "about:blank" && e.Source != trustedDocument) return;
                try { using var json = JsonDocument.Parse(e.WebMessageAsJson); await Dispatch(json.RootElement.Clone()); }
                catch { notice = "That action could not be completed. Your saved chats are unchanged."; SendState(); }
            };
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Hearth.app.html")!;
            var html = (await new StreamReader(stream).ReadToEndAsync()).Replace("\r\n", "\n");
            var script = html.Split("<script>")[1].Split("</script>")[0];
            html = html.Replace("SCRIPT_HASH", Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(script))));
            trustedDocument = "data:text/html;charset=utf-8;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(html));
            web.CoreWebView2.NavigateToString(html);
            initializationStage = "HTML loaded; awaiting bridge";
        }
        catch (Exception e)
        {
            if (uiReport != null) { Directory.CreateDirectory(Path.GetDirectoryName(uiReport)!); File.WriteAllText(uiReport, JsonSerializer.Serialize(new { passed = false, initializationStage, errorType = e.GetType().Name, detail = e.Message })); Close(); return; }
            if (e is not WebView2RuntimeNotFoundException) { MessageBox.Show(this, "Hearth could not start its interface. Your saved chats have been preserved. Try reopening it; if this repeats, share this error type with the person who sent you Hearth: " + e.GetType().Name, "Hearth could not start"); Close(); return; }
            var result = MessageBox.Show(this, "Hearth needs Microsoft Edge WebView2 Runtime, normally included with Windows. Install it from Microsoft, then reopen Hearth.\n\nOpen Microsoft's download page?", "One Windows component is missing", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
            if (result == DialogResult.Yes) Process.Start(new ProcessStartInfo("https://developer.microsoft.com/en-us/microsoft-edge/webview2/") { UseShellExecute = true });
            Close();
        }
    }
    bool Confirm(string title, string message)
    {
        if (InvokeRequired) return (bool)Invoke(() => Confirm(title, message));
        using var dialog = new Form { Text = title, Width = 650, Height = 470, StartPosition = FormStartPosition.CenterParent, MinimizeBox = false, MaximizeBox = false };
        var text = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, Dock = DockStyle.Fill, Text = message, Font = new Font("Segoe UI", 10) };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 60, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(12) };
        var no = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Width = 100, Height = 32 };
        var yes = new Button { Text = "Approve", DialogResult = DialogResult.OK, Width = 100, Height = 32 };
        buttons.Controls.Add(no); buttons.Controls.Add(yes); dialog.Controls.Add(text); dialog.Controls.Add(buttons);
        dialog.AcceptButton = no; dialog.CancelButton = no;
        return dialog.ShowDialog(this) == DialogResult.OK;
    }
    void Event(string type, object value)
    {
        if (IsDisposed || !ready) return;
        if (InvokeRequired) { BeginInvoke(() => Event(type, value)); return; }
        web.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new { type, value }));
    }
    void SendState()
    {
        if (InvokeRequired) { BeginInvoke(SendState); return; }
        var chat = chats.FirstOrDefault(c => c.Id == selected);
        Event("state", new
        {
            connected = key != null, busy, selected, notice, computerAccess, folder, theme = prefs.Theme,
            model = chat?.Model is { Length: > 0 } ? chat.Model : prefs.Model,
            models = models.Select(m => new { id = m.Id, name = m.Name, input = m.InputRate, output = m.OutputRate, vision = m.Vision }),
            chats = chats.OrderByDescending(c => c.Updated).Select(c => new { id = c.Id, title = c.Title, working = c.Turns.Any(t => t.Status == "working") }),
            chat = chat == null ? null : new
            {
                id = chat.Id, title = chat.Title,
                turns = chat.Turns.Select(t => new { user = t.User, answer = t.Answer, html = Markdown.ToHtml(t.Answer, markdown), status = t.Status, model = t.Model, activity = t.Activity, input = t.InputTokens, output = t.OutputTokens, estimate = t.EstimatedCost, started = t.Started })
            }
        });
    }
    async Task RefreshModels()
    {
        if (key == null) return;
        try
        {
            models = await Corvex.Models(key);
            if (models.Count == 0) throw new InvalidOperationException("No ready models were returned by Corvex.");
            if (!models.Any(m => m.Id == prefs.Model)) prefs.Model = models[0].Id;
            store.SavePreferences(prefs); notice = "Connected to Corvex. Chats are saved on this Windows account.";
        }
        catch (Exception e) { notice = e is InvalidOperationException ? e.Message : "Could not refresh models. Check your internet connection or try again later."; }
        SendState();
    }
    async Task Dispatch(JsonElement msg)
    {
        var action = msg.GetProperty("action").GetString();
        string Value(string name) => msg.TryGetProperty(name, out var value) ? value.GetString() ?? "" : "";
        if (action == "ready") { ready = true; SendState(); if (uiReport != null) await VerifyUi(); else await RefreshModels(); return; }
        if (action == "stop") { run?.Cancel(); return; }
        if (action == "select") { selected = chats.FirstOrDefault(c => c.Id == Value("id"))?.Id; SendState(); return; }
        if (action == "new") { selected = null; SendState(); return; }
        if (action == "openAccount") { OpenUrl(Corvex.AccountUrl); return; }
        if (action == "link") { OpenUrl(Value("url")); return; }
        if (action == "copy") { var text = Value("text"); if (text.Length <= 1000000) Clipboard.SetText(text.Length > 0 ? text : " "); return; }
        if (action == "theme") { prefs.Theme = prefs.Theme == "dark" ? "light" : "dark"; store.SavePreferences(prefs); SendState(); return; }
        if (busy) { notice = "Please stop the current reply before changing the connection or local access."; SendState(); return; }
        if (connecting) { notice = "Please wait for the Corvex connection check to finish."; SendState(); return; }
        switch (action)
        {
            case "connect":
                var candidate = Value("key").Trim();
                if (candidate.Length < 10 || candidate.Length > 2048 || candidate.Any(char.IsWhiteSpace)) { notice = "Paste the full API key from Corvex. It should not contain spaces."; Event("connectionFinished", false); break; }
                connecting = true; var connectionSucceeded = false;
                try
                {
                    var discovered = await Corvex.Models(candidate);
                    if (discovered.Count == 0) throw new InvalidOperationException("Corvex returned no ready models.");
                    models = discovered;
                    store.SaveKey(candidate); key = candidate; prefs.Model = models[0].Id; store.SavePreferences(prefs);
                    notice = "Connected. Your key is protected by your Windows account and never included in chat exports.";
                    connectionSucceeded = true;
                }
                catch (Exception e) { notice = e is InvalidOperationException ? e.Message : "Could not connect. Check your internet connection and API key."; }
                finally { connecting = false; }
                Event("connectionFinished", connectionSucceeded); break;
            case "disconnect": key = null; models.Clear(); store.Disconnect(); notice = "Disconnected. Your saved chats remain on this computer."; break;
            case "refresh": await RefreshModels(); return;
            case "model":
                if (models.Any(m => m.Id == Value("model"))) { prefs.Model = Value("model"); store.SavePreferences(prefs); var c = chats.FirstOrDefault(c => c.Id == selected); if (c != null) { c.Model = prefs.Model; store.Save(c); } } break;
            case "computer":
                computerAccess = !computerAccess && Confirm("Allow computer diagnostics?", "When you ask for computer help, Hearth can send Corvex basic Windows version, CPU/memory use, disk free space and app names/process IDs. It does not read browsing history or passwords.\n\nEach request to close an app requires a separate confirmation. Access lasts only until Hearth closes or you turn it off."); break;
            case "folder":
                if (folder != null) { folder = null; break; }
                using (var picker = new FolderBrowserDialog { Description = "Choose the folder Hearth may read. Text from this folder can be sent to Corvex when used.", UseDescriptionForTitle = true })
                    if (picker.ShowDialog(this) == DialogResult.OK)
                    {
                        ToolBox.ResolveScopedPath(picker.SelectedPath, ".");
                        if (Confirm("Use this folder?", picker.SelectedPath + "\n\nHearth can list names and read small text files in this folder and its ordinary subfolders. Selected content is sent to Corvex when used. Saving drafts requires approval. Avoid folders containing passwords or sensitive records.")) folder = picker.SelectedPath;
                    }
                break;
            case "send": await Send(Value("text"), false); return;
            case "retry": await Send("", true); return;
            case "rename":
                var rename = chats.FirstOrDefault(c => c.Id == selected); var title = Value("title").Trim();
                if (rename != null && title.Length is > 0 and <= 100) { rename.Title = title; store.Save(rename); } break;
            case "archive":
                var archive = chats.FirstOrDefault(c => c.Id == selected);
                if (archive != null && Confirm("Archive this conversation?", "It will leave the sidebar, but the file remains in Hearth's local archived folder.")) { store.Archive(archive); chats.Remove(archive); selected = null; } break;
            case "export": Export(); break;
            case "speech": ToggleSpeech(); break;
            case "dataFolder": Process.Start(new ProcessStartInfo("explorer.exe", store.Root) { UseShellExecute = true }); break;
        }
        SendState();
    }
    void OpenUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https" || !string.IsNullOrEmpty(uri.UserInfo)) return;
        Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
    }
    async Task Send(string text, bool retry)
    {
        if (key == null || models.Count == 0) { notice = "Connect your Corvex account first."; SendState(); return; }
        var chat = chats.FirstOrDefault(c => c.Id == selected);
        if (retry && (chat?.Turns.LastOrDefault()?.Status is not ("error" or "stopped" or "interrupted"))) return;
        text = text.Trim();
        if (!retry && (text.Length == 0 || text.Length > 30000)) { notice = "Write a message of up to 30,000 characters."; SendState(); return; }
        if (chat == null) { chat = new() { Title = text.Length > 48 ? text[..48] + "…" : text, Model = prefs.Model }; chats.Insert(0, chat); selected = chat.Id; }
        var model = models.FirstOrDefault(m => m.Id == chat.Model);
        if (model == null) { notice = "This model is no longer in the live catalogue. Choose an available model above."; SendState(); return; }
        var turn = retry ? chat.Turns.Last() : new Turn { User = text };
        if (!retry) chat.Turns.Add(turn);
        else { turn.Answer = ""; turn.Activity.Add("Retry requested. Previous attempts may still have incurred Corvex charges."); }
        turn.Model = model.Name; turn.Status = "working"; turn.Started = DateTimeOffset.Now;
        turn.InputTokens = null; turn.OutputTokens = null; turn.EstimatedCost = null;
        chat.Updated = DateTimeOffset.Now; store.Save(chat); busy = true; notice = "";
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(10)); run = cancellation;
        var workingChat = chat;
        void Activity(string item)
        {
            void Update() { turn.Activity.Add(item); store.Save(workingChat); SendState(); }
            if (InvokeRequired) Invoke(Update); else Update();
        }
        var tools = new ToolBox(computerAccess, folder, Confirm, Activity, cancellation.Token);
        SendState(); Event("sent", true);
        try
        {
            turn.Answer = await Task.Run(() => Corvex.Run(key, model, workingChat, turn, tools, () => { }, cancellation.Token));
            turn.Status = "done";
        }
        catch (OperationCanceledException) { turn.Status = "stopped"; turn.Activity.Add("Stopped or reached the ten-minute limit. Completed tool actions remain in effect. Corvex may bill work already performed; usage is unknown."); }
        catch (ClientResultException e) { turn.Status = "error"; turn.Activity.Add(Corvex.FriendlyStatus(e.Status)); }
        catch { turn.Status = "error"; turn.Activity.Add("The reply could not finish. Your message is saved. Check the connection, then choose Retry. Usage for this attempt is unknown."); }
        finally { busy = false; run = null; store.Save(chat); SendState(); }
    }
    void Export()
    {
        var chat = chats.FirstOrDefault(c => c.Id == selected); if (chat == null) return;
        using var picker = new SaveFileDialog { Filter = "Markdown conversation (*.md)|*.md", FileName = "Hearth conversation.md", OverwritePrompt = true };
        if (picker.ShowDialog(this) != DialogResult.OK) return;
        var body = "# " + chat.Title + "\n\n" + string.Join("\n\n---\n\n", chat.Turns.Select(t => "## You\n\n" + t.User + "\n\n## Hearth · " + t.Model + "\n\n" + t.Answer + "\n\nStatus: " + t.Status + "\n\n" + string.Join("\n", t.Activity.Select(x => "- " + x))));
        File.WriteAllText(picker.FileName, body); notice = "Conversation exported. No API key was included.";
    }
    void ToggleSpeech()
    {
        if (speech != null) { speech.RecognizeAsyncCancel(); speech.Dispose(); speech = null; Event("listening", false); return; }
        try
        {
            var recognizer = SpeechRecognitionEngine.InstalledRecognizers().FirstOrDefault(r => r.Culture.TwoLetterISOLanguageName == "en") ?? SpeechRecognitionEngine.InstalledRecognizers().FirstOrDefault();
            if (recognizer == null) throw new InvalidOperationException();
            speech = new SpeechRecognitionEngine(recognizer);
            speech.LoadGrammar(new DictationGrammar()); speech.SetInputToDefaultAudioDevice();
            speech.SpeechRecognized += (_, e) => { if (e.Result.Confidence >= .3) Event("dictation", e.Result.Text); };
            speech.RecognizeAsync(RecognizeMode.Multiple); Event("listening", true);
            notice = "Listening using Windows' installed speech recognizer. Click the microphone again to stop. Review the text before sending.";
        }
        catch { speech?.Dispose(); speech = null; notice = "Windows offline dictation is not available here. You can type, or click the message box and press Windows + H for Windows voice typing (which may use Microsoft's online speech service)."; }
    }
    async Task VerifyUi()
    {
        var checks = new List<object>(); var dir = Path.GetDirectoryName(uiReport!)!; Directory.CreateDirectory(dir);
        async Task Check(string name, string expression)
        {
            var result = await web.CoreWebView2.ExecuteScriptAsync(expression);
            var pass = result == "true"; checks.Add(new { name, pass }); if (!pass) throw new InvalidOperationException(name);
        }
        async Task Capture(string name) { using var file = File.Create(Path.Combine(dir, name + ".png")); await web.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, file); }
        try
        {
            await Task.Delay(500);
            await Check("Embedded CSP permits app and native bridge", "document.getElementById('setup').open === true");
            await Capture("01-setup");
            await web.CoreWebView2.ExecuteScriptAsync("document.getElementById('later').click()");
            await Check("Setup Later action", "!document.getElementById('setup').open");
            key = "ui-fixture-not-a-real-credential";
            models = [new("fixture/GLM", "GLM 5.2 FP8 · preview", .75m, 2.4m, false)]; prefs.Model = models[0].Id;
            SendState(); await Task.Delay(250); await Capture("02-welcome");
            await web.CoreWebView2.ExecuteScriptAsync("document.querySelector('[data-prompt]').click()");
            await Check("Starter suggestion populates composer", "document.getElementById('message').value.includes('friendly email')");
            var chat = new Conversation { Title = "A little help with my computer", Model = prefs.Model, Turns = [new() { User = "My laptop feels slow. Can you help me understand why?", Model = "GLM 5.2 FP8", Status = "done", Answer = "## Let’s take it one step at a time\n\nI can check **memory, CPU usage and free disk space**, then help you decide what to do.\n\n1. Turn on **Computer help** below.\n2. Ask me to check again.\n3. I’ll explain the findings before suggesting changes.\n\nYour programs will stay open unless you approve a close request.", Activity = ["No diagnostics accessed: Computer help is off."], InputTokens = 345, OutputTokens = 108, EstimatedCost = .0005m }] };
            chats.Add(chat); selected = chat.Id; store.Save(chat); SendState(); await Task.Delay(250);
            await Check("Markdown and saved conversation render", "document.querySelector('.answer strong').textContent.includes('memory') && document.querySelectorAll('.chatlink').length === 1");
            await Capture("03-chat");
            await web.CoreWebView2.ExecuteScriptAsync("document.getElementById('theme').click()"); await Task.Delay(250);
            await Check("Theme action crosses native bridge", "document.body.classList.contains('dark')"); await Capture("04-dark");
            Width = 800; Height = 580; await Task.Delay(300); await Capture("05-compact");
            await Check("Compact layout does not overflow page width", "document.documentElement.scrollWidth <= window.innerWidth");
            await web.CoreWebView2.ExecuteScriptAsync("document.getElementById('new').click()"); await Task.Delay(200);
            await Check("New conversation action", "document.querySelector('.welcome') !== null");
            File.WriteAllText(uiReport!, JsonSerializer.Serialize(new { passed = true, checks, dataRoot = store.Root }, LocalStore.Json));
        }
        catch (Exception e) { File.WriteAllText(uiReport!, JsonSerializer.Serialize(new { passed = false, checks, error = e.GetType().Name }, LocalStore.Json)); }
        finally { key = null; BeginInvoke(Close); }
    }
}
