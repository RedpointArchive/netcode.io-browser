## Install

1. get dependencies
```bash
go get github.com/wirepair/netcode

```

2. Platform specific build instructions

* `go build` - for the current OS
* `env GOOS=windows GOARCH=amd64 go build` - to build for Windows (linux-style to set environment variables)
* `env GOOS=linux GOARCH=amd64 go build` - to build for Linux (linux-style to set environment variables)
* `env GOOS=darwin GOARCH=amd64 go build` - to build for Mac (linux-style to set environment variables)

3. install

`./netcode.io.host`

This should run with no errors, and install itself as a native messaging extension.
