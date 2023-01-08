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

# --------------------------  SETUP PARAMETERS

[ -z "$CS_DEBUG" ] && CS_DEBUG=0
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
  if [ -n "$1" ]; then
    if ! lib_has "$1"; then cs_exit "$1"; fi
  fi
  if [ $CS_DEBUG -eq 1 ]; then
    if ! lib_has brew; then cs_exit "brew"; fi
  fi
  if ! aws sts get-caller-identity >/dev/null; then exit 1; fi
}

getNodeGroup() {
  eksctl get ng --cluster "$1" -o json | jq -r '.[].Name'
}

getPublicDns() {
  aws ec2 describe-instances --instance-ids "$1" --query 'Reservations[*].Instances[*].PublicDnsName' --output text
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
    cat <<-EOF >> "$SSH_CONFIG"
Host $match
  Hostname $hostname
  User $username
EOF
  fi
  lib_msg "\nModified ~/.ssh/config:"
  cat ~/.ssh/config
}

listCompatibleRuntimes() {
  lib_echo "Compatible Runtimes"
  curl -fsSL "https://$1.layers.newrelic-external.com/get-layers" | jq -r '.Layers[].LatestMatchingVersion.CompatibleRuntimes[]' | sort | uniq
}

listLayerNames() {
  lib_echo "Layer Names"
  curl -fsSL "https://$1.layers.newrelic-external.com/get-layers" | jq -r '.Layers[].LayerName' | sort | uniq
}

getLatestBuild() {
  curl -fsSL "https://$1.layers.newrelic-external.com/get-layers" | jq -r --arg LAYER "$2" '
  .Layers[]
  | .LayerName as $name
  | .LatestMatchingVersion.Version as $build
  | select($name==$LAYER)
  | [$build]
  | @csv'
}

getLayerArn() {
  curl -fsSL "https://$1.layers.newrelic-external.com/get-layers?CompatibleRuntime=$compatibleRuntime" | jq .
}

# --------------------------- FUNCTIONS

list-layers() {
  local compatibleRuntime
  local region

  compatibleRuntime="$1"
  region="$2"

  checks "aws"
  checks "curl"

  [ -z "$region" ] && region="$(aws configure get region)"
  [ -z "$region" ] && region="us-west-2"
  [ -z "$compatibleRuntime" ] && listCompatibleRuntimes "$region" && exit 0

  if [ "$compatibleRuntime" == "all" ]; then
    curl -fsSL "https://$region.layers.newrelic-external.com/get-layers" | jq .
  else
    curl -fsSL "https://$region.layers.newrelic-external.com/get-layers?CompatibleRuntime=$compatibleRuntime" | jq .
  fi
}

download-layers() {
  local layer
  local region
  local build
  local arn

  layer="$1"
  region="$2"
  build="$3"

  checks "aws"
  checks "curl"

  [ -z "$region" ] && region="$(aws configure get region)"
  [ -z "$region" ] && region="us-west-2"
  [ -z "$layer" ] && listLayerNames "$region" && exit 0
  [ -z "$build" ] && build=$(getLatestBuild "$region" "$layer")

  checks "unzip"
  checks "xargs"

  if [ "$layer" == "all" ]; then
    # while loop
      # lib_echo "Layer ARN"
      # arn="arn:aws:lambda:$region:451483290750:layer:$layer"
      # lib_msg "$arn:$build"
      lib_msg "todo: download all layers"
      lib_echo "Download Layers"
      # aws --region "$region" lambda get-layer-version --layer-name "$arn" --version-number "$build" | jq -r .Content.Location | xargs curl -so "$layer:$build.zip" && unzip -qo "$layer:$build.zip" -d "$layer:$build" && ls -l "$layer:$build"
    # end
  else
    lib_echo "Layer ARN"
    arn="arn:aws:lambda:$region:451483290750:layer:$layer"
    lib_msg "$arn:$build"
    lib_echo "Download Layers"
    aws --region "$region" lambda get-layer-version --layer-name "$arn" --version-number "$build" | jq -r .Content.Location | xargs curl -so "$layer:$build.zip" && unzip -qo "$layer:$build.zip" -d "$layer:$build" && ls -l "$layer:$build"
  fi
}

eks-status() {
  lib_echo "Status"
  aws eks describe-cluster --name "$1" --query 'cluster.status' --output text
  lib_msg "nodegroup capacity: $(eksctl get ng --cluster "$1" -o json | jq -r '.[].DesiredCapacity')"
}

