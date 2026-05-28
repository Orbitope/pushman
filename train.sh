#!/bin/bash
# Pushman training launcher
# Usage:
#   ./train.sh                                              # fast experiment, editor mode
#   ./train.sh --config=ppo_discrete_baseline --id=Pushman_v1
#   ./train.sh --standalone                                 # use builds/Pushman_Training binary
#   ./train.sh --resume                                     # resume from last checkpoint
#   ./train.sh --init-from=Pushman_Bot_v2                   # warm-start weights from prior run
#   ./train.sh --config=ppo_selfplay --id=Pushman_SelfPlay_v1 --standalone

set -e

VENV="$(dirname "$0")/venv-mlagents"
CONFIG="ppo_fast_experiment"
RUN_ID="Pushman_Fast_v1"
STANDALONE=false
RESUME=false
INIT_FROM=""
EXTRA_ARGS=""

for arg in "$@"; do
  case $arg in
    --config=*)    CONFIG="${arg#*=}" ;;
    --id=*)        RUN_ID="${arg#*=}" ;;
    --standalone)  STANDALONE=true ;;
    --resume)      RESUME=true ;;
    --init-from=*) INIT_FROM="${arg#*=}" ;;
    --force)       EXTRA_ARGS="$EXTRA_ARGS --force" ;;
  esac
done

if [ ! -d "$VENV" ]; then
  echo "ERROR: venv-mlagents not found. See PLAN.md Training Pipeline > Setup."
  exit 1
fi

source "$VENV/bin/activate"

CMD="mlagents-learn TrainingConfigs/${CONFIG}.yaml --run-id=${RUN_ID} --timeout-wait=600"

if $STANDALONE; then
  BUILD_DIR="$(dirname "$0")/builds/Pushman_Training"
  if [ -d "$BUILD_DIR" ] && [ -f "$BUILD_DIR/Pushman" ]; then
    # Preferred: Unity Server Build folder (genuinely headless — no graphics pipeline init).
    BUILD="$BUILD_DIR/Pushman"
    echo "[train.sh] Mode: STANDALONE (Server Build)  binary=$BUILD"
    # gRPC native lib lives in PlugIns/ but the loader searches Data/Managed/ — recreate
    # symlink every launch since a fresh build wipes Data/Managed/.
    GRPC_SRC="$BUILD_DIR/PlugIns/libgrpc_csharp_ext.x64.bundle"
    GRPC_DST="$BUILD_DIR/Data/Managed/libgrpc_csharp_ext.x64.bundle"
    if [ -f "$GRPC_SRC" ]; then
      # Use a path relative to the symlink's own directory so it resolves correctly
      # regardless of where train.sh is invoked from.
      ln -sf "../../PlugIns/libgrpc_csharp_ext.x64.bundle" "$GRPC_DST"
      echo "[train.sh] gRPC symlink refreshed."
    else
      echo "WARNING: gRPC bundle not found at $GRPC_SRC — communication may fail."
    fi
  elif [ -d "$BUILD_DIR.app" ]; then
    # Fallback: regular macOS .app — still works but slower (inits graphics even with --no-graphics).
    BUILD="$BUILD_DIR"
    echo "[train.sh] Mode: STANDALONE (macOS .app — prefer Server Build for speed)  build=$BUILD_DIR.app"
  else
    echo "ERROR: Standalone build not found at builds/Pushman_Training or builds/Pushman_Training.app"
    echo "       For training: Build Profiles → macOS Server → Build → save as builds/Pushman_Training"
    exit 1
  fi
  CMD="$CMD --env=$BUILD --no-graphics"
else
  echo "[train.sh] Mode: EDITOR — press Play in Unity on ML_Training_Scene_Small after 'Listening on port 5004'"
fi

if $RESUME; then
  CMD="$CMD --resume"
  echo "[train.sh] Resuming from existing checkpoint."
else
  CMD="$CMD --force"
fi

if [ -n "$INIT_FROM" ]; then
  CMD="$CMD --initialize-from=${INIT_FROM}"
  echo "[train.sh] Warm-starting weights from run: $INIT_FROM"
fi

CMD="$CMD $EXTRA_ARGS"

echo "[train.sh] Config: $CONFIG  Run ID: $RUN_ID"
echo "[train.sh] Running: $CMD"
echo ""

eval $CMD
