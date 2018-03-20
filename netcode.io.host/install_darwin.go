package main

import "path/filepath"

func getNetcodeBinaryDir(userHomeDir string) string {
	return filepath.Join(userHomeDir, ".netcode")
}

func getFirefoxNativeExtensionsDir(userHomeDir string) string {
	return filepath.Join(userHomeDir, "Library", "Application Support", "Mozilla", "NativeMessagingHosts")
}

func getChromeNativeExtensionsDir(userHomeDir string) string {
	return filepath.Join(userHomeDir, "Library", "Application Support", "Google", "Chrome", "NativeMessagingHosts")
}

func registerManifests(firefoxManifestPath, chromeManifestPath string) error {
	return nil
}
