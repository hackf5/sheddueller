window.ShedduellerDashboard = window.ShedduellerDashboard || {};

window.ShedduellerDashboard.highlightCode = function (element) {
  if (!element || !window.Prism || typeof window.Prism.highlightElement !== "function") {
    return;
  }

  window.Prism.highlightElement(element);
};
