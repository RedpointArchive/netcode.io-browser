package main

import (
	"golang.org/x/sys/windows/registry"
	"os"
	"path/filepath"
)

func getNetcodeBinaryDir(userHomeDir string) string {
	rootDir := os.Getenv("LOCALAPPDATA")
	if rootDir == "" {
		rootDir = userHomeDir
	}
	return filepath.Join(rootDir, "netcode.io")
}

func getFirefoxNativeExtensionsDir(userHomeDir string) string {
	return filepath.Join(getNetcodeBinaryDir(userHomeDir), "firefox")
}

func getChromeNativeExtensionsDir(userHomeDir string) string {
	return filepath.Join(getNetcodeBinaryDir(userHomeDir), "chrome")
}

func registerManifests(firefoxManifestPath, chromeManifestPath string) error {
	key, _, err := registry.CreateKey(registry.CURRENT_USER, `SOFTWARE\Mozilla\NativeMessagingHosts\netcode.io`, registry.WRITE)
	if err != nil {
		return err
	}
	err = key.SetStringValue("", firefoxManifestPath)
	if err != nil {
		return err
	}

	key, _, err = registry.CreateKey(registry.CURRENT_USER, `SOFTWARE\Google\Chrome\NativeMessagingHosts\netcode.io`, registry.WRITE)
	if err != nil {
		return err
	}
	err = key.SetStringValue("", chromeManifestPath)
	if err != nil {
		return err
	}

	return nil
}
