# Zapret2Ultra roadmap

## Desktop experience

- Beautiful WPF interface inspired by zapret2UI, with the visual language retained while avoiding unnecessary complexity.
- Simple / Advanced modes.
- Tray-first operation and one-click Start/Stop.
- Per-target status and actionable diagnostics.
- Import/export profiles and automatic backup/rollback.
- Portable single-file self-contained EXE.

## Strategy engine

- Strategy catalog with compatibility metadata.
- Automated probe matrix against user-selected targets.
- Score strategies by success, latency, stability and collateral impact.
- Remember the last known-good strategy per target/network fingerprint.
- Safe fallback when a strategy degrades.
- Separate engine adapter from UI so the native FlyDPI datapath can replace legacy winws2 integration without rewriting the front-end.

## Local phone gateway

- Optional WireGuard server on the Windows PC.
- QR provisioning for Android/iOS clients.
- Per-peer enable/disable and traffic statistics.
- IPv4 forwarding/NAT with transactional rollback.
- DNS configuration and leak diagnostics.
- Automatic start with Windows, only when explicitly enabled.

## Release quality gates

- `dotnet build -c Release -p:Platform=x64`
- `dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true`
- Unit tests for config/state transitions.
- Integration tests for engine lifecycle and gateway rollback.
- No secrets or private keys committed to the repository.
