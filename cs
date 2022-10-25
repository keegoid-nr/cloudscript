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

# $1: eksctl (optional)
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

# --------------------------- FUNCTIONS

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

dns() {
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
  # echo "EC2 Usage: $1 start|stop|restart|status|dns|user [instanceId]"
  echo "EC2 Usage: $1 start|stop|restart|status|dns [instanceId]"
  echo "EKS Usage: $1 eks start|stop|status [cluster] [nodes]"
  echo
}

# --------------------------- CLI

# display message before exit
cs_thanks() {
  echo
  if lib_has figlet; then
    lib_msg "Thanks for using CloudScript!" | figlet -f digital
  else
    lib_msg "Thanks for using CloudScript!"
  fi
  lib_msg "Made with <3 by Keegan Mullaney, a Senior Technical Support Engineer at New Relic."
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
  'dns')
    dns "$2"
    ;;
  *)
    usage "$0"
    exit 1
    ;;
  esac
}

# unset functions to free up memmory
cs_unset() {
  unset -f cs_go cs_eks_go cs_thanks
}

# --------------------------  MAIN

[ $CS_DEBUG -eq 1 ] && echo "arguments:" "$@"

if [ "eks" = "$1" ]; then
  # eks
  cs_eks_go "$@"
else
  # ec2
  cs_go "$@"
fi
cs_thanks
cs_unset
exit 0
