Set-Location $PSScriptRoot

$HostPath = Resolve-Path ..\..\netcode.io.host\bin\Debug\netcode.io.host.exe
$ManifestChromeJsonPath = "$(pwd)\manifest.windows.chrome.json"
$ManifestChromeJson = @{
  name = "netcode.io";
  description = "netcode.io helper";
  path = $HostPath.Path;
  type = "stdio";
  allowed_origins = @(
    "chrome-extension://fkcdbgdmpjenlkecdjadcpnkchaecbpn/",
    "chrome-extension://hpecmifakhimhidjpcpjmihpacijicbd/"
  );
}
ConvertTo-Json $ManifestChromeJson | Out-File -Encoding UTF8 $ManifestChromeJsonPath
$ManifestFirefoxJsonPath = "$(pwd)\manifest.windows.firefox.json"
$ManifestFirefoxJson = @{
  name = "netcode.io";
  description = "netcode.io helper";
  path = $HostPath.Path;
  type = "stdio";
  allowed_extensions = @(
    "{1af46c0f-6130-426a-b504-1f5b8295a173}"
  );
}
ConvertTo-Json $ManifestFirefoxJson | Out-File -Encoding UTF8 $ManifestFirefoxJsonPath

$RegistryPath = "HKCU:\SOFTWARE\Google\Chrome\NativeMessagingHosts\netcode.io"
if (!(Test-Path $RegistryPath)) {
  New-Item -ItemType Directory $RegistryPath
}
New-ItemProperty -Path $RegistryPath -Name "(Default)" -Value $ManifestChromeJsonPath -Force | Out-Null
$RegistryPath = "HKCU:\SOFTWARE\Mozilla\NativeMessagingHosts\netcode.io"
if (!(Test-Path $RegistryPath)) {
  New-Item -ItemType Directory $RegistryPath
}
New-ItemProperty -Path $RegistryPath -Name "(Default)" -Value $ManifestFirefoxJsonPath -Force | Out-Null