#!/bin/bash

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

lib_has() {
  type "$1" >/dev/null 2>&1
}

lib_echo() {
  echo
  echo "~~~ ${1} ~~~"
  echo
}

# output message with encoded characters
# $1 -> string
lib_msg() {
  echo -e "$1"
}

# display error message
# $1 -> string
# $2 -> string
lib_error_check() {
  if [[ $RET -gt 0 ]]; then
    lib_msg "RET=$RET"
    lib_msg "${FUNCNAME[1]}(${BASH_LINENO[0]}) - An error has occurred. ${1}${2}"
    exit 1
  fi
}

# display debug info
lib_debug() {
  [[ $CS_DEBUG -eq 1 ]] && lib_msg "${FUNCNAME[1]}(${BASH_LINENO[0]}) - ARGS: $*"
}

# --------------------------  SETUP PARAMETERS

[[ -z $CS_DEBUG ]] && CS_DEBUG=0
SSH_CONFIG="$HOME/.ssh/config"

# --------------------------- HELPER FUNCTIONS

cs_exit() {
  echo >&2 "Please install $1 before running this script."
  exit 1
}

# $1: additional requirement
checks() {
  if ! lib_has aws; then cs_exit "aws cli v2"; fi
  if ! lib_has jq; then cs_exit "jq"; fi
  if [[ -n $1 ]]; then
    if ! lib_has "$1"; then cs_exit "$1"; fi
  fi
  if [ "$CS_DEBUG" -eq 1 ]; then
    if ! lib_has brew; then cs_exit "brew"; fi
  fi
  if ! aws sts get-caller-identity >/dev/null; then exit 1; fi
  RET="$?"
}

printVersion() {
  lib_msg "CloudScript $1"
}

getNodeGroup() {
  eksctl get ng --cluster "$1" -o json | jq -r '.[].Name'
  RET="$?"
}

getPublicDns() {
  aws ec2 describe-instances --instance-ids "$1" --query 'Reservations[*].Instances[*].PublicDnsName' --output text
  RET="$?"
}

updateSSH() {
  local hostname
  local currentUser
  lib_msg "Checking if instance $1 is running"
  aws ec2 wait instance-running --instance-ids "$1"
  lib_msg "Getting public DNS name"
  hostname=$(getPublicDns "$1")
  lib_msg "\nCurrent ~/.ssh/config:"
  cat ~/.ssh/config
  echo
  lib_msg "Enter a \"Host\" to update from $SSH_CONFIG"
  echo
  read -erp "   : " match
  currentUser=$(sed -rne "/$match/,/User/ {s/.*User (.*)/\1/p}" "$SSH_CONFIG")
  lib_msg "Enter a \"User\" to update from $SSH_CONFIG"
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
  lib_msg "\nModified ~/.ssh/config:"
  cat ~/.ssh/config
  RET="$?"
}

listCompatibleRuntimes() {
  curl -fsSL "https://$1.layers.newrelic-external.com/get-layers" | jq -r '.Layers[].LatestMatchingVersion.CompatibleRuntimes[]' | sort | uniq
  RET="$?"
}

listLayerNames() {
  curl -fsSL "https://$1.layers.newrelic-external.com/get-layers" | jq -r '.Layers[].LayerName' | sort | uniq
  RET="$?"
}

getLatestBuild() {
  curl -fsSL "https://$1.layers.newrelic-external.com/get-layers" | jq -r --arg LAYER "$2" '
  .Layers[]
  | .LayerName as $name
  | .LatestMatchingVersion.Version as $build
  | select($name==$LAYER)
  | [$build]
  | @csv'
  RET="$?"
}

