#!/usr/bin/env bash
# Assertion helpers for the CI guards.
#
# Sourced (not executed) by steps in .github/workflows/site.yml and by the eng/verify-*.sh scripts
# that .github/workflows/ci.yml runs, so a failing assertion prints a GitHub `::error::` annotation
# naming the file and the expectation, instead of aborting the step with a bare non-zero exit status.
#
# Three shell hazards these helpers exist to contain:
#   * Under `set -e` a negated command (! grep ...) is EXEMPT from errexit (POSIX), so asserting
#     absence by negation silently passes. assert_not_grep captures the status instead.
#   * `if grep ...` cannot tell "no match" (exit 1) from "grep itself failed" (exit >=2, for example a
#     grep built without PCRE rejecting -P, or an invalid pattern). EVERY helper that runs grep --
#     assert_count included -- treats >=2 as a check-level failure, so a broken check can never
#     masquerade as a clean result. This matters most for a zero-count assertion, where swallowing
#     grep's error would make a MISSING FILE look like a satisfied expectation.
#   * Under `pipefail` a non-matching grep fails the whole pipeline. assert_count therefore captures
#     grep's output and status without a pipe rather than papering over it with `|| true`, which would
#     also swallow the >=2 case above.

# Print a GitHub error annotation and abort the step.
fail() {
  echo "::error::$1" >&2
  exit 1
}

# Guard against a wrong-arity call to one of the public assertion functions below. Without this,
# a caller that drops an argument (most often the trailing message) does not fail until the
# assertion itself needs that argument, surfacing as a confusing "unbound variable" abort instead
# of naming the actual mistake.
_assert_argc() {
  if [ "$#" -ne 3 ]; then
    fail "_assert_argc: expected 3 arguments, got $#"
  fi
  if [ "$3" -ne "$2" ]; then
    fail "$1: expected $2 argument(s), got $3"
  fi
}

# Classify a grep exit status: 0 match, 1 no match, >=2 the check itself failed.
# Restores the caller's errexit setting rather than forcing it on, so sourcing these helpers into an
# interactive or non-errexit shell cannot silently change its error mode.
_assert_grep_status() {
  local rc prev=$-
  if [ ! -f "$2" ]; then
    fail "expected file does not exist: $2"
  fi

  set +e
  grep -qE -- "$1" "$2"
  rc=$?
  case $prev in *e*) set -e ;; esac

  if [ "$rc" -gt 1 ]; then
    fail "the check for '$1' could not run (grep exited $rc) on $2"
  fi

  return "$rc"
}

# The pattern must match at least once.
assert_grep() {
  _assert_argc assert_grep 3 "$#"
  if ! _assert_grep_status "$1" "$2"; then
    fail "$3 (file: $2, expected pattern: $1)"
  fi
}

# The pattern must not match.
assert_not_grep() {
  _assert_argc assert_not_grep 3 "$#"
  if _assert_grep_status "$1" "$2"; then
    fail "$3 (file: $2, forbidden pattern: $1)"
  fi
}

# The pattern must match exactly the expected number of occurrences.
assert_count() {
  local out rc prev=$- actual
  _assert_argc assert_count 4 "$#"
  # Guard the expected-count argument itself: with this validated, the exact string comparison
  # below ("$actual" != "$3") is safe, since "01" or "+1" can no longer sneak past it disguised as
  # a number.
  case "$3" in
    ''|*[!0-9]*) fail "assert_count: expected count must be a non-negative integer, got '$3'" ;;
  esac
  if [ ! -f "$2" ]; then
    fail "expected file does not exist: $2"
  fi

  set +e
  out=$(grep -oE -- "$1" "$2")
  rc=$?
  case $prev in *e*) set -e ;; esac

  if [ "$rc" -gt 1 ]; then
    fail "the count check for '$1' could not run (grep exited $rc) on $2"
  fi

  # rc=1 means no match, which is a legitimate count of zero. Only rc=0 has lines to count.
  actual=0
  if [ "$rc" -eq 0 ]; then
    actual=$(printf '%s\n' "$out" | wc -l | tr -d '[:space:]')
  fi

  if [ "$actual" != "$3" ]; then
    fail "$4 (file: $2, pattern: $1, expected: $3, actual: $actual)"
  fi
}

# No entry matching the glob may exist directly under the directory.
#
# The directory-existence check is the point of this helper, not a nicety: `find` exits 0 and prints
# nothing both when the glob matches nothing AND (with the check omitted) when the whole directory is
# gone. A missing _framework/ would then read as "no ICU data, as expected" -- the same class of
# defect as a zero-count assertion against a missing file.
assert_no_glob() {
  local matches rc prev=$-
  _assert_argc assert_no_glob 3 "$#"
  if [ ! -d "$1" ]; then
    fail "expected directory does not exist: $1"
  fi

  set +e
  matches=$(find "$1" -maxdepth 1 -name "$2" -print)
  rc=$?
  case $prev in *e*) set -e ;; esac

  if [ "$rc" -ne 0 ]; then
    fail "the glob check for '$2' could not run (find exited $rc) in $1"
  fi

  if [ -n "$matches" ]; then
    fail "$3 (directory: $1, forbidden glob: $2, matched: $(printf '%s' "$matches" | tr '\n' ' '))"
  fi
}

# The file must exist.
assert_file() {
  _assert_argc assert_file 2 "$#"
  if [ ! -f "$1" ]; then
    fail "$2 (missing file: $1)"
  fi
}

# The file must not exist.
assert_no_file() {
  _assert_argc assert_no_file 2 "$#"
  if [ -e "$1" ]; then
    fail "$2 (unexpected file: $1)"
  fi
}

# Every occurrence of the selector pattern must also match the required pattern.
#
# A selector that matches NOTHING is a failure, not a vacuous pass: "every URL in this sitemap uses
# the production origin" must not be satisfiable by a sitemap with no URLs in it. Neither grep runs
# under a negation -- the second one uses -v and its status is captured -- so errexit's exemption for
# negated commands cannot swallow either result.
assert_every_line_matches() {
  local selected offenders rc prev=$-
  _assert_argc assert_every_line_matches 4 "$#"
  if [ ! -f "$3" ]; then
    fail "expected file does not exist: $3"
  fi

  set +e
  selected=$(grep -oE -- "$1" "$3")
  rc=$?
  case $prev in *e*) set -e ;; esac

  if [ "$rc" -gt 1 ]; then
    fail "the selector '$1' could not run (grep exited $rc) on $3"
  fi
  if [ "$rc" -eq 1 ]; then
    fail "$4 (file: $3, selector matched nothing: $1)"
  fi

  set +e
  offenders=$(printf '%s\n' "$selected" | grep -vE -- "$2")
  rc=$?
  case $prev in *e*) set -e ;; esac

  if [ "$rc" -gt 1 ]; then
    fail "the required pattern '$2' could not run (grep exited $rc) over the selected text of $3"
  fi
  if [ -n "$offenders" ]; then
    fail "$4 (file: $3, required pattern: $2, offending: $(printf '%s' "$offenders" | tr '\n' ' '))"
  fi
}
