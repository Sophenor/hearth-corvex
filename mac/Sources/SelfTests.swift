import Foundation

@MainActor enum SelfTests {
    static func run(_ report: String) async -> Bool {
        var checks: [JSONObject] = []
        func check(_ name: String, _ passed: Bool) throws { checks.append(["name": name, "pass": passed]); if !passed { throw HearthError.message(name) } }
        let root = FileManager.default.temporaryDirectory.appendingPathComponent("Hearth-tests-" + UUID().uuidString)
        let keys = KeyStore(service: "com.sophenor.hearth.fixture." + UUID().uuidString)
        defer { try? keys.disconnect() }
        var passed = false; var failure = ""
        do {
            let store = try Store(root: root.appendingPathComponent("store"))
            try keys.save("fixture-key-one"); try check("Keychain round trip", try keys.load() == "fixture-key-one")
            try keys.save("fixture-key-two"); try check("Keychain updates existing connection", try keys.load() == "fixture-key-two")
            try keys.disconnect(); try check("Keychain disconnect", try keys.load() == nil)
            let chat = Conversation(title: "Original", model: "fixture"); let turn = Turn(user: "Remember this", model: "fixture"); turn.answer = "Saved answer"; turn.status = "done"; chat.turns = [turn]
            try store.save(chat); chat.title = "Updated"; try store.save(chat)
            try check("Saved chat reload", store.loadChats().first?.turns.first?.answer == "Saved answer")
            try Data("broken".utf8).write(to: store.path(chat.id)); try check("Chat backup recovery", store.loadChats().first?.title == "Original")
            turn.status = "working"; try store.save(chat); try check("Interrupted reply does not restart", store.loadChats().first?.turns.first?.status == "interrupted")
            try store.archive(chat); try check("Archive preserves file and removes sidebar entry", store.loadChats().isEmpty && FileManager.default.fileExists(atPath: root.appendingPathComponent("store/archived/" + chat.id + ".json").path))
            let scope = root.appendingPathComponent("scope"); try FileManager.default.createDirectory(at: scope, withIntermediateDirectories: true)
            try Data("fixture text".utf8).write(to: scope.appendingPathComponent("hello.txt"))
            for input in ["../escape.txt", "/etc/passwd", "~/private.txt", "x:secret", "..\\escape", ".env", ".ssh/key", ".git/config"] {
                var denied = false; do { _ = try Scope.resolve(scope, input, mustExist: false) } catch { denied = true }; try check("Scope rejects \(input)", denied)
            }
            try Data("private fixture".utf8).write(to: root.appendingPathComponent("private.txt"))
            try FileManager.default.createSymbolicLink(at: scope.appendingPathComponent("linked.txt"), withDestinationURL: root.appendingPathComponent("private.txt"))
            var linkedDenied = false; do { _ = try Scope.resolve(scope, "linked.txt") } catch { linkedDenied = true }; try check("Symlink escape blocked", linkedDenied)
            let no = Tools(computer: false, folder: scope, approve: { _, _ in false }, activity: { _ in })
            try check("Selected text read", try no.invoke("read_text", ["relativePath": "hello.txt"]) == "fixture text")
            _ = try no.invoke("save_text", ["relativePath": "denied.txt", "content": "not written"]); try check("Declined save creates no file", !FileManager.default.fileExists(atPath: scope.appendingPathComponent("denied.txt").path))
            let yes = Tools(computer: false, folder: scope, approve: { _, _ in true }, activity: { _ in })
            _ = try yes.invoke("save_text", ["relativePath": "allowed.md", "content": "approved"]); try check("Approved save writes exact text", try String(contentsOf: scope.appendingPathComponent("allowed.md"), encoding: .utf8) == "approved")
            _ = try yes.invoke("save_text", ["relativePath": "allowed.md", "content": "replacement"])
            let backups = try FileManager.default.contentsOfDirectory(at: scope, includingPropertiesForKeys: nil).filter { $0.lastPathComponent.hasPrefix("allowed.md.") && $0.pathExtension == "bak" }
            try check("Replacing a file preserves its previous version", backups.count == 1 && (try? String(contentsOf: backups[0], encoding: .utf8)) == "approved")
            var executableDenied = false; do { _ = try yes.invoke("save_text", ["relativePath": "run.sh", "content": "exit 0"]) } catch { executableDenied = true }; try check("Executable save rejected", executableDenied)
            try Data(repeating: 65, count: 100001).write(to: scope.appendingPathComponent("large.txt")); var largeDenied = false; do { _ = try no.invoke("read_text", ["relativePath": "large.txt"]) } catch { largeDenied = true }; try check("Large file rejected", largeDenied)
            let none = Tools(computer: false, folder: nil, approve: { _, _ in true }, activity: { _ in }); try check("No tools before opt-in", none.schemas.isEmpty)
            var permissionDenied = false; do { _ = try none.invoke("computer_health", [:]) } catch { permissionDenied = true }; try check("Tool permission enforced in native code", permissionDenied)
            let long = Conversation(title: "long", model: "fixture"); for _ in 0..<10 { long.turns.append(Turn(user: String(repeating: "x", count: 20000), model: "fixture")) }; try check("History context is bounded", Agent.context(long).count == 5)
            let model = Model(id: "fixture", name: "fixture", input: 1, output: 1)
            func response(_ message: JSONObject) -> JSONObject { ["choices": [["message": message]], "usage": ["prompt_tokens": 3, "completion_tokens": 4]] }
            func conversation() -> (Conversation, Turn) { let c = Conversation(title: "Fixture", model: "fixture"); let t = Turn(user: "Read hello.txt", model: "fixture"); c.turns = [t]; return (c, t) }
            let (c, t) = conversation(); var requests = 0; var sawToolResult = false; var sawReasoning = false
            let answer = try await Agent.run(chat: c, turn: t, model: model, tools: no, transport: { body in
                requests += 1; let messages = body["messages"] as? [JSONObject] ?? []
                if requests == 1 { return response(["role": "assistant", "content": NSNull(), "reasoning_content": "Fixture reasoning", "tool_calls": [["id": "read1", "type": "function", "function": ["name": "read_text", "arguments": "{\"relativePath\":\"hello.txt\"}"]]]]) }
                sawToolResult = messages.last?["content"] as? String == "fixture text" && messages.last?["tool_call_id"] as? String == "read1"
                sawReasoning = messages.contains { $0["reasoning_content"] as? String == "Fixture reasoning" }
                return response(["role": "assistant", "content": "fixture text"])
            }, changed: {})
            try check("Harness executes tool and returns answer", requests == 2 && sawToolResult && answer == "fixture text")
            try check("Reasoning fields retained between tool steps", sawReasoning)
            try check("Usage aggregates all tool-loop requests", t.input == 6 && t.output == 8 && t.estimate != nil)
            let (blankChat, blankTurn) = conversation(); var blankCalls = 0; var noMoreTools = false
            let recovered = try await Agent.run(chat: blankChat, turn: blankTurn, model: model, tools: no, transport: { body in blankCalls += 1; if blankCalls == 2 { noMoreTools = body["tools"] == nil }; return response(["content": blankCalls == 1 ? "" : "Final text"]) }, changed: {})
            try check("Blank response gets one text-only continuation", blankCalls == 2 && recovered == "Final text" && noMoreTools)
            let (loopChat, loopTurn) = conversation(); var loopCalls = 0; var reads = 0
            let loopTools = Tools(computer: false, folder: scope, approve: { _, _ in false }, activity: { _ in reads += 1 })
            let loopAnswer = try await Agent.run(chat: loopChat, turn: loopTurn, model: model, tools: loopTools, transport: { _ in loopCalls += 1; return response(["content": NSNull(), "tool_calls": [["id": "call\(loopCalls)", "function": ["name": "read_text", "arguments": "{\"relativePath\":\"hello.txt\"}"]]]]) }, changed: {})
            try check("Runaway tool loop bounded", loopCalls == 9 && reads == 8 && loopAnswer.contains("limit"))
            let cancelled = Task { let (c, t) = conversation(); return try await Agent.run(chat: c, turn: t, model: model, tools: none, transport: { _ in throw HearthError.message("Should not call provider") }, changed: {}) }; cancelled.cancel(); var cancellationSeen = false; do { _ = try await cancelled.value } catch is CancellationError { cancellationSeen = true }; try check("Cancellation stops before provider request", cancellationSeen)
            let health = Tools(computer: true, folder: nil, approve: { _, _ in false }, activity: { _ in }); let summary = try health.invoke("computer_health", [:]); let info = try JSONSerialization.jsonObject(with: Data(summary.utf8)) as? JSONObject
            try check("Real macOS read-only diagnostics", (info?["physicalMemoryGB"] as? Double ?? 0) > 0 && info?["apps"] is [JSONObject])
            var protected = false; do { _ = try health.invoke("request_close_app", ["processId": Int(ProcessInfo.processInfo.processIdentifier)]) } catch { protected = true }; try check("Cannot close Hearth itself", protected)
            passed = true
        } catch { failure = (error as? HearthError)?.localizedDescription ?? String(describing: type(of: error)) }
        let file = URL(fileURLWithPath: report); try? FileManager.default.createDirectory(at: file.deletingLastPathComponent(), withIntermediateDirectories: true)
        try? jsonData(["passed": passed, "checks": checks, "failure": failure, "architecture": ProcessInfo.processInfo.environment["RUNNER_ARCH"] ?? "local", "timestamp": isoNow()]).write(to: file)
        return passed
    }
}