getLayer() {
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
    [[ $1 == @(*Java*) ]] && aws --region "$2" lambda get-layer-version --layer-name "$arn" --version-number "$3" | jq -r .Content.Location | xargs curl "$xargsOpts" "$1:$3.zip" && unzip "$unzipOpts" "$1:$3.zip" -d "$1:$3" && stat --format="%y %n" "$1":"$3"/*/*/NewRelic*
    [[ $1 == @(*NodeJS*) ]] && aws --region "$2" lambda get-layer-version --layer-name "$arn" --version-number "$3" | jq -r .Content.Location | xargs curl "$xargsOpts" "$1:$3.zip" && unzip "$unzipOpts" "$1:$3.zip" -d "$1:$3" && stat --format="%y %n" "$1":"$3"/*/*/newrelic/newrelic*
    [[ $1 == @(*Python*) ]] && aws --region "$2" lambda get-layer-version --layer-name "$arn" --version-number "$3" | jq -r .Content.Location | xargs curl "$xargsOpts" "$1:$3.zip" && unzip "$unzipOpts" "$1:$3.zip" -d "$1:$3" && stat --format="%y %n" "$1":"$3"/*/*/*/*/newrelic/agent*
    [[ $1 == @(*Extension*) ]] && lib_msg "an agent does not exist in the $1 layer"
    lib_error_check
  elif [[ $4 == extension ]]; then
    aws --region "$2" lambda get-layer-version --layer-name "$arn" --version-number "$3" | jq -r .Content.Location | xargs curl "$xargsOpts" "$1:$3.zip" && unzip "$unzipOpts" "$1:$3.zip" -d "$1:$3" && stat --format="%y %n" "$1":"$3"/*/newrelic*
    lib_error_check
  else
    lib_msg "------------------------------------------------------------"
    lib_msg "$arn:$3"
    aws --region "$2" lambda get-layer-version --layer-name "$arn" --version-number "$3" | jq -r .Content.Location | xargs curl "$xargsOpts" "$1:$3.zip" && unzip "$unzipOpts" "$1:$3.zip" -d "$1:$3" && ls -l "$1:$3"
    lib_error_check
  fi
  RET="$?"
}

# --------------------------- FUNCTIONS

list-layers() {
  lib_debug "$@"
  local compatibleRuntime
  local region

  compatibleRuntime="$1"
  region="$2"

  [[ -z $region ]] && region="$(aws configure get region)"
  [[ -z $region ]] && region="us-west-2"
  [[ -z $compatibleRuntime ]] && listCompatibleRuntimes "$region" && exit 0

  if [[ $compatibleRuntime == all ]]; then
    curl -fsSL "https://$region.layers.newrelic-external.com/get-layers" | jq .
  else
    curl -fsSL "https://$region.layers.newrelic-external.com/get-layers?CompatibleRuntime=$compatibleRuntime" | jq .
  fi
  lib_error_check
}

download-layers() {
  lib_debug "$@"
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
  [[ -z $layer ]] && listLayerNames "$region" && exit 0

  checks "unzip"
  checks "xargs"

  if [[ $layer == all ]]; then
    for l in $(listLayerNames "$region"); do
      build=$(getLatestBuild "$region" "$l")
      getLayer "$l" "$region" "$build" "$glob"
      lib_debug "$@"
      lib_error_check "$l:$region:$build"
    done
  else
    [[ -z $build ]] || [[ $build == latest ]] && build=$(getLatestBuild "$region" "$layer")
    getLayer "$layer" "$region" "$build" "$glob"
  fi
  lib_error_check
}

clusters() {
  lib_debug
  aws eks list-clusters --output text
  # eksctl get cluster -o json | jq -r '.[].metadata.name'
  RET="$?"
  lib_error_check
}

eks-status() {
  lib_debug "$@"
  [[ -z $1 ]] && clusters && cs_unset 1
  lib_echo "Status"
  aws eks describe-cluster --name "$1" --query 'cluster.status' --output text
  lib_msg "nodegroup capacity: $(eksctl get ng --cluster "$1" -o json | jq -r '.[].DesiredCapacity')"
  RET="$?"
  lib_error_check
}