eks-start() {
  lib_echo "Start (scale up)"
  eksctl scale ng "$(getNodeGroup "$1")" --cluster "$1" -N "$2"
}

eks-stop() {
  lib_echo "Stop (scale down)"
  eksctl scale ng "$(getNodeGroup "$1")" --cluster "$1" -N 0
}

clusters() {
  lib_echo "Clusters"
  lib_msg "Getting EKS cluster names"
  aws eks list-clusters --output text
  # eksctl get cluster -o json | jq -r '.[].metadata.name'
}

status() {
  lib_echo "Status"
  aws ec2 describe-instance-status --instance-ids "$1"
}

start() {
  lib_echo "Start"
  aws ec2 start-instances --instance-ids "$1"
  updateSSH "$1"
}

stop() {
  lib_echo "Stop"
  aws ec2 stop-instances --instance-ids "$1"
}

restart() {
  lib_echo "Restart"
  aws ec2 reboot-instances --instance-ids "$1"
}

ssh() {
  start "$1"
}

ids() {
  lib_echo "Ids"
  lib_msg "Getting EC2 names and instanceIds"
  # aws ec2 describe-instances | jq -r '.Reservations[].Instances[] | .InstanceId as $id | .Tags[]? | select(.Key=="Name") | .Value as $value | [$value, $id] | @csv'
  aws ec2 describe-instances | jq -r '
  .Reservations[].Instances[]
  | .InstanceId as $id
  | .Tags[]?
  | select(.Key=="Name")
  | .Value as $value
  | [$value, $id]
  | @csv'
}

usage() {
  echo
  echo "cs command [required] (optional)"
  echo "--------------------------------"
  echo "EC2    Usage: $1        start|stop|restart|status|ssh [instanceId]"
  echo "EKS    Usage: $1 eks    start [cluster] [nodes] | stop|status [cluster]"
  echo "Lambda Usage: $1 lambda list-layers (compatibleRuntime|all) (region) | download-layers (layer|all) (region) (build)"
  echo
}

# --------------------------- CLI

# display message before exit
cs_thanks() {
  echo
  if lib_has figlet; then
    figlet -f small "CloudScript"
  else
    lib_msg "CloudScript"
  fi
  lib_msg "Made with <3 by Keegan Mullaney, a Senior Technical Support Engineer at New Relic."
}

# $1: lambda
# $2: operation
# list-layers
# $3: compatibleRuntime|layer|all (optional), if blank will get a list of compatible runtimes or layer names
# $4: region (optional), if blank will use default region
# $5: build (optional), if blank will get latest layers
cs_lambda_go() {
  case "$2" in
  'list-layers')
    list-layers "$3" "$4"
    ;;
  'download-layers')
    download-layers "$3" "$4" "$5"
    ;;
  *)
    usage "$0"
    exit 1
    ;;
  esac
}

# $1: eks
# $2: operation
# $3: cluster name (optional), if blank will get list of available clusters
# $4: node count for scaling up
cs_eks_go() {
  checks "eksctl"

  # if no cluster name is provided, get them
  if [ -n "$2" ] && [ -z "$3" ]; then
    clusters
    exit 1
  fi

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
    usage "$0"
    exit 1
    ;;
  esac
}

# $1: operation
# $2: instanceId (optional), if blank will get list of available instanceIds
cs_go() {
  checks

  # if no instanceId is provided, get them
  if [ -n "$1" ] && [ -z "$2" ]; then
    ids
    exit 1
  fi

  case "$1" in
  'start')
    start "$2"
    ;;
  'stop')
    stop "$2"
    ;;
  'restart')
    restart "$2"
    ;;
  'status')
    status "$2"
    ;;
  'ssh')
    ssh "$2"
    ;;
  *)
    usage "$0"
    exit 1
    ;;
  esac
}

# unset functions to free up memmory
cs_unset() {
  unset -f cs_go cs_eks_go cs_lambda_go cs_thanks
}

# --------------------------  MAIN

[ $CS_DEBUG -eq 1 ] && echo "arguments:" "$@"

if [ "lambda" = "$1" ]; then
  # lambda
  cs_lambda_go "$@"
elif [ "eks" = "$1" ]; then
  # eks
  cs_eks_go "$@"
else
  # ec2
  cs_go "$@"
fi
cs_thanks
cs_unset
exit 0
