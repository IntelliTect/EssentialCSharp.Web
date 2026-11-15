#!/usr/bin/env bash
# Turns scheduled smoke-test results into a single tracking issue, so failures aren't lost
# (GitHub only emails the last person who edited a scheduled workflow).
#
# - failure, no open issue, previous completed run also failed -> open an issue
# - failure, no open issue, previous run passed                -> warn only (one blip is not an incident)
# - failure, issue already open                                -> update the issue body with the latest run
#                                                                 (an edit, not a comment, to avoid a notification every 6 hours)
# - success, issue open                                        -> comment and close it
# - cancelled                                                  -> do nothing
#
# The workflow reports "failure" when the smoke step failed *or* never ran because setup failed, so the
# run's overall conclusion matches what was reported here and "previous run failed" means the same thing.
#
# Environment:
#   OUTCOME        success | failure | cancelled
#   GH_REPO        owner/name (also read by the gh CLI)
#   GH_TOKEN       token with issues: write and actions: read
#   RUN_ID, RUN_URL, WORKFLOW_FILE, BRANCH, SITE_BASE_URL
#   DRY_RUN=true   print the gh commands that would change things instead of running them
set -euo pipefail

: "${OUTCOME:?}" "${GH_REPO:?}" "${RUN_ID:?}" "${RUN_URL:?}" "${WORKFLOW_FILE:?}"
branch="${BRANCH:-main}"
site="${SITE_BASE_URL:-the development site}"
label="smoke-test-failure"
title="Scheduled smoke tests are failing against ${site}"

mutate() {
  if [[ "${DRY_RUN:-false}" == "true" ]]; then
    printf 'DRY RUN:'; printf ' %q' "$@"; printf '\n'
  else
    "$@"
  fi
}

issue_body() {
  cat <<EOF
The scheduled smoke tests against ${site} are failing (opened after two consecutive failed runs).

Latest failing run: ${RUN_URL} ($(date -u '+%Y-%m-%d %H:%M UTC'))

The run's \`smoke-test-results\` artifact has the TRX file with each failing check; if it is missing, the run failed before the tests started (checkout or .NET setup). Common causes: expired secrets or Key Vault access, external login configuration, missing book content, or the site being down.

This description is updated with the latest failing run, and the issue closes automatically when a run passes.
EOF
}

# No error suppression here: if the lookup fails, an empty result would look like "no open issue"
# and could open a duplicate (or leave a resolved one open). Failing the step is the safer outcome.
open_issue="$(gh issue list --repo "${GH_REPO}" --label "${label}" --state open --json number --jq '.[0].number // empty')"

case "${OUTCOME}" in
  success)
    if [[ -n "${open_issue}" ]]; then
      mutate gh issue comment "${open_issue}" --repo "${GH_REPO}" --body "Smoke tests are passing again: ${RUN_URL}. Closing."
      mutate gh issue close "${open_issue}" --repo "${GH_REPO}" --reason completed
    fi
    echo "Smoke tests passed."
    ;;

  failure)
    if [[ -n "${open_issue}" ]]; then
      mutate gh issue edit "${open_issue}" --repo "${GH_REPO}" --body "$(issue_body)"
      echo "Smoke tests failed again; updated #${open_issue}."
      exit 0
    fi

    # The latest earlier run that actually passed or failed; cancelled/skipped runs don't break a failure streak.
    # (The current run is still in progress, so it isn't in the completed list.) Failing quietly here is safe:
    # it only means "warn instead of opening an issue this time".
    previous="$(gh run list --repo "${GH_REPO}" --workflow "${WORKFLOW_FILE}" --branch "${branch}" --status completed --limit 10 \
      --json databaseId,conclusion \
      --jq "[.[] | select(.databaseId != ${RUN_ID} and (.conclusion == \"success\" or .conclusion == \"failure\"))][0].conclusion // empty" \
      2>/dev/null || true)"

    if [[ "${previous}" != "failure" ]]; then
      echo "::warning::Smoke tests failed (previous run: ${previous:-none}). An issue will be opened if the next run also fails."
      exit 0
    fi

    mutate gh label create "${label}" --repo "${GH_REPO}" --color B60205 --force \
      --description "Scheduled smoke tests against a deployed environment are failing"
    mutate gh issue create --repo "${GH_REPO}" --label "${label}" --title "${title}" --body "$(issue_body)"
    echo "Opened an issue for consecutive smoke-test failures."
    ;;

  *)
    echo "Run was ${OUTCOME}; nothing to report."
    ;;
esac
