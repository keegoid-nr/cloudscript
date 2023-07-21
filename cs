#!/usr/bin/env bash
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

set -o errexit
set -o nounset
set -o pipefail

# Determine the shell that the script is being run from
if [ -n "$ZSH_VERSION" ]; then
  # Zsh is running, set offset to 1
  offset=1
  emulate -LR zsh
else
  # Bash is running, set offset to 0
  offset=0
fi

# --------------------------  SETUP PARAMETERS

[[ -z $CS_DEBUG ]] && CS_DEBUG=0
SSH_CONFIG="$HOME/.ssh/config"
VERSION="v1.5"

# --------------------------  LIBRARIES

is_number() {
    input="$1"

    case $input in
        ''|*[!0-9]*) return 1 ;; # false, input is not a number (integer)
        *) return 0 ;;           # true, input is a number (integer)
    esac
}

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
  local error_message="${1:-}"
  local exit_code="${2:-1}"
  if [ -n "$error_message" ]; then
    lib-msg "An error occurred: $error_message"
  else
    lib_debug
  fi
  exit "$exit_code"
}

# display debug info
lib-debug() {
  if [ -n "$ZSH_VERSION" ]; then
    lib-msg "${funcstack[$offset+1]}(${funcline[$offset]}) - ARGS: $*"
  else
    lib-msg "${FUNCNAME[$offset+1]}(${BASH_LINENO[$offset]}) - ARGS: $*"
  fi
}

# --------------------------  SETUP PARAMETERS

[[ -z $CS_DEBUG ]] && CS_DEBUG=0
SSH_CONFIG="$HOME/.ssh/config"
VERSION="v1.6"

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
  if ! aws sts get-caller-identity >/dev/null; then exit 1; fi
  RET="$?"
}

print_table() {
  input="$1"
  echo -e "$input" | column -t -s '|'
}

print_row() {
  printf "%-40s %-9s %-19s %-19s %-9s %-9s %-13s %-16s %-14s %-12s\n" "$@"
}

print-version() {
  lib-msg "CloudScript $1"
}

get-node-group() {
  eksctl get ng --cluster "$1" -o json | jq -r '.[].Name'
  RET="$?"
}

get-public-dns() {
  aws ec2 describe-instances --instance-ids "$1" --query 'Reservations[*].Instances[*].PublicDnsName' --output text --no-paginate
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
  curl -fsSL "https://$1.layers.newrelic-external.com/get-layers" | jq -r --arg LAYER "$2" '.Layers[] | select(.LayerName == $LAYER) | .LatestMatchingVersion.Version'
  RET="$?"
  [[ $RET -gt 0 ]] && list-layer-names "$1" && exit 0
}

get-layer() {
  local arn
  local build
  local xargsOpts
  local unzipOpts
  local pythonRuntime
  local v
  arn="arn:aws:lambda:$2:451483290750:layer:$1"
  build="$3"
  pythonRuntime=${1: -2}
  v="${pythonRuntime:0:1}.${pythonRuntime:1}"

  if ! is_number "$build"; then
    lib-msg "expected: (build#), received: ($build)"
    echo
    cs-usage 1
  fi

  if [[ $CS_DEBUG -eq 1 ]]; then
    xargsOpts="-o"
    unzipOpts="-o"
  else
    xargsOpts="-so"
    unzipOpts="-qo"
  fi

  if [[ $4 == agent ]]; then
    [[ $1 == @(*Java*) ]] && aws --region "$2" lambda get-layer-version --layer-name "$arn" --version-number "$3" --query 'Content.Location' --output text --no-paginate | xargs curl "$xargsOpts" "$1:$3.zip" && unzip "$unzipOpts" "$1:$3.zip" -d "$1:$3" && stat -c "%y %n" "$1":"$3"/*/*/NewRelic*
    [[ $1 == @(*NodeJS*) ]] && aws --region "$2" lambda get-layer-version --layer-name "$arn" --version-number "$3" --query 'Content.Location' --output text --no-paginate | xargs curl "$xargsOpts" "$1:$3.zip" && unzip "$unzipOpts" "$1:$3.zip" -d "$1:$3" && stat -c "%y %n" "$1":"$3"/*/*/newrelic/newrelic* &&  grep 'newrelic/-' "$1":"$3"/nodejs/package-lock.json | uniq
    [[ $1 == @(*Python*) ]] && aws --region "$2" lambda get-layer-version --layer-name "$arn" --version-number "$3" --query 'Content.Location' --output text --no-paginate | xargs curl "$xargsOpts" "$1:$3.zip" && unzip "$unzipOpts" "$1:$3.zip" -d "$1:$3" && stat -c "%y %n" "$1":"$3"/*/*/*/*/newrelic/agent* && cat "$1":"$3"/python/lib/python"$v"/site-packages/newrelic/version.txt && echo
    [[ $1 == @(*Extension*) ]] && lib-msg "an agent does not exist in the $1 layer"
    lib-error-check "$?"
  elif [[ $4 == extension ]]; then
    aws --region "$2" lambda get-layer-version --layer-name "$arn" --version-number "$3" --query 'Content.Location' --output text --no-paginate | xargs curl "$xargsOpts" "$1:$3.zip" && unzip "$unzipOpts" "$1:$3.zip" -d "$1:$3" && stat -c "%y %n" "$1":"$3"/*/newrelic*
    lib-error-check "$?"
  else
    lib-msg "------------------------------------------------------------"
    lib-msg "$arn:$3"
    aws --region "$2" lambda get-layer-version --layer-name "$arn" --version-number "$3" --query 'Content.Location' --output text --no-paginate | xargs curl "$xargsOpts" "$1:$3.zip" && unzip "$unzipOpts" "$1:$3.zip" -d "$1:$3" && ls -l "$1:$3"
    lib-error-check "$?"
  fi
}

