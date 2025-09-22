#!/bin/bash

# パフォーマンステストスクリプト
# 大量会議室データでのエンドツーエンド応答時間計測

echo "========================================"
echo "Graph Calendar PoC パフォーマンステスト"
echo "実行時刻: $(date)"
echo "========================================"

BASE_URL="http://localhost:7071/api"
ROOMS=("ConfRoom1" "ConfRoom2" "ConfRoom3" "ConfRoom4" "ConfRoom5")
DOMAIN="@bbslooklab.onmicrosoft.com"

# テスト結果集計用変数
TOTAL_TESTS=0
PASSED_TESTS=0
FAILED_TESTS=0
declare -a RESPONSE_TIMES=()

# ヘルスチェック
echo ""
echo "🔍 1. ヘルスチェック"
echo "----------------------------------------"
HEALTH_TIME=$(curl -w "%{time_total}" -s -o /dev/null "$BASE_URL/health")
echo "Health Check: ${HEALTH_TIME}s"
if (( $(echo "$HEALTH_TIME < 1.0" | bc -l) )); then
    echo "✅ ヘルスチェック: OK"
else
    echo "❌ ヘルスチェック: SLOW"
fi

# 会議室一覧取得テスト
echo ""
echo "🏢 2. 会議室一覧取得テスト"
echo "----------------------------------------"
ROOMS_TIME=$(curl -w "%{time_total}" -s -o /tmp/rooms_response.json "$BASE_URL/rooms")
ROOM_COUNT=$(jq '.rooms | length' /tmp/rooms_response.json 2>/dev/null || echo "0")
echo "会議室一覧取得: ${ROOMS_TIME}s (${ROOM_COUNT}室)"
RESPONSE_TIMES+=($ROOMS_TIME)
TOTAL_TESTS=$((TOTAL_TESTS + 1))
if (( $(echo "$ROOMS_TIME < 10.0" | bc -l) )); then
    PASSED_TESTS=$((PASSED_TESTS + 1))
    echo "✅ 会議室一覧: OK"
else
    FAILED_TESTS=$((FAILED_TESTS + 1))
    echo "❌ 会議室一覧: SLOW"
fi

# 個別会議室イベント取得テスト（raw=true使用）
echo ""
echo "📅 3. 会議室イベント取得テスト (raw=true)"
echo "----------------------------------------"
for ROOM in "${ROOMS[@]}"; do
    ROOM_UPN="${ROOM}${DOMAIN}"
    
    # raw=trueでフィルタ無効化
    EVENTS_TIME=$(curl -w "%{time_total}" -s -o /tmp/events_response.json "$BASE_URL/rooms/$ROOM_UPN/events?raw=true")
    EVENT_COUNT=$(jq '.eventCount' /tmp/events_response.json 2>/dev/null || echo "0")
    RAW_COUNT=$(jq '.rawCount' /tmp/events_response.json 2>/dev/null || echo "0")
    
    echo "$ROOM: ${EVENTS_TIME}s (${EVENT_COUNT}/${RAW_COUNT}イベント)"
    RESPONSE_TIMES+=($EVENTS_TIME)
    TOTAL_TESTS=$((TOTAL_TESTS + 1))
    
    if (( $(echo "$EVENTS_TIME < 10.0" | bc -l) )); then
        PASSED_TESTS=$((PASSED_TESTS + 1))
        echo "✅ $ROOM: OK"
    else
        FAILED_TESTS=$((FAILED_TESTS + 1))
        echo "❌ $ROOM: SLOW"
    fi
done

# 負荷テスト：全会議室同時取得
echo ""
echo "⚡ 4. 負荷テスト: 全会議室同時アクセス"
echo "----------------------------------------"
START_TIME=$(date +%s.%N)

# バックグラウンドで全会議室のイベントを同時取得
pids=()
for ROOM in "${ROOMS[@]}"; do
    ROOM_UPN="${ROOM}${DOMAIN}"
    (
        curl -w "%{time_total}" -s -o /tmp/concurrent_${ROOM}.json "$BASE_URL/rooms/$ROOM_UPN/events?raw=true"
        echo "$ROOM: $(cat /tmp/concurrent_${ROOM}.json | jq -r '.responseTimeMs // "N/A"')ms"
    ) &
    pids+=($!)
done

# 全プロセス完了まで待機
for pid in "${pids[@]}"; do
    wait $pid
done

END_TIME=$(date +%s.%N)
CONCURRENT_TIME=$(echo "$END_TIME - $START_TIME" | bc)
echo "全会議室同時取得完了: ${CONCURRENT_TIME}s"

# 統計計算
if [ ${#RESPONSE_TIMES[@]} -gt 0 ]; then
    # 平均応答時間
    TOTAL_TIME=0
    for time in "${RESPONSE_TIMES[@]}"; do
        TOTAL_TIME=$(echo "$TOTAL_TIME + $time" | bc)
    done
    AVG_TIME=$(echo "scale=3; $TOTAL_TIME / ${#RESPONSE_TIMES[@]}" | bc)
    
    # 最大・最小応答時間
    MAX_TIME=$(printf '%s\n' "${RESPONSE_TIMES[@]}" | sort -n | tail -1)
    MIN_TIME=$(printf '%s\n' "${RESPONSE_TIMES[@]}" | sort -n | head -1)
fi

# 結果サマリー
echo ""
echo "========================================"
echo "📊 テスト結果サマリー"
echo "========================================"
echo "実行時刻: $(date)"
echo "総テスト数: $TOTAL_TESTS"
echo "成功: $PASSED_TESTS (10秒以内)"
echo "失敗: $FAILED_TESTS (10秒超過)"
echo "成功率: $(echo "scale=1; $PASSED_TESTS * 100 / $TOTAL_TESTS" | bc)%"
echo ""
echo "応答時間統計:"
echo "  平均: ${AVG_TIME:-N/A}s"
echo "  最小: ${MIN_TIME:-N/A}s"
echo "  最大: ${MAX_TIME:-N/A}s"
echo "  同時実行: ${CONCURRENT_TIME}s"
echo ""

# 10秒条件の評価
if [ $FAILED_TESTS -eq 0 ]; then
    echo "🎉 結果: 全テストが10秒以内でクリア"
    echo "✅ エンドツーエンド条件達成: OK"
else
    echo "⚠️  結果: ${FAILED_TESTS}個のテストが10秒を超過"
    echo "❌ エンドツーエンド条件: 要改善"
fi

echo "========================================"

# 詳細レポート用JSONファイル生成
cat > /tmp/performance_report.json << EOF
{
  "timestamp": "$(date -u +%Y-%m-%dT%H:%M:%S.%3NZ)",
  "summary": {
    "totalTests": $TOTAL_TESTS,
    "passed": $PASSED_TESTS,
    "failed": $FAILED_TESTS,
    "successRate": $(echo "scale=3; $PASSED_TESTS * 100 / $TOTAL_TESTS" | bc)
  },
  "performance": {
    "averageResponseTime": ${AVG_TIME:-0},
    "minResponseTime": ${MIN_TIME:-0},
    "maxResponseTime": ${MAX_TIME:-0},
    "concurrentExecutionTime": $CONCURRENT_TIME
  },
  "condition": {
    "target": "P95 ≤ 10秒",
    "achieved": $([ $FAILED_TESTS -eq 0 ] && echo "true" || echo "false")
  }
}
EOF

echo "詳細レポート: /tmp/performance_report.json に保存"