package main

import (
	"io"
	"io/ioutil"
	"os"
	"os/user"
	"path/filepath"
	"strings"
)

const (
	extensionName = "netcode.io"

	firefoxManifest = `{
    "path":  "__PATH__",
    "description":  "__NAME__ helper",
    "name":  "__NAME__",
    "allowed_extensions": [
        "{1af46c0f-6130-426a-b504-1f5b8295a173}",
        "{4279ff57-aad6-4990-bf0b-2010a55ed5d5}"
    ],
    "type":  "stdio"
}`

	chromeManifest = `{
    "path":  "__PATH__",
    "description":  "__NAME__ helper",
    "name":  "__NAME__",
    "allowed_origins":  [
        "chrome-extension://fkcdbgdmpjenlkecdjadcpnkchaecbpn/",
        "chrome-extension://hpecmifakhimhidjpcpjmihpacijicbd/"
    ],
    "type":  "stdio"
}`
)

func installNetcode() error {
	currentUser, err := user.Current()
	if err != nil {
		return err
	}

	netcodeBinaryPath, err := copyNetcodeBinary(currentUser.HomeDir)
	if err != nil {
		return err
	}

	firefoxNativeExtensionsDir := getFirefoxNativeExtensionsDir(currentUser.HomeDir)
	firefoxManifestPath, err := createManifestFile(firefoxNativeExtensionsDir, firefoxManifest, netcodeBinaryPath)
	if err != nil {
		return err
	}

	chromeNativeExtensionsDir := getChromeNativeExtensionsDir(currentUser.HomeDir)
	chromeManifestPath, err := createManifestFile(chromeNativeExtensionsDir, chromeManifest, netcodeBinaryPath)
	if err != nil {
		return err
	}

	err = registerManifests(firefoxManifestPath, chromeManifestPath)
	if err != nil {
		return err
	}

	return nil
}

func copyNetcodeBinary(userHomeDir string) (string, error) {
	netcodeBinaryDir := getNetcodeBinaryDir(userHomeDir)
	err := os.MkdirAll(netcodeBinaryDir, 0777)
	if err != nil {
		return "", err
	}

	netcodeBinaryPath := filepath.Join(netcodeBinaryDir, filepath.Base(os.Args[0]))
	dest, err := os.OpenFile(netcodeBinaryPath, os.O_CREATE|os.O_WRONLY, 0744)
	if err != nil {
		return "", err
	}
	defer dest.Close()

	source, err := os.Open(os.Args[0])
	if err != nil {
		return "", err
	}
	defer source.Close()

	_, err = io.Copy(dest, source)
	return netcodeBinaryPath, err
}

func createManifestFile(manifestDir, manifestTemplate, netcodeBinaryPath string) (string, error) {
	err := os.MkdirAll(manifestDir, 0777)
	if err != nil {
		return "", err
	}
	manifestPath := filepath.Join(manifestDir, extensionName+".json")
	manifest := strings.Replace(manifestTemplate, "__PATH__", strings.Replace(netcodeBinaryPath, "\\", "\\\\", -1), -1)
	manifest = strings.Replace(manifest, "__NAME__", extensionName, -1)
	err = ioutil.WriteFile(manifestPath, []byte(manifest), 0644)
	return manifestPath, err
}