# --------------------------- LAMBDA FUNCTIONS

list-layers() {
  [[ $CS_DEBUG -eq 1 ]] && lib-debug "$@"
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
  lib-error-check "$?"
}

download-layers() {
  [[ $CS_DEBUG -eq 1 ]] && lib-debug "$@"
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
      [[ $CS_DEBUG -eq 1 ]] && lib-debug "$@"
      lib-error-check 1 "$l:$region:$build"
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
  lib-error-check "$?"
}

# --------------------------- EKS FUNCTIONS

clusters() {
  [[ $CS_DEBUG -eq 1 ]] && lib-debug "$@"
  clusters=$(aws eks list-clusters --query 'clusters[]' --output text --no-paginate)

  print_row "Cluster Name" "Status" "Node Group Name" "Node Group Status" "Min Size" "Max Size" "Desired Size" "Node Group Type" "Instance Type" "K8s Version"

  for cluster_name in $clusters; do
    cluster_status=$(eksctl get cluster --name "$cluster_name" --output json | jq -r '.[0].Status')
    node_groups=$(eksctl get nodegroup --cluster "$cluster_name" --output json | jq -r 'map(.Name)[]')
    [[ -z $node_groups ]] && print_row "$cluster_name" "$cluster_status" "" "INACTIVE" "" "" "" "" "" ""

    for node_group_name in $node_groups; do
      node_group_info=$(eksctl get nodegroup --cluster "$cluster_name" --name "$node_group_name" --output=json | jq '{status: .[0].Status, minSize: .[0].MinSize, maxSize: .[0].MaxSize, desiredSize: .[0].DesiredCapacity, type: .[0].Type, instanceType: .[0].InstanceType, version: .[0].Version}')
      node_group_status=$(echo "$node_group_info" | jq -r '.status')
      min_size=$(echo "$node_group_info" | jq -r '.minSize')
      max_size=$(echo "$node_group_info" | jq -r '.maxSize')
      desired_size=$(echo "$node_group_info" | jq -r '.desiredSize')
      type=$(echo "$node_group_info" | jq -r '.type')
      instanceType=$(echo "$node_group_info" | jq -r '.instanceType')
      version=$(echo "$node_group_info" | jq -r '.version')

      [[ $CS_DEBUG == 1 ]] && echo "$node_group_info"
      print_row "$cluster_name" "$cluster_status" "$node_group_name" "$node_group_status" "$min_size" "$max_size" "$desired_size" "$type" "$instanceType" "$version"
    done
  done
  lib-error-check "$?"
}

eks-status() {
  [[ $CS_DEBUG -eq 1 ]] && lib-debug "$@"
  [[ -z $1 ]] && clusters "$@" && return 0
  output=$(eksctl get ng --cluster "$1" -o json | jq -r '.[] | "\(.Name) | \(.Status) | \(.MinSize) | \(.MaxSize) | \(.DesiredCapacity) | \(.Type) | \(.InstanceType) | \(.Version)"')
  header="Node Group Name | Node Group Status | Min Size | Max Size | Desired Size | Node Group Type | Instance Type | K8s Version"
  print_table "$header\n$output"
  lib-error-check "$?"
}

eks-start() {
  [[ $CS_DEBUG -eq 1 ]] && lib-debug "$@"
  [[ -z $1 ]] && clusters "$@" && return 0
  [[ -z $2 ]] && cs-usage 1
  eksctl scale ng "$(get-node-group "$1")" --cluster "$1" -N "$2"
  lib-error-check "$?"
}

