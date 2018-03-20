param([Switch] $Run = $false, [Switch] $Push = $false)

Push-Location $PSScriptRoot\..\netcode.io.demoserver
dotnet publish -c release -r linux-x64 .\netcode.io.demoserver.sln
if ($LASTEXITCODE -ne 0) { exit 1; }
docker build . -t redpointgames/netcode-demo-server:latest
if ($LASTEXITCODE -ne 0) { exit 1; }
if ($Push) {
  docker push redpointgames/netcode-demo-server:latest
  if ($LASTEXITCODE -ne 0) { exit 1; }
}
if ($Run) {
  try {
    docker stop netcodeio-demo-server
  } catch { }
  docker run --rm --name netcodeio-demo-server -p 127.0.0.1:8080:8080 -p 127.0.0.1:40000:40000/udp redpointgames/netcode-demo-server:latest --non-interactive --server-address 127.0.0.1 --server-port 40000 --http-address "+" --http-port 8080
}