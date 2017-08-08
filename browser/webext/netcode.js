var _typeCreateClient = 101;
var _typeSetClientTickRate = 102;
var _typeConnectClient = 103;
var _typeSendPacket = 104;
var _typeReceivePacket = 105;
var _typeGetClientState = 106;
var _typeDestroyClient = 107;
var _typeClientDestroyed = 108;
var _typeCheckPresence = 109;
var _typeClientStateChanged = 110;
var _resultClientCreated = 201;
var _resultSuccess = 202;
var _resultError = 203;
var _resultErrorInternal = 204;

var _callbacks = {};
var _clients = {};

var _createMessage = function(callback) {
  var min = -2147483647;
  var max = 2147483646;
  var messageId = Math.floor(Math.random() * (max - min + 1)) + min;
  while (_callbacks[messageId] != undefined) {
    messageId = Math.floor(Math.random() * (max - min + 1)) + min;
  }
  _callbacks[messageId] = callback;
  return messageId;
}

var setTickRate = function(clientId, tickRate, callback) {
  var messageId = _createMessage(function(type, args) {
    if (type == _resultSuccess) {
      callback(null);
    } else {
      // Error
      callback(new Error(args[0]));
    }
  });
  window.postMessage({
    type: "netcode.io-send",
    message: [_typeSetClientTickRate, messageId, clientId, tickRate]
  }, "*");
}

var connect = function(clientId, token, callback) {
  var messageId = _createMessage(function(type, args) {
    if (type == _resultSuccess) {
      callback(null);
    } else {
      // Error
      callback(new Error(args[0]));
    }
  });
  window.postMessage({
    type: "netcode.io-send",
    message: [_typeConnectClient, messageId, clientId, bufferToB64(token)]
  }, "*");
}

var send = function(clientId, packetBuffer, callback) {
  var messageId = _createMessage(function(type, args) {
    if (type == _resultSuccess) {
      callback(null);
    } else {
      // Error
      callback(new Error(args[0]));
    }
  });
  window.postMessage({
    type: "netcode.io-send",
    message: [_typeSendPacket, messageId, clientId, bufferToB64(packetBuffer)]
  }, "*");
}

var getClientState = function(clientId, callback) {
  var messageId = _createMessage(function(type, args) {
    if (type == _resultSuccess) {
      callback(null, args[0]);
    } else {
      // Error
      callback(new Error(args[0]), null);
    }
  });
  window.postMessage({
    type: "netcode.io-send",
    message: [_typeGetClientState, messageId, clientId]
  }, "*");
}

var destroy = function(clientId, callback) {
  var messageId = _createMessage(function(type, args) {
    if (type == _resultSuccess) {
      callback(null);
    } else {
      // Error
      callback(new Error(args[0]));
    }
  });
  window.postMessage({
    type: "netcode.io-send",
    message: [_typeDestroyClient, messageId, clientId]
  }, "*");
}

var b64ToBuffer = function(b64) {
  var raw = window.atob(b64);
  var rawLength = raw.length;
  var array = new Uint8Array(new ArrayBuffer(rawLength));

  for(i = 0; i < rawLength; i++) {
    array[i] = raw.charCodeAt(i);
  }
  return array;
}

var bufferToB64 = function(buffer) {
  return btoa(String.fromCharCode.apply(null, buffer));
}

window.netcode = {
  isNativeHelperInstalled: function(callback) {
    var handle = window.setTimeout(function() {
      callback(null, false);
    }, 2000);
    var messageId = _createMessage(function(type, args) {
      window.clearTimeout(handle);
      if (type == _resultSuccess) {
        if (args.length >= 1 && args[0] == '0.1.0') {
          callback(null, true);
        } else {
          // Mismatched version, treat as missing so users are
          // prompted to install it again.
          callback(null, false);
        }
      } else {
        callback(new Error(args[0]), null);
      }
    });
    window.postMessage({
      type: "netcode.io-send",
      message: [_typeCheckPresence, messageId]
    }, "*");
  },
  createClient: function(protocol, callback) {
    var messageId = _createMessage(function(type, args) {
      if (type == _resultClientCreated) {
        var clientId = args[0];
        _clients[clientId] = {
          _recvCallbacks: [],
          _stateChangeCallbacks: [],
          _isDestroyed: false,
          setTickRate: function(tickRate, callback) {
            if (this._isDestroyed) {
              callback(new Error('client has been destroyed'));
            }
            setTickRate(clientId, tickRate, callback);
          },
          connect: function(token, callback) {
            if (this._isDestroyed) {
              callback(new Error('client has been destroyed'));
            }
            connect(clientId, token, callback);
          },
          send: function(packetBuffer, callback) {
            if (this._isDestroyed) {
              callback(new Error('client has been destroyed'));
            }
            send(clientId, packetBuffer, callback);
          },
          getClientState: function(callback) {
            if (this._isDestroyed) {
              callback(null, 'destroyed');
            }
            getClientState(clientId, callback);
          },
          destroy: function(callback) {
            if (this._isDestroyed) {
              // calling destroy on already destroyed client is okay
              callback(null);
            }
            destroy(clientId, callback);
          },
          addEventListener: function(type, callback) {
            if (type == "receive") {
              this._recvCallbacks.push(callback);
            }
            else if(type == "stateChange") {
              this._stateChangeCallbacks.push(callback);
            }
          },
        };
        callback(null, _clients[clientId]);
      } else {
        // Error
        callback(new Error(args[0]), null);
      }
    });
    window.postMessage({
      type: "netcode.io-send",
      message: [_typeCreateClient, messageId, protocol == 'ipv6']
    }, "*");
  },
}

window.addEventListener("message", function(event) {
  if (event.data.type == "netcode.io-recv") {
    var resultType = event.data.message[0];
    if (resultType == _typeReceivePacket) {
      // Received a packet.
      var clientId = event.data.message[1];
      var base64Data = event.data.message[2];
      var buffer = b64ToBuffer(base64Data);
      if (_clients[clientId] != undefined) {
        for (var i = 0; i < _clients[clientId]._recvCallbacks.length; i++) {
          _clients[clientId]._recvCallbacks[i](clientId, buffer);
        }
      }
    } else if (resultType == _typeClientDestroyed) {
      // Client destroyed.
      var clientId = event.data.message[1];
      _clients[clientId]._isDestroyed = true;
      delete _clients[clientId];
      // TODO: Fire event before deletion.
    } else if (resultType == _typeClientStateChanged) {
      // Client state changed
      var clientId = event.data.message[1];
      var stateStr = event.data.message[2];
      if(_clients[clientId] != undefined) {
        for (var i = 0; i < _clients[clientId]._stateChangeCallbacks.length; i++) {
          _clients[clientId]._stateChangeCallbacks[i](clientId, stateStr);
        }
      }
    } else {
      // Received a return value.
      var messageId = event.data.message[1];
      if (_callbacks[messageId] != undefined) {
        event.data.message.splice(0, 2);
        _callbacks[messageId](resultType, event.data.message);
        delete _callbacks[messageId];
      }
    }
  }
}, false);
