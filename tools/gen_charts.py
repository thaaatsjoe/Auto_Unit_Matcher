"""
Generate a single beautiful dashboard HTML from the Optuna SQLite study.

Outputs:
  tools/charts/dashboard.html  — self-contained, auto-refreshable
  tools/charts/trial_data.json — raw data for the dashboard

Usage:
  python tools/gen_charts.py          # generate dashboard
  python tools/gen_charts.py --watch  # regenerate every 60s (leave running)
"""
import sqlite3
import json
import os
import sys
import time
from pathlib import Path

DB_PATH = "tools/optuna_cpu_study.db"
OUT_DIR = "tools/charts"
os.makedirs(OUT_DIR, exist_ok=True)


def read_trials():
    conn = sqlite3.connect(DB_PATH)
    rows = conn.execute("""
        SELECT t.number,
               tv_vs.param_value,
               tv_kp.param_value,
               tv_sr.param_value,
               tv_id.param_value,
               tv.value,
               t.datetime_start,
               t.datetime_complete
        FROM trials t
        JOIN trial_values tv ON t.trial_id = tv.trial_id
        LEFT JOIN trial_params tv_vs ON t.trial_id = tv_vs.trial_id AND tv_vs.param_name='VoxelSize'
        LEFT JOIN trial_params tv_kp ON t.trial_id = tv_kp.trial_id AND tv_kp.param_name='KeypointVoxelSize'
        LEFT JOIN trial_params tv_sr ON t.trial_id = tv_sr.trial_id AND tv_sr.param_name='ShotRadius'
        LEFT JOIN trial_params tv_id ON t.trial_id = tv_id.trial_id AND tv_id.param_name='IcpFitnessDecay'
        WHERE t.state = 'COMPLETE'
        ORDER BY t.number
    """).fetchall()

    running = conn.execute("SELECT COUNT(*) FROM trials WHERE state='RUNNING'").fetchone()[0]
    total = conn.execute("SELECT COUNT(*) FROM trials").fetchone()[0]
    conn.close()

    trials = []
    for r in rows:
        trials.append({
            "num": r[0],
            "voxel": round(r[1], 4),
            "kpVoxel": round(r[2], 4),
            "shotR": round(r[3], 4),
            "decay": round(r[4], 4),
            "score": round(r[5], 2),
            "start": r[6],
            "end": r[7],
        })

    # Read live worker status files
    workers = []
    for wf in sorted(Path(OUT_DIR).glob("live_w*.json")):
        try:
            with open(wf) as f:
                workers.append(json.load(f))
        except Exception:
            pass

    return {
        "trials": trials,
        "completed": len(trials),
        "running": running,
        "total": total,
        "generated": time.strftime("%Y-%m-%d %H:%M:%S"),
        "workers": workers,
    }


