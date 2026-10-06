# Build image for the musl Linux binaries (linux-musl-x64, linux-musl-arm64).
#
# Alpine Linux uses the musl C library. Testing and smoke testing here proves the musl binaries run
# on Alpine 3.22, the minimum in README's supported platforms table. The image is Microsoft's .NET 10
# SDK image for Alpine 3.22.
FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine3.22
