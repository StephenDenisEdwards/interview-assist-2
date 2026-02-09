# Evaluation Visualisation and Improvements

**Date:** 2026-02-09
**Status:** Idea

## Summary

Analysis of how the evaluation framework's results could be visualised graphically, and how the evaluation process itself could be improved. The evaluation system already produces rich data (confusion matrices, threshold sweeps, confidence distributions, error patterns, strategy comparisons) -- the missing layer is visualisation and some process gaps.

---

## Part 1: Visualisation Opportunities

### What Data Is Already Available

The evaluation framework produces structured data that is ready for visualisation:

| Data Source | Current Format | Visualisation Opportunity |
|---|---|---|
| `ConfusionMatrix` | Text table via `ToFormattedString()` | Annotated heatmap |
| `ThresholdTuningResult` | Console text | PR curve / threshold sweep line chart |
| `ConfidenceBucket` | Console text | Stacked bar chart (TP vs FP per bucket) |
| `ErrorAnalysisResult` | Console text with counts | Horizontal bar chart |
| `SubtypeMetrics` | Console table | Grouped bar chart (P/R/F1 per subtype) |
| `StrategyComparisonResult` | Console table | Grouped bar chart (P/R/F1 per strategy) |
| `Matches/Missed/FalseAlarms` | Console lists | Per-utterance timeline scatter plot |

### Recommended Visualisations

#### 1. Confusion Matrix Heatmap

A 2D grid where cell colour intensity represents count and the value is overlaid as text. Rows = actual class, columns = predicted class. This is the standard format used by scikit-learn's `ConfusionMatrixDisplay`.

Key considerations:
- Normalise by row to show per-class recall, or by column for per-class precision
- Use a diverging colourmap (white-to-blue) so high-count cells stand out
- With 5-6 intent subtypes, the matrix is small enough to be fully readable

#### 2. Precision/Recall/F1 Charts

- **Strategy comparison:** Grouped bar chart with strategies on x-axis, three coloured bars (P/R/F1) per strategy
- **Per-subtype metrics:** Grouped bar chart with subtypes on x-axis and P/R/F1 as bars
- **Threshold sweep:** Line chart with threshold on x-axis and three lines (Precision, Recall, F1) -- maps directly to `ThresholdTuningResult.Results`

#### 3. PR Curves (Preferred Over ROC)

Plot Precision (y-axis) vs Recall (x-axis) at each confidence threshold. The `ThresholdResult` records already contain exactly these values. PR curves are preferred over ROC for imbalanced classification -- the data is heavily imbalanced since most utterances are not questions.

#### 4. Per-Utterance Timeline

A scatter/strip chart where:
- X-axis = utterance index or time offset
- Colour-coded markers: green = TP, red = FP, yellow = FN
- Hover tooltip shows the question text

This reveals whether errors cluster at particular points in a conversation (e.g., early warm-up, topic transitions).

#### 5. Error Distribution

- **Error patterns:** Horizontal bar chart sorted by count descending (trailing intonation, "I think" statements, relative clauses, etc.)
- **Confidence distribution:** Stacked bar chart with confidence buckets on x-axis, TP (green) vs FP (red) stacked bars -- data already exists in `ConfidenceDistribution`
- **Missed question analysis:** Donut chart showing `MissedAnalysis.BySubtype` breakdown

### Library Recommendations

#### Primary: Plotly.NET.CSharp (HTML Reports)

Self-contained interactive HTML files via `Chart.SaveHtml()`. Supports all five visualisation types: annotated heatmaps, grouped/stacked bars, line charts, scatter plots with hover tooltips. The plotly.js library (~3.5MB) is embedded in each file, making them portable with no server required.

| Visualisation | Plotly.NET Approach |
|---|---|
| Confusion matrix | `Chart.AnnotatedHeatmap` with custom colourscale |
| P/R/F1 bars | `Chart.Bar` with grouped mode |
| PR curve / threshold sweep | `Chart.Line` or `Chart.Scatter` |
| Timeline | `Chart.Scatter` with `Mode.Markers` and per-point colours |
| Error distribution | `Chart.Bar` horizontal |
| Confidence histogram | `Chart.Bar` stacked |

Fits naturally into the existing `ComprehensiveReport` class as a new `ExportHtmlAsync()` method alongside the existing `ExportJsonAsync()`.

#### Secondary: Spectre.Console (Immediate Console Feedback)

For quick visual feedback during `--evaluate` and `--compare` runs without opening a browser:
- Colour-coded metrics tables (green for good, red for poor)
- Horizontal bar chart for error pattern distribution
- Breakdown chart for subtype accuracy proportions

No conflict with Terminal.Gui since evaluation modes run in separate code paths.

#### Tertiary: CSV Export (External Tools)

Flat exports for ad-hoc analysis in Excel, Python, or R:
- `threshold-sweep.csv`: Threshold, Precision, Recall, F1, DetectionCount
- `confusion-matrix.csv`: Actual, Predicted, Count
- `per-utterance.csv`: UtteranceId, OffsetMs, Text, Classification, Confidence, GroundTruth
- `strategy-comparison.csv`: Strategy, Precision, Recall, F1, AvgLatencyMs

#### Other Libraries Considered