eks-stop() {
  [[ $CS_DEBUG -eq 1 ]] && lib-debug "$@"
  [[ -z $1 ]] && clusters "$@" && return 0
  eksctl scale ng "$(get-node-group "$1")" --cluster "$1" -N 0
  lib-error-check "$?"
}

# --------------------------- EC2 FUNCTIONS

ids() {
  [[ $CS_DEBUG -eq 1 ]] && lib-debug "$@"
  output=$(aws ec2 describe-instances --query 'Reservations[].Instances[].[InstanceId, Tags[?Key==`Name`].Value|[0], State.Name]' --output json --no-paginate | jq -r 'sort_by(.[1])[] | "\(.[0]) | \(.[1]) | \(.[2])"')
  header="Instance ID | Name | State"
  print_table "$header\n$output"
  lib-error-check "$?"
}

status() {
  [[ $CS_DEBUG -eq 1 ]] && lib-debug "$@"
  [[ -z $1 ]] && ids "$@" && return 0
  output=$(aws ec2 describe-instance-status --instance-ids "$1" --output json | jq -r '.InstanceStatuses[] | "\(.InstanceId) | \(.InstanceState.Name) | \(.InstanceStatus.Status) | \(.SystemStatus.Status)"')
  header="Instance ID | Instance State | Instance Status | System Status"
  print_table "$header\n$output"
  lib-error-check "$?"
}

start() {
  [[ $CS_DEBUG -eq 1 ]] && lib-debug "$@"
  [[ -z $1 ]] && ids "$@" && return 0
  aws ec2 start-instances --instance-ids "$1" --output text --no-paginate
  lib-error-check "$?"
  update-ssh "$1"
  lib-error-check "$?"
}

stop() {
  [[ $CS_DEBUG -eq 1 ]] && lib-debug "$@"
  [[ -z $1 ]] && ids "$@" && return 0
  aws ec2 stop-instances --instance-ids "$1" --output text --no-paginate
  lib-error-check "$?"
}

restart() {
  [[ $CS_DEBUG -eq 1 ]] && lib-debug "$@"
  [[ -z $1 ]] && ids "$@" && return 0
  aws ec2 reboot-instances --instance-ids "$1" --output text --no-paginate
  lib-error-check "$?"
}

ssh() {
  [[ $CS_DEBUG -eq 1 ]] && lib-debug "$@"
  [[ -z $1 ]] && ids "$@" && return 0
  start "$1"
}

# --------------------------- CLI

cs-usage() {
  echo "Usage: cs COMPONENT <REQUIRED ARGS> [OPTIONAL ARGS]"
  echo ""
  echo "About:"
  echo "  cs -v, --version  Show version"
  echo "  cs --help         Show this message"
  echo ""
  echo "Components:"
  echo "  ec2     Manage EC2 instance states"
  echo "  eks     Manage EKS node states"
  echo "  lambda  List and download New Relic Lambda layers"
  echo ""
  echo "Components and Args:"
  echo "  ec2 status"
  echo "  ec2 start|stop|restart|ssh <instanceId>"
  echo "  eks status"
  echo "  eks start <cluster> <number of nodes>"
  echo "  eks stop <cluster>"
  echo "  lambda list-layers [runtime]|[all] [region]"
  echo "  lambda download-layers [layer]|[all] [region] [build]|[latest] [extension]|[agent]"
  echo ""
  echo "Examples:"
  echo "  cs ec2 status"
  echo "  cs eks start my-cluster 2"
  echo "  cs lambda list-layers                                     List layer names"
  echo "  cs lambda list-layers all                                 Details for all layers"
  echo "  cs lambda list-layers nodejs18.x us-west-2                Details for a specific layer"
  echo "  cs lambda download-layers NewRelicNodeJS18X us-west-2 24  Download build #24 for a layer"
  echo "  cs lambda download-layers all us-west-2 latest extension  Download all latest layers & show extension details"
  cs-unset $1
}

# unset functions to free up memmory
cs-unset() {
  unset -f cs-version cs-usage cs-thanks cs-lambda-go cs-eks-go cs-ec2-go
  exit $1
}

cs-version() {
  print-version "$1"
}

# display message before exit
# $1 -> version string
cs-thanks() {
  echo
  cs-version "$1"
  lib-msg "Made with <3 by Keegan Mullaney, a Senior Technical Support Engineer at New Relic."
  cs-unset 0
}

# $1: lambda
# $2: operation
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
userCommand=("$@") || lib-error-check 1 "Error executing user command: ${userCommand[*]}"

for c in ${userCommand[$offset]}; do
  [[ $CS_DEBUG == 1 ]] && echo "$c"
  if [[ $c == "-v" ]] || [[ $c == "--version" ]]; then
    cs-version $VERSION
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

cs-thanks $VERSION
