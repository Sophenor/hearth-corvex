import AppKit
let folder = URL(fileURLWithPath: CommandLine.arguments[1])
for size in [16, 32, 128, 256, 512] {
    for scale in [1, 2] {
        let pixels = size * scale
        let rep = NSBitmapImageRep(bitmapDataPlanes: nil, pixelsWide: pixels, pixelsHigh: pixels, bitsPerSample: 8, samplesPerPixel: 4, hasAlpha: true, isPlanar: false, colorSpaceName: .deviceRGB, bytesPerRow: 0, bitsPerPixel: 0)!
        NSGraphicsContext.saveGraphicsState(); NSGraphicsContext.current = NSGraphicsContext(bitmapImageRep: rep)
        let edge = CGFloat(pixels)
        NSColor(calibratedRed: 0.192, green: 0.365, blue: 0.275, alpha: 1).setFill()
        NSBezierPath(roundedRect: NSRect(x: edge * 0.04, y: edge * 0.04, width: edge * 0.92, height: edge * 0.92), xRadius: edge * 0.23, yRadius: edge * 0.23).fill()
        let text = "h" as NSString; let font = NSFont(name: "Georgia", size: edge * 0.78) ?? NSFont.systemFont(ofSize: edge * 0.78)
        let attrs: [NSAttributedString.Key: Any] = [.font: font, .foregroundColor: NSColor(calibratedRed: 0.98, green: 0.976, blue: 0.965, alpha: 1)]
        let dimensions = text.size(withAttributes: attrs)
        text.draw(at: NSPoint(x: (edge - dimensions.width) / 2, y: (edge - dimensions.height) / 2 + edge * 0.055), withAttributes: attrs)
        NSGraphicsContext.restoreGraphicsState()
        let name = "icon_\(size)x\(size)" + (scale == 2 ? "@2x" : "") + ".png"
        try rep.representation(using: .png, properties: [:])!.write(to: folder.appendingPathComponent(name))
    }
}
