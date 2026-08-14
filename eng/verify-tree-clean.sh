#!/usr/bin/env bash
# The working tree must carry no change after a CI job has run.
#
# Both jobs in .github/workflows/site.yml that touch the checkout end with this, for two different
# writers. In the verify job it is the whole-tree backstop for the DocGen drift gate, which runs
# `git diff --exit-code` over three named generated files and so cannot see a step that writes any
# other tracked path. In the deploy job it is a canary for wrangler-action installing outside the
# root prepared for it (#282), which otherwise surfaces as one warning line in a long green log.
#
# A script rather than a block repeated in both jobs: the two copies would drift, and a check that
# lives in a YAML string cannot be watched failing without pushing, which CONTRIBUTING.md
# §Engineering standard does not accept.
#
# No pathspec, deliberately. The point is to notice a write nobody predicted, including one outside
# the directory the job was working in.
set -euo pipefail

dirty=$(git status --porcelain)
if [ -n "$dirty" ]; then
  echo "::error::the working tree is not clean, so what was built here does not match the commit it was built from. Ignore whatever wrote the paths below, or stop it from writing there." >&2
  printf '%s\n' "$dirty" >&2
  exit 1
fi
