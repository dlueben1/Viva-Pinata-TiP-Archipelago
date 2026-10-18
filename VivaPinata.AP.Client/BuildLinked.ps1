# Set Working Directory
Split-Path $MyInvocation.MyCommand.Path | Push-Location
[Environment]::CurrentDirectory = $PWD

Remove-Item "$env:RELOADEDIIMODS/VivaPinata.AP.Client/*" -Force -Recurse
dotnet publish "./VivaPinata.AP.Client.csproj" -c Release -o "$env:RELOADEDIIMODS/VivaPinata.AP.Client" /p:OutputPath="./bin/Release" /p:ReloadedILLink="true"

# Restore Working Directory
Pop-Location