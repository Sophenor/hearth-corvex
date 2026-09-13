import Foundation
import AppKit
import Darwin

enum Scope {
    static func resolve(_ root: URL, _ relative: String, mustExist: Bool = true) throws -> URL {
        guard !relative.hasPrefix("/"), !relative.hasPrefix("~"), !relative.contains(":"), !relative.contains("\\"), !relative.split(separator: "/").contains("..") else { throw HearthError.message("Use a relative path inside the chosen folder.") }
        let base = root.standardizedFileURL; let result = base.appendingPathComponent(relative).standardizedFileURL
        let prefix = base.path.hasSuffix("/") ? base.path : base.path + "/"
        guard result.path == base.path || result.path.hasPrefix(prefix) else { throw HearthError.message("Outside the chosen folder.") }
        var item = result
        while true {
            if let attrs = try? FileManager.default.attributesOfItem(atPath: item.path), attrs[.type] as? FileAttributeType == .typeSymbolicLink { throw HearthError.message("Linked files and folders are not accessible.") }
            if item.path == base.path || item.path == "/" { break }; item.deleteLastPathComponent()
        }
        if mustExist && !FileManager.default.fileExists(atPath: result.path) { throw HearthError.message("The selected file or folder does not exist.") }
        let denied = [".ssh", ".aws", ".git", ".codex", "keychains", "connection.bin"]
        if result.pathComponents.contains(where: { denied.contains($0.lowercased()) || $0.lowercased().hasPrefix(".env") }) { throw HearthError.message("Known credential and private configuration paths are blocked.") }
        return result
    }
}

