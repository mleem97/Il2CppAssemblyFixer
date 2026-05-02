#!/usr/bin/env bash
# Simple msg-filter for git filter-branch that produces a Conventional Commit-like first line.
orig=$(cat -)
[ -z "$orig" ] && exit 0
first=$(echo "$orig" | sed -n '1p')
type=chore
echo "$orig" | grep -qiE 'BREAKING|BREAKING CHANGE|!$' && type=feat || true
if echo "$orig" | grep -qiE '\b(fix|bug)\b'; then
  type=fix
elif echo "$orig" | grep -qiE '\b(add|feat|feature)\b'; then
  type=feat
elif echo "$orig" | grep -qiE '\b(remove|delete|rm)\b'; then
  type=chore
elif echo "$orig" | grep -qiE 'readme|docs'; then
  type=docs
fi

printf '%s: %s\n\nOriginal commit message:\n%s' "$type" "$first" "$orig"
