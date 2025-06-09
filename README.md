## Remote node for Haveno app
This application requires Java 21

## Notes
This program downloads the latest release of the Haveno daemon from https://github.com/atsamd21/haveno/releases. In the future, network administrators will have to provide a built daemon.
It also has a reverse proxy to translate grpc-web from the Haveno app since the Orbot proxy does not support HTTP2.

The remote node now runs as a hidden service so no port forwarding is required

## Install
1. Download one of the releases or build from source
2. Unzip
3. chmod +x Manta.Remote 
4. Install Java 21
5. Run Manta.Remote