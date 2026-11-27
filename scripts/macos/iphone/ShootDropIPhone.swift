// shootdrop-iphone: reads videos from a USB-connected iPhone with Apple's ImageCaptureCore (what Image Capture uses).
// READ-ONLY: it lists files and downloads copies. It never deletes or changes anything on the phone.
// Built into ShootDrop.app/Contents/MacOS by scripts/macos/package-app.sh. One JSON object per output line.
//
//   shootdrop-iphone watch              {"event":"added"|"removed","device":"..."} as iPhones come and go
//   shootdrop-iphone list               {"status":"locked"} while locked, then one line per video:
//                                       {"device","id","name","size","recorded"(unix seconds)} and {"status":"done"}
//   shootdrop-iphone get <dir> <id>...  downloads into <dir>, file time = recording time:
//                                       {"id","path","size"} or {"id","error"} per file, then {"status":"done"}
import Foundation
import ImageCaptureCore

func emit(_ obj: [String: Any]) {
    if let d = try? JSONSerialization.data(withJSONObject: obj), let s = String(data: d, encoding: .utf8) {
        print(s); fflush(stdout)
    }
}

func isPhone(_ d: ICDevice) -> Bool {
    let kind = (d.productKind ?? "").lowercased()
    return kind.contains("iphone") || kind.contains("ipad") || (d as? ICCameraDevice)?.isAccessRestrictedAppleDevice == true
        || (d.name ?? "").lowercased().contains("iphone")
}

func idOf(_ f: ICCameraFile) -> String {
    "\(f.name ?? "")|\(Int(f.creationDate?.timeIntervalSince1970 ?? 0))|\(f.fileSize)"
}

func isVideo(_ f: ICCameraFile) -> Bool {
    let n = (f.name ?? "").uppercased()
    return n.hasSuffix(".MOV") || n.hasSuffix(".MP4") || n.hasSuffix(".M4V")
}

final class Reader: NSObject, ICDeviceBrowserDelegate, ICCameraDeviceDelegate {
    let browser = ICDeviceBrowser()
    let mode: String
    let args: [String]
    var phone: ICCameraDevice?
    var saidLocked = false

    init(mode: String, args: [String]) {
        self.mode = mode; self.args = args
        super.init()
        browser.delegate = self
        browser.browsedDeviceTypeMask = ICDeviceTypeMask(rawValue: ICDeviceTypeMask.camera.rawValue | ICDeviceLocationTypeMask.local.rawValue)!
        browser.start()
    }

    // ICDeviceBrowserDelegate
    func deviceBrowser(_ browser: ICDeviceBrowser, didAdd device: ICDevice, moreComing: Bool) {
        guard isPhone(device), let cam = device as? ICCameraDevice else { return }
        if mode == "watch" { emit(["event": "added", "device": cam.name ?? "iPhone"]); return }
        guard phone == nil else { return }
        phone = cam
        cam.delegate = self
        cam.requestOpenSession()
    }

    func deviceBrowser(_ browser: ICDeviceBrowser, didRemove device: ICDevice, moreGoing: Bool) {
        guard isPhone(device) else { return }
        if mode == "watch" { emit(["event": "removed", "device": device.name ?? "iPhone"]); return }
        if device === phone { emit(["status": "error", "error": "The iPhone was disconnected."]); exit(1) }
    }

    // ICDeviceDelegate
    func didRemove(_ device: ICDevice) {}
    func device(_ device: ICDevice, didOpenSessionWithError error: Error?) { if error != nil { locked() } }
    func device(_ device: ICDevice, didCloseSessionWithError error: Error?) {}

