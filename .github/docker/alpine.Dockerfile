# Build image for the musl Linux binaries (linux-musl-x64, linux-musl-arm64).
#
# Alpine Linux uses the musl C library. Testing and smoke testing here proves the musl binaries run
# on Alpine 3.22, the oldest Alpine with a .NET 10 SDK image.
FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine3.22
