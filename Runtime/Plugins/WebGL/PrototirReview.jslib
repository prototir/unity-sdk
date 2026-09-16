mergeInto(LibraryManager.library, {
  $PrototirReviewBridge: { pending: null },
  Prototir_ReviewEnable__deps: ['$PrototirReviewBridge'],
  Prototir_ReviewEnable: function (jsonPointer, receiverPointer) {
    var options = JSON.parse(UTF8ToString(jsonPointer));
    var receiver = UTF8ToString(receiverPointer);
    options.onOpenChange = function (open) { SendMessage(receiver, 'OnReviewVisibility', open ? 'true' : 'false'); };
    options.capture = function () {
      return new Promise(function (resolve, reject) {
        if (PrototirReviewBridge.pending) { reject(new Error('Capture already pending.')); return; }
        var timeout = setTimeout(function () { PrototirReviewBridge.pending = null; reject(new Error('Engine capture timed out.')); }, 8000);
        PrototirReviewBridge.pending = function (data) { clearTimeout(timeout); resolve(data); };
        SendMessage(receiver, 'CaptureReview', '');
      });
    };
    window.Prototir.review.enable(options);
  },
  Prototir_ReviewCaptured__deps: ['$PrototirReviewBridge'],
  Prototir_ReviewCaptured: function (pointer) {
    var pending = PrototirReviewBridge.pending; PrototirReviewBridge.pending = null;
    if (pending) pending(UTF8ToString(pointer));
  },
  Prototir_ReviewDisable: function () { if (window.Prototir) window.Prototir.review.disable(); }
});
