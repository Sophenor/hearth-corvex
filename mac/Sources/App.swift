import AppKit
import WebKit
import Foundation

@MainActor final class AppController: NSObject, NSApplicationDelegate, NSWindowDelegate, WKScriptMessageHandler, WKNavigationDelegate, WKUIDelegate {
    var window: NSWindow!; var web: WKWebView!; var uiURL: URL!
    var store: Store!; var keys = KeyStore(); var preferences = Preferences()
    var chats: [Conversation] = []; var models: [Model] = []; var key: String?; var selected: String?
    var folder: URL?; var computer = false; var ready = false; var busy = false; var connecting = false
    var notice = ""; var job: Task<Void, Never>?; var watchdog: Task<Void, Never>?
    let args = CommandLine.arguments
    var uiTest: Bool { args.contains("--ui-test") }
    var reportPath: String { if let i = args.firstIndex(of: "--report"), i + 1 < args.count { return args[i + 1] }; return FileManager.default.temporaryDirectory.appendingPathComponent("Hearth-report.json").path }
    func applicationDidFinishLaunching(_ notification: Notification) {
        if args.contains("--self-test") {
            Task { let passed = await SelfTests.run(reportPath); exit(passed ? 0 : 1) }; return
        }
        do {
            let fixture = uiTest ? FileManager.default.temporaryDirectory.appendingPathComponent("Hearth-UI-" + UUID().uuidString) : nil
            store = try Store(root: fixture); chats = store.loadChats(); preferences = store.loadPreferences(); selected = chats.first?.id
            if !uiTest { do { key = try keys.load() } catch { notice = error.localizedDescription } }
            notice += store.warnings.joined(separator: " ")
            installMenu()
            let config = WKWebViewConfiguration(); config.websiteDataStore = .nonPersistent(); config.preferences.javaScriptCanOpenWindowsAutomatically = false
            config.userContentController.add(self, name: "hearth")
            web = WKWebView(frame: .zero, configuration: config); web.navigationDelegate = self; web.uiDelegate = self
            if #available(macOS 13.3, *) { web.isInspectable = false }
            window = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 1180, height: 800), styleMask: [.titled, .closable, .miniaturizable, .resizable], backing: .buffered, defer: false)
            window.title = "Hearth · Corvex"; window.minSize = NSSize(width: 780, height: 560); window.delegate = self
            window.contentView = web; window.center(); window.isReleasedWhenClosed = false
            guard let url = Bundle.main.url(forResource: "app", withExtension: "html") else { throw HearthError.message("The app resources are missing. Download Hearth again from its GitHub release.") }; uiURL = url
            if uiTest { window.setFrameOrigin(NSPoint(x: -20000, y: -20000)); window.orderBack(nil) }
            else { window.makeKeyAndOrderFront(nil); NSApp.activate(ignoringOtherApps: true) }
            web.loadFileURL(url, allowingReadAccessTo: url.deletingLastPathComponent())
            if uiTest { watchdog = Task { try? await Task.sleep(nanoseconds: 60_000_000_000); if !Task.isCancelled { writeUITestFailure("UI startup timed out"); exit(1) } } }
        } catch { if uiTest { writeUITestFailure(error.localizedDescription); exit(1) }; let alert = NSAlert(); alert.messageText = "Hearth could not start"; alert.informativeText = error.localizedDescription; alert.runModal(); NSApp.terminate(nil) }
    }
    func installMenu() {
        let bar = NSMenu(); let appItem = NSMenuItem(); let appMenu = NSMenu(); appMenu.addItem(withTitle: "About Hearth", action: #selector(about), keyEquivalent: "").target = self; appMenu.addItem(.separator()); appMenu.addItem(withTitle: "Quit Hearth", action: #selector(NSApplication.terminate(_:)), keyEquivalent: "q"); appItem.submenu = appMenu; bar.addItem(appItem)
        let editItem = NSMenuItem(); editItem.title = "Edit"; let edit = NSMenu(title: "Edit")
        for (name, action, key) in [("Undo", "undo:", "z"), ("Redo", "redo:", "Z"), ("Cut", "cut:", "x"), ("Copy", "copy:", "c"), ("Paste", "paste:", "v"), ("Select All", "selectAll:", "a")] { edit.addItem(withTitle: name, action: NSSelectorFromString(action), keyEquivalent: key) }
        edit.addItem(.separator()); edit.addItem(withTitle: "Start Dictation", action: NSSelectorFromString("startDictation:"), keyEquivalent: ""); editItem.submenu = edit; bar.addItem(editItem); NSApp.mainMenu = bar
    }
    @objc func about() { let alert = NSAlert(); alert.messageText = "Hearth for Corvex · Mac preview 0.1"; alert.informativeText = "An independent assistant using your Corvex account. Not Developer-ID signed or notarized. App licence: MIT. Markdown: Marked 18.0.13 (MIT); HTML sanitization: DOMPurify 3.4.15 (Apache-2.0 or MPL-2.0). Full licences are inside Hearth.app/Contents/Resources/licenses."; alert.runModal() }
    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool { true }
    func applicationShouldTerminate(_ sender: NSApplication) -> NSApplication.TerminateReply { if busy && !approve("Stop and quit Hearth?", "A reply is still running. Your saved messages will remain, but the current reply will stop. Completed tool actions remain in effect.") { return .terminateCancel }; job?.cancel(); return .terminateNow }
    func windowShouldClose(_ sender: NSWindow) -> Bool { if busy && !approve("Stop and close Hearth?", "A reply is still running. Your saved messages will remain; completed tool actions are not undone.") { return false }; busy = false; job?.cancel(); return true }
    func approve(_ title: String, _ content: String) -> Bool {
        if uiTest { return false }
        let alert = NSAlert(); alert.messageText = title; alert.informativeText = "Review the request below. Nothing will change unless you approve."
        let cancel = alert.addButton(withTitle: "Cancel"); cancel.keyEquivalent = "\r"; let yes = alert.addButton(withTitle: "Approve"); yes.keyEquivalent = ""
        let scroll = NSScrollView(frame: NSRect(x: 0, y: 0, width: 560, height: 280)); scroll.hasVerticalScroller = true
        let text = NSTextView(frame: scroll.bounds); text.string = content; text.isEditable = false; text.isSelectable = true; text.font = NSFont.systemFont(ofSize: 13); text.isVerticallyResizable = true; text.autoresizingMask = [.width]; text.textContainer?.widthTracksTextView = true; scroll.documentView = text; alert.accessoryView = scroll
        return alert.runModal() == .alertSecondButtonReturn
    }
    func webView(_ webView: WKWebView, decidePolicyFor navigationAction: WKNavigationAction, decisionHandler: @escaping (WKNavigationActionPolicy) -> Void) {
        let url = navigationAction.request.url
        decisionHandler(url?.isFileURL == true && url?.standardizedFileURL.path == uiURL?.standardizedFileURL.path && navigationAction.targetFrame?.isMainFrame != false ? .allow : .cancel)
    }
    func webView(_ webView: WKWebView, createWebViewWith configuration: WKWebViewConfiguration, for navigationAction: WKNavigationAction, windowFeatures: WKWindowFeatures) -> WKWebView? { nil }
    func webView(_ webView: WKWebView, requestMediaCapturePermissionFor origin: WKSecurityOrigin, initiatedByFrame frame: WKFrameInfo, type: WKMediaCaptureType, decisionHandler: @escaping (WKPermissionDecision) -> Void) { decisionHandler(.deny) }
    func userContentController(_ userContentController: WKUserContentController, didReceive message: WKScriptMessage) {
        guard message.frameInfo.isMainFrame, message.frameInfo.request.url?.standardizedFileURL.path == uiURL?.standardizedFileURL.path, let body = message.body as? JSONObject, let action = body["action"] as? String else { return }
        Task { await dispatch(action, body) }
    }
    func event(_ type: String, _ value: Any) {
        guard ready, let json = try? jsonString(["type": type, "value": value]) else { return }
        web.evaluateJavaScript("window.hearthReceive(\(json))", completionHandler: nil)
    }
    func state() {
        let chat = chats.first { $0.id == selected }
        event("state", ["connected": key != nil, "busy": busy, "selected": optionalJSON(selected), "notice": notice, "computerAccess": computer, "folder": optionalJSON(folder?.path), "theme": preferences.theme, "model": chat?.model ?? preferences.model, "models": models.map(\.view), "chats": chats.sorted { $0.updated > $1.updated }.map { ["id": $0.id, "title": $0.title, "working": $0.turns.contains { $0.status == "working" }] as JSONObject }, "chat": chat?.view as Any? ?? NSNull()])
    }
    func refresh() async {
        guard let key = key else { return }; connecting = true; defer { connecting = false; state() }
        do { let values = try await Corvex.models(key); guard !values.isEmpty else { throw HearthError.message("Corvex returned no ready models.") }; models = values; if !models.contains(where: { $0.id == preferences.model }) { preferences.model = values[0].id }; try store.savePreferences(preferences); notice = "Connected to Corvex. Your chats are saved on this Mac." }
        catch { notice = friendly(error) }
    }
    func friendly(_ error: Error) -> String { (error as? HearthError)?.localizedDescription ?? "That request could not finish. Check your connection or selected file and try again. Your saved messages remain." }
    func dispatch(_ action: String, _ body: JSONObject) async {
        func value(_ field: String) -> String { body[field] as? String ?? "" }
        if action == "ready" { guard !ready else { return }; ready = true; state(); if uiTest { await verifyUI() } else { await refresh() }; return }
        if action == "stop" { job?.cancel(); return }
        if action == "new" { selected = nil; state(); return }
        if action == "select" { selected = chats.first { $0.id == value("id") }?.id; state(); return }
        if action == "openAccount" { NSWorkspace.shared.open(Corvex.accountURL); return }
        if action == "link" { if let url = URL(string: value("url")), url.scheme == "https", url.user == nil, url.password == nil { NSWorkspace.shared.open(url) }; return }
        if action == "copy" { let text = value("text"); if text.utf8.count <= 1_000_000 { NSPasteboard.general.clearContents(); NSPasteboard.general.setString(text, forType: .string) }; return }
        if busy || connecting { notice = "Please wait or stop the current reply before changing the connection or permissions."; state(); return }
        do {
            switch action {
            case "connect":
                let candidate = value("key").trimmingCharacters(in: .whitespacesAndNewlines)
                guard (10...2048).contains(candidate.count), !candidate.contains(where: \.isWhitespace) else { notice = "Paste the full API key from Corvex, without spaces."; event("connectionFinished", false); state(); return }
                connecting = true
                do { let available = try await Corvex.models(candidate); guard !available.isEmpty else { throw HearthError.message("Corvex returned no ready models.") }; try keys.save(candidate); key = candidate; models = available; preferences.model = available[0].id; try store.savePreferences(preferences); notice = "Connected. Your key is stored in macOS Keychain and is not included in chat exports."; event("connectionFinished", true) }
                catch { notice = friendly(error); event("connectionFinished", false) }; connecting = false
            case "disconnect": try keys.disconnect(); key = nil; models = []; notice = "Disconnected. Your saved conversations remain on this Mac."
            case "refresh": await refresh(); return
            case "theme": preferences.theme = preferences.theme == "dark" ? "light" : "dark"; try store.savePreferences(preferences)
            case "model": if models.contains(where: { $0.id == value("model") }) { preferences.model = value("model"); try store.savePreferences(preferences); if let chat = chats.first(where: { $0.id == selected }) { chat.model = preferences.model; try store.save(chat) } }
            case "computer": computer = !computer && approve("Allow computer help?", "When you ask for computer help, Hearth can send Corvex basic Mac version, physical memory, CPU load, disk space, and visible app names and resource use. It does not read browsing history or passwords.\n\nEach request to quit an app needs separate approval. Access resets when you close Hearth.")
            case "folder":
                if folder != nil { folder = nil; break }
                let panel = NSOpenPanel(); panel.canChooseDirectories = true; panel.canChooseFiles = false; panel.allowsMultipleSelection = false; panel.message = "Choose one folder Hearth may read. Selected text can be sent to Corvex."
                if panel.runModal() == .OK, let url = panel.url { _ = try Scope.resolve(url, "."); if approve("Use this folder?", url.path + "\n\nHearth can list names and read small text files in this folder and ordinary subfolders. Their contents may be sent to Corvex. Avoid folders containing secrets or sensitive records. Saving a draft requires separate approval.") { folder = url } }
            case "send": try start(value("text"), retry: false); return
            case "retry": try start("", retry: true); return
            case "rename": let title = value("title").trimmingCharacters(in: .whitespacesAndNewlines); if (1...100).contains(title.count), let chat = chats.first(where: { $0.id == selected }) { chat.title = title; try store.save(chat) }
            case "archive": if let chat = chats.first(where: { $0.id == selected }), approve("Archive conversation?", "The conversation will leave the sidebar, but its files remain in Hearth's local archived folder.") { try store.archive(chat); chats.removeAll { $0.id == chat.id }; selected = nil }
            case "export": try export()
            case "dataFolder": NSWorkspace.shared.open(store.root)
            case "speech":
                web.evaluateJavaScript("document.getElementById('message').focus()") { [weak self] _, _ in
                    guard let self = self else { return }; _ = NSApp.sendAction(NSSelectorFromString("startDictation:"), to: nil, from: self)
                }
                notice = "Use your Mac's dictation shortcut after enabling Dictation in System Settings → Keyboard. Check the words before sending. Depending on your Mac and settings, Apple may process the audio."
            default: break
            }
        } catch { notice = friendly(error) }
        state()
    }
    func start(_ original: String, retry: Bool) throws {
        guard let connection = key, !models.isEmpty else { notice = "Connect your Corvex account first."; state(); return }
        var chat = chats.first { $0.id == selected }; let text = original.trimmingCharacters(in: .whitespacesAndNewlines)
        if retry { guard let last = chat?.turns.last, ["error", "stopped", "interrupted"].contains(last.status) else { return } }
        else { guard !text.isEmpty && text.count <= 30000 else { notice = "Write a message of up to 30,000 characters."; state(); return } }
        if chat == nil { let created = Conversation(title: String(text.prefix(48)), model: preferences.model); chats.insert(created, at: 0); selected = created.id; chat = created }
        guard let conversation = chat, let model = models.first(where: { $0.id == conversation.model }) else { notice = "Choose a model from the live catalogue above."; state(); return }
        let turn = retry ? conversation.turns.last! : Turn(user: text, model: model.name)
        if retry { turn.answer = ""; turn.activity.append("Retry requested. Previous attempts may have incurred charges.") } else { conversation.turns.append(turn) }
        turn.status = "working"; turn.started = isoNow(); turn.model = model.name; turn.input = nil; turn.output = nil; turn.estimate = nil; conversation.updated = Date().timeIntervalSince1970
        try store.save(conversation); busy = true; notice = ""; state(); event("sent", true)
        let tools = Tools(computer: computer, folder: folder, approve: { [weak self] title, text in self?.approve(title, text) ?? false }, activity: { [weak self] item in turn.activity.append(item); try? self?.store.save(conversation); self?.state() })
        job = Task {
            let deadline = Task { [weak self] in try? await Task.sleep(nanoseconds: 600_000_000_000); if !Task.isCancelled { self?.job?.cancel() } }
            defer { deadline.cancel(); busy = false; job = nil; do { try store.save(conversation) } catch { notice = "Could not save the latest update. Export this conversation before closing Hearth." }; state() }
            do { turn.answer = try await Agent.run(chat: conversation, turn: turn, model: model, tools: tools, transport: { body in try await Corvex.request(key: connection, path: "/chat/completions", body: body) }, changed: { [weak self] in self?.state() }); turn.status = "done" }
            catch { if Task.isCancelled || error is CancellationError { turn.status = "stopped"; turn.activity.append("Stopped or reached the ten-minute limit. Completed actions remain in effect; Corvex may bill work already performed.") } else { turn.status = "error"; turn.activity.append(friendly(error)) } }
        }
    }
    func export() throws {
        guard let chat = chats.first(where: { $0.id == selected }) else { return }
        let panel = NSSavePanel(); panel.nameFieldStringValue = "Hearth conversation.md"
        guard panel.runModal() == .OK, let url = panel.url else { return }
        let body = "# \(chat.title)\n\n" + chat.turns.map { "## You\n\n\($0.user)\n\n## Hearth · \($0.model)\n\n\($0.answer)\n\nStatus: \($0.status)\n\n" + $0.activity.joined(separator: "\n") }.joined(separator: "\n\n---\n\n")
        try Data(body.utf8).write(to: url, options: .atomic); notice = "Conversation exported. No API key was included."
    }
    func writeUITestFailure(_ message: String) { try? FileManager.default.createDirectory(atPath: (reportPath as NSString).deletingLastPathComponent, withIntermediateDirectories: true); try? jsonData(["passed": false, "error": message]).write(to: URL(fileURLWithPath: reportPath)) }
    func verifyUI() async {
        var checks: [JSONObject] = []; let directory = URL(fileURLWithPath: reportPath).deletingLastPathComponent()
        func check(_ name: String, _ expression: String) async throws { let value = try await web.evaluateJavaScript(expression); let passed = value as? Bool == true; checks.append(["name": name, "pass": passed]); if !passed { throw HearthError.message(name) } }
        func pause() async throws { try await Task.sleep(nanoseconds: 300_000_000) }
        func capture(_ name: String) async throws { let image = try await web.takeSnapshot(configuration: nil); guard let data = image.tiffRepresentation, let bitmap = NSBitmapImageRep(data: data), let png = bitmap.representation(using: .png, properties: [:]) else { throw HearthError.message("Screenshot unavailable") }; try png.write(to: directory.appendingPathComponent(name + ".png")) }
        do {
            try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true); try await pause()
            try await check("Native bridge and setup", "document.getElementById('setup').open"); try await capture("01-setup")
            _ = try await web.evaluateJavaScript("document.getElementById('later').click()")
            key = "ui-fixture-not-real"; models = [Model(id: "fixture", name: "GLM 5.2 · preview", input: 0.75, output: 2.4)]; preferences.model = "fixture"; state(); try await pause(); try await capture("02-welcome")
            _ = try await web.evaluateJavaScript("document.querySelector('[data-prompt]').click()")
            try await check("Starter prompt", "document.getElementById('message').value.includes('friendly email')")
            let chat = Conversation(title: "A little help with my Mac", model: "fixture"); let turn = Turn(user: "My Mac feels slow. Can you help?", model: "GLM 5.2")
            turn.status = "done"; turn.answer = "## Let's check together\n\nTurn on **Computer help** below. I can inspect a snapshot of CPU load, physical memory, disk space and open apps.\n\nI'll explain the findings before suggesting changes.\n\n<script>window.fixtureInjected=true</script>\n\n[unsafe](javascript:window.fixtureInjected=true)"; chat.turns = [turn]; chats = [chat]; selected = chat.id; try store.save(chat); state(); try await pause()
            try await check("Markdown renders", "document.querySelector('.answer strong').textContent === 'Computer help'")
            try await check("Untrusted HTML and links sanitized", "!window.fixtureInjected && document.querySelectorAll('.answer script, .answer a[href^=\"javascript:\"]').length === 0")
            try await capture("03-chat"); _ = try await web.evaluateJavaScript("document.getElementById('theme').click()"); try await pause()
            try await check("Native theme action", "document.body.classList.contains('dark')"); try await capture("04-dark")
            window.setContentSize(NSSize(width: 780, height: 560)); try await pause(); try await capture("05-compact")
            try await check("Compact layout", "document.documentElement.scrollWidth <= window.innerWidth")
            _ = try await web.evaluateJavaScript("document.getElementById('new').click()"); try await pause()
            try await check("New conversation", "document.querySelector('.welcome') !== null")
            try jsonData(["passed": true, "checks": checks, "architecture": ProcessInfo.processInfo.environment["RUNNER_ARCH"] ?? "local"]).write(to: URL(fileURLWithPath: reportPath)); watchdog?.cancel(); exit(0)
        } catch { try? jsonData(["passed": false, "checks": checks, "error": error.localizedDescription]).write(to: URL(fileURLWithPath: reportPath)); watchdog?.cancel(); exit(1) }
    }
}

@main struct HearthMain {
    @MainActor static func main() {
        let app = NSApplication.shared; let controller = AppController(); app.delegate = controller
        app.setActivationPolicy(CommandLine.arguments.contains("--self-test") || CommandLine.arguments.contains("--ui-test") ? .accessory : .regular)
        withExtendedLifetime(controller) { app.run() }
    }
}
