mergeInto(LibraryManager.library, {
  $PrototirBridge: {
    source: 'prototir',
    version: 1,
    installed: false,
    targetOrigin: function () {
      try {
        var url = new URL(window.location.href);
        return url.searchParams.get('prototir_origin') ||
          new URLSearchParams(url.hash.replace(/^#/, '')).get('prototir_origin') || '*';
      } catch (_) {
        return '*';
      }
    },
    post: function (message) {
      if (window.parent === window) return;
      message.source = PrototirBridge.source;
      message.v = PrototirBridge.version;
      window.parent.postMessage(message, PrototirBridge.targetOrigin());
    },
    install: function () {
      if (PrototirBridge.installed) return;
      PrototirBridge.installed = true;
      window.addEventListener('message', function (event) {
        if (event.source !== window.parent) return;
        var message = event.data;
        if (!message || message.source !== PrototirBridge.source || message.v !== PrototirBridge.version) return;
        if (message.type === 'storage:result') {
          SendMessage('__PrototirBridge', 'OnPrototirStorageResult', JSON.stringify(message));
        } else if (message.type === 'ai:result') {
          SendMessage('__PrototirBridge', 'OnPrototirAiResult', JSON.stringify(message));
        }
      });
    }
  },

  Prototir_Install__deps: ['$PrototirBridge'],
  Prototir_Install: function () {
    PrototirBridge.install();
  },

  Prototir_Ready__deps: ['$PrototirBridge'],
  Prototir_Ready: function () {
    PrototirBridge.post({ type: 'ready' });
  },

  Prototir_Event__deps: ['$PrototirBridge'],
  Prototir_Event: function (namePointer, dataPointer) {
    var message = { type: 'event', name: UTF8ToString(namePointer) };
    var json = UTF8ToString(dataPointer);
    if (json) {
      try {
        var data = JSON.parse(json);
        if (data && typeof data === 'object' && !Array.isArray(data)) message.data = data;
      } catch (_) {}
    }
    PrototirBridge.post(message);
  },

  Prototir_Score__deps: ['$PrototirBridge'],
  Prototir_Score: function (value) {
    PrototirBridge.post({ type: 'score', value: value });
  },

  Prototir_Storage__deps: ['$PrototirBridge'],
  Prototir_Storage: function (operationPointer, keyPointer, valuePointer, id) {
    var operation = UTF8ToString(operationPointer);
    var message = {
      type: 'storage',
      op: operation,
      key: UTF8ToString(keyPointer),
      id: id
    };
    if (operation === 'set') message.value = UTF8ToString(valuePointer);
    PrototirBridge.post(message);
  },

  Prototir_Ai__deps: ['$PrototirBridge'],
  Prototir_Ai: function (promptPointer, maxTokens, id) {
    var message = {
      type: 'ai',
      prompt: UTF8ToString(promptPointer),
      id: id
    };
    if (maxTokens > 0) message.maxTokens = maxTokens;
    PrototirBridge.post(message);
  }
});
