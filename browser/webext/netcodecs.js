// Inject netcode APIs into page
var s = document.createElement("script");
s.src = chrome.extension.getURL('netcode.js');
s.async = false;
document.documentElement.appendChild(s);

// Handle API call messages from page
window.addEventListener("message", function(event) {
  if (event.source != window) {
    return;
  }
  if (event.data.type != "netcode.io-send") {
    return;
  }

  // Pass message up to extension
  chrome.runtime.sendMessage(event.data.message);
}, false);

// Handle responses from extension
chrome.runtime.onMessage.addListener(function(message, sender, sendResponse) {
  window.postMessage({
    type: "netcode.io-recv",
    message: message,
  }, "*");
});