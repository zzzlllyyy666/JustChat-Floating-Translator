// swift-tools-version: 6.0

import PackageDescription

let package = Package(
    name: "JustChatWriter",
    platforms: [
        .macOS(.v14)
    ],
    products: [
        .executable(name: "JustChatWriter", targets: ["JustChatWriter"])
    ],
    targets: [
        .executableTarget(
            name: "JustChatWriter",
            path: "Sources/JustChatWriter"
        ),
        .testTarget(
            name: "JustChatWriterTests",
            dependencies: ["JustChatWriter"],
            path: "Tests/JustChatWriterTests"
        )
    ],
    swiftLanguageModes: [.v5]
)
