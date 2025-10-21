## Remote Haveno node for the mobile app
This application requires <a href="https://adoptium.net/temurin/releases/?version=21" target="_blank">Java 21</a>

## About
Installs the Haveno daemon and starts a reverse proxy to translate grpc-web from the Haveno app. The remote node runs a hidden service so no port forwarding is required.

Currently works on windows and linux

> [!note]
> This repository is configured for the public test network/stagenet. You will need to use a third party mainnet network to make real trades. The developers of this repository do not endorse any networks at this time.

## Network operators
The application can be built using GitHub actions and is set up to trigger a build whenever a new tag is pushed.

1. Set up your Haveno repo to build the daemon
	
	Merge https://github.com/haveno-dex/haveno/commit/fc788c6f646fdfb6bf97493d0368101709867174.

2. Open Manta.Remote.csproj in a text editor

    | Property              | Value                                 
    |-----------------------|---------------------------------------
    | DaemonUrl             | Url of where the daemon.jar files are hosted. If you're using GitHub actions, use the url of the release that was created
    | Network               | Change this to XMR_MAINNET
	| HavenoAppName         | Change this to haveno-[SOMETHING]-node, this works the same as the app data directory for the desktop app

## Install
1. Download one of the releases or build from source
2. Unzip
3. chmod +x Manta.Remote 
4. Install Java 21
5. Run Manta.Remote