def build_dashboard(data):
    trials = data["trials"]
    json_str = json.dumps(data, indent=2)

    # Auto-refresh when workers are running
    auto_refresh = '<meta http-equiv="refresh" content="15">' if data.get('running', 0) > 0 else ''

    html = f"""<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="UTF-8">
{auto_refresh}
<title>AUM Optimizer — Live Dashboard</title>
<script src="https://cdn.plot.ly/plotly-2.27.0.min.js"></script>
<style>
  * {{ margin: 0; padding: 0; box-sizing: border-box; }}
  body {{
    font-family: 'Segoe UI', -apple-system, sans-serif;
    background: #0d1117;
    color: #e6edf3;
    padding: 24px;
    line-height: 1.6;
  }}
  .header {{
    display: flex;
    justify-content: space-between;
    align-items: center;
    margin-bottom: 24px;
    flex-wrap: wrap;
    gap: 16px;
  }}
  h1 {{
    font-size: 28px;
    font-weight: 700;
    background: linear-gradient(135deg, #58a6ff, #a371f7);
    -webkit-background-clip: text;
    -webkit-text-fill-color: transparent;
  }}
  .status-bar {{
    display: flex;
    gap: 20px;
    background: #161b22;
    border: 1px solid #30363d;
    border-radius: 8px;
    padding: 12px 20px;
    font-size: 14px;
  }}
  .status-item {{ text-align: center; }}
  .status-item .label {{ color: #8b949e; font-size: 11px; text-transform: uppercase; letter-spacing: 1px; }}
  .status-item .value {{ font-size: 22px; font-weight: 700; color: #58a6ff; }}
  .status-item .value.best {{ color: #3fb950; }}
  .refresh-btn {{
    background: linear-gradient(135deg, #238636, #2ea043);
    border: none;
    color: white;
    padding: 10px 20px;
    border-radius: 6px;
    font-size: 14px;
    cursor: pointer;
    font-weight: 600;
    transition: all 0.2s;
  }}
  .refresh-btn:hover {{ transform: scale(1.05); box-shadow: 0 0 20px rgba(46,160,67,0.3); }}
  .refresh-btn:active {{ transform: scale(0.98); }}
  .refresh-info {{ font-size: 11px; color: #8b949e; margin-top: 4px; text-align: center; }}
  .chart-grid {{
    display: grid;
    grid-template-columns: 1fr 1fr;
    gap: 20px;
    margin-top: 20px;
  }}
  .chart-card {{
    background: #161b22;
    border: 1px solid #30363d;
    border-radius: 12px;
    padding: 20px;
    transition: border-color 0.2s;
  }}
  .chart-card:hover {{ border-color: #58a6ff; }}
  .chart-card.full {{ grid-column: 1 / -1; }}
  .chart-title {{ font-size: 16px; font-weight: 600; margin-bottom: 4px; }}
  .chart-explain {{
    font-size: 13px;
    color: #8b949e;
    margin-bottom: 12px;
    padding: 8px 12px;
    background: #0d1117;
    border-radius: 6px;
    border-left: 3px solid #58a6ff;
  }}
  .best-params {{
    background: #161b22;
    border: 1px solid #238636;
    border-radius: 12px;
    padding: 20px;
    margin-top: 20px;
  }}
  .best-params h2 {{
    color: #3fb950;
    font-size: 18px;
    margin-bottom: 12px;
  }}
  .param-grid {{
    display: grid;
    grid-template-columns: repeat(4, 1fr);
    gap: 12px;
  }}
  .param-box {{
    background: #0d1117;
    border-radius: 8px;
    padding: 12px;
    text-align: center;
  }}
  .param-box .name {{ color: #8b949e; font-size: 12px; }}
  .param-box .val {{ font-size: 20px; font-weight: 700; color: #3fb950; }}
  .param-box .unit {{ color: #8b949e; font-size: 11px; }}
  .leaderboard {{
    margin-top: 20px;
    background: #161b22;
    border: 1px solid #30363d;
    border-radius: 12px;
    padding: 20px;
  }}
  .leaderboard h2 {{ font-size: 18px; margin-bottom: 12px; }}
  table {{ width: 100%; border-collapse: collapse; font-size: 13px; }}
  th {{ text-align: left; padding: 8px; color: #8b949e; border-bottom: 1px solid #30363d; font-weight: 600; }}
  td {{ padding: 8px; border-bottom: 1px solid #21262d; }}
  tr:first-child td {{ color: #3fb950; font-weight: 600; }}
  tr:hover td {{ background: #1c2128; }}
  .footer {{ margin-top: 32px; text-align: center; color: #484f58; font-size: 12px; }}
  .progress-section {{
    margin-top: 20px;
    background: #161b22;
    border: 1px solid #30363d;
    border-radius: 12px;
    padding: 16px 20px;
  }}
  .progress-header {{
    display: flex;
    justify-content: space-between;
    align-items: center;
    margin-bottom: 10px;
  }}
  .progress-header .title {{ font-size: 14px; font-weight: 600; }}
  .progress-header .pct {{ font-size: 22px; font-weight: 700; color: #58a6ff; }}
  .progress-track {{
    width: 100%;
    height: 20px;
    background: #0d1117;
    border-radius: 10px;
    overflow: hidden;
    position: relative;
  }}
  .progress-fill {{
    height: 100%;
    border-radius: 10px;
    background: linear-gradient(90deg, #238636, #2ea043, #3fb950);
    transition: width 0.8s ease;
    position: relative;
  }}
  .progress-fill.active::after {{
    content: '';
    position: absolute;
    top: 0; left: 0; right: 0; bottom: 0;
    background: linear-gradient(90deg, transparent, rgba(255,255,255,0.15), transparent);
    animation: shimmer 2s infinite;
  }}
  @keyframes shimmer {{
    0% {{ transform: translateX(-100%); }}
    100% {{ transform: translateX(100%); }}
  }}
  .progress-labels {{
    display: flex;
    justify-content: space-between;
    margin-top: 8px;
    font-size: 12px;
    color: #8b949e;
  }}
  .progress-labels .eta {{ color: #58a6ff; }}
  .live-section {{
    margin-top: 20px;
    display: grid;
    grid-template-columns: 1fr 1fr;
    gap: 16px;
  }}
  .live-card {{
    background: #161b22;
    border: 1px solid #30363d;
    border-radius: 12px;
    padding: 16px;
    position: relative;
    overflow: hidden;
  }}
  .live-card.active {{ border-color: #58a6ff; }}
  .live-card .pulse {{
    position: absolute;
    top: 12px;
    right: 12px;
    width: 10px;
    height: 10px;
    border-radius: 50%;
    background: #3fb950;
    animation: pulse 1.5s infinite;
  }}
  @keyframes pulse {{
    0%, 100% {{ opacity: 1; }}
    50% {{ opacity: 0.3; }}
  }}
  .live-header {{ font-size: 14px; font-weight: 600; margin-bottom: 8px; }}
  .live-params {{
    display: flex;
    gap: 8px;
    flex-wrap: wrap;
    margin-bottom: 10px;
  }}
  .live-params .tag {{
    background: #0d1117;
    border-radius: 4px;
    padding: 2px 8px;
    font-size: 11px;
    color: #8b949e;
  }}
  .live-params .tag b {{ color: #58a6ff; }}
  .live-mini-progress {{
    width: 100%;
    height: 6px;
    background: #0d1117;
    border-radius: 3px;
    margin: 6px 0;
    overflow: hidden;
  }}
  .live-mini-fill {{
    height: 100%;
    border-radius: 3px;
    background: linear-gradient(90deg, #238636, #3fb950);
    transition: width 0.5s;
  }}
  .live-stats {{
    display: flex;
    justify-content: space-between;
    font-size: 12px;
    color: #8b949e;
    margin-bottom: 8px;
  }}
  .live-stats .acc {{ color: #3fb950; font-weight: 700; }}
  .auto-refresh-badge {{
    display: inline-block;
    background: #0d1117;
    border: 1px solid #30363d;
    border-radius: 12px;
    padding: 2px 10px;
    font-size: 11px;
    color: #8b949e;
    margin-left: 8px;
    animation: pulse 2s infinite;
  }}
  @media (max-width: 900px) {{
    .chart-grid {{ grid-template-columns: 1fr; }}
    .param-grid {{ grid-template-columns: repeat(2, 1fr); }}
    .live-section {{ grid-template-columns: 1fr; }}
  }}
</style>
</head>
<body>

<div class="header">
  <h1>🦷 AUM Optimizer Dashboard</h1>
  <div style="text-align:center">
    <button class="refresh-btn" onclick="refreshData()">🔄 Refresh Data</button>
    {'<span class="auto-refresh-badge">⚡ Auto-refreshing every 15s</span>' if data.get('running', 0) > 0 else ''}
    <div class="refresh-info">Last updated: <span id="lastUpdate">{data['generated']}</span></div>
  </div>
</div>

<div class="status-bar">
  <div class="status-item">
    <div class="label">Completed</div>
    <div class="value" id="statCompleted">{data['completed']}</div>
  </div>
  <div class="status-item">
    <div class="label">Running</div>
    <div class="value" id="statRunning">{data['running']}</div>
  </div>
  <div class="status-item">
    <div class="label">Target</div>
    <div class="value">50</div>
  </div>
  <div class="status-item">
    <div class="label">Best Score</div>
    <div class="value best" id="statBest">{max(t['score'] for t in trials) if trials else 0}</div>
  </div>
  <div class="status-item">
    <div class="label">Best Accuracy</div>
    <div class="value best" id="statAcc">—</div>
  </div>
</div>

<div class="progress-section">
  <div class="progress-header">
    <span class="title">⏳ Optimization Progress</span>
    <span class="pct" id="progressPct">0%</span>
  </div>
  <div class="progress-track">
    <div class="progress-fill" id="progressFill" style="width: 0%"></div>
  </div>
  <div class="progress-labels">
    <span id="progressCount">0 / 50 trials completed</span>
    <span class="eta" id="progressEta"></span>
  </div>
</div>

<div id="liveSection" class="live-section"></div>

<div id="bestParamsSection"></div>

<div class="chart-grid">
  <div class="chart-card full">
    <div class="chart-title">📈 Optimization Progress</div>
    <div class="chart-explain">
      Each dot is one experiment. The <b>red line</b> tracks the best score found so far.
      A rising red line means the optimizer is finding better settings. A flat line means
      it's converging — the best settings have been found.
    </div>
    <div id="chart1" style="height:350px"></div>
  </div>

  <div class="chart-card">
    <div class="chart-title">🔬 Voxel Size vs Score</div>
    <div class="chart-explain">
      <b>Voxel Size</b> controls how detailed the 3D scan is processed. Too small = slow,
      too large = loses detail. Best results cluster around <b>0.25–0.30mm</b>.
    </div>
    <div id="chart2" style="height:300px"></div>
  </div>

  <div class="chart-card">
    <div class="chart-title">📡 Shot Radius vs Score</div>
    <div class="chart-explain">
      <b>Shot Radius</b> is how far each point "looks" to understand its neighborhood shape.
      Larger radius = captures more of the crown's curvature. Sweet spot is <b>1.5–2.0mm</b>.
    </div>
    <div id="chart3" style="height:300px"></div>
  </div>

  <div class="chart-card">
    <div class="chart-title">🎯 Voxel Size vs Shot Radius</div>
    <div class="chart-explain">
      This shows how the two most important settings interact. <b>Bright/large dots = better results.</b>
      The optimizer is searching for the brightest zone.
    </div>
    <div id="chart4" style="height:300px"></div>
  </div>

  <div class="chart-card">
    <div class="chart-title">🔁 ICP Decay vs Score</div>
    <div class="chart-explain">
      <b>ICP Decay</b> controls how aggressively point mismatches are penalized during verification.
      Higher decay = more forgiving. The best results show the ideal strictness level.
    </div>
    <div id="chart5" style="height:300px"></div>
  </div>
</div>

<div class="leaderboard">
  <h2>🏆 Leaderboard — Top 10 Experiments</h2>
  <table>
    <thead>
      <tr><th>Rank</th><th>Trial #</th><th>Score</th><th>Voxel Size</th><th>Keypoint Size</th><th>Shot Radius</th><th>ICP Decay</th></tr>
    </thead>
    <tbody id="leaderboardBody"></tbody>
  </table>
</div>

<div class="footer">
  Auto Unit Matcher — Optuna Hyperparameter Optimization<br>
  To refresh: run <code>python tools/gen_charts.py</code> then click Refresh
</div>

<script>
const DATA = {json_str};

function render(data) {{
  const trials = data.trials;
  if (!trials.length) return;

  document.getElementById('lastUpdate').textContent = data.generated;
  document.getElementById('statCompleted').textContent = data.completed;
  document.getElementById('statRunning').textContent = data.running;

  const best = trials.reduce((a, b) => a.score > b.score ? a : b);
  document.getElementById('statBest').textContent = best.score.toFixed(1);

  // Progress bar
  const pct = Math.round((data.completed / 50) * 100);
  document.getElementById('progressPct').textContent = pct + '%';
  document.getElementById('progressFill').style.width = pct + '%';
  document.getElementById('progressCount').textContent = data.completed + ' / 50 trials completed';
  const fillEl = document.getElementById('progressFill');
  if (data.running > 0) {{
    fillEl.classList.add('active');
    const remaining = 50 - data.completed;
    // Estimate ~30 min per trial with 2 workers
    const etaMin = Math.round(remaining / 2 * 30);
    const etaH = Math.floor(etaMin / 60);
    const etaM = etaMin % 60;
    document.getElementById('progressEta').textContent = 
      '~' + (etaH > 0 ? etaH + 'h ' : '') + etaM + 'm remaining (est.)';
  }} else if (data.completed >= 50) {{
    fillEl.classList.remove('active');
    fillEl.style.background = 'linear-gradient(90deg, #3fb950, #56d364)';
    document.getElementById('progressEta').textContent = '✅ Complete!';
  }} else {{
    fillEl.classList.remove('active');
    document.getElementById('progressEta').textContent = 'Paused — no workers running';
  }}

  // Live worker cards
  const workers = data.workers || [];
  let liveHtml = '';
  workers.forEach((w, idx) => {{
    const phase = w.phase || 'idle';
    const isActive = phase !== 'idle';
    const m = w.matching || {{}};
    const e = w.extraction || {{}};
    const p = w.params || {{}};
    const done = phase === 'matching' ? (m.done || 0) : (e.done || 0);
    const total = phase === 'matching' ? (m.total || 100) : (e.total || 300);
    const pctW = total > 0 ? Math.round(100 * done / total) : 0;
    const acc = m.done > 0 ? (100 * (m.correct || 0) / m.done).toFixed(1) : '—';

    liveHtml += `
      <div class="live-card ${{isActive ? 'active' : ''}}">
        ${{isActive ? '<div class="pulse"></div>' : ''}}
        <div class="live-header">Worker ${{w.worker}} — Trial #${{w.trial}}</div>
        <div class="live-params">
          <span class="tag">V: <b>${{p.VoxelSize || '?'}}</b></span>
          <span class="tag">K: <b>${{p.KpVoxelSize || '?'}}</b></span>
          <span class="tag">S: <b>${{p.ShotRadius || '?'}}</b></span>
          <span class="tag">D: <b>${{p.IcpDecay || '?'}}</b></span>
        </div>
        <div class="live-stats">
          <span>${{phase === 'extracting' ? '🔧 Extracting' : '🔍 Matching'}}: ${{done}}/${{total}}</span>
          ${{phase === 'matching' ? `<span class="acc">${{acc}}% accuracy (${{m.correct || 0}} correct)</span>` : ''}}
        </div>
        <div class="live-mini-progress">
          <div class="live-mini-fill" style="width: ${{pctW}}%; background: ${{phase === 'matching' ? 'linear-gradient(90deg, #58a6ff, #a371f7)' : 'linear-gradient(90deg, #238636, #3fb950)'}}"></div>
        </div>
        ${{phase === 'matching' ? `<div id="liveCurve${{idx}}" style="height:180px; margin-top:8px"></div>` : `<div style="text-align:center;color:#484f58;font-size:12px;padding:20px">Accuracy curve appears during matching phase</div>`}}
        <div style="font-size:11px;color:#484f58;margin-top:4px">Updated: ${{w.timestamp || '—'}}</div>
      </div>`;
  }});

  if (liveHtml) {{
    document.getElementById('liveSection').innerHTML = liveHtml;
  }} else {{
    document.getElementById('liveSection').innerHTML =
      '<div class="live-card" style="grid-column:1/-1;text-align:center;padding:24px;color:#484f58">' +
      'No live workers detected. Start the optimizer to see live progress here.' +
      '</div>';
  }}

  // Render accuracy curves
  workers.forEach((w, idx) => {{
    const curve = w.matching?.accuracy_curve;
    if (curve && curve.length > 0 && w.phase === 'matching') {{
      const xs = curve.map(c => c[0]);
      const ys = curve.map(c => c[1]);
      Plotly.newPlot(`liveCurve${{idx}}`, [
        {{ x: xs, y: ys, mode: 'lines+markers', name: 'Accuracy %',
           line: {{ color: '#58a6ff', width: 2 }},
           marker: {{ size: 4 }},
           fill: 'tozeroy', fillcolor: 'rgba(88,166,255,0.1)' }}
      ], {{
        paper_bgcolor: 'transparent', plot_bgcolor: '#0d1117',
        font: {{ color: '#8b949e', size: 10 }},
        xaxis: {{ title: 'Queries Processed', gridcolor: '#21262d', range: [0, 100] }},
        yaxis: {{ title: 'Accuracy %', gridcolor: '#21262d', range: [0, 100] }},
        margin: {{ l: 40, r: 10, t: 5, b: 30 }},
        showlegend: false,
      }}, {{ responsive: true }});
    }}
  }});

  // Best params section
  document.getElementById('bestParamsSection').innerHTML = `
    <div class="best-params">
      <h2>🏅 Best Settings Found (Trial #${{best.num}})</h2>
      <div class="param-grid">
        <div class="param-box">
          <div class="name">Voxel Size</div>
          <div class="val">${{best.voxel}}</div>
          <div class="unit">mm — scan detail level</div>
        </div>
        <div class="param-box">
          <div class="name">Keypoint Spacing</div>
          <div class="val">${{best.kpVoxel}}</div>
          <div class="unit">mm — feature point density</div>
        </div>
        <div class="param-box">
          <div class="name">Shot Radius</div>
          <div class="val">${{best.shotR}}</div>
          <div class="unit">mm — neighborhood size</div>
        </div>
        <div class="param-box">
          <div class="name">ICP Decay</div>
          <div class="val">${{best.decay}}</div>
          <div class="unit">strictness factor</div>
        </div>
      </div>
    </div>`;

  const nums = trials.map(t => t.num);
  const scores = trials.map(t => t.score);
  const voxels = trials.map(t => t.voxel);
  const shots = trials.map(t => t.shotR);
  const decays = trials.map(t => t.decay);

  // Running best
  let bestSoFar = [], cur = -1;
  scores.forEach(s => {{ cur = Math.max(cur, s); bestSoFar.push(cur); }});

  const dark = {{
    paper_bgcolor: 'transparent', plot_bgcolor: '#0d1117',
    font: {{ color: '#8b949e' }},
    xaxis: {{ gridcolor: '#21262d' }}, yaxis: {{ gridcolor: '#21262d' }},
    margin: {{ l: 50, r: 20, t: 10, b: 40 }}
  }};

  // Chart 1: History
  Plotly.newPlot('chart1', [
    {{ x: nums, y: scores, mode: 'markers', name: 'Score',
       marker: {{ size: 10, color: scores, colorscale: 'Viridis' }} }},
    {{ x: nums, y: bestSoFar, mode: 'lines', name: 'Best So Far',
       line: {{ color: '#f85149', width: 3 }} }}
  ], {{ ...dark, xaxis: {{ ...dark.xaxis, title: 'Trial #' }},
        yaxis: {{ ...dark.yaxis, title: 'Score', range: [0, 100] }} }},
  {{ responsive: true }});

  // Chart 2: Voxel vs Score
  Plotly.newPlot('chart2', [
    {{ x: voxels, y: scores, mode: 'markers', name: 'Voxel',
       marker: {{ size: 12, color: scores, colorscale: 'Viridis' }},
       text: nums.map(n => 'Trial ' + n), hovertemplate: '%{{text}}<br>Voxel: %{{x:.3f}}<br>Score: %{{y:.1f}}' }}
  ], {{ ...dark, xaxis: {{ ...dark.xaxis, title: 'Voxel Size (mm)' }},
        yaxis: {{ ...dark.yaxis, title: 'Score', range: [0, 100] }} }},
  {{ responsive: true }});

  // Chart 3: Shot vs Score
  Plotly.newPlot('chart3', [
    {{ x: shots, y: scores, mode: 'markers', name: 'ShotR',
       marker: {{ size: 12, color: scores, colorscale: 'Viridis' }},
       text: nums.map(n => 'Trial ' + n), hovertemplate: '%{{text}}<br>Radius: %{{x:.3f}}<br>Score: %{{y:.1f}}' }}
  ], {{ ...dark, xaxis: {{ ...dark.xaxis, title: 'Shot Radius (mm)' }},
        yaxis: {{ ...dark.yaxis, title: 'Score', range: [0, 100] }} }},
  {{ responsive: true }});

  // Chart 4: Voxel vs Shot scatter
  Plotly.newPlot('chart4', [
    {{ x: voxels, y: shots, mode: 'markers+text', name: 'Trials',
       text: nums.map(n => 'T' + n), textposition: 'top center',
       textfont: {{ size: 9, color: '#8b949e' }},
       marker: {{ size: scores.map(s => 8 + s/5), color: scores,
                  colorscale: 'Viridis', showscale: true,
                  colorbar: {{ title: 'Score' }} }},
       hovertemplate: 'Trial %{{text}}<br>Voxel: %{{x:.3f}}<br>Shot: %{{y:.3f}}<br>Score: %{{marker.color:.1f}}' }}
  ], {{ ...dark, xaxis: {{ ...dark.xaxis, title: 'Voxel Size (mm)' }},
        yaxis: {{ ...dark.yaxis, title: 'Shot Radius (mm)' }} }},
  {{ responsive: true }});

  // Chart 5: Decay vs Score
  Plotly.newPlot('chart5', [
    {{ x: decays, y: scores, mode: 'markers', name: 'Decay',
       marker: {{ size: 12, color: scores, colorscale: 'Viridis' }},
       text: nums.map(n => 'Trial ' + n), hovertemplate: '%{{text}}<br>Decay: %{{x:.3f}}<br>Score: %{{y:.1f}}' }}
  ], {{ ...dark, xaxis: {{ ...dark.xaxis, title: 'ICP Decay' }},
        yaxis: {{ ...dark.yaxis, title: 'Score', range: [0, 100] }} }},
  {{ responsive: true }});

  // Leaderboard
  const sorted = [...trials].sort((a, b) => b.score - a.score).slice(0, 10);
  document.getElementById('leaderboardBody').innerHTML = sorted.map((t, i) =>
    `<tr><td>${{i+1}}</td><td>T${{t.num}}</td><td>${{t.score.toFixed(1)}}</td>` +
    `<td>${{t.voxel}}</td><td>${{t.kpVoxel}}</td><td>${{t.shotR}}</td><td>${{t.decay}}</td></tr>`
  ).join('');
}}

function refreshData() {{
  fetch('trial_data.json?t=' + Date.now())
    .then(r => r.json())
    .then(data => {{ render(data); }})
    .catch(() => {{
      // If fetch fails (file:// protocol), just reload the page
      window.location.reload();
    }});
}}

render(DATA);
</script>
</body>
</html>"""
    return html


def main():
    watch = "--watch" in sys.argv

    while True:
        data = read_trials()
        print(f"Completed trials: {data['completed']}")
        if data["trials"]:
            best = max(data["trials"], key=lambda t: t["score"])
            print(f"Best score: {best['score']} (Trial {best['num']})")

        # Save JSON (for refresh button)
        json_path = os.path.join(OUT_DIR, "trial_data.json")
        with open(json_path, "w") as f:
            json.dump(data, f, indent=2)

        # Save dashboard HTML
        html = build_dashboard(data)
        html_path = os.path.join(OUT_DIR, "dashboard.html")
        with open(html_path, "w", encoding="utf-8") as f:
            f.write(html)
        print(f"Dashboard saved to {html_path}")

        if not watch:
            break

        print("Watching for changes every 15s (Ctrl+C to stop)...")
        time.sleep(15)


if __name__ == "__main__":
    main()
