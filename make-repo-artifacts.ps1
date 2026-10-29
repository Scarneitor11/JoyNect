# Makes the artifacts this repository should ship to GitHub.
# Run from the project root.
dotnet restore
dotnet publish src/JoyNect.App/JoyNect.App.csproj -c Release -r win-x64 --self-contained true -o publishes/win-x64 -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:IncludeAllContentForSelfExtract=true -p:EnableSourceLink=false -p:DebugType=none -p:DebugSymbols=false
Compress-Archive -Force -Path "publishes/win-x64/*" -DestinationPath "JoyNect-1.1.0-win-x64.zip"
Get-ChildItem JoyNect-1.1.0-win-x64.zip, publishes/win-x64/JoyNect.exe |
    Select-Object Name, Length, LastWriteTime |
    Format-Table -AutoSize
# NOTE: there was also an older publish copy under artifacts/JoyNect-1.1.0-win-x64.
# If that directory should be part of the repository, add it explicitly and do not let
# this script silently merge it into the root-level ZIP.
# NOTE: this script does not add or commit anything. Repository hygiene is a human decision.
# NOTE: the remote is not set yet from this checkout. Do not paste one into a script that runs blindly.
