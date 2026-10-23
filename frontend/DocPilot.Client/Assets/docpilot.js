// Ponašanje stranice odgovora u WebView2 (bez inline skripti, zbog Content-Security-Policy).
(function () {
  "use strict";

  // poruka aplikaciji (MarkdownView); van WebView2 (npr. u browseru) se samo preskače
  function post(message) {
    if (window.chrome && window.chrome.webview) {
      window.chrome.webview.postMessage(message);
    }
  }

  // visina sadržaja, da bi balončić odgovora bio tačno toliko visok
  var lastHeight = 0;
  function reportHeight() {
    var h = Math.ceil(document.documentElement.scrollHeight);
    if (h !== lastHeight) {
      lastHeight = h;
      post({ type: "height", value: h });
    }
  }

  // linkovi se ne otvaraju u balončiću nego u browseru (odlučuje aplikacija)
  document.addEventListener("click", function (e) {
    var a = e.target.closest ? e.target.closest("a") : null;
    if (a && a.href) {
      e.preventDefault();
      post({ type: "link", href: a.href });
    }
  });

  document.addEventListener("DOMContentLoaded", function () {
    if (window.hljs) {
      window.hljs.highlightAll();
    }
    reportHeight();
    // slike se učitavaju naknadno i menjaju visinu
    Array.prototype.forEach.call(document.images, function (img) {
      img.addEventListener("load", reportHeight);
      img.addEventListener("error", reportHeight);
    });
    if (window.ResizeObserver) {
      new ResizeObserver(reportHeight).observe(document.body);
    }
  });
  window.addEventListener("resize", reportHeight);
})();