@MainActor final class Tools {
    let computer: Bool; let folder: URL?
    let approve: (String, String) -> Bool; let activity: (String) -> Void
    var observed: [pid_t: Date] = [:]
    init(computer: Bool, folder: URL?, approve: @escaping (String, String) -> Bool, activity: @escaping (String) -> Void) { self.computer = computer; self.folder = folder; self.approve = approve; self.activity = activity }
    var schemas: [JSONObject] {
        func function(_ name: String, _ description: String, _ properties: JSONObject = [:], _ required: [String] = []) -> JSONObject {
            ["type": "function", "function": ["name": name, "description": description, "parameters": ["type": "object", "properties": properties, "required": required, "additionalProperties": false]]]
        }
        let path: JSONObject = ["relativePath": ["type": "string", "description": "Relative path inside the selected folder. Use . for that folder."]]
        var output: [JSONObject] = []
        if computer {
            output.append(function("computer_health", "Read macOS memory, CPU load averages, home-disk free space and visible running apps. Changes nothing."))
            output.append(function("request_close_app", "Ask for approval to request a normal quit of an app in the latest computer_health report. Never force-quits.", ["processId": ["type": "integer"]], ["processId"]))
        }
        if folder != nil {
            output.append(function("list_folder", "List up to 150 entries in the user-selected folder. No symlinks.", path, ["relativePath"]))
            output.append(function("read_text", "Read a small UTF-8 text file, up to 100 KB, in the chosen folder. No PDF, Office or image support.", path, ["relativePath"]))
            var save = path; save["content"] = ["type": "string"]
            output.append(function("save_text", "Save a .txt, .md or .csv draft only after native approval of its exact path and contents. Preserve the previous file if replacing it.", save, ["relativePath", "content"]))
        }
        return output
    }
    func invoke(_ name: String, _ args: JSONObject) throws -> String {
        try Task.checkCancellation()
        guard schemas.contains(where: { ($0["function"] as? JSONObject)?["name"] as? String == name }) else { throw HearthError.message("This tool has not been enabled by the user.") }
        switch name {
        case "computer_health": return try health()
        case "request_close_app": guard let id = args["processId"] as? Int, id > 0, id <= Int(Int32.max) else { throw HearthError.message("Invalid app identifier.") }; return try close(pid_t(id))
        default:
            guard let root = folder, let relative = args["relativePath"] as? String, relative.utf8.count <= 4096 else { throw HearthError.message("Choose a folder and supply a relative path.") }
            switch name {
            case "list_folder":
                let url = try Scope.resolve(root, relative); activity("Listing the selected folder")
                let enumerator = try FileManager.default.contentsOfDirectory(at: url, includingPropertiesForKeys: [.isDirectoryKey, .isSymbolicLinkKey])
                let rows: [JSONObject] = try enumerator.prefix(150).map { entry in let attrs = try entry.resourceValues(forKeys: [.isDirectoryKey, .isSymbolicLinkKey]); return ["name": entry.lastPathComponent, "isFolder": attrs.isDirectory ?? false, "linked": attrs.isSymbolicLink ?? false] }
                return try jsonString(["entries": rows, "truncated": enumerator.count > 150])
            case "read_text":
                let url = try Scope.resolve(root, relative)
                guard ["txt", "md", "csv", "json", "log", "swift", "cs", "py", "js", "html", "css", "xml", "yaml", "yml"].contains(url.pathExtension.lowercased()) else { throw HearthError.message("Only small text files are supported.") }
                let attrs = try FileManager.default.attributesOfItem(atPath: url.path)
                guard attrs[.type] as? FileAttributeType == .typeRegular, ((attrs[.size] as? NSNumber)?.intValue ?? Int.max) <= 100000 else { throw HearthError.message("Choose a regular text file no larger than 100 KB.") }
                let bytes = try Data(contentsOf: url); guard bytes.count <= 100000, let text = String(data: bytes, encoding: .utf8) else { throw HearthError.message("This file is not small UTF-8 text.") }
                activity("Reading " + url.lastPathComponent); return text
            case "save_text":
                guard let text = args["content"] as? String, text.utf8.count <= 100000 else { throw HearthError.message("Drafts are limited to 100 KB of UTF-8 text.") }
                let url = try Scope.resolve(root, relative, mustExist: false)
                guard ["txt", "md", "csv"].contains(url.pathExtension.lowercased()), !url.lastPathComponent.hasPrefix("."), FileManager.default.fileExists(atPath: url.deletingLastPathComponent().path) else { throw HearthError.message("Save a .txt, .md or .csv draft in an existing folder.") }
                if let attrs = try? FileManager.default.attributesOfItem(atPath: url.path) { guard attrs[.type] as? FileAttributeType == .typeRegular, ((attrs[.size] as? NSNumber)?.intValue ?? Int.max) <= 1_000_000 else { throw HearthError.message("Only a regular text draft up to 1 MB can be replaced.") } }
                activity("Waiting for approval to save " + url.lastPathComponent)
                guard approve("Save this file?", url.path + "\n\n" + text) else { return "The user declined. Nothing was written." }
                try Task.checkCancellation(); _ = try Scope.resolve(root, relative, mustExist: false)
                if FileManager.default.fileExists(atPath: url.path) { try FileManager.default.copyItem(at: url, to: url.appendingPathExtension(UUID().uuidString + ".bak")) }
                try Store.write(Data(text.utf8), to: url, backup: false); activity("Saved " + url.lastPathComponent)
                return "Saved \(relative). Any replaced version was kept beside it under a unique .bak name."
            default: throw HearthError.message("Unknown tool.")
            }
        }
    }
    // This is a fixed read-only OS command, never a model-supplied command or argument.
    private func processMetrics() -> [Int: (Double, Double)] {
        let process = Process(); let output = Pipe()
        process.executableURL = URL(fileURLWithPath: "/bin/ps"); process.arguments = ["-axo", "pid=,rss=,pcpu="]
        process.standardOutput = output; process.standardError = FileHandle.nullDevice
        do {
            try process.run()
            let watchdog = DispatchWorkItem { if process.isRunning { process.terminate() } }; DispatchQueue.global().asyncAfter(deadline: .now() + 3, execute: watchdog)
            let bytes = output.fileHandleForReading.readDataToEndOfFile(); process.waitUntilExit(); watchdog.cancel()
            guard bytes.count <= 1_000_000 else { return [:] }
            var values: [Int: (Double, Double)] = [:]
            for line in String(decoding: bytes, as: UTF8.self).split(separator: "\n") { let fields = line.split(whereSeparator: \.isWhitespace); if fields.count == 3, let pid = Int(fields[0]), let rss = Double(fields[1]), let cpu = Double(fields[2]) { values[pid] = (rss / 1024, cpu) } }
            return values
        } catch { return [:] }
    }
    func health() throws -> String {
        guard computer else { throw HearthError.message("Computer help is off.") }
        activity("Checking Mac memory, CPU load, disk space and running apps")
        let metrics = processMetrics(); var loads = [Double](repeating: 0, count: 3); let count = getloadavg(&loads, 3)
        let volume = try? FileManager.default.homeDirectoryForCurrentUser.resourceValues(forKeys: [.volumeAvailableCapacityKey, .volumeTotalCapacityKey])
        observed.removeAll()
        let apps = NSWorkspace.shared.runningApplications.filter { $0.activationPolicy == .regular && $0.processIdentifier != ProcessInfo.processInfo.processIdentifier }.sorted { (metrics[Int($0.processIdentifier)]?.0 ?? 0) > (metrics[Int($1.processIdentifier)]?.0 ?? 0) }.prefix(60)
        let rows: [JSONObject] = apps.map { app in
            if let started = app.launchDate { observed[app.processIdentifier] = started }
            return ["processId": Int(app.processIdentifier), "app": app.localizedName ?? "Application", "memoryMB": optionalJSON(metrics[Int(app.processIdentifier)]?.0), "cpuPercentSample": optionalJSON(metrics[Int(app.processIdentifier)]?.1), "canRequestClose": app.launchDate != nil]
        }
        return try jsonString(["os": ProcessInfo.processInfo.operatingSystemVersionString, "physicalMemoryGB": Double(ProcessInfo.processInfo.physicalMemory) / 1e9, "logicalProcessors": ProcessInfo.processInfo.activeProcessorCount, "loadAverages1_5_15Minutes": count == 3 ? loads : [], "loadNote": "Load averages are counts of runnable/uninterruptible tasks, not CPU percentages. App CPU samples can exceed 100% for multiple cores. This is a diagnostic snapshot, not proof of the cause of slowness.", "homeVolumeFreeGB": optionalJSON(volume?.volumeAvailableCapacity.map { Double($0) / 1e9 }), "homeVolumeTotalGB": optionalJSON(volume?.volumeTotalCapacity.map { Double($0) / 1e9 }), "uptimeHours": ProcessInfo.processInfo.systemUptime / 3600, "apps": rows])
    }
    private func close(_ pid: pid_t) throws -> String {
        guard computer, let started = observed[pid], let app = NSRunningApplication(processIdentifier: pid), app.launchDate == started, app.activationPolicy == .regular, pid != ProcessInfo.processInfo.processIdentifier else { throw HearthError.message("Run computer_health first and choose an app from that report.") }
        let blocked = ["com.apple.finder", "com.apple.dock", "com.apple.loginwindow", "com.apple.systemuiserver", "com.apple.ActivityMonitor", "com.sophenor.hearth"]
        guard !blocked.contains(app.bundleIdentifier ?? "") else { throw HearthError.message("This app cannot be closed through Hearth.") }
        let name = app.localizedName ?? "Application"; activity("Waiting for approval to close " + name)
        guard approve("Close \(name)?", "Save your work first.\n\nHearth will ask \(name) (process \(pid)) to quit normally. The app may ask you to save changes. Hearth will not force-quit it.") else { return "The user declined. The app was not changed." }
        try Task.checkCancellation(); guard !app.isTerminated, app.launchDate == started else { return "The app has already exited or changed." }
        let requested = app.terminate(); activity("Requested a normal quit of " + name)
        return requested ? "A normal quit was requested. This is not proof the app has exited; it may be waiting for unsaved work to be saved." : "The app did not accept the request. No force-quit was attempted."
    }
}
