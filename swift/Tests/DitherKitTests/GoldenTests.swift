import XCTest
import SwiftUI
import ImageIO
import Metal
@testable import DitherKit

final class GoldenTests: XCTestCase {
    struct Fixtures: Decodable { let scenes: [Scene] }
    struct Scene: Decodable { let name: String; let w: Int; let h: Int; let program: [Float] }
    private var fixturesURL: URL {
        URL(fileURLWithPath: #filePath).deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent().appendingPathComponent("fixtures")
    }

    @MainActor func testAllMetalGoldenImages() throws {
        XCTAssertNotNil(MTLCreateSystemDefaultDevice(), "Metal is required: this test must run on a macOS host with GPU access")
        let scenes = try JSONDecoder().decode(Fixtures.self, from: Data(contentsOf: fixturesURL.appendingPathComponent("scenes.json"))).scenes
        XCTAssertEqual(scenes.count, 74, "Every shared fixture must participate")
        var renderedCount = 0
        for scene in scenes {
            let renderer = ImageRenderer(content: DitherSurface(testProgram: scene.program, cell: 1).frame(width: CGFloat(scene.w), height: CGFloat(scene.h)))
            renderer.scale = 1
            renderer.colorMode = .nonLinear
            renderer.isOpaque = false
            let actualImage = try XCTUnwrap(renderer.cgImage, "ImageRenderer returned no image for \(scene.name)")
            let source = try XCTUnwrap(CGImageSourceCreateWithURL(fixturesURL.appendingPathComponent("png/\(scene.name).png") as CFURL, nil))
            let expectedImage = try XCTUnwrap(CGImageSourceCreateImageAtIndex(source, 0, nil))
            let actual = try rgba(actualImage)
            let expected = try rgba(expectedImage)
            XCTAssertEqual(actualImage.width, scene.w, scene.name)
            XCTAssertEqual(actualImage.height, scene.h, scene.name)
            var mismatches = 0
            var differingPixels = 0
            var maximumDifference = 0
            for pixel in 0..<(scene.w * scene.h) {
                let offset = pixel * 4
                let differences = (0..<4).map { abs(Int(actual[offset + $0]) - Int(expected[offset + $0])) }
                maximumDifference = max(maximumDifference, differences.max() ?? 0)
                if differences.contains(where: { $0 > 0 }) { differingPixels += 1 }
                if differences.contains(where: { $0 > 2 }) { mismatches += 1 }
            }
            print("Metal golden \(scene.name): max channel delta=\(maximumDifference), nonidentical pixels=\(differingPixels)/\(scene.w * scene.h), beyond ±2=\(mismatches)/\(scene.w * scene.h)")
            XCTAssertLessThanOrEqual(maximumDifference, 2, "\(scene.name): maximum channel delta must be at most 2")
            let ratio = Double(differingPixels) / Double(scene.w * scene.h)
            XCTAssertLessThanOrEqual(ratio, 0.01, "\(scene.name): \(differingPixels)/\(scene.w * scene.h) pixels differ (maximum \(maximumDifference)); shaders must render, never skip")
            renderedCount += 1
        }
        XCTAssertEqual(renderedCount, 74)
    }

    @MainActor func testProductionPremultipliedCompositing() throws {
        let scenes = try JSONDecoder().decode(Fixtures.self, from: Data(contentsOf: fixturesURL.appendingPathComponent("scenes.json"))).scenes
        var tested = 0
        for scene in scenes {
            let renderer = ImageRenderer(content: DitherSurface(program: scene.program, cell: 1).frame(width: CGFloat(scene.w), height: CGFloat(scene.h)))
            renderer.scale = 1
            renderer.colorMode = .nonLinear
            renderer.isOpaque = false
            let actual = try rgba(XCTUnwrap(renderer.cgImage))
            let source = try XCTUnwrap(CGImageSourceCreateWithURL(fixturesURL.appendingPathComponent("png/\(scene.name).png") as CFURL, nil))
            let expected = try rgba(XCTUnwrap(CGImageSourceCreateImageAtIndex(source, 0, nil)))
            var maximumDifference = 0
            for offset in stride(from: 0, to: actual.count, by: 4) {
                let alpha = Int(expected[offset + 3])
                for channel in 0..<4 {
                    let premultiplied = channel == 3 ? alpha : (Int(expected[offset + channel]) * alpha + 127) / 255
                    maximumDifference = max(maximumDifference, abs(Int(actual[offset + channel]) - premultiplied))
                }
                for channel in 0..<3 { XCTAssertLessThanOrEqual(Int(actual[offset + channel]), Int(actual[offset + 3]) + 1, "Production output must be premultiplied: \(scene.name)") }
            }
            print("Production premultiplied \(scene.name): maximum channel delta=\(maximumDifference)")
            XCTAssertLessThanOrEqual(maximumDifference, 2, "\(scene.name): production compositing must match premultiplied golden bytes")
            tested += 1
        }
        XCTAssertEqual(tested, 74)
    }

    /// Test transport emits straight RGBA in both passes. Read raw bytes so
    /// CGContext does not clamp RGB to alpha or introduce another conversion.
    private func rgba(_ image: CGImage) throws -> [UInt8] {
        guard image.bitsPerComponent == 8, image.bitsPerPixel == 32 else {
            throw NSError(domain: "DitherGolden.PixelFormat", code: 1, userInfo: [NSLocalizedDescriptionKey: "Expected RGBA8, got \(image.bitsPerComponent)/\(image.bitsPerPixel)"])
        }
        let first: Bool
        switch image.alphaInfo {
        case .first, .premultipliedFirst: first = true
        case .last, .premultipliedLast: first = false
        default: throw NSError(domain: "DitherGolden.PixelFormat", code: 2)
        }
        let little = image.bitmapInfo.intersection(.byteOrderMask) == .byteOrder32Little
        let channels = little ? (first ? [2, 1, 0, 3] : [3, 2, 1, 0]) : (first ? [1, 2, 3, 0] : [0, 1, 2, 3])
        let data = try XCTUnwrap(image.dataProvider?.data) as Data
        var bytes: [UInt8] = []
        bytes.reserveCapacity(image.width * image.height * 4)
        for row in 0..<image.height {
            for column in 0..<image.width {
                let offset = row * image.bytesPerRow + column * 4
                for channel in channels { bytes.append(data[offset + channel]) }
            }
        }
        return bytes
    }
}
