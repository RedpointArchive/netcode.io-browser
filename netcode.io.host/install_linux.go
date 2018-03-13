package main

import "path/filepath"

func getNetcodeBinaryDir(userHomeDir string) string {
	return filepath.Join(userHomeDir, ".netcode")
}

func getFirefoxNativeExtensionsDir(userHomeDir string) string {
	return filepath.Join(userHomeDir, ".mozilla", "native-messaging-hosts")
}

func getChromeNativeExtensionsDir(userHomeDir string) string {
	return filepath.Join(userHomeDir, ".config", "google-chrome", "NativeMessagingHosts")
}

func registerManifests(firefoxManifestPath, chromeManifestPath string) error {
	return nil
}