eks-start() {
  lib_debug "$@"
  [[ -z $1 ]] && clusters && cs_unset 1
  [[ -z $2 ]] && cs_usage 1
  lib_echo "Start (scale up)"
  eksctl scale ng "$(getNodeGroup "$1")" --cluster "$1" -N "$2"
  RET="$?"
  lib_error_check
}

eks-stop() {
  lib_debug "$@"
  [[ -z $1 ]] && clusters && cs_unset 1
  lib_echo "Stop (scale down)"
  eksctl scale ng "$(getNodeGroup "$1")" --cluster "$1" -N 0
  RET="$?"
  lib_error_check
}

ids() {
  lib_debug
  aws ec2 describe-instances | jq -r '
  .Reservations[].Instances[]
  | .InstanceId as $id
  | .Tags[]?
  | select(.Key=="Name")
  | .Value as $value
  | [$value, $id]
  | @csv'
  RET="$?"
  lib_error_check
}

status() {
  lib_debug "$@"
  [[ -z $1 ]] && ids && cs_unset 1
  lib_echo "Status"
  aws ec2 describe-instance-status --instance-ids "$1"
  RET="$?"
  lib_error_check
}

start() {
  lib_debug "$@"
  [[ -z $1 ]] && ids && cs_unset 1
  lib_echo "Start"
  aws ec2 start-instances --instance-ids "$1"
  updateSSH "$1"
  lib_error_check
}

stop() {
  lib_debug "$@"
  [[ -z $1 ]] && ids && cs_unset 1
  lib_echo "Stop"
  aws ec2 stop-instances --instance-ids "$1"
  RET="$?"
  lib_error_check
}

restart() {
  lib_debug "$@"
  [[ -z $1 ]] && ids && cs_unset 1
  lib_echo "Restart"
  aws ec2 reboot-instances --instance-ids "$1"
  RET="$?"
  lib_error_check
}

ssh() {
  lib_debug "$@"
  [[ -z $1 ]] && ids && cs_unset 1
  start "$1"
  lib_error_check
}

# --------------------------- CLI

# unset functions to free up memmory
cs_unset() {
  unset -f cs_version cs_usage cs_thanks cs_lambda_go cs_eks_go cs_ec2_go
  exit $1
}

cs_version() {
  printVersion "v0.7"
  cs_unset
}

cs_usage() {
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
  cs_unset $1
}

# display message before exit
cs_thanks() {
  echo
  if lib_has figlet; then
    figlet -f small "CloudScript"
  else
    lib_msg "CloudScript"
  fi
  lib_msg "Made with <3 by Keegan Mullaney, a Senior Technical Support Engineer at New Relic."
  cs_unset 0
}

# $1: lambda
# $2: operation
# list-layers
# $3: compatibleRuntime|layer|all (optional), if blank will get a list of compatible runtimes or layer names
# $4: region (optional), if blank will use default region
# $5: build#|latest (optional), if blank will get latest layers
# $6: extension|agent (optional), if blank will show details for both
cs_lambda_go() {
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
    cs_usage 1
    ;;
  esac
}

# $1: eks
# $2: operation
# $3: cluster name (optional), if blank will get list of available clusters
# $4: node count for scaling up
cs_eks_go() {
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
    cs_usage 1
    ;;
  esac
}

# $1: operation
# $2: instanceId (optional), if blank will get list of available instanceIds
cs_ec2_go() {
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
    cs_usage 1
    ;;
  esac
}

# --------------------------  MAIN

if [[ "-v" == "$1" ]] || [[ "--version" == "$1" ]]; then
  cs_version "$1"
elif [[ "--help" == "$1" ]]; then
  cs_usage 0
elif [[ lambda == "$1" ]]; then
  # lambda
  cs_lambda_go "$@"
elif [[ eks == "$1" ]]; then
  # eks
  cs_eks_go "$@"
elif [[ ec2 == "$1" ]]; then
  # ec2
  cs_ec2_go "$@"
else
 cs_usage 1
fi
cs_thanks
