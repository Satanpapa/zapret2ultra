# Zapret2Ultra

Modern Windows front-end and gateway layer for a local DPI-bypass engine.

> Project goal: keep the excellent usability of zapret2UI while making the architecture engine-agnostic, strategy-aware and capable of turning a Windows PC into an optional WireGuard gateway for a phone.

## Highlights

- Beautiful WPF desktop UI with Simple / Advanced workflow.
- Native-engine adapter architecture; compatibility adapters can coexist.
- Adaptive strategy selection and per-network memory.
- Diagnostics, rollback and safe state transitions.
- Portable self-contained single-file `Zapret2Ultra.exe`.
- Optional local WireGuard gateway: `Phone -> tunnel -> Windows -> local engine -> Internet`.
- QR-based client provisioning is planned for the gateway module.

## Build

```powershell
dotnet build Zapret2Ultra.csproj -c Release
dotnet publish Zapret2Ultra.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
```

The current repository contains the new application shell and architecture foundation. The complete zapret2UI feature migration and native gateway implementation are being integrated incrementally so each stage can be built and tested independently.

## Attribution

The UI direction is inspired by and intended to integrate concepts from Asterlike's `zapret2UI`. See the upstream project for its license and attribution requirements. The DPI engine remains a separate component with its own license.
