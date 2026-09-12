$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$project = Join-Path $projectRoot 'src\Hearth\Hearth.csproj'
$output = Join-Path $projectRoot 'dist\Hearth-Windows'
& dotnet publish $project -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true -p:DebugType=None -p:DebugSymbols=false `
    -o $output --nologo
if ($LASTEXITCODE -ne 0) { throw 'Publishing failed; no release archive was produced.' }
Copy-Item -LiteralPath (Join-Path $projectRoot 'START HERE.txt') -Destination $output
Copy-Item -LiteralPath (Join-Path $projectRoot 'LICENSE.txt') -Destination $output
Copy-Item -LiteralPath (Join-Path $projectRoot 'THIRD-PARTY-NOTICES.txt') -Destination $output
Copy-Item -LiteralPath (Join-Path $projectRoot 'licenses') -Destination $output -Recurse -Force
$zip = Join-Path $projectRoot 'dist\Hearth-Windows-0.1.0.zip'
Compress-Archive -LiteralPath $output -DestinationPath $zip -CompressionLevel Optimal -Force
Get-FileHash -LiteralPath $zip -Algorithm SHA256 | Format-List