| Library | Verdict |
|---|---|
| **ScottPlot** | Good for static PNG generation. Useful if images needed for CI or documentation. No interactivity. |
| **OxyPlot** | Viable but less actively maintained. ScottPlot is more ergonomic for headless use. |
| **LiveCharts2** | Overkill for batch reports. Better suited for a future live dashboard. |
| **Custom HTML/CSS** | Zero dependencies but limited to basic tables with background colours. No proper charts. |

### Proposed Architecture

Extend `ComprehensiveReport` with:

```
GenerateHtmlReport(ComprehensiveReportData report, string outputPath)
```

The HTML report would contain:
1. Summary metrics table (HTML)
2. Confusion matrix heatmap (Plotly)
3. Threshold sweep chart (Plotly line)
4. PR curve (Plotly scatter)
5. Strategy comparison bars (Plotly grouped bars)
6. Error distribution bars (Plotly horizontal bars)
7. Confidence distribution stacked bars (Plotly)
8. Per-utterance timeline (Plotly scatter)

CLI integration: `--evaluate session.jsonl --html report.html`

---

## Part 2: Evaluation Process Improvements

### Statistical Rigour

**Add macro and weighted averages.** The current evaluation computes only overall P/R/F1. Adding:
- **Macro average:** Unweighted mean of per-subtype F1 -- penalises poor performance on rare classes
- **Weighted average:** Weighted by support count -- reflects overall volume

This matches the scikit-learn `classification_report` standard and provides a more complete picture.

**Always show support counts.** A subtype with 100% accuracy on 1 sample is meaningless. Display `n=` alongside each metric in both console output and reports.

**Include a baseline comparison.** Add a "random" or "always-positive" baseline classifier to contextualise whether the detector is actually useful. A detector with 70% recall is only impressive if random achieves 5%.

### Ground Truth Quality

**Ground truth is currently single-source.** The `GroundTruthExtractor` relies solely on GPT-4o with no human validation. This means evaluation metrics are only as reliable as GPT-4o's extraction.

Improvements:
- **Human annotation tool:** A simple UI (could be Terminal.Gui based) for reviewing and correcting LLM-extracted ground truth before evaluation
- **Multi-model consensus:** Run extraction with multiple models (GPT-4o, Claude, Gemini) and use majority vote for ground truth
- **Cached ground truth:** Once validated, store ground truth alongside recordings so re-evaluation doesn't re-extract (and isn't subject to model drift)

### Dataset Scale

**Curated datasets are small** (15-20 items each in questions.jsonl, statements.jsonl, edge-cases.jsonl).

Improvements:
- **Expand curated sets** to 100+ items per category for statistical significance
- **Multi-session evaluation:** Add `--evaluate-all recordings/*.jsonl` to run across multiple recordings and aggregate metrics. Currently each session is evaluated independently with no cross-session summary.
- **Stratified sampling:** Ensure datasets cover all subtypes proportionally

### Evaluation Features

**Prompt A/B testing integration.** The `PromptTester` class exists but is not fully integrated into the CLI. Completing this would allow testing different extraction prompts for their effect on ground truth quality.

**Configurable similarity thresholds.** The matching thresholds (0.7 for detection matching, 0.8 for deduplication) are hard-coded. Making these configurable per evaluation run via CLI flags (e.g., `--match-threshold 0.6`) would support sensitivity analysis.

**Response quality evaluation.** Currently only detection accuracy is evaluated. The system also generates answers to detected questions. Evaluating answer quality (relevance, correctness, completeness) is a natural next step, though significantly harder to automate.

**Latency in comparison output.** The `LatencyTracker` captures detailed timing (min, max, avg, p95, p99, std dev) but this data is not included in the strategy comparison output. Including it would help weigh accuracy vs speed trade-offs.

### Regression and CI Integration

**Automated regression in CI.** The `--regression` and `--create-baseline` commands exist but there's no CI pipeline using them. Adding a GitHub Actions workflow that runs evaluation on a standard dataset and fails if metrics drop below baseline thresholds would catch regressions automatically.

**Versioned baselines.** Store baselines with the detection strategy version so historical trends can be tracked over time.

### Operational Improvements

**Rate limiting.** No explicit rate limiting for OpenAI API calls during evaluation. Bulk evaluation across many sessions could hit rate limits.

**Large transcript handling.** The GPT-4o response is capped at 4096 tokens, which may truncate ground truth extraction for long sessions. Consider chunking long transcripts.

**Cost tracking.** Evaluation uses OpenAI API calls (ground truth extraction, LLM strategy runs). Tracking token usage and estimated cost per evaluation run would help manage API spend.

---

## Summary of Recommendations

### Quick Wins (Low Effort, High Value)
1. Add macro/weighted averages and support counts to console output
2. CSV export of threshold sweep and confusion matrix data
3. Include latency metrics in strategy comparison output
4. Make similarity thresholds configurable via CLI flags

### Medium Effort
5. Plotly.NET HTML report generation in `ComprehensiveReport`
6. Spectre.Console colour-coded output for evaluation modes
7. Multi-session aggregated evaluation (`--evaluate-all`)
8. Cache validated ground truth alongside recordings

### Larger Investment
9. Human annotation review tool for ground truth validation
10. Automated regression testing in CI pipeline
11. Response quality evaluation framework
12. Expand curated datasets to 100+ items per category
