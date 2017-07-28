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
  rm -Recurse -Force $root\output
}
mkdir $root\output

function ZipFiles( $zipfilename, $sourcedir )
{
   Add-Type -Assembly System.IO.Compression.FileSystem
   $compressionLevel = [System.IO.Compression.CompressionLevel]::Optimal
   [System.IO.Compression.ZipFile]::CreateFromDirectory($sourcedir,
        $zipfilename, $compressionLevel, $false)
}

echo "Creating ZIP for Google Chrome..."
ZipFiles -zipfilename $root\output\GoogleChrome.zip -sourcedir $root\browser\chrome

echo "Building netcode.io helper..."
if (Test-Path $root\netcode.io.host\bin\Release) {
  rm -Recurse -Force $root\netcode.io.host\bin\Release
}
& "C:\Program Files (x86)\MSBuild\14.0\Bin\MSBuild.exe" /m /p:Configuration=Release netcode.io.host.sln
cp $root\browser\hostapp\manifest.windows.relative.json $root\netcode.io.host\bin\Release\manifest.windows.relative.json

echo "Packaging netcode.io helper..."
ZipFiles -zipfilename $root\netcode.io.wininstall\package.zip -sourcedir $root\netcode.io.host\bin\Release

echo "Building netcode.io Windows installer..."
& "C:\Program Files (x86)\MSBuild\14.0\Bin\MSBuild.exe" /m /p:Configuration=Release netcode.io.wininstall\netcode.io.wininstall.csproj

