using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace Hearth;

public static class Verification
{
    public static async Task<int> Run(string[] args)
    {
        var root = Path.Combine(Path.GetTempPath(), "Hearth-Verification-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root); var checks = new List<object>();
        var output = args.SkipWhile(a => a != "--report").Skip(1).FirstOrDefault() ?? Path.Combine(root, "report.json");
        object? live = null;
        void Check(string name, bool pass) { checks.Add(new { name, pass }); if (!pass) throw new Exception(name); }
        try
        {
            var store = new LocalStore(Path.Combine(root, "store"));
            store.SaveKey("verification-fake-key-only");
            Check("Windows DPAPI credential round trip", store.LoadKey() == "verification-fake-key-only");
            Check("Credential not stored in plaintext", !File.ReadAllText(Path.Combine(store.Root, "connection.bin")).Contains("verification-fake-key-only"));
            store.Disconnect();
            Check("Disconnect clears usable key and its previous backup", store.LoadKey() == null && File.ReadAllText(Path.Combine(store.Root, "connection.bin.bak")) == "");
            var chat = new Conversation { Title = "Recovery test", Turns = [new() { User = "Remember test", Answer = "Saved answer", Status = "done" }] };
            store.Save(chat); chat.Title = "New title"; store.Save(chat);
            Check("Chat survives application reload", store.LoadChats().Single().Turns.Single().Answer == "Saved answer");
            File.WriteAllText(Path.Combine(store.Root, "chats", chat.Id + ".json"), "invalid json");
            Check("Corrupt current chat recovers previous backup", store.LoadChats().Single().Title == "Recovery test");
            chat.Turns.Add(new() { User = "Interrupted request" }); store.Save(chat);
            Check("Interrupted turn marked, never silently restarted", store.LoadChats().Single().Turns.Last().Status == "interrupted");
            var scope = Path.Combine(root, "scope"); Directory.CreateDirectory(scope); File.WriteAllText(Path.Combine(scope, "hello.txt"), "fixture text");
            foreach (var input in new[] { "../escape.txt", "..\\escape.txt", "C:\\Windows\\win.ini", "hello.txt:secret", ".env", ".ssh/id_rsa" })
            {
                var denied = false; try { ToolBox.ResolveScopedPath(scope, input, false); } catch { denied = true; }
                Check("Scope rejects " + input, denied);
            }
            var deniedTool = new ToolBox(false, scope, (_, _) => false, _ => { }, CancellationToken.None);
            Check("Read selected text file", deniedTool.ReadText("hello.txt") == "fixture text");
            deniedTool.SaveText("denied.txt", "must not exist"); Check("Declined write creates no file", !File.Exists(Path.Combine(scope, "denied.txt")));
            var approvedTool = new ToolBox(false, scope, (_, _) => true, _ => { }, CancellationToken.None);
            approvedTool.SaveText("allowed.md", "approved content"); Check("Approved draft writes exact contents", File.ReadAllText(Path.Combine(scope, "allowed.md")) == "approved content");
            approvedTool.SaveText("allowed.md", "replacement");
            Check("Replacing a draft preserves prior contents in unique backup", Directory.GetFiles(scope, "allowed.md.*.bak").Any(p => File.ReadAllText(p) == "approved content") && File.ReadAllText(Path.Combine(scope, "allowed.md")) == "replacement");
            Check("Drive root scope remains absolute", ToolBox.ResolveScopedPath(Path.GetPathRoot(scope)!, ".") == Path.GetPathRoot(scope));
            approvedTool.SaveText("run.ps1", "bad"); Check("Executable file types cannot be written", !File.Exists(Path.Combine(scope, "run.ps1")));
            Check("Only approval-gated search before opting into files/computer", new ToolBox(false, null, (_, _) => true, _ => { }, CancellationToken.None).Tools().Count == 1);
            Check("Declined web search makes no request", (await deniedTool.WebSearch("example")).Contains("declined"));
            Check("Search JSON parsing", ToolBox.ParseSearch("{\"result\":{\"content\":[{\"text\":\"https://example.com verified fixture\"}]}}").Contains("https://example.com"));
            Check("Search SSE parsing", ToolBox.ParseSearch("event: message\ndata: {\"result\":{\"content\":[{\"text\":\"fixture SSE\"}]}}\n").Contains("fixture SSE"));
            deniedTool.EditText("hello.txt", "fixture", "changed");
            Check("Declined edit preserves original", File.ReadAllText(Path.Combine(scope, "hello.txt")) == "fixture text");
            approvedTool.EditText("hello.txt", "fixture", "changed");
            Check("Approved exact edit and backup", File.ReadAllText(Path.Combine(scope, "hello.txt")) == "changed text" && Directory.GetFiles(scope, "hello.txt.*.bak").Any(p => File.ReadAllText(p) == "fixture text"));
            approvedTool.CreateFolder("drafts"); approvedTool.TransferFile("hello.txt", "drafts/copy.txt");
            Check("Copy preserves both files", File.ReadAllText(Path.Combine(scope, "drafts/copy.txt")) == "changed text" && File.Exists(Path.Combine(scope, "hello.txt")));
            using (var zip = System.IO.Compression.ZipFile.Open(Path.Combine(scope, "example.docx"), System.IO.Compression.ZipArchiveMode.Create))
            using (var writer = new StreamWriter(zip.CreateEntry("word/document.xml").Open())) writer.Write("<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body><w:p><w:r><w:t>Known document text</w:t></w:r></w:p></w:body></w:document>");
            Check("Word extraction reads known fixture", approvedTool.ReadDocument("example.docx").Contains("Known document text"));
            if (args.Contains("--web-test")) Check("Live search returns official Python source", (await approvedTool.WebSearch("site:docs.python.org pathlib Path official documentation")).Contains("docs.python.org"));
            var longChat = new Conversation { Turns = Enumerable.Range(0, 10).Select(i => new Turn { User = new string('x', 20000), Answer = "answer", Status = "done" }).ToList() };
            var context = Corvex.Context(longChat, "system", out var included);
            Check("Context bounded on whole turns", included < 10 && context.Count == 1 + included * 2);
            var fake = new ScriptedClient(); using var harness = new FunctionInvokingChatClient(fake) { MaximumIterationsPerRequest = 3, AllowConcurrentInvocation = false };
            var response = await harness.GetResponseAsync([new(ChatRole.User, "fixture")], new ChatOptions { Tools = [AIFunctionFactory.Create(() => 42, "known_answer")] });
            Check("Open-source harness executes tool and carries result into follow-up", fake.SawResult && response.Text == "accepted 42");
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel(); var cancelSeen = false;
            try { await harness.GetResponseAsync([new(ChatRole.User, "cancel")], cancellationToken: cancelled.Token); } catch (OperationCanceledException) { cancelSeen = true; }
            Check("Cancellation reaches model client", cancelSeen);
            var recovery = new EmptyThenFinalClient();
            var recoveryTurn = new Turn { User = "fixture" }; var recoveryChat = new Conversation { Turns = [recoveryTurn] };
            var recovered = await Corvex.Run("fixture", new("fixture", "fixture", 1, 1, false), recoveryChat, recoveryTurn, new(false, null, (_, _) => false, _ => { }, CancellationToken.None), () => { }, CancellationToken.None, () => recovery);
            Check("Empty final text receives one bounded text-only continuation", recovered == "recovered final answer" && recovery.Calls == 2 && recovery.FinalToolsAbsent);
            Check("Recovery usage includes both requests", recoveryTurn.InputTokens == 6 && recoveryTurn.OutputTokens == 8);
            if (args.Contains("--live-test"))
            {
                var key = Environment.GetEnvironmentVariable("CORVEX_API_KEY", EnvironmentVariableTarget.User) ?? throw new Exception("No configured test credential.");
                var models = await Corvex.Models(key); var model = models.First(m => m.Id == "zai-org/GLM-5.2-FP8");
                var called = false;
                using var client = new FunctionInvokingChatClient(Corvex.Client(key, model.Id)) { MaximumIterationsPerRequest = 3, AllowConcurrentInvocation = false };
                using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
                var answer = await client.GetResponseAsync([new(ChatRole.User, "Call the known_answer tool once, then reply exactly with the number it returns. Do not guess the number.")], new ChatOptions { MaxOutputTokens = 8192, Tools = [AIFunctionFactory.Create(() => { called = true; return 73619; }, "known_answer", "Returns the test's secret fixture number.")] }, timeout.Token);
                live = new { provider = "Corvex", model = model.Id, called, toolAnswer = answer.Text, toolUsage = answer.Usage, finish = answer.FinishReason?.ToString(), contents = answer.Messages.SelectMany(m => m.Contents).Select(c => c.GetType().Name) };
                Check("Live Corvex GLM tool invocation and result in transcript", called && answer.Messages.SelectMany(m => m.Contents).OfType<FunctionResultContent>().Any(c => c.Result?.ToString() == "73619"));
                var followup = await Corvex.Client(key, model.Id).GetResponseAsync([new(ChatRole.User, "My fictional dog's name is Basil."), new(ChatRole.Assistant, "Your fictional dog is Basil."), new(ChatRole.User, "What is my fictional dog's name? Reply with just its name.")], new ChatOptions { MaxOutputTokens = 1024 }, timeout.Token);
                Check("Live multi-turn conversation context", followup.Text.Contains("Basil", StringComparison.OrdinalIgnoreCase));
                var appRuns = new List<object>();
                foreach (var listed in models)
                {
                    var prompt = new Turn { User = "Use read_text to read hello.txt in the selected folder. Reply with exactly its contents. Do not guess.", Model = listed.Id };
                    var conversation = new Conversation { Model = listed.Id, Turns = [prompt] };
                    var activities = new List<string>();
                    var packet = new ToolBox(false, scope, (_, _) => false, activities.Add, timeout.Token);
                    var text = await Corvex.Run(key, listed, conversation, prompt, packet, () => { }, timeout.Token);
                    appRuns.Add(new { model = listed.Id, answer = text, activities, prompt.InputTokens, prompt.OutputTokens, prompt.EstimatedCost });
                    Check("Production app harness reads fixture through " + listed.Id, activities.Contains("Reading hello.txt") && text.Contains("fixture text"));
                }
                live = new { provider = "Corvex", model = model.Id, toolAnswer = answer.Text, followup = followup.Text, toolUsage = answer.Usage, followupUsage = followup.Usage, models = models.Select(m => new { m.Id, m.Name }), appRuns };
            }
            File.WriteAllText(output, JsonSerializer.Serialize(new { passed = true, checks, live, fixtureRoot = root, timestamp = DateTimeOffset.UtcNow }, LocalStore.Json)); return 0;
        }
        catch (Exception e)
        {
            // Never include provider raw response bodies or credential-bearing exception strings.
            File.WriteAllText(output, JsonSerializer.Serialize(new { passed = false, checks, live, errorType = e.GetType().Name, fixtureRoot = root, timestamp = DateTimeOffset.UtcNow }, LocalStore.Json)); return 1;
        }
    }
    sealed class ScriptedClient : IChatClient
    {
        public bool SawResult;
        int calls;
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested(); calls++;
            if (calls == 1) return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("call1", "known_answer", new Dictionary<string, object?>())])));
            SawResult = messages.SelectMany(m => m.Contents).OfType<FunctionResultContent>().Any(c => c.Result?.ToString() == "42");
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "accepted 42")));
        }
        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        { await Task.CompletedTask; yield break; }
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }
    sealed class EmptyThenFinalClient : IChatClient
    {
        public int Calls; public bool FinalToolsAbsent;
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            Calls++; if (Calls > 2) throw new Exception("Too many recovery requests");
            if (Calls == 2) FinalToolsAbsent = options?.Tools == null || options.Tools.Count == 0;
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, Calls == 1 ? "" : "recovered final answer")) { Usage = new() { InputTokenCount = 3, OutputTokenCount = 4 } });
        }
        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default) { await Task.CompletedTask; yield break; }
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }
}
