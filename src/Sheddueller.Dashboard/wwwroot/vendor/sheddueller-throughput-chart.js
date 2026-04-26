(function () {
  const charts = new WeakMap();

  function read(model, key) {
    const lower = key.charAt(0).toLowerCase() + key.slice(1);
    return model[lower] ?? model[key];
  }

  function destroy(element) {
    const existing = charts.get(element);
    if (!existing) {
      return;
    }

    if (existing.kind === "uplot") {
      existing.chart.destroy();
    }

    charts.delete(element);
    element.replaceChildren();
  }

  function render(element, model) {
    if (!element || !model) {
      return;
    }

    if (window.uPlot) {
      renderUPlot(element, model);
      return;
    }

    renderCanvas(element, model);
  }

  function renderUPlot(element, model) {
    const data = [
      read(model, "Timestamps"),
      read(model, "Queued"),
      read(model, "Started"),
      read(model, "Succeeded"),
      read(model, "Failed"),
      read(model, "Canceled"),
      read(model, "FailedAttempts"),
    ];
    const existing = charts.get(element);
    if (existing?.kind === "uplot") {
      existing.chart.setData(data);
      existing.chart.setSize(chartSize(element));
      return;
    }

    destroy(element);
    const options = {
      ...chartSize(element),
      cursor: {
        drag: { x: false, y: false },
      },
      legend: {
        show: false,
      },
      scales: {
        x: { time: true },
        y: { range: [0, null] },
      },
      axes: [
        {
          stroke: axisColor(),
          grid: { stroke: gridColor() },
        },
        {
          stroke: axisColor(),
          grid: { stroke: gridColor() },
          values: (_, values) => values.map((value) => value.toLocaleString()),
        },
      ],
      series: [
        {},
        series("Queued", "#515f74"),
        series("Started", "#1d4ed8"),
        series("Succeeded", "#166534"),
        series("Failed", "#ba1a1a"),
        series("Canceled", "#8a5a00"),
        series("Failed Attempts", "#7c3aed"),
      ],
    };

    const chart = new window.uPlot(options, data, element);
    charts.set(element, { kind: "uplot", chart });
  }

  function renderCanvas(element, model) {
    let existing = charts.get(element);
    if (existing?.kind !== "canvas") {
      destroy(element);
      const canvas = document.createElement("canvas");
      canvas.className = "throughput-chart__canvas";
      element.appendChild(canvas);
      existing = { kind: "canvas", canvas };
      charts.set(element, existing);
    }

    drawCanvas(existing.canvas, model);
  }

  function drawCanvas(canvas, model) {
    const rect = canvas.parentElement.getBoundingClientRect();
    const dpr = window.devicePixelRatio || 1;
    const width = Math.max(320, Math.floor(rect.width));
    const height = Math.max(220, Math.floor(rect.height));
    canvas.width = width * dpr;
    canvas.height = height * dpr;
    canvas.style.width = `${width}px`;
    canvas.style.height = `${height}px`;

    const context = canvas.getContext("2d");
    context.setTransform(dpr, 0, 0, dpr, 0, 0);
    context.clearRect(0, 0, width, height);

    const padding = { top: 14, right: 18, bottom: 24, left: 40 };
    const plotWidth = width - padding.left - padding.right;
    const plotHeight = height - padding.top - padding.bottom;
    const datasets = [
      { values: read(model, "Queued"), color: "#515f74" },
      { values: read(model, "Started"), color: "#1d4ed8" },
      { values: read(model, "Succeeded"), color: "#166534" },
      { values: read(model, "Failed"), color: "#ba1a1a" },
      { values: read(model, "Canceled"), color: "#8a5a00" },
      { values: read(model, "FailedAttempts"), color: "#7c3aed" },
    ];
    const pointCount = datasets[0].values.length;
    const maxValue = Math.max(1, ...datasets.flatMap((dataset) => dataset.values));

    context.strokeStyle = gridColor();
    context.lineWidth = 1;
    for (let index = 0; index <= 4; index++) {
      const y = padding.top + (plotHeight / 4) * index;
      context.beginPath();
      context.moveTo(padding.left, y);
      context.lineTo(width - padding.right, y);
      context.stroke();
    }

    context.fillStyle = axisColor();
    context.font = "11px ui-monospace, SFMono-Regular, Menlo, Consolas, monospace";
    context.textAlign = "right";
    context.textBaseline = "middle";
    for (let index = 0; index <= 4; index++) {
      const value = Math.round(maxValue - (maxValue / 4) * index);
      const y = padding.top + (plotHeight / 4) * index;
      context.fillText(value.toLocaleString(), padding.left - 8, y);
    }

    for (const dataset of datasets) {
      context.strokeStyle = dataset.color;
      context.lineWidth = 1.5;
      context.beginPath();
      for (let index = 0; index < pointCount; index++) {
        const x = padding.left + (plotWidth * index) / Math.max(1, pointCount - 1);
        const y = padding.top + plotHeight - (plotHeight * dataset.values[index]) / maxValue;
        if (index === 0) {
          context.moveTo(x, y);
        } else {
          context.lineTo(x, y);
        }
      }

      context.stroke();
    }
  }

  function series(label, stroke) {
    return {
      label,
      stroke,
      width: 1.5,
      points: { show: false },
    };
  }

  function chartSize(element) {
    const rect = element.getBoundingClientRect();
    return {
      width: Math.max(320, Math.floor(rect.width)),
      height: Math.max(220, Math.floor(rect.height)),
    };
  }

  function axisColor() {
    return getComputedStyle(document.documentElement).getPropertyValue("--sd-on-surface-variant").trim() || "#45464d";
  }

  function gridColor() {
    return getComputedStyle(document.documentElement).getPropertyValue("--sd-outline-variant").trim() || "#c6c6cd";
  }

  window.shedduellerThroughputChart = { render, destroy };
})();
