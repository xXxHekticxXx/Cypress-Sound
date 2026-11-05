# Cypress Sound

By Cypress Works. A Windows sound app: fast output switching and a real EQ for every device.

Install: download `Cypress-Sound-Setup.exe` from the [latest release](https://github.com/xXxHekticxXx/Cypress-Sound/releases/latest).

The EQ runs inside Windows through [Equalizer APO](https://sourceforge.net/projects/equalizerapo/) (GPLv2), which the installer adds. Headphone correction uses [AutoEq](https://github.com/jaakkopasanen/AutoEq) data (MIT).

Build: `dotnet test tests/CypressSound.Tests` and `dotnet publish src/CypressSound -c Release -r win-x64`. CI builds the installer and publishes a release for each new version in `CypressSound.csproj`.
