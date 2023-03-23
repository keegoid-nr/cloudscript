#!/usr/bin/env bash

# Determine the shell that the script is being run from
if [ -n "$ZSH_VERSION" ]; then
  The script is being run from zsh
  emulate -LR zsh
fi

# -----------------------------------------------------
# CloudScript
# Quickly do cloud stuff without leaving the terminal.
#
# Author : Keegan Mullaney
# Company: New Relic
# Email  : kmullaney@newrelic.com
# Website: github.com/keegoid-nr/cloudscript
# License: MIT
#
# debug  : export CS_DEBUG=1
# -----------------------------------------------------

# --------------------------  LIBRARIES

lib-has() {
  type "$1" >/dev/null 2>&1
}

lib-echo() {
  echo "~~~ ${1} ~~~"
}

# output message with encoded characters
# $1 -> string
lib-msg() {
  echo -e "$1"
}

# display error message
# $1 -> string
# $2 -> string
lib-error-check() {
  if [[ $RET -gt 0 ]]; then
    lib-msg "RET=$RET"
    lib-msg "${FUNCNAME[1]}(${BASH_LINENO[0]}) - An error has occurred. ${1}${2}"
    exit 1
  fi
}

# display debug info
lib-debug() {
  [[ $CS_DEBUG -eq 1 ]] && lib-msg "${FUNCNAME[1]}(${BASH_LINENO[0]}) - ARGS: $*"
}

# --------------------------  SETUP PARAMETERS

[[ -z $CS_DEBUG ]] && CS_DEBUG=0
SSH_CONFIG="$HOME/.ssh/config"

# --------------------------- HELPER FUNCTIONS

cs-exit() {
  echo >&2 "Please install $1 before running this script."
  exit 1
}

# $1: additional requirement
checks() {
  if ! lib-has aws; then cs-exit "aws cli v2"; fi
  if ! lib-has jq; then cs-exit "jq"; fi
  if [[ -n $1 ]]; then
    if ! lib-has "$1"; then cs-exit "$1"; fi
  fi
  if [ "$CS_DEBUG" -eq 1 ]; then
    if ! lib-has brew; then cs-exit "brew"; fi
  fi
  if ! aws sts get-caller-identity >/dev/null; then exit 1; fi
  RET="$?"
}

print-version() {
  lib-msg "CloudScript $1"
}

get-node-group() {
  eksctl get ng --cluster "$1" -o json | jq -r '.[].Name'
  RET="$?"
}

get-public-dns() {
  aws ec2 describe-instances --instance-ids "$1" --query 'Reservations[*].Instances[*].PublicDnsName' --output text
  RET="$?"
}

update-ssh() {
  local hostname
  local currentUser
  lib-msg "Checking if instance $1 is running"
  aws ec2 wait instance-running --instance-ids "$1"
  lib-msg "Getting public DNS name"
  hostname=$(get-public-dns "$1")
  lib-msg "\nCurrent ~/.ssh/config:"
  cat ~/.ssh/config
  echo
  lib-msg "Enter a \"Host\" to update from $SSH_CONFIG"
  echo
  read -erp "   : " match
  currentUser=$(sed -rne "/$match/,/User/ {s/.*User (.*)/\1/p}" "$SSH_CONFIG")
  lib-msg "Enter a \"User\" to update from $SSH_CONFIG"
  read -erp "   : " -i "$currentUser" username
  echo
  if grep -q "$match" "$SSH_CONFIG"; then
    # for an existing host, modify Hostname and User
    sed -i.bak -e "/$match/,/User/ s/Hostname.*/Hostname $hostname/" \
      -e "/$match/,/User/ s/User.*/User $username/" "$SSH_CONFIG"
  else
    # add new host
    cat <<-EOF >>"$SSH_CONFIG"
Host $match
  Hostname $hostname
  User $username
EOF
  fi
  lib-msg "\nModified ~/.ssh/config:"
  cat ~/.ssh/config
  RET="$?"
}

