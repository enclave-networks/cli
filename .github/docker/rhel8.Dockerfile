# Build image for the glibc Linux binaries (linux-x64, linux-arm64).
#
# AlmaLinux 8 is binary-compatible with RHEL 8 and has its glibc, 2.28. Testing and smoke testing
# here proves the binaries run on RHEL 8, which is in .NET 10's supported list
# (https://github.com/dotnet/core/blob/main/release-notes/10.0/supported-os.md).
FROM almalinux:8

# The libraries .NET needs on RHEL (https://learn.microsoft.com/dotnet/core/install/linux-rhel#dependencies)
# and the tools the .NET install script runs.
RUN dnf install -y glibc libgcc ca-certificates openssl-libs libstdc++ libicu tzdata krb5-libs \
      curl tar gzip findutils \
 && dnf clean all

# The latest .NET 10 SDK, matching what setup-dotnet installs from global.json on the other runners.
RUN curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh \
 && bash /tmp/dotnet-install.sh --channel 10.0 --install-dir /usr/share/dotnet \
 && rm /tmp/dotnet-install.sh

ENV DOTNET_ROOT=/usr/share/dotnet
ENV PATH=/usr/share/dotnet:$PATH
