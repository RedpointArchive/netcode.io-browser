param()

trap
{
    Write-Host "An error occurred"
    Write-Host $_
    Write-Host $_.Exception.StackTrace
    exit 1
}

$ErrorActionPreference = 'Stop'

cd $PSScriptRoot\..
$root = Get-Location

if (Test-Path $root\output) {
  Remove-Item -Recurse -Force $root\output
}
mkdir $root\output

function ZipFiles( $zipfilename, $sourcedir )
{
   Add-Type -Assembly System.IO.Compression.FileSystem
   $compressionLevel = [System.IO.Compression.CompressionLevel]::Optimal
   [System.IO.Compression.ZipFile]::CreateFromDirectory($sourcedir,
        $zipfilename, $compressionLevel, $false)
}

Write-Output "Creating Web Extension ZIP (standard)..."
ZipFiles -zipfilename $root\output\WebExtension.zip -sourcedir $root\browser\webext

Write-Output "Creating Web Extension ZIP (self-dist)..."
Copy-Item -Force $root\browser\webext\hostmsg.js $root\browser\webext-selfdist\
Copy-Item -Force $root\browser\webext\netcode.js $root\browser\webext-selfdist\
Copy-Item -Force $root\browser\webext\netcodecs.js $root\browser\webext-selfdist\
ZipFiles -zipfilename $root\output\WebExtension-SelfDist.zip -sourcedir $root\browser\webext-selfdist

Write-Output "Building netcode.io helper / installers..."
Push-Location netcode.io.host
try {
  go get github.com/wirepair/netcode
  go get golang.org/x/sys/windows/registry
  $env:GOARCH="amd64"
  $env:GOOS="windows"
  go build
  Move-Item -Force netcode.io.host.exe ..\output\NetcodeInstaller-Windows.exe
  $env:GOOS="darwin"
  go build
  Move-Item -Force netcode.io.host ..\output\NetcodeInstaller-macOS
  $env:GOOS="linux"
  go build
  Move-Item -Force netcode.io.host ..\output\NetcodeInstaller-Linux
} finally {
  Pop-Location
}