list-compatible-runtimes() {
  curl -fsSL "https://$1.layers.newrelic-external.com/get-layers" | jq -r '.Layers[].LatestMatchingVersion.CompatibleRuntimes[]' | sort | uniq
  RET="$?"
}

list-layer-names() {
  curl -fsSL "https://$1.layers.newrelic-external.com/get-layers" | jq -r '.Layers[].LayerName' | sort | uniq
  RET="$?"
}

get-latest-build() {
  curl -fsSL "https://$1.layers.newrelic-external.com/get-layers" | jq -r --arg LAYER "$2" '
  .Layers[]
  | .LayerName as $name
  | .LatestMatchingVersion.Version as $build
  | select($name==$LAYER)
  | [$build]
  | @csv'
  RET="$?"
  [[ $RET -gt 0 ]] && list-layer-names "$1" && exit 0
}

get-layer() {
  local arn
  local xargsOpts
  local unzipOpts
  arn="arn:aws:lambda:$2:451483290750:layer:$1"

  if [[ $CS_DEBUG -eq 1 ]]; then
    xargsOpts="-o"
    unzipOpts="-o"
  else
    xargsOpts="-so"
    unzipOpts="-qo"
  fi

  if [[ $4 == agent ]]; then
    [[ $1 == @(*Java*) ]] && aws --region "$2" lambda get-layer-version --layer-name "$arn" --version-number "$3" | jq -r .Content.Location | xargs curl "$xargsOpts" "$1:$3.zip" && unzip "$unzipOpts" "$1:$3.zip" -d "$1:$3" && stat -c "%y %n" "$1":"$3"/*/*/NewRelic*
    [[ $1 == @(*NodeJS*) ]] && aws --region "$2" lambda get-layer-version --layer-name "$arn" --version-number "$3" | jq -r .Content.Location | xargs curl "$xargsOpts" "$1:$3.zip" && unzip "$unzipOpts" "$1:$3.zip" -d "$1:$3" && stat -c "%y %n" "$1":"$3"/*/*/newrelic/newrelic*
    [[ $1 == @(*Python*) ]] && aws --region "$2" lambda get-layer-version --layer-name "$arn" --version-number "$3" | jq -r .Content.Location | xargs curl "$xargsOpts" "$1:$3.zip" && unzip "$unzipOpts" "$1:$3.zip" -d "$1:$3" && stat -c "%y %n" "$1":"$3"/*/*/*/*/newrelic/agent*
    [[ $1 == @(*Extension*) ]] && lib-msg "an agent does not exist in the $1 layer"
    lib-error-check
  elif [[ $4 == extension ]]; then
    aws --region "$2" lambda get-layer-version --layer-name "$arn" --version-number "$3" | jq -r .Content.Location | xargs curl "$xargsOpts" "$1:$3.zip" && unzip "$unzipOpts" "$1:$3.zip" -d "$1:$3" && stat -c "%y %n" "$1":"$3"/*/newrelic*
    lib-error-check
  else
    lib-msg "------------------------------------------------------------"
    lib-msg "$arn:$3"
    aws --region "$2" lambda get-layer-version --layer-name "$arn" --version-number "$3" | jq -r .Content.Location | xargs curl "$xargsOpts" "$1:$3.zip" && unzip "$unzipOpts" "$1:$3.zip" -d "$1:$3" && ls -l "$1:$3"
    lib-error-check
  fi
  RET="$?"
}

# --------------------------- FUNCTIONS

list-layers() {
  lib-debug "$@"
  local compatibleRuntime
  local region

  compatibleRuntime="$1"
  region="$2"

  [[ -z $region ]] && region="$(aws configure get region)"
  [[ -z $region ]] && region="us-west-2"
  [[ -z $compatibleRuntime ]] && list-compatible-runtimes "$region" && exit 0

  if [[ $compatibleRuntime == all ]]; then
    curl -fsSL "https://$region.layers.newrelic-external.com/get-layers" | jq .
  else
    curl -fsSL "https://$region.layers.newrelic-external.com/get-layers?CompatibleRuntime=$compatibleRuntime" | jq .
  fi
  lib-error-check
}

