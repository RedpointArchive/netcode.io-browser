Set-Location $PSScriptRoot

$HostPath = Resolve-Path ..\..\netcode.io.host\bin\Debug\netcode.io.host.exe
$ManifestJsonPath = Resolve-Path manifest.windows.json
$ManifestJson = @{
  name = "netcode.io";
  description = "netcode.io helper";
  path = $HostPath.Path;
  type = "stdio";
  allowed_origins = @(
    "chrome-extension://fkcdbgdmpjenlkecdjadcpnkchaecbpn/",
    "chrome-extension://hpecmifakhimhidjpcpjmihpacijicbd/"
  );
  allowed_extensions = @(
    "webext@netcode.redpoint.games"
  );
}
ConvertTo-Json $ManifestJson | Out-File -Encoding UTF8 $ManifestJsonPath

$RegistryPath = "HKCU:\SOFTWARE\Google\Chrome\NativeMessagingHosts\netcode.io"
if (!(Test-Path $RegistryPath)) {
  New-Item -ItemType Directory $RegistryPath
}
New-ItemProperty -Path $RegistryPath -Name "(Default)" -Value $ManifestJsonPath -Force | Out-Null