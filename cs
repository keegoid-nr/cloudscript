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

# --------------------------  SETUP PARAMETERS

[[ -z $CS_DEBUG ]] && CS_DEBUG=0
SSH_CONFIG="$HOME/.ssh/config"
VERSION="v1.8"
# ensure brace expansion is on for the shell
set -o braceexpand && [[ $CS_DEBUG -eq 1 ]] && set -o && echo "$SHELL"
# ensure Zsh uses zero-based arrays
[[ "$SHELL" == "/bin/zsh" ]] && zsh -c 'emulate -LR zsh; setopt ksharrays' && [[ $CS_DEBUG -eq 1 ]] && echo "$SHELL"

# --------------------------  LIBRARIES

lib-is-number() {
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
# $1 -> exit code
# $2 -> string
lib-error-check() {
  local exit_code="${1:-0}"
  local error_message="${2:-}"
  if (( exit_code != 0 )); then
    lib-debug
    if [[-n "$error_message" ]]; then
      lib-msg "exit code: $exit_code, from: $error_message"
    fi
    exit "$exit_code"
  fi
}

# display debug info
lib-debug() {
  if [[ "$SHELL" == "/bin/zsh" ]]; then
    lib-msg "${funcstack[1]}(${funcline[0]}) - ARGS: $*"
  else
    lib-msg "${FUNCNAME[1]}(${BASH_LINENO[0]}) - ARGS: $*"
  fi
}

# --------------------------- CS HELPER FUNCTIONS

cs-exit() {
  echo >&2 "Please install $1 before running this script."
  exit 1
}

# $1: additional requirement
cs-checks() {
  if ! lib-has aws; then cs-exit "aws cli v2"; fi
  if ! lib-has jq; then cs-exit "jq"; fi
  if [[ -n $1 ]]; then
    if ! lib-has "$1"; then cs-exit "$1"; fi
  fi
  if ! aws sts get-caller-identity >/dev/null; then exit 1; fi
}

cs-print-table() {
  input="$1"
  echo -e "$input" | column -t -s '|'
}

cs-print-row() {
  printf "%-40s %-9s %-19s %-19s %-9s %-9s %-13s %-16s %-14s %-12s\n" "$@"
}

cs-print-version() {
  lib-msg "CloudScript $1"
}

# --------------------------- EC2 FUNCTIONS

ec2-get-public-dns() {
  aws ec2 describe-instances --instance-ids "$1" --query 'Reservations[*].Instances[*].PublicDnsName' --output text --no-paginate
}

ec2-update-ssh() {
  local hostname
  local currentUser
  lib-msg "Checking if instance $1 is running"
  aws ec2 wait instance-running --instance-ids "$1"
  lib-msg "Getting public DNS name"
  hostname=$(ec2-get-public-dns "$1")
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
}

ec2-ids() {
  [[ $CS_DEBUG -eq 1 ]] && lib-debug "$@"
  output=$(aws ec2 describe-instances --query 'Reservations[].Instances[].[InstanceId, Tags[?Key==`Name`].Value|[0], State.Name]' --output json --no-paginate | jq -r 'sort_by(.[1])[] | "\(.[0]) | \(.[1]) | \(.[2])"')
  header="Instance ID | Name | State"
  cs-print-table "$header\n$output"
  lib-error-check "$?" "ec2-ids"
}

ec2-status() {
  [[ $CS_DEBUG -eq 1 ]] && lib-debug "$@"
  [[ -z $1 ]] && ec2-ids "$@" && return 0
  output=$(aws ec2 describe-instance-status --instance-ids "$1" --output json | jq -r '.InstanceStatuses[] | "\(.InstanceId) | \(.InstanceState.Name) | \(.InstanceStatus.Status) | \(.SystemStatus.Status)"')
  header="Instance ID | Instance State | Instance Status | System Status"
  cs-print-table "$header\n$output"
  lib-error-check "$?" "ec2-status"
}

ec2-start() {
  [[ $CS_DEBUG -eq 1 ]] && lib-debug "$@"
  [[ -z $1 ]] && ec2-ids "$@" && return 0
  aws ec2 start-instances --instance-ids "$1" --output text --no-paginate
  lib-error-check "$?" "start-instances"
  ec2-update-ssh "$1"
  lib-error-check "$?" "ec2-update-ssh"
}

ec2-stop() {
  [[ $CS_DEBUG -eq 1 ]] && lib-debug "$@"
  [[ -z $1 ]] && ec2-ids "$@" && return 0
  aws ec2 stop-instances --instance-ids "$1" --output text --no-paginate
  lib-error-check "$?" "ec2-stop"
}

ec2-restart() {
  [[ $CS_DEBUG -eq 1 ]] && lib-debug "$@"
  [[ -z $1 ]] && ec2-ids "$@" && return 0
  aws ec2 reboot-instances --instance-ids "$1" --output text --no-paginate
  lib-error-check "$?" "ec2-restart"
}

ec2-ssh() {
  [[ $CS_DEBUG -eq 1 ]] && lib-debug "$@"
  [[ -z $1 ]] && ec2-ids "$@" && return 0
  ec2-start "$1"
}

# --------------------------- EKS FUNCTIONS

eks-get-node-group() {
  eksctl get ng --cluster "$1" -o json | jq -r '.[].Name'
}

eks-clusters() {
  [[ $CS_DEBUG -eq 1 ]] && lib-debug "$@"
  clusters=$(aws eks list-clusters --query 'clusters[]' --output text --no-paginate)

  cs-print-row "Cluster Name" "Status" "Node Group Name" "Node Group Status" "Min Size" "Max Size" "Desired Size" "Node Group Type" "Instance Type" "K8s Version"

  for cluster_name in $clusters; do
    cluster_status=$(eksctl get cluster --name "$cluster_name" --output json | jq -r '.[0].Status')
    node_groups=$(eksctl get nodegroup --cluster "$cluster_name" --output json | jq -r 'map(.Name)[]')
    [[ -z $node_groups ]] && cs-print-row "$cluster_name" "$cluster_status" "" "INACTIVE" "" "" "" "" "" ""

    for node_group_name in $node_groups; do
      node_group_info=$(eksctl get nodegroup --cluster "$cluster_name" --name "$node_group_name" --output=json | jq '{status: .[0].Status, minSize: .[0].MinSize, maxSize: .[0].MaxSize, desiredSize: .[0].DesiredCapacity, type: .[0].Type, instanceType: .[0].InstanceType, version: .[0].Version}')
      node_group_status=$(echo "$node_group_info" | jq -r '.status')
      min_size=$(echo "$node_group_info" | jq -r '.minSize')
      max_size=$(echo "$node_group_info" | jq -r '.maxSize')
      desired_size=$(echo "$node_group_info" | jq -r '.desiredSize')
      type=$(echo "$node_group_info" | jq -r '.type')
      instanceType=$(echo "$node_group_info" | jq -r '.instanceType')
      version=$(echo "$node_group_info" | jq -r '.version')

      [[ $CS_DEBUG -eq 1 ]] && echo "$node_group_info"
      cs-print-row "$cluster_name" "$cluster_status" "$node_group_name" "$node_group_status" "$min_size" "$max_size" "$desired_size" "$type" "$instanceType" "$version"
    done
  done
  lib-error-check "$?" "eks-clusters"
}

eks-status() {
  [[ $CS_DEBUG -eq 1 ]] && lib-debug "$@"
  [[ -z $1 ]] && eks-clusters "$@" && return 0
  output=$(eksctl get ng --cluster "$1" -o json | jq -r '.[] | "\(.Name) | \(.Status) | \(.MinSize) | \(.MaxSize) | \(.DesiredCapacity) | \(.Type) | \(.InstanceType) | \(.Version)"')
  header="Node Group Name | Node Group Status | Min Size | Max Size | Desired Size | Node Group Type | Instance Type | K8s Version"
  cs-print-table "$header\n$output"
  lib-error-check "$?" "eks-status"
}

eks-start() {
  [[ $CS_DEBUG -eq 1 ]] && lib-debug "$@"
  [[ -z $1 ]] && eks-clusters "$@" && return 0
  [[ -z $2 ]] && cs-usage 1
  eksctl scale ng "$(eks-get-node-group "$1")" --cluster "$1" -N "$2"
  lib-error-check "$?" "eks-start"
}

eks-stop() {
  [[ $CS_DEBUG -eq 1 ]] && lib-debug "$@"
  [[ -z $1 ]] && eks-clusters "$@" && return 0
  eksctl scale ng "$(eks-get-node-group "$1")" --cluster "$1" -N 0
  lib-error-check "$?" "eks-stop"
}

# --------------------------- LAMBDA FUNCTIONS

lambda-list-compatible-runtimes() {
  curl -fsSL "https://$1.layers.newrelic-external.com/get-layers" | jq -r '.Layers[].LatestMatchingVersion.CompatibleRuntimes[]' | sort | uniq
  return 0
}

lambda-list-layer-names() {
  curl -fsSL "https://$1.layers.newrelic-external.com/get-layers" | jq -r '.Layers[].LayerName' | sort | uniq
  return 0
}

lambda-get-latest-build() {
  curl -fsSL "https://$1.layers.newrelic-external.com/get-layers" | jq -r --arg LAYER "$2" '.Layers[] | select(.LayerName == $LAYER) | .LatestMatchingVersion.Version'
  RET="$?"
  [[ $RET -gt 0 ]] && lambda-list-layer-names "$1" && exit 0
  return 0
}

lambda-get-layer-version() {
  local arn
  local region
  local build
  local xargsOpts
  local unzipOpts
  local path
  local globPattern
  arn="$1"
  region="$2"
  build="$3"
  xargsOpts="$4"
  unzipOpts="$5"
  path="$6"
  globPattern=( "$7" )

  aws --region "$region" lambda get-layer-version --layer-name "$arn" --version-number "$build" --query 'Content.Location' --output text --no-paginate | xargs curl "$xargsOpts" "$path.zip" && unzip "$unzipOpts" "$path.zip" -d "$path"
  if [[ -n "$globPattern" ]]; then
    if [[ "$SHELL" == "/bin/zsh" ]]; then
      stat -L -F "%y %n" $globPattern 2>/dev/null
    else
      stat -c "%y %n" $globPattern
    fi
  fi
  return 0
}

lambda-get-layer() {
  local arn
  local name
  local region
  local build
  local xargsOpts
  local unzipOpts
  local pythonRuntime
  local v
  local path
  local globPattern
  name="$1"
  region="$2"
  arn="arn:aws:lambda:$region:451483290750:layer:$name"
  build="$3"
  pythonRuntime=${1: -2}
  v="${pythonRuntime:0:1}.${pythonRuntime:1}"
  path="$1":"$3"

  if ! lib-is-number "$build"; then
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
    [[ $1 == *Java* ]] && globPattern="$path/*/*/NewRelic*"
    [[ $1 == *NodeJS* ]] && globPattern="$path/*/*/newrelic/newrelic*"
    [[ $1 == *Python* ]] && globPattern="$path/*/*/*/*/newrelic/agent*"
    lambda-get-layer-version "$arn" "$region" "$build" "$xargsOpts" "$unzipOpts" "$path" "$globPattern"
    [[ $1 == *NodeJS* ]] && grep 'newrelic/-' "$path/nodejs/package-lock.json" | uniq
    [[ $1 == *Python* ]] && cat "$path/python/lib/python$v/site-packages/newrelic/version.txt"
    [[ $1 == *Extension* ]] && lib-msg "an agent does not exist in the $1 layer"
  elif [[ $4 == extension ]]; then
    globPattern="$path/*/newrelic*"
    lambda-get-layer-version "$arn" "$region" "$build" "$xargsOpts" "$unzipOpts" "$path" "$globPattern"
    lib-error-check "$?" "get-layer extension"
  else
    lib-msg "------------------------------------------------------------"
    lib-msg "$arn:$3"
    lambda-get-layer-version "$arn" "$region" "$build" "$xargsOpts" "$unzipOpts" "$path"
    lib-error-check "$?" "lambda-get-layer"
  fi
  return 0
}

lambda-list-layers() {
  [[ $CS_DEBUG -eq 1 ]] && lib-debug "$@"
  local compatibleRuntime
  local region

  compatibleRuntime="$1"
  region="$2"

  [[ -z $region ]] && region="$(aws configure get region)"
  [[ -z $region ]] && region="us-west-2"
  [[ -z $compatibleRuntime ]] && lambda-list-compatible-runtimes "$region" && exit 0

  if [[ $compatibleRuntime == all ]]; then
    curl -fsSL "https://$region.layers.newrelic-external.com/lambda-get-layers" | jq .
  else
    curl -fsSL "https://$region.layers.newrelic-external.com/lambda-get-layers?CompatibleRuntime=$compatibleRuntime" | jq .
  fi
  lib-error-check "$?" "lambda-list-layers"
}

lambda-download-layers() {
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
  [[ -z $layer ]] && lambda-list-layer-names "$region" && exit 0

  cs-checks "unzip"
  cs-checks "xargs"

  if [[ $layer == all ]]; then
    for l in $(lambda-list-layer-names "$region"); do
      build=$(lambda-get-latest-build "$region" "$l")
      lambda-get-layer "$l" "$region" "$build" "$glob"
      [[ $CS_DEBUG -eq 1 ]] && lib-debug "$@"
    done
  else
    [[ -z $build ]] || [[ $build == latest ]] && build=$(lambda-get-latest-build "$region" "$layer")
    if [[ -z $build ]]; then
      lib-msg "-------------------------------------------------------"
      lib-msg "- check that your layer name is correct and try again -"
      lib-msg "-------------------------------------------------------"
      lambda-list-layer-names "$region" && exit 0
    else
      lambda-get-layer "$layer" "$region" "$build" "$glob"
    fi
  fi
  return 0
}

# --------------------------- CLI

cs-usage() {
  echo "Usage: cs COMPONENT <REQUIRED ARGS> [OPTIONAL ARGS]"
  echo ""
  echo "About:"
  echo "  cs -v, --version  Show version"
  echo "  cs -h, --help     Show this message"
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
  echo "  lambda lambda-list-layers [runtime]|[all] [region]"
  echo "  lambda lambda-download-layers [layer]|[all] [region] [build]|[latest] [extension]|[agent]"
  echo ""
  echo "Examples:"
  echo "  cs ec2 status"
  echo "  cs eks start my-cluster 2"
  echo "  cs lambda lambda-list-layers                                     List layer names"
  echo "  cs lambda lambda-list-layers all                                 Details for all layers"
  echo "  cs lambda lambda-list-layers nodejs18.x us-west-2                Details for a specific layer"
  echo "  cs lambda lambda-download-layers NewRelicNodeJS18X us-west-2 24  Download build #24 for a layer"
  echo "  cs lambda lambda-download-layers all us-west-2 latest extension  Download all latest layers & show extension details"
  cs-unset $1
}

# unset functions to free up memmory
cs-unset() {
  unset -f cs-version cs-usage cs-thanks cs-lambda-go cs-eks-go cs-ec2-go
  exit $1
}

cs-version() {
  cs-print-version "$1"
}

# display message before exit
# $1 -> version string
cs-thanks() {
  echo
  cs-version "$1"
  lib-msg "Made with <3 by Keegan Mullaney, a Lead Technical Support Engineer at New Relic."
  cs-unset 0
}

# $1: lambda
# $2: operation
# $3: compatibleRuntime|layer|all (optional), if blank will get a list of compatible runtimes or layer names
# $4: region (optional), if blank will use default region
# $5: build#|latest (optional), if blank will get latest layers
# $6: extension|agent (optional), if blank will show details for both
cs-lambda-go() {
  cs-checks "aws"
  cs-checks "curl"

  case "$2" in
  'list-layers')
    lambda-list-layers "$3" "$4"
    ;;
  'download-layers')
   lambda-download-layers "$3" "$4" "$5" "$6"
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
  cs-checks "eksctl"

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
  cs-checks

  case "$2" in
  'start')
    ec2-start "$3"
    ;;
  'stop')
    ec2-stop "$3"
    ;;
  'restart')
    ec2-restart "$3"
    ;;
  'status')
    ec2-status "$3"
    ;;
  'ssh')
    ec2-ssh "$3"
    ;;
  *)
    cs-usage 1
    ;;
  esac
}

# --------------------------  MAIN

# check number of arguments
if [[ $# -eq 0 ]]; then
  cs-usage 1
fi

# validate arguments passed to script
if [[ "$1" != "-v" ]] && [[ "$1" != "--version" ]] && [[ "$1" != "-h" ]] && [[ "$1" != "--help" ]] && [[ "$1" != "lambda" ]] && [[ "$1" != "eks" ]] && [[ "$1" != "ec2" ]]; then
  echo "Invalid component: $1"
  cs-usage 1
fi

# capture input array
userCommand=("$@") || lib-error-check 1 "Error executing user command: ${userCommand[*]}"
[[ $CS_DEBUG -eq 1 ]] && echo "$@" && echo "${userCommand[0]}" # && exit 0

for c in ${userCommand[0]}; do
  [[ $CS_DEBUG -eq 1 ]] && echo "$c"
  if [[ $c == "-v" ]] || [[ $c == "--version" ]]; then
    cs-version $VERSION
  elif [[ $c == "--help" ]] || [[ $c == "-h" ]]; then
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
