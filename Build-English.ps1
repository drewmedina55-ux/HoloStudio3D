# Disc Image Studio - English Edition one-click build
# Produces a single self-contained DiscImageStudio.exe (no .NET runtime needed to run it).
# Requires: Windows 10 2004+/11 and the .NET 10 SDK (https://dotnet.microsoft.com/download/dotnet/10.0)

$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'The .NET SDK was not found. Install the .NET 10 SDK, then run this script again.'
}
$sdks = & dotnet --list-sdks
if (-not ($sdks -match '^10\.')) {
    throw "A .NET 10 SDK is required. Installed SDKs:`n$($sdks -join "`n")"
}

# The finished apps go in this folder, next to this script.
$out = Join-Path $PSScriptRoot 'HoloStudio3D-App'
if (Test-Path -LiteralPath $out) { Remove-Item -LiteralPath $out -Recurse -Force }
# Builds before v17 used a folder called publish-english; remove it so there's only one place to look.
$oldOut = Join-Path $PSScriptRoot 'publish-english'
if (Test-Path -LiteralPath $oldOut) { Remove-Item -LiteralPath $oldOut -Recurse -Force -ErrorAction SilentlyContinue }

Write-Host 'Building Disc Image Studio (English Edition)...' -ForegroundColor Cyan
& dotnet publish 'src/DiscImageStudio.App/DiscImageStudio.App.csproj' `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    --output $out `
    -maxcpucount:1 `
    -p:BuildInParallel=false `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE." }

Get-ChildItem -LiteralPath $out -Filter '*.pdb' -File -Recurse | Remove-Item -Force
$exe = Join-Path $out 'DiscImageStudio.exe'
if (-not (Test-Path -LiteralPath $exe)) { throw "Build finished but $exe is missing." }

Write-Host 'Building HoloStudio3D...' -ForegroundColor Cyan
& dotnet publish 'tools/Disc3DLab/Disc3DLab.csproj' `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    --output $out `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true
if ($LASTEXITCODE -ne 0) { throw "HoloStudio3D publish failed with exit code $LASTEXITCODE." }
Get-ChildItem -LiteralPath $out -Filter '*.pdb' -File -Recurse | Remove-Item -Force
if (-not (Test-Path -LiteralPath (Join-Path $out 'HoloStudio3D.exe'))) { throw 'HoloStudio3D.exe is missing.' }
Copy-Item -LiteralPath 'HOLOSTUDIO3D.md' -Destination $out
# Sample pictures, depth maps and models (the app's file boxes open here first).
if (Test-Path -LiteralPath 'samples') { Copy-Item -LiteralPath 'samples' -Destination (Join-Path $out 'samples') -Recurse -Force }

# ── Stage 1: disc-probe with its own private Python (no system install, nothing else touched) ──
Write-Host 'Setting up disc-probe (Stage 1) with a private Python...' -ForegroundColor Cyan
$probe = Join-Path $out 'disc-probe'
$src = Join-Path $probe 'src'
$py = Join-Path $probe 'python'
New-Item -ItemType Directory -Force -Path $src | Out-Null
Copy-Item -Path 'tools/disc-probe/*' -Destination $src -Recurse -Force
Copy-Item -LiteralPath 'tools/Disc3DLab/discprobe-bridge/labrun.py' -Destination $src -Force
try {
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    $temp = Join-Path ([IO.Path]::GetTempPath()) ('disc3dlab-python-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Force -Path $temp | Out-Null
    $zip = Join-Path $temp 'python-embed.zip'
    $got = $false
    foreach ($ver in '3.12.10', '3.12.9', '3.12.8') {
        try {
            Invoke-WebRequest -UseBasicParsing "https://www.python.org/ftp/python/$ver/python-$ver-embed-amd64.zip" -OutFile $zip
            $got = $true; Write-Host "  Python $ver"; break
        } catch { }
    }
    if (-not $got) { throw 'Could not download Python from python.org.' }
    Expand-Archive -LiteralPath $zip -DestinationPath $py -Force
    # The embedded Python ignores PYTHONPATH; its ._pth file decides what it can import.
    $pth = Get-ChildItem -LiteralPath $py -Filter 'python3*._pth' | Select-Object -First 1
    $stdlib = [IO.Path]::GetFileNameWithoutExtension($pth.Name) + '.zip'
    Set-Content -LiteralPath $pth.FullName -Encoding ascii -Value @($stdlib, '.', '..\src', 'Lib\site-packages', 'import site')
    Invoke-WebRequest -UseBasicParsing 'https://bootstrap.pypa.io/get-pip.py' -OutFile (Join-Path $temp 'get-pip.py')
    & (Join-Path $py 'python.exe') (Join-Path $temp 'get-pip.py') --no-warn-script-location --disable-pip-version-check
    if ($LASTEXITCODE -ne 0) { throw 'pip setup failed.' }
    & (Join-Path $py 'python.exe') -m pip install --no-warn-script-location --disable-pip-version-check numpy scipy pillow
    if ($LASTEXITCODE -ne 0) { throw 'Installing numpy, scipy and pillow failed.' }
    & (Join-Path $py 'python.exe') -c "import numpy, scipy, PIL, discprobe; print('  disc-probe ready')"
    if ($LASTEXITCODE -ne 0) { throw 'disc-probe could not start.' }
    Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue
}
catch {
    Write-Warning "disc-probe's Python could not be set up: $($_.Exception.Message)"
    Write-Warning 'Everything else is built. Stage 1 will say so; run this build again while online to add it.'
    if (Test-Path -LiteralPath $py) { Remove-Item -LiteralPath $py -Recurse -Force -ErrorAction SilentlyContinue }
}

Copy-Item -LiteralPath 'LICENSE', 'THIRD-PARTY-NOTICES.txt' -Destination $out
Write-Host ''
Write-Host "Done: $exe" -ForegroundColor Green
Write-Host "      $(Join-Path $out 'HoloStudio3D.exe')" -ForegroundColor Green
Write-Host 'Run it with no arguments for the app, or with arguments for the command line (e.g. DiscImageStudio.exe selftest).'
Write-Host 'Keep the disc-probe folder next to HoloStudio3D.exe: Stage 1 uses it.'
Write-Host ''
Write-Host "Your apps are in: $out" -ForegroundColor Green
# Show the folder in File Explorer (not on GitHub's build machines).
if (-not $env:CI) { Start-Process -FilePath 'explorer.exe' -ArgumentList "`"$out`"" }
