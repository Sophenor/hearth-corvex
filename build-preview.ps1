$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
$output = Join-Path $PSScriptRoot 'dist\Hearth-Windows-0.1.1-preview'
& dotnet publish src/Hearth/Hearth.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=None -p:DebugSymbols=false -p:Version=0.1.1 -o $output --nologo
if ($LASTEXITCODE -ne 0) { throw 'Preview publish failed.' }
& "$output\Hearth.exe" --self-test --report "$output\verification.json"
if ($LASTEXITCODE -ne 0) { throw 'Preview verification failed.' }
foreach ($name in @('START HERE.txt', 'PREVIEW.txt', 'LICENSE.txt', 'THIRD-PARTY-NOTICES.txt')) { Copy-Item -LiteralPath $name -Destination $output -Force }
Copy-Item -LiteralPath licenses -Destination $output -Recurse -Force
Compress-Archive -LiteralPath $output -DestinationPath 'dist\Hearth-Windows-0.1.1-preview.zip' -Force
