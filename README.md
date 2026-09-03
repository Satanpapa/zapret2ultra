# Zapret2Ultra

Modern WPF control plane for **zapret2 / winws2** with an optional WireGuard gateway that turns a Windows PC into a local VPN exit for a phone.

## What is implemented

- Dark modern WPF interface with Home / Strategies / VPN Gateway / Diagnostics / Settings.
- Real `winws2` integration using the official `bol-van/zapret2` Windows bundle.
- Automatic download on first use with SHA-256 verification (currently pinned to zapret2 v1.0.5).
- Balanced and Aggressive engine profiles based on the documented zapret2 `preset2_example` architecture.
- Engine process lifecycle, logs, administrator checks and diagnostics.
- WireGuard gateway generation with server/client keypairs, tunnel service installation, Windows NAT and firewall configuration.
- QR code generation for the phone WireGuard profile.
- Exportable `phone.conf` for Android / iOS WireGuard clients.
- Self-contained .NET 9 x64 single-file publishing.

## Important gateway detail

The WireGuard server is local to this PC. For a phone on the same LAN/Wi-Fi, the generated LAN endpoint is enough. For a phone on a different network, the router/firewall must forward the chosen UDP port to this PC. This application does not attempt to change arbitrary consumer routers automatically.

## Build

```powershell
dotnet restore Zapret2Ultra.csproj
dotnet build Zapret2Ultra.csproj -c Release
dotnet publish Zapret2Ultra.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
```

## Run

For the DPI engine and WireGuard gateway, run the application **as Administrator**.

```powershell
.\publish\Zapret2Ultra.exe
```

## Real winws2 dependency

The application downloads the official zapret2 v1.0.5 Windows archive from:

`https://github.com/bol-van/zapret2/releases/download/v1.0.5/zapret2-v1.0.5.zip`

The archive is SHA-256 verified before extraction. The bundle contains `winws2.exe`, WinDivert components and the Lua runtime expected by zapret2.

## WireGuard

Install the official **WireGuard for Windows** first. The app uses its `wireguard.exe` / `wg.exe` command line and Windows tunnel service. Server private keys never leave the PC and are stored under `%LOCALAPPDATA%\Zapret2Ultra\wireguard`.

## Credits / licenses

- zapret2 / `winws2`: https://github.com/bol-van/zapret2
- Windows bundle: https://github.com/bol-van/zapret-win-bundle
- UI ideas: https://github.com/Asterlike/zapret2UI
- WireGuard for Windows: https://github.com/WireGuard/wireguard-windows
- QR generation: QRCoder (MIT)

See the upstream projects for their respective licenses and redistribution terms.
