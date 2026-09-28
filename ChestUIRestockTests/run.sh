#!/usr/bin/env bash
#
# Runs the ChestUI.Restock suite against a tModLoader built from this tree's
# patches/, which is the point: the code under test is tModLoader's own, so an
# already-installed build would not exercise the change.
#
# Every path outside this repository comes from the environment; nothing here
# assumes a layout. Required:
#
#   TESTARIA_PATH   a Testaria checkout, the one holding scripts/run-tests.sh
#   TML_INSTALL     a tModLoader installation to use as the base for the
#                   patched copy, and the subject itself when BASELINE=1
#   MODS_SRC        the Mods directory that building a mod writes its .tmod to
#
# Optional:
#
#   SCRATCH_TML     where to assemble the patched install (default: a mktemp dir,
#                   removed on exit unless SCRATCH_TML was supplied)
#   BASELINE=1      test TML_INSTALL unmodified instead of building. The suite
#                   is expected to fail there, which is how its ability to fail
#                   is checked rather than assumed.
#
# Remaining arguments are passed to Testaria's run-tests.sh; FILTER=<regex>
# narrows the run.

set -euo pipefail

: "${TESTARIA_PATH:?set TESTARIA_PATH to a Testaria checkout}"
: "${TML_INSTALL:?set TML_INSTALL to a tModLoader installation}"
: "${MODS_SRC:?set MODS_SRC to the Mods directory mods build into}"

HERE="$(cd "$(dirname "$0")" && pwd)"
REPO="$(dirname "$HERE")"

[ -f "$TESTARIA_PATH/scripts/run-tests.sh" ] || {
	echo "TESTARIA_PATH has no scripts/run-tests.sh: $TESTARIA_PATH" >&2
	exit 2
}

[ -f "$TML_INSTALL/tModLoader.dll" ] || {
	echo "TML_INSTALL has no tModLoader.dll: $TML_INSTALL" >&2
	exit 2
}

if [ "${BASELINE:-0}" = "1" ]; then
	TML="$TML_INSTALL"
	echo "baseline: testing $TML unmodified"
else
	if [ -n "${SCRATCH_TML:-}" ]; then
		SCRATCH="$SCRATCH_TML"
		rm -rf "$SCRATCH"
		mkdir -p "$SCRATCH"
	else
		SCRATCH="$(mktemp -d)"
		trap 'rm -rf "$SCRATCH"' EXIT
	fi

	echo "building tModLoader from patches/..."
	( cd "$REPO" && ./setup-cli.sh regen-source --no-prompts --plain-progress >/dev/null )
	( cd "$REPO" && dotnet build src/tModLoader/Terraria/Terraria.csproj \
		-c Release -v q -p:WarningLevel=0 --nologo >/dev/null )

	BUILT="$REPO/src/tModLoader/Terraria/bin/Release/net10.0"

	# A real copy, never cp -al: hardlinks would write the overlay through into
	# TML_INSTALL.
	cp -a "$TML_INSTALL" "$SCRATCH/install"
	cp -a "$BUILT/." "$SCRATCH/install/"

	cmp -s "$BUILT/tModLoader.dll" "$SCRATCH/install/tModLoader.dll" \
		|| { echo "overlay did not take" >&2; exit 2; }

	TML="$SCRATCH/install"
	echo "patched install: $TML"
fi

# paths.local.sh in a Testaria checkout assigns TML_PATH unconditionally and is
# sourced before anything already set is honoured, so without this the run
# silently tests TML_INSTALL and every result still looks plausible.
export TESTARIA_IGNORE_LOCAL_PATHS=1
export TML_PATH="$TML"
export MODS_SRC
export ENABLED="${ENABLED:-Testaria ChestUIRestockTests}"
export RUN_NAME="${RUN_NAME:-ChestUIRestockTests}"
export MOD_PROJECT_PATH="${MOD_PROJECT_PATH:-$HERE}"
export BLANK="${BLANK:-1}"

exec "$TESTARIA_PATH/scripts/run-tests.sh" "$@"
