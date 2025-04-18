## Remote node for Haveno app
This application requires Java 21

## Notes
On Linux, you need to chmod +x Manta.Remote before running it with ./Manta.Remote

This program downloads the latest release of the Haveno daemon from https://github.com/atsamd21/haveno/releases. In the future, network administrators will have to provide a built daemon.
It also has a reverse proxy to translate grpc-web from the Haveno app since the Orbot proxy does not support HTTP2.

If it's required, this could be run as a hidden service and I have started writing some of that code but it does not work as of now.

## Install
1. Download one of the releases or build from source
2. Unzip
3. Give execute permissions if on Linux (chmod +x Manta.Remote)
4. Install Java 21
5. Run Manta.Remote