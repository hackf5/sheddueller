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
    const timestamps = read(model, "Timestamps") ?? [];
    const modelSeries = chartSeries(model);
    const seriesKeys = modelSeries.map((series) => series.key).join("|");
    const data = [timestamps, ...modelSeries.map((series) => series.values)];
    const scale = scaleInfo(timestamps, modelSeries);
    const timeScale = timeScaleRange(model, timestamps);
    const existing = charts.get(element);
    if (existing?.kind === "uplot" && existing.seriesKeys === seriesKeys) {
      existing.chart.setSize(chartSize(element));
      existing.chart.setData(data, false);
      applyScales(existing.chart, timeScale, scale.max);
      renderClipMarkers(element, scale.clipped, timestamps, existing.chart);
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
        x: { time: true, range: [timeScale.min, timeScale.max] },
        y: { range: [0, scale.max] },
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
        ...modelSeries.map((modelSeries) => series(modelSeries.label, modelSeries.color)),
      ],
    };

    const chart = new window.uPlot(options, data, element);
    applyScales(chart, timeScale, scale.max);
    charts.set(element, { kind: "uplot", chart, seriesKeys });
    renderClipMarkers(element, scale.clipped, timestamps, chart);
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
    const timestamps = read(model, "Timestamps") ?? [];
    const datasets = chartSeries(model);
    const pointCount = timestamps.length;
    const scale = scaleInfo(timestamps, datasets);
    const maxValue = scale.max;

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
        const value = Math.min(dataset.values[index] ?? 0, maxValue);
        const y = padding.top + plotHeight - (plotHeight * value) / maxValue;
        if (index === 0) {
          context.moveTo(x, y);
        } else {
          context.lineTo(x, y);
        }
      }

      context.stroke();
    }

    renderClipMarkers(canvas.parentElement, scale.clipped, timestamps);
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

  function applyScales(chart, timeScale, yMax) {
    chart.setScale("x", timeScale);
    chart.setScale("y", { min: 0, max: yMax });
  }

  function timeScaleRange(model, timestamps) {
    const min = read(model, "WindowStartUnixSeconds") ?? timestamps[0] ?? 0;
    const max = read(model, "WindowEndUnixSeconds") ?? timestamps[timestamps.length - 1] ?? min;
    return { min, max: Math.max(min + 1, max) };
  }

  function chartSeries(model) {
    return (read(model, "Series") ?? []).map((series) => ({
      key: read(series, "Key"),
      label: read(series, "Label"),
      color: read(series, "Color"),
      values: read(series, "Values") ?? [],
    }));
  }

  function scaleInfo(timestamps, series) {
    const values = series
      .flatMap((series) => Array.from(series.values))
      .filter((value) => Number.isFinite(value) && value > 0)
      .sort((left, right) => left - right);

    if (values.length === 0) {
      return { max: 1, clipped: [] };
    }

    const max = values[values.length - 1];
    if (values.length === 1) {
      const yMax = niceMax(max * 1.1);
      return { max: yMax, clipped: clippedPoints(timestamps, series, yMax) };
    }

    const p95 = values[Math.floor((values.length - 1) * 0.95)];
    const target = max > p95 * 5 ? p95 * 1.25 : max * 1.05;
    const yMax = niceMax(Math.max(1, target));
    return { max: yMax, clipped: clippedPoints(timestamps, series, yMax) };
  }

  function clippedPoints(timestamps, series, yMax) {
    return series.flatMap((series) =>
      series.values
        .map((value, index) => ({
          timestamp: timestamps[index],
          value,
          label: series.label,
          color: series.color,
        }))
        .filter((point) => Number.isFinite(point.timestamp) && Number.isFinite(point.value) && point.value > yMax),
    );
  }

  function renderClipMarkers(element, clipped, timestamps, chart) {
    element.querySelector(".throughput-chart__clip-layer")?.remove();
    if (clipped.length === 0) {
      return;
    }

    const bounds = plotBounds(element, chart);
    const start = timestamps[0] ?? 0;
    const end = timestamps[timestamps.length - 1] ?? start;
    const span = Math.max(1, end - start);
    const layer = document.createElement("div");
    layer.className = "throughput-chart__clip-layer";
    layer.setAttribute("aria-hidden", "true");

    const timestampCounts = new Map();
    for (const point of clipped) {
      const stackIndex = timestampCounts.get(point.timestamp) ?? 0;
      timestampCounts.set(point.timestamp, stackIndex + 1);
      const marker = document.createElement("span");
      marker.className = "throughput-chart__clip-marker";
      marker.title = `${point.label}: ${point.value.toLocaleString()} clipped above visible scale`;
      marker.style.backgroundColor = point.color;
      marker.style.left = `${xPosition(point.timestamp, start, span, bounds, chart)}px`;
      marker.style.top = `${bounds.top + 2 + stackIndex * 8}px`;
      layer.appendChild(marker);
    }

    element.appendChild(layer);
  }

  function plotBounds(element, chart) {
    if (chart?.bbox) {
      const rect = element.getBoundingClientRect();
      const scale = chart.bbox.width > rect.width ? window.devicePixelRatio || 1 : 1;
      return {
        left: chart.bbox.left / scale,
        top: chart.bbox.top / scale,
        width: chart.bbox.width / scale,
        height: chart.bbox.height / scale,
      };
    }

    return {
      left: 40,
      top: 14,
      width: Math.max(1, element.getBoundingClientRect().width - 58),
      height: Math.max(1, element.getBoundingClientRect().height - 38),
    };
  }

  function xPosition(timestamp, start, span, bounds, chart) {
    if (chart?.valToPos) {
      const position = chart.valToPos(timestamp, "x");
      if (Number.isFinite(position)) {
        return position <= bounds.width + 1 ? bounds.left + position : position;
      }
    }

    return bounds.left + bounds.width * ((timestamp - start) / span);
  }

  function niceMax(value) {
    const exponent = Math.floor(Math.log10(value));
    const magnitude = 10 ** exponent;
    const normalized = value / magnitude;
    let niceNormalized;
    const steps = [1, 1.25, 1.5, 2, 2.5, 5, 7.5, 10];
    niceNormalized = steps.find((step) => normalized <= step) ?? 10;

    return niceNormalized * magnitude;
  }

  function axisColor() {
    return getComputedStyle(document.documentElement).getPropertyValue("--sd-on-surface-variant").trim() || "#45464d";
  }

  function gridColor() {
    return getComputedStyle(document.documentElement).getPropertyValue("--sd-outline-variant").trim() || "#c6c6cd";
  }

  window.shedduellerThroughputChart = { render, destroy };
})();