    // ICCameraDeviceDelegate
    func cameraDevice(_ camera: ICCameraDevice, didAdd items: [ICCameraItem]) {}
    func cameraDevice(_ camera: ICCameraDevice, didRemove items: [ICCameraItem]) {}
    func cameraDevice(_ camera: ICCameraDevice, didReceiveThumbnail thumbnail: CGImage?, for item: ICCameraItem, error: Error?) {}
    func cameraDevice(_ camera: ICCameraDevice, didReceiveMetadata metadata: [AnyHashable: Any]?, for item: ICCameraItem, error: Error?) {}
    func cameraDevice(_ camera: ICCameraDevice, didRenameItems items: [ICCameraItem]) {}
    func cameraDeviceDidChangeCapability(_ camera: ICCameraDevice) {}
    func cameraDevice(_ camera: ICCameraDevice, didReceivePTPEvent eventData: Data) {}
    func cameraDeviceDidRemoveAccessRestriction(_ device: ICDevice) {}
    func cameraDeviceDidEnableAccessRestriction(_ device: ICDevice) { locked() }

    func locked() {
        if !saidLocked { saidLocked = true; emit(["status": "locked"]) }
    }

    func deviceDidBecomeReady(withCompleteContentCatalog camera: ICCameraDevice) {
        let videos = (camera.mediaFiles ?? []).compactMap { $0 as? ICCameraFile }.filter(isVideo)
        if mode == "list" {
            for f in videos {
                emit(["device": camera.name ?? "iPhone", "id": idOf(f), "name": f.name ?? "", "size": f.fileSize,
                      "recorded": Int(f.creationDate?.timeIntervalSince1970 ?? 0)])
            }
            emit(["status": "done"]); exit(0)
        }
        guard args.count >= 1 else { emit(["status": "error", "error": "no folder"]); exit(1) }
        let dir = URL(fileURLWithPath: args[0], isDirectory: true)
        let wanted = Set(args.dropFirst())
        download(videos.filter { wanted.contains(idOf($0)) }, into: dir)
    }

    func download(_ files: [ICCameraFile], into dir: URL) {
        guard let f = files.first else { emit(["status": "done"]); exit(0) }
        let rest = Array(files.dropFirst())
        // Each file goes to its own temporary folder first: phones reuse names like IMG_1352.MOV.
        let tmp = dir.appendingPathComponent(".download-\(UUID().uuidString)", isDirectory: true)
        try? FileManager.default.createDirectory(at: tmp, withIntermediateDirectories: true)
        f.requestDownload(options: [ICDownloadOption.downloadsDirectoryURL: tmp, ICDownloadOption.overwrite: false]) { name, error in
            defer { try? FileManager.default.removeItem(at: tmp); self.download(rest, into: dir) }
            if let e = error { emit(["id": idOf(f), "error": e.localizedDescription]); return }
            let got = tmp.appendingPathComponent(name ?? f.name ?? "clip")
            let size = (try? FileManager.default.attributesOfItem(atPath: got.path)[.size] as? Int64) ?? -1
            guard size == Int64(f.fileSize) else { emit(["id": idOf(f), "error": "size \(size) instead of \(f.fileSize)"]); return }
            var target = dir.appendingPathComponent(got.lastPathComponent)
            if FileManager.default.fileExists(atPath: target.path) {
                let stem = target.deletingPathExtension().lastPathComponent, ext = target.pathExtension
                target = dir.appendingPathComponent("\(stem)_\(Int(f.creationDate?.timeIntervalSince1970 ?? 0)).\(ext)")
            }
            do {
                try FileManager.default.moveItem(at: got, to: target)
                if let d = f.creationDate { try? FileManager.default.setAttributes([.modificationDate: d], ofItemAtPath: target.path) }
                emit(["id": idOf(f), "path": target.path, "size": size])
            } catch {
                emit(["id": idOf(f), "error": error.localizedDescription])
            }
        }
    }
}

let argv = CommandLine.arguments
let mode = argv.count > 1 ? argv[1] : "list"
let reader = Reader(mode: mode, args: Array(argv.dropFirst(2)))
if mode != "watch" {
    // Give the person time to unlock the phone; downloads can take long, so only listing has a hard limit.
    DispatchQueue.main.asyncAfter(deadline: .now() + 180) {
        if reader.phone == nil || mode == "list" { emit(["status": "error", "error": "No unlocked iPhone found."]); exit(2) }
    }
}
RunLoop.main.run()
