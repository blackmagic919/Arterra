# LiteNetLib 2.1.4

Pinned managed netstandard2.1 build of https://github.com/RevenantX/LiteNetLib/releases/tag/2.1.4.
Matches the Arterra discovery service. No Unity Transport or local source checkout is required.

Source archive: https://codeload.github.com/RevenantX/LiteNetLib/legacy.tar.gz/refs/tags/2.1.4
Archive SHA-256: 1f758a94ac2bac37e6495268a84e58cff4acdb113d0793f20c21114eb7e6720a

Build the release source with:
```
dotnet build LiteNetLib/LiteNetLib.csproj -c Release -f netstandard2.1 -p:TargetFrameworks=netstandard2.1 -p:GeneratePackageOnBuild=false -p:IsTrimmable=false -p:EnableTrimAnalyzer=false
```

Copy bin/Release/netstandard2.1/LiteNetLib.dll here alongside its MIT license.
