#!/bin/bash

# Graph Calendar Delta Combo - パフォーマンステスト実行スクリプト
# 使用方法: ./run-performance-tests.sh [room_count] [events_per_room]

set -e

# デフォルト設定
ROOM_COUNT=${1:-116}
EVENTS_PER_ROOM=${2:-5}
BASE_URL=${BASE_URL:-"http://localhost:7071"}
OUTPUT_DIR="performance-reports"

echo "=========================================="
echo "Graph Calendar Delta Combo"
echo "パフォーマンステスト実行スクリプト"
echo "=========================================="
echo "対象会議室数: $ROOM_COUNT"
echo "会議室あたりイベント数: $EVENTS_PER_ROOM"
echo "Base URL: $BASE_URL"
echo "=========================================="

# 出力ディレクトリを作成
mkdir -p "$OUTPUT_DIR"

# タイムスタンプ
TIMESTAMP=$(date +"%Y%m%d_%H%M%S")

echo "🚀 Azure Functions の起動確認..."
if ! curl -s "$BASE_URL/api/health" > /dev/null; then
    echo "❌ Azure Functions が起動していません。以下のコマンドで起動してください:"
    echo "   cd FunctionApp && func start"
    exit 1
fi
echo "✅ Azure Functions が起動しています"

echo ""
echo "🧪 パフォーマンステストスイート実行中..."
echo "予想実行時間: 約 $((ROOM_COUNT * EVENTS_PER_ROOM / 10)) 秒"

# パフォーマンステストスイート実行
curl -X POST "$BASE_URL/api/performance/test-suite?roomCount=$ROOM_COUNT&eventsPerRoom=$EVENTS_PER_ROOM&withSubscription=true&testDeltaSync=true" \
    -H "Content-Type: application/json" \
    -o "$OUTPUT_DIR/test_results_$TIMESTAMP.json" \
    -w "\n実行時間: %{time_total}s\nレスポンスコード: %{http_code}\n"

if [ $? -eq 0 ]; then
    echo "✅ テスト実行完了"
else
    echo "❌ テスト実行に失敗しました"
    exit 1
fi

echo ""
echo "📊 レポート生成中..."

# HTMLレポート生成
curl -s "$BASE_URL/api/performance/report?format=html&includeDetails=true" \
    -o "$OUTPUT_DIR/report_$TIMESTAMP.html"

# Markdownレポート生成
curl -s "$BASE_URL/api/performance/report?format=markdown&includeDetails=true" \
    -o "$OUTPUT_DIR/report_$TIMESTAMP.md"

# JSONレポート生成
curl -s "$BASE_URL/api/performance/report?format=json&includeDetails=true" \
    -o "$OUTPUT_DIR/report_$TIMESTAMP.json"

echo "✅ レポート生成完了"
echo ""
echo "📁 生成されたファイル:"
echo "   - テスト結果: $OUTPUT_DIR/test_results_$TIMESTAMP.json"
echo "   - HTMLレポート: $OUTPUT_DIR/report_$TIMESTAMP.html"
echo "   - Markdownレポート: $OUTPUT_DIR/report_$TIMESTAMP.md"
echo "   - JSONレポート: $OUTPUT_DIR/report_$TIMESTAMP.json"

echo ""
echo "🔍 テスト結果サマリー:"
cat "$OUTPUT_DIR/test_results_$TIMESTAMP.json" | grep -A 5 '"summary"' | head -10

echo ""
echo "🌐 HTMLレポートをブラウザで表示:"
echo "   file://$(pwd)/$OUTPUT_DIR/report_$TIMESTAMP.html"

# テスト結果の基本チェック
if command -v jq &> /dev/null; then
    OVERALL_RESULT=$(cat "$OUTPUT_DIR/test_results_$TIMESTAMP.json" | jq -r '.summary.overallResult // "UNKNOWN"')
    if [ "$OVERALL_RESULT" = "PASS" ]; then
        echo "✅ 総合結果: PASS - 要件を満たしています"
    elif [ "$OVERALL_RESULT" = "FAIL" ]; then
        echo "❌ 総合結果: FAIL - 要件を満たしていません"
        exit 1
    else
        echo "⚠️  総合結果: $OVERALL_RESULT"
    fi
else
    echo "💡 jqをインストールすると詳細な結果確認ができます: brew install jq"
fi

echo ""
echo "🎉 パフォーマンステスト完了!"