download-layers() {
  lib-debug "$@"
  local layer
  local region
  local build
  local glob
  local arn

  layer="$1"
  region="$2"
  build="$3"
  glob="$4"

  [[ -z $region ]] && region="$(aws configure get region)"
  [[ -z $region ]] && region="us-west-2"
  [[ -z $layer ]] && list-layer-names "$region" && exit 0

  checks "unzip"
  checks "xargs"

  if [[ $layer == all ]]; then
    for l in $(list-layer-names "$region"); do
      build=$(get-latest-build "$region" "$l")
      get-layer "$l" "$region" "$build" "$glob"
      lib-debug "$@"
      lib-error-check "$l:$region:$build"
    done
  else
    [[ -z $build ]] || [[ $build == latest ]] && build=$(get-latest-build "$region" "$layer")
    if [[ -z $build ]]; then
      lib-msg "-------------------------------------------------------"
      lib-msg "- check that your layer name is correct and try again -"
      lib-msg "-------------------------------------------------------"
      list-layer-names "$region" && exit 0
    else
      get-layer "$layer" "$region" "$build" "$glob"
    fi
  fi
  RET="$?"
  lib-error-check
}

clusters() {
  lib-debug "$@"
  aws eks list-clusters | jq -r '.clusters[] | .'
  # aws eks list-clusters --output text
  # eksctl get cluster -o json | jq -r '.[].metadata.name'
  RET="$?"
  lib-error-check
}

eks-status() {
  lib-debug "$@"
  [[ -z $1 ]] && clusters "$@" && cs-unset 1
  lib-echo "Status"
  aws eks describe-cluster --name "$1" --query 'cluster.status' --output text
  lib-msg "nodegroup capacity: $(eksctl get ng --cluster "$1" -o json | jq -r '.[].DesiredCapacity')"
  RET="$?"
  lib-error-check
}

eks-start() {
  lib-debug "$@"
  [[ -z $1 ]] && clusters "$@" && cs-unset 1
  [[ -z $2 ]] && cs-usage 1
  lib-echo "Start (scale up)"
  eksctl scale ng "$(get-node-group "$1")" --cluster "$1" -N "$2"
  RET="$?"
  lib-error-check
}

eks-stop() {
  lib-debug "$@"
  [[ -z $1 ]] && clusters "$@" && cs-unset 1
  lib-echo "Stop (scale down)"
  eksctl scale ng "$(get-node-group "$1")" --cluster "$1" -N 0
  RET="$?"
  lib-error-check
}

ids() {
  lib-debug "$@"
  aws ec2 describe-instances | jq -r '
  .Reservations[].Instances[]
  | .InstanceId as $id
  | .Tags[]?
  | select(.Key=="Name")
  | .Value as $value
  | [$value, $id]
  | @csv'
  RET="$?"
  lib-error-check
}

status() {
  lib-debug "$@"
  [[ -z $1 ]] && ids "$@" && cs-unset 1
  lib-echo "Status"
  aws ec2 describe-instance-status --instance-ids "$1"
  RET="$?"
  lib-error-check
}

start() {
  lib-debug "$@"
  [[ -z $1 ]] && ids && cs-unset 1
  lib-echo "Start"
  aws ec2 start-instances --instance-ids "$1"
  RET="$?"
  lib-error-check
  update-ssh "$1"
  RET="$?"
  lib-error-check
}

stop() {
  lib-debug "$@"
  [[ -z $1 ]] && ids && cs-unset 1
  lib-echo "Stop"
  aws ec2 stop-instances --instance-ids "$1"
  RET="$?"
  lib-error-check
}

