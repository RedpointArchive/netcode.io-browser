var nativePorts = {};

chrome.tabs.onRemoved.addListener(function(tabId, removed) {
  if (nativePorts[tabId] != undefined) {
    nativePorts[tabId].disconnect();
  }
});

chrome.runtime.onMessage.addListener(
  function(request, sender, sendResponse) {
    if (!sender.tab) {
      console.warn("[netcode.io] message did not originate from a tab; ignoring...");
      return;
    }

    if (nativePorts[sender.tab.id] == undefined) {
      nativePorts[sender.tab.id] = chrome.runtime.connectNative("netcode.io");
      nativePorts[sender.tab.id].onMessage.addListener(function(message) {
        chrome.tabs.sendMessage(
          sender.tab.id,
          message);
      });
      nativePorts[sender.tab.id].onDisconnect.addListener(function() {
        delete nativePorts[sender.tab.id];
      });
    }

    nativePorts[sender.tab.id].postMessage(request);
  });