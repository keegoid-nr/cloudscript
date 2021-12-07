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

checks() {
  if ! lib_has aws; then cs_exit "aws cli v2"; fi
  if ! lib_has jq; then cs_exit "jq"; fi
  if [ $CS_DEBUG -eq 1 ]; then
    if ! lib_has brew; then cs_exit "brew"; fi
  fi
  if ! aws sts get-caller-identity >/dev/null; then exit 1; fi
}

getPublicDns() {
  aws ec2 describe-instances --instance-ids "$1" --query 'Reservations[*].Instances[*].PublicDnsName' --output text
}

getDefaultUser() {
  local imageId
  local imageDetails
  local platform

  # get platform info from aws
  imageId=$(aws ec2 describe-instances --instance-ids "$1" --query 'Reservations[*].Instances[*].ImageId' --output text)
  imageDetails=$(aws ec2 describe-images --image-ids "$imageId" --query 'Images[*].[Name,Description]' --output text)
  platform="$(aws ec2 describe-instances --instance-ids "$1" --query 'Reservations[*].Instances[*].PlatformDetails' --output text) $imageDetails"

  # check value
  [ $CS_DEBUG -eq 1 ] && echo "$platform"

  # logic to determine username from platform string
  if grep -iq "ubuntu" <<< "$platform"; then
    echo "ubuntu"
  elif grep -iq "debian" <<< "$platform"; then
    echo "admin"
  elif grep -iq "bitnami" <<< "$platform"; then
    echo "bitnami"
  elif grep -iq "windows" <<< "$platform"; then
    echo "Administrator"
  else
    echo "ec2-user"
  fi
}

updateSSH() {
  local dns
  local username
  lib_msg "Checking if instance $1 is running"
  aws ec2 wait instance-running --instance-ids "$1"
  # if [ "${FUNCNAME[1]}" == "start" ]; then
  #   lib_msg "waiting for public IPv4 address to update"
  #   sleep 5s
  # fi
  lib_msg "Getting public DNS name"
  dns=$(getPublicDns "$1")
  lib_msg "Getting default user name"
  username=$(getDefaultUser "$1")
  lib_msg "Enter a \"Host\" to update from $SSH_CONFIG"
  echo
  cat ~/.ssh/config
  echo
  read -erp "   : " host
  if grep "$host" "$SSH_CONFIG"; then
    # modify existing host
    sed -i.bak -e "/$host/,//  s/Hostname.*/Hostname $dns/" "$SSH_CONFIG"
  else
    # add new host
    cat <<-EOF >> "$SSH_CONFIG"
Host $host
  Hostname $dns
  User $username
EOF
  fi
}

# --------------------------- FUNCTIONS

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
  lib_echo "DNS"
  updateSSH "$1"
}

user() {
  lib_echo "User"
  getDefaultUser "$1"
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
  echo "Usage: $1 start|stop|restart|status|dns|user [instanceId]"
  echo
}

# --------------------------- CLI

# display message before exit
cs_thanks() {
  if lib_has figlet; then
    lib_msg "Thanks for using CloudScript!" | figlet -f mini
  else
    lib_msg "Thanks for using CloudScript!"
  fi
  lib_msg "Made with <3 by Keegan Mullaney, a Senior Technical Support Engineer at New Relic."
}

# $1: operation
# $2: instanceId (optional), if blank will get list of available instanceIds
cs_go() {
  checks

  # if no instanceId is provided, get them
  if [ -n "$1" ] && [ -z "$2" ]; then
    ids
    exit 0
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
  'user')
    user "$2"
    ;;
  *)
    usage "$0"
    exit 1
    ;;
  esac
}

# unset functions to free up memmory
cs_unset() {
  unset -f cs_go cs_thanks
}

# --------------------------  MAIN

cs_go "$@"
cs_thanks
cs_unset
exit 0