restart() {
  lib-debug "$@"
  [[ -z $1 ]] && ids && cs-unset 1
  lib-echo "Restart"
  aws ec2 reboot-instances --instance-ids "$1"
  RET="$?"
  lib-error-check
}

ssh() {
  lib-debug "$@"
  [[ -z $1 ]] && ids && cs-unset 1
  start "$1"
}

# --------------------------- CLI

# unset functions to free up memmory
cs-unset() {
  unset -f cs-version cs-usage cs-thanks cs-lambda-go cs-eks-go cs-ec2-go
  exit $1
}

cs-version() {
  print-version "v0.9"
  cs-unset
}

cs-usage() {
  echo "Usage: cs [OPTIONS] COMPONENT COMMAND [REQUIRED ARGS]... (OPTIONAL ARGS)..."
  echo
  echo "Options:"
  echo "  -v, --version  Show version"
  echo "  --help         Show this message and exit."
  echo
  echo "Components:"
  echo "  ec2            Manage EC2 instance states"
  echo "  eks            Manage EKS node states"
  echo "  lambda         List and download New Relic Lambda layers"
  echo
  echo "Commands and Args:"
  echo "  ec2 start|stop|restart|status|ssh [instanceId]"
  echo "  eks start [cluster] [number of nodes]"
  echo "  eks stop|status [cluster]"
  echo "  lambda list-layers (compatibleRuntime|all) (region)"
  echo "  lambda download-layers (layer|all) (region) (build#|latest) (extension|agent)"
  cs-unset $1
}

# display message before exit
cs-thanks() {
  if lib-has figlet; then
    figlet -f small "CloudScript"
  else
    lib-msg "CloudScript"
  fi
  lib-msg "Made with <3 by Keegan Mullaney, a Senior Technical Support Engineer at New Relic."
  cs-unset 0
}

# $1: lambda
# $2: operation
# list-layers
# $3: compatibleRuntime|layer|all (optional), if blank will get a list of compatible runtimes or layer names
# $4: region (optional), if blank will use default region
# $5: build#|latest (optional), if blank will get latest layers
# $6: extension|agent (optional), if blank will show details for both
cs-lambda-go() {
  checks "aws"
  checks "curl"

  case "$2" in
  'list-layers')
    list-layers "$3" "$4"
    ;;
  'download-layers')
   download-layers "$3" "$4" "$5" "$6"
    ;;
  *)
    cs-usage 1
    ;;
  esac
}

# $1: eks
# $2: operation
# $3: cluster name (optional), if blank will get list of available clusters
# $4: node count for scaling up
cs-eks-go() {
  checks "eksctl"

  case "$2" in
  'start')
    eks-start "$3" "$4"
    ;;
  'stop')
    eks-stop "$3"
    ;;
  'status')
    eks-status "$3"
    ;;
  *)
    cs-usage 1
    ;;
  esac
}

# $1: operation
# $2: instanceId (optional), if blank will get list of available instanceIds
cs-ec2-go() {
  checks

  case "$2" in
  'start')
    start "$3"
    ;;
  'stop')
    stop "$3"
    ;;
  'restart')
    restart "$3"
    ;;
  'status')
    status "$3"
    ;;
  'ssh')
    ssh "$3"
    ;;
  *)
    cs-usage 1
    ;;
  esac
}

# --------------------------  MAIN

# capture input array
userCommand=("$@")

for c in ${userCommand[0]}; do
  [[ $CS_DEBUG == 1 ]] && echo "$c"
  if [[ $c == "-v" ]] || [[ $c == "--version" ]]; then
    cs-version "$c"
  elif [[ $c == "--help" ]]; then
    cs-usage 0
  elif [[ $c == "lambda" ]]; then
    cs-lambda-go "${userCommand[@]}"
  elif [[ $c == "eks" ]]; then
    cs-eks-go "${userCommand[@]}"
  elif [[ $c == "ec2" ]]; then
    cs-ec2-go "${userCommand[@]}"
  else
    cs-usage 1
  fi
done

cs-thanks
