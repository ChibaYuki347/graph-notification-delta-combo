using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text.Json;
using System.Text;

namespace FunctionApp.Functions;

public class PerformanceReportFunction
{
    private readonly ILogger<PerformanceReportFunction> _logger;

    public PerformanceReportFunction(ILogger<PerformanceReportFunction> logger)
    {
        _logger = logger;
    }

    [Function("GeneratePerformanceReport")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "performance/report")] HttpRequestData req)
    {
        _logger.LogInformation("パフォーマンスレポート生成開始");

        try
        {
            var queryParams = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
            var format = queryParams.Get("format") ?? "json"; // json, html, markdown
            var includeDetails = bool.Parse(queryParams.Get("includeDetails") ?? "true");

            // パフォーマンステスト結果を取得（実際の実装では外部APIを呼び出すか、ストレージから取得）
            var testResults = await GetLatestTestResults();

            string content;
            string contentType;

            switch (format.ToLower())
            {
                case "html":
                    content = GenerateHtmlReport(testResults, includeDetails);
                    contentType = "text/html";
                    break;
                case "markdown":
                    content = GenerateMarkdownReport(testResults, includeDetails);
                    contentType = "text/markdown";
                    break;
                default:
                    content = JsonSerializer.Serialize(testResults, new JsonSerializerOptions 
                    { 
                        WriteIndented = true,
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                    });
                    contentType = "application/json";
                    break;
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            response.Headers.Add("Content-Type", contentType);
            response.Headers.Add("Access-Control-Allow-Origin", "*");
            
            await response.WriteStringAsync(content);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError($"パフォーマンスレポート生成エラー: {ex.Message}");
            
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new
            {
                success = false,
                error = ex.Message,
                timestamp = DateTimeOffset.UtcNow
            }));
            return errorResponse;
        }
    }

    private async Task<object> GetLatestTestResults()
    {
        // 実際の実装では、ストレージやデータベースから最新のテスト結果を取得
        // ここではサンプルデータを返す
        await Task.Delay(10); // 非同期処理のシミュレーション

        return new
        {
            summary = new
            {
                testSuiteVersion = "v1.0",
                executionTime = DateTimeOffset.UtcNow.AddMinutes(-5),
                totalExecutionTimeMs = 8734,
                overallResult = "PASS",
                targetRequirement = "End-to-End Response ≤ 10 seconds",
                testEnvironment = new
                {
                    runtime = "Azure Functions .NET 8 Isolated",
                    storage = "Azure Storage Emulator (Azurite)",
                    graphApi = "Microsoft Graph API v1.0",
                    rooms = "ConfRoom1-116@bbslooklab.onmicrosoft.com"
                }
            },
            performanceResults = new
            {
                bulkCreateTest = new
                {
                    totalTimeMs = 6234,
                    roomsProcessed = 116,
                    totalEventsCreated = 580,
                    totalErrors = 0,
                    eventsPerSecond = 93.1,
                    averageTimePerRoomMs = 53.7,
                    pass = true
                },
                endToEndTest = new
                {
                    totalTimeMs = 2890,
                    roomsProcessed = 10,
                    averageEndToEndMs = 289.0,
                    p50EndToEndMs = 267.5,
                    p95EndToEndMs = 445.2,
                    p99EndToEndMs = 498.7,
                    maxEndToEndMs = 512.3,
                    pass = true,
                    target = "P95 ≤ 10秒"
                },
                deltaSyncTest = new
                {
                    totalTimeMs = 3456,
                    roomsProcessed = 20,
                    successCount = 20,
                    totalEventsProcessed = 87,
                    totalVisitorIdsExtracted = 43,
                    averageLatencyMs = 172.8,
                    p95LatencyMs = 287.4,
                    pass = true
                },
                cacheSearchTest = new
                {
                    totalTimeMs = 834,
                    roomsSearched = 20,
                    averageSearchMs = 41.7,
                    p95SearchMs = 76.3,
                    maxSearchMs = 89.1,
                    pass = true,
                    target = "すべて ≤ 1秒"
                }
            },
            complianceReport = new
            {
                requirement = "10秒以内のエンドツーエンド接続",
                status = "COMPLIANT",
                details = true
            },
            recommendations = new[]
            {
                "現在の設定で要件を満たしています",
                "本番環境での継続的な監視を推奨",
                "API Rate Limit対応の実装を検討"
            },
            nextSteps = new[]
            {
                "Azure環境での本格テスト実行",
                "実際のOutlook予定作成によるE2Eテスト",
                "Graph API Rate Limitへの対応確認",
                "監視・アラート設定の実装"
            }
        };
    }

    private string GenerateHtmlReport(object testResults, bool includeDetails)
    {
        var data = (dynamic)testResults;
        var summary = data.summary;
        var performance = data.performanceResults;
        var compliance = data.complianceReport;

        var html = new StringBuilder();
        html.AppendLine("<!DOCTYPE html>");
        html.AppendLine("<html lang='ja'>");
        html.AppendLine("<head>");
        html.AppendLine("    <meta charset='UTF-8'>");
        html.AppendLine("    <meta name='viewport' content='width=device-width, initial-scale=1.0'>");
        html.AppendLine("    <title>パフォーマンステストレポート</title>");
        html.AppendLine("    <style>");
        html.AppendLine("        body { font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif; margin: 20px; background-color: #f5f5f5; }");
        html.AppendLine("        .container { max-width: 1200px; margin: 0 auto; background: white; padding: 30px; border-radius: 8px; box-shadow: 0 2px 10px rgba(0,0,0,0.1); }");
        html.AppendLine("        .header { text-align: center; border-bottom: 3px solid #0078d4; padding-bottom: 20px; margin-bottom: 30px; }");
        html.AppendLine("        .header h1 { color: #0078d4; margin-bottom: 10px; }");
        html.AppendLine("        .status-pass { color: #107c10; font-weight: bold; }");
        html.AppendLine("        .status-fail { color: #d83b01; font-weight: bold; }");
        html.AppendLine("        .metric-card { background: #f8f9fa; border-left: 4px solid #0078d4; padding: 15px; margin: 15px 0; border-radius: 4px; }");
        html.AppendLine("        .metric-title { font-weight: bold; color: #323130; margin-bottom: 8px; }");
        html.AppendLine("        .metric-value { font-size: 1.2em; color: #0078d4; }");
        html.AppendLine("        .table { width: 100%; border-collapse: collapse; margin: 20px 0; }");
        html.AppendLine("        .table th, .table td { padding: 12px; text-align: left; border-bottom: 1px solid #edebe9; }");
        html.AppendLine("        .table th { background-color: #f3f2f1; font-weight: 600; }");
        html.AppendLine("        .recommendations { background: #fff4ce; border: 1px solid #ffb900; border-radius: 4px; padding: 20px; margin: 20px 0; }");
        html.AppendLine("        .compliance { background: #dff6dd; border: 1px solid #107c10; border-radius: 4px; padding: 20px; margin: 20px 0; }");
        html.AppendLine("    </style>");
        html.AppendLine("</head>");
        html.AppendLine("<body>");
        html.AppendLine("    <div class='container'>");

        // ヘッダー
        html.AppendLine("        <div class='header'>");
        html.AppendLine("            <h1>Graph Calendar Delta Combo</h1>");
        html.AppendLine("            <h2>パフォーマンステストレポート</h2>");
        html.AppendLine($"            <p>実行日時: {summary.executionTime:yyyy年MM月dd日 HH:mm:ss}</p>");
        html.AppendLine($"            <p>総合結果: <span class='status-{(summary.overallResult == "PASS" ? "pass" : "fail")}'>{summary.overallResult}</span></p>");
        html.AppendLine("        </div>");

        // サマリー情報
        html.AppendLine("        <section>");
        html.AppendLine("            <h2>テスト概要</h2>");
        html.AppendLine("            <div class='metric-card'>");
        html.AppendLine($"                <div class='metric-title'>テスト実行時間</div>");
        html.AppendLine($"                <div class='metric-value'>{summary.totalExecutionTimeMs:N0} ms</div>");
        html.AppendLine("            </div>");
        html.AppendLine("            <div class='metric-card'>");
        html.AppendLine($"                <div class='metric-title'>対象要件</div>");
        html.AppendLine($"                <div class='metric-value'>{summary.targetRequirement}</div>");
        html.AppendLine("            </div>");
        html.AppendLine("        </section>");

        // パフォーマンス結果
        html.AppendLine("        <section>");
        html.AppendLine("            <h2>パフォーマンス結果</h2>");
        
        // 大量イベント作成テスト
        html.AppendLine("            <h3>大量イベント作成テスト</h3>");
        html.AppendLine("            <table class='table'>");
        html.AppendLine("                <tr><th>項目</th><th>値</th><th>ステータス</th></tr>");
        html.AppendLine($"                <tr><td>処理時間</td><td>{performance.bulkCreateTest.totalTimeMs:N0} ms</td><td class='status-{(performance.bulkCreateTest.pass ? "pass" : "fail")}'>{(performance.bulkCreateTest.pass ? "PASS" : "FAIL")}</td></tr>");
        html.AppendLine($"                <tr><td>処理会議室数</td><td>{performance.bulkCreateTest.roomsProcessed:N0} 室</td><td>-</td></tr>");
        html.AppendLine($"                <tr><td>作成イベント数</td><td>{performance.bulkCreateTest.totalEventsCreated:N0} 件</td><td>-</td></tr>");
        html.AppendLine($"                <tr><td>処理速度</td><td>{performance.bulkCreateTest.eventsPerSecond:F1} 件/秒</td><td>-</td></tr>");
        html.AppendLine("            </table>");

        // エンドツーエンドテスト
        html.AppendLine("            <h3>エンドツーエンドレスポンステスト</h3>");
        html.AppendLine("            <table class='table'>");
        html.AppendLine("                <tr><th>項目</th><th>値</th><th>ターゲット</th><th>ステータス</th></tr>");
        html.AppendLine($"                <tr><td>P95レスポンス時間</td><td>{performance.endToEndTest.p95EndToEndMs:F1} ms</td><td>≤ 10,000 ms</td><td class='status-{(performance.endToEndTest.pass ? "pass" : "fail")}'>{(performance.endToEndTest.pass ? "PASS" : "FAIL")}</td></tr>");
        html.AppendLine($"                <tr><td>平均レスポンス時間</td><td>{performance.endToEndTest.averageEndToEndMs:F1} ms</td><td>-</td><td>-</td></tr>");
        html.AppendLine($"                <tr><td>最大レスポンス時間</td><td>{performance.endToEndTest.maxEndToEndMs:F1} ms</td><td>-</td><td>-</td></tr>");
        html.AppendLine("            </table>");
        html.AppendLine("        </section>");

        // コンプライアンス報告
        html.AppendLine("        <section>");
        html.AppendLine("            <h2>要件適合性</h2>");
        html.AppendLine("            <div class='compliance'>");
        html.AppendLine($"                <h3>✅ {compliance.requirement}</h3>");
        html.AppendLine($"                <p><strong>ステータス:</strong> {compliance.status}</p>");
        html.AppendLine("                <p>本システムは要求されたパフォーマンス要件を満たしています。</p>");
        html.AppendLine("            </div>");
        html.AppendLine("        </section>");

        // 推奨事項
        var recommendations = (string[])data.recommendations;
        html.AppendLine("        <section>");
        html.AppendLine("            <h2>推奨事項</h2>");
        html.AppendLine("            <div class='recommendations'>");
        html.AppendLine("                <ul>");
        foreach (var recommendation in recommendations)
        {
            html.AppendLine($"                    <li>{recommendation}</li>");
        }
        html.AppendLine("                </ul>");
        html.AppendLine("            </div>");
        html.AppendLine("        </section>");

        html.AppendLine("    </div>");
        html.AppendLine("</body>");
        html.AppendLine("</html>");

        return html.ToString();
    }

    private string GenerateMarkdownReport(object testResults, bool includeDetails)
    {
        var data = (dynamic)testResults;
        var summary = data.summary;
        var performance = data.performanceResults;
        var compliance = data.complianceReport;

        var md = new StringBuilder();
        md.AppendLine("# Graph Calendar Delta Combo パフォーマンステストレポート");
        md.AppendLine();
        md.AppendLine($"**実行日時:** {summary.executionTime:yyyy年MM月dd日 HH:mm:ss}");
        md.AppendLine($"**総合結果:** {summary.overallResult} {(summary.overallResult == "PASS" ? "✅" : "❌")}");
        md.AppendLine($"**テスト実行時間:** {summary.totalExecutionTimeMs:N0} ms");
        md.AppendLine();

        md.AppendLine("## 📊 テスト概要");
        md.AppendLine();
        md.AppendLine($"- **対象要件:** {summary.targetRequirement}");
        md.AppendLine($"- **テスト環境:** {summary.testEnvironment.runtime}");
        md.AppendLine($"- **対象会議室:** {summary.testEnvironment.rooms}");
        md.AppendLine($"- **Graph API:** {summary.testEnvironment.graphApi}");
        md.AppendLine();

        md.AppendLine("## 🚀 パフォーマンス結果");
        md.AppendLine();

        md.AppendLine("### 大量イベント作成テスト");
        md.AppendLine();
        md.AppendLine("| 項目 | 値 | ステータス |");
        md.AppendLine("|------|----|-----------:|");
        md.AppendLine($"| 処理時間 | {performance.bulkCreateTest.totalTimeMs:N0} ms | {(performance.bulkCreateTest.pass ? "✅ PASS" : "❌ FAIL")} |");
        md.AppendLine($"| 処理会議室数 | {performance.bulkCreateTest.roomsProcessed:N0} 室 | - |");
        md.AppendLine($"| 作成イベント数 | {performance.bulkCreateTest.totalEventsCreated:N0} 件 | - |");
        md.AppendLine($"| 処理速度 | {performance.bulkCreateTest.eventsPerSecond:F1} 件/秒 | - |");
        md.AppendLine();

        md.AppendLine("### エンドツーエンドレスポンステスト");
        md.AppendLine();
        md.AppendLine("| 項目 | 値 | ターゲット | ステータス |");
        md.AppendLine("|------|----|------------|------------|");
        md.AppendLine($"| P95レスポンス時間 | {performance.endToEndTest.p95EndToEndMs:F1} ms | ≤ 10,000 ms | {(performance.endToEndTest.pass ? "✅ PASS" : "❌ FAIL")} |");
        md.AppendLine($"| 平均レスポンス時間 | {performance.endToEndTest.averageEndToEndMs:F1} ms | - | - |");
        md.AppendLine($"| 最大レスポンス時間 | {performance.endToEndTest.maxEndToEndMs:F1} ms | - | - |");
        md.AppendLine();

        md.AppendLine("### Delta同期テスト");
        md.AppendLine();
        md.AppendLine("| 項目 | 値 | ステータス |");
        md.AppendLine("|------|----|------------|");
        md.AppendLine($"| 処理時間 | {performance.deltaSyncTest.totalTimeMs:N0} ms | {(performance.deltaSyncTest.pass ? "✅ PASS" : "❌ FAIL")} |");
        md.AppendLine($"| 処理会議室数 | {performance.deltaSyncTest.roomsProcessed:N0} 室 | - |");
        md.AppendLine($"| 抽出VisitorID数 | {performance.deltaSyncTest.totalVisitorIdsExtracted:N0} 件 | - |");
        md.AppendLine($"| P95レイテンシ | {performance.deltaSyncTest.p95LatencyMs:F1} ms | - |");
        md.AppendLine();

        md.AppendLine("## ✅ 要件適合性");
        md.AppendLine();
        md.AppendLine($"### {compliance.requirement}");
        md.AppendLine();
        md.AppendLine($"**ステータス:** {compliance.status} {(compliance.status == "COMPLIANT" ? "✅" : "❌")}");
        md.AppendLine();
        md.AppendLine("本システムは要求されたパフォーマンス要件を満たしています。");
        md.AppendLine();

        md.AppendLine("## 💡 推奨事項");
        md.AppendLine();
        var recommendations = (string[])data.recommendations;
        foreach (var recommendation in recommendations)
        {
            md.AppendLine($"- {recommendation}");
        }
        md.AppendLine();

        md.AppendLine("## 📋 次のステップ");
        md.AppendLine();
        var nextSteps = (string[])data.nextSteps;
        foreach (var step in nextSteps)
        {
            md.AppendLine($"1. {step}");
        }
        md.AppendLine();

        md.AppendLine("---");
        md.AppendLine($"*レポート生成日時: {DateTimeOffset.UtcNow:yyyy年MM月dd日 HH:mm:ss}*");

        return md.ToString();
    }
}