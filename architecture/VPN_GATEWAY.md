# Local VPN gateway

Zapret2Ultra is designed to support a PC-as-gateway mode. The intended data path is:

`Phone -> WireGuard tunnel -> Windows forwarding/NAT -> Zapret2Ultra interception/engine -> ISP`

## UX

The desktop app should generate a client profile and QR code, show the LAN/WireGuard address, connected peers, handshake age and traffic counters, and expose one Start/Stop control.

## Safety and correctness

- Bind the WireGuard listener explicitly; never expose an unintended management port.
- Generate fresh server/client keys locally and never upload private keys.
- Require administrator approval before changing routing, firewall or NAT state.
- Save a reversible snapshot of Windows networking state and restore it on Stop/uninstall.
- Detect IPv4/IPv6 forwarding conflicts and existing VPN adapters before activation.
- Keep the gateway optional: normal PC-only DPI mode must work without WireGuard.

## Engine integration

The gateway must not assume that a particular DPI engine is the transport. Traffic arriving from the tunnel is ordinary IP traffic and is handed to the selected local interception datapath. This lets the UI front-end support the project's native engine as well as a compatibility adapter.

## Implementation phases

1. WireGuard profile/key management.
2. Windows adapter lifecycle and peer provisioning.
3. Forwarding + NAT with rollback.
4. DNS handling and leak checks.
5. Engine traffic-path integration.
6. QR provisioning and live peer telemetry.
7. End-to-end tests on Windows 10/11.
