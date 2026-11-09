#!/usr/bin/env bash
# Keep exactly one open "Dependency report" issue, identified by the dependency-report label.
#
#   report-issue.sh resolve              -> prints the issue number (creates + pins the issue if none is open,
#                                           closes extra open copies so only the oldest one remains)
#   report-issue.sh publish <n> <body>   -> replaces the body of issue <n> with the file <body>
#   report-issue.sh warn <n> <message>   -> comments on issue <n> (used when a scheduled run fails)
#
# Requires the gh CLI with GH_TOKEN (the workflow's GITHUB_TOKEN, permissions issues: write).
set -euo pipefail

LABEL="dependency-report"
TITLE="Dependency report"
# Must match REPORT_MARKER in lib/render-report.mjs. Only issues carrying it are treated as the report,
# so a person reusing the label on another issue never gets that issue overwritten or closed.
MARKER="<!-- dependency-report:managed -->"

resolve() {
  gh label create "$LABEL" --color "B60205" \
    --description "Báo cáo phiên bản thư viện tự động hằng tuần" --force >/dev/null

  local numbers
  numbers=$(gh issue list --label "$LABEL" --state open --limit 50 --json number,body \
    --jq "map(select(.body | contains(\"$MARKER\"))) | map(.number) | sort | .[]")

  if [[ -z "$numbers" ]]; then
    local url
    url=$(gh issue create --title "$TITLE" --label "$LABEL" \
      --body "$MARKER
Đang tạo báo cáo lần đầu, nội dung sẽ được ghi đè sau vài phút.")
    local created="${url##*/}"
    gh issue pin "$created" >/dev/null 2>&1 || echo "::warning::Không ghim được issue #$created; ghim tay một lần trên GitHub." >&2
    echo "$created"
    return
  fi

  local keep
  keep=$(head -n1 <<<"$numbers")
  for extra in $(tail -n +2 <<<"$numbers"); do
    gh issue close "$extra" --comment "Trùng với #$keep; báo cáo chỉ cập nhật một issue duy nhất." >/dev/null
  done
  echo "$keep"
}

case "${1:-}" in
  resolve)
    n=$(resolve)
    [[ "$n" =~ ^[0-9]+$ ]] || { echo "resolve did not return an issue number: '$n'" >&2; exit 1; }
    echo "$n"
    ;;
  publish) gh issue edit "$2" --title "$TITLE" --body-file "$3" >/dev/null ;;
  warn) gh issue comment "$2" --body "$3" >/dev/null ;;
  *) echo "usage: $0 resolve | publish <issue> <body-file> | warn <issue> <message>" >&2; exit 2 ;;
esac
