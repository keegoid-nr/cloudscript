#!/bin/bash
# -----------------------------------------------------
# Stop AWS Resources
#
# Author : Keegan Mullaney
# Company: New Relic
# Email  : kmullaney@newrelic.com
# Website: github.com/keegoid-nr/cloudscript
# License: MIT
# $ crontab -l
# PATH=/usr/local/bin:$PATH
# 0 9 * * * /usr/local/bin/stop-aws-resources-us.sh >> /var/log/aws-resource-logs/stop-aws-resources-us.log 2>&1
# 0 14 * * * /usr/local/bin/stop-aws-resources-ap.sh >> /var/log/aws-resource-logs/stop-aws-resources-ap.log 2>&1
# 0 19 * * * /usr/local/bin/stop-aws-resources-eu.sh >> /var/log/aws-resource-logs/stop-aws-resources-eu.log 2>&1
# -----------------------------------------------------

# --------------------------  SETUP PARAMETERS

REGIONS=("eu-west-1" "eu-west-2")

# ensure brace expansion is on for the shell
set -o braceexpand && [[ $CS_DEBUG -eq 1 ]] && set -o && echo "$SHELL"

VERSION="v0.2"
SHIFT="us"
KUBECONFIG=/home/ec2-user/.kube/config

# Updated Global variables for exclusion lists
EXCLUDE_METRIC_STREAMS=()
EXCLUDE_INSTANCES=("i-0cb1c25d3f6adb70b")
EXCLUDE_ECS_CLUSTERS=()
EXCLUDE_EKS_CLUSTERS=()
EXCLUDE_LOG_GROUPS=("/aws/lambda/aws-controltower-NotificationForwarder")

# CW log group retention
RETENTION_PERIOD=3

# --------------------------  TRAPS

# Function to be executed when Ctrl+C is pressed
handle_ctrl_c() {
  # shellcheck disable=SC2317
  echo "Ctrl+C pressed. Exiting the script."
  # shellcheck disable=SC2317
  exit 1
}

# Associate the function with the SIGINT signal (Ctrl+C)
trap handle_ctrl_c SIGINT

# Define the cleanup function
cleanup() {
  unset -f scale_down_ecs_clusters scale_down_eks_clusters stop_metric_streams stop_instances set_log_group_retention
}

# Set the trap to call the cleanup function on specific signals
trap cleanup EXIT HUP INT TERM

# --------------------------  HELPER FUNCTIONS

lib-has() {
  type "$1" >/dev/null 2>&1
}

lib-exit() {
  echo >&2 "Please install $1 before running this script."
  exit 1
}

# $1: additional requirement
lib-checks() {
  if [[ -n $1 ]]; then
    if ! lib-has "$1"; then lib-exit "$1"; fi
  fi
  if ! aws sts get-caller-identity >/dev/null; then exit 1; fi
}

# output message with encoded characters
# $1 -> string
lib-msg() {
  echo -e "$1"
}

lib-version() {
  lib-msg "stop-aws-resources-$SHIFT $1"
}

lib-log() {
  timestamp=$(date +"%Y-%m-%d %H:%M:%S")
  log_message="$timestamp | $1"
  echo "$log_message"
}

# display message before exit
# $1 -> version string
lib-thanks() {
  echo
  lib-version "$1"
  lib-msg "Made with <3 by Keegan Mullaney, a Lead Technical Support Engineer at New Relic."
}

lib_is_excluded() {
  local resource=$1
  local -n exclusion_list=$2 # -n makes exclusion_list a reference to the array passed
  for exclude in "${exclusion_list[@]}"; do
    [[ "$resource" == "$exclude" ]] && lib-log "skipping $resource" && return 0
  done
  return 1
}

lib_print_header() {
  local service=${1}
  local action=${2^^}
  lib-msg "$service: $action"
}

# --------------------------  FUNCTIONS

stop_instances() {
  lib-checks "aws"
  local region=$1
  # local action=0
  # shellcheck disable=SC2016
  for instance in $(aws ec2 describe-instances --region "$region" --query 'Reservations[].Instances[?State.Name==`running`].InstanceId' --output text); do
    lib_is_excluded "$instance" EXCLUDE_INSTANCES && continue
    lib-log "stopping $instance"
    aws ec2 stop-instances --region "$region" --instance-ids "$instance" --output text --no-paginate
    # action=1
  done
  # [[ "$action" -eq 1 ]] && lib_print_header "EC2 instances" "Stopped"
}

scale_down_ecs_clusters() {
  lib-checks "aws"
  local region=$1
  for cluster in $(aws ecs list-clusters --region "$region" --query 'clusterArns[]' --output text); do
    lib_is_excluded "$cluster" EXCLUDE_ECS_CLUSTERS && continue

    # Update all services to have 0 desired count
    for service in $(aws ecs list-services --region "$region" --cluster "$cluster" --query 'serviceArns[]' --output text); do
      lib-log "scaling $service in $cluster to 0"
      aws ecs update-service --region "$region" --cluster "$cluster" --service "$service" --desired-count 0 --output text --no-paginate
    done

    # Stop all running tasks
    for task in $(aws ecs list-tasks --region "$region" --cluster "$cluster" --query 'taskArns[]' --output text); do
      lib-log "stopping task $task in $cluster"
      aws ecs stop-task --region "$region" --cluster "$cluster" --task "$task" --output text --no-paginate
    done
  done
  # lib_print_header "ECS Clusters" "Scaled down"
}

scale_down_eks_clusters() {
  lib-checks "aws"
  lib-checks "eksctl"
  lib-checks "kubectl"
  local region=$1
  for eks_cluster in $(aws eks list-clusters --region "$region" --query 'clusters[]' --output text); do
    lib_is_excluded "$eks_cluster" EXCLUDE_EKS_CLUSTERS && continue

    # if the node groups were created with aws
    aws_nodegroup_names=$(aws eks list-nodegroups --region "$region" --cluster-name "$eks_cluster" --query 'nodegroups[]' --output text)
    for nodegroup in $aws_nodegroup_names; do
      current_node_count=$(aws eks describe-nodegroup --region "$region" --cluster-name "$eks_cluster" --nodegroup-name "$nodegroup" --query 'nodegroup.scalingConfig.desiredSize')
      if [ "$current_node_count" != "0" ]; then
        lib-log "scaling $nodegroup in $eks_cluster to 0"
        aws eks update-nodegroup-config --region "$region" --cluster-name "$eks_cluster" --nodegroup-name "$nodegroup" --scaling-config desiredSize=0,minSize=0 --output text --no-paginate
      fi
    done

    # if the node groups were created with aws, no need to attempt eksctl
    [[ -n "$aws_nodegroup_names" ]] && continue
    aws eks update-kubeconfig --name "$eks_cluster" --region "$region"
    eksctl_nodegroup_names=$(eksctl get ng --region "$region" --cluster "$eks_cluster" | awk 'NR>1 {print $2}')
    for nodegroup in $eksctl_nodegroup_names; do
      current_node_count=$(eksctl get ng --region "$region" --cluster "$eks_cluster" --name "$nodegroup" | awk 'NR>1 {print $7}')
      if [ "$current_node_count" != "0" ]; then
        lib-log "scaling $nodegroup in $eks_cluster to 0"
        eksctl scale ng "$nodegroup" --region "$region" --cluster "$eks_cluster" -N 0
      fi
    done
  done
  # lib_print_header "EKS clusters" "Scaled down"
}

stop_metric_streams() {
  lib-checks "aws"
  local region=$1
  for stream in $(aws cloudwatch list-metric-streams --region "$region" --query 'Entries[].Name' --output text); do
    lib_is_excluded "$stream" EXCLUDE_METRIC_STREAMS && continue
    stream_state=$(aws cloudwatch get-metric-stream --region "$region" --name "$stream" --query 'State' --output text)
    if [ "$stream_state" != "stopped" ]; then
      lib-log "stopping $stream"
      aws cloudwatch stop-metric-streams --region "$region" --names "$stream"
    fi
  done
  # lib_print_header "CW Metric Streams" "Stopped"
}

set_log_group_retention() {
  local region=$1

  # iterate through the log groups and call the set log group retention
  while read -r log_group current_retention; do
    if [ "$current_retention" != "$RETENTION_PERIOD" ]; then
      lib_is_excluded "$log_group" EXCLUDE_LOG_GROUPS && continue
      lib-log "setting CW log group retention for $log_group to $RETENTION_PERIOD days"
      aws logs put-retention-policy --region "$region" --log-group-name "$log_group" --retention-in-days $RETENTION_PERIOD --output text --no-paginate
    fi
  done <<<"$(aws logs describe-log-groups --region "$region" --query 'logGroups[*].[logGroupName, retentionInDays]' --output text)"
  # lib_print_header "CW log groups" "Retention set"
}

# --------------------------  MAIN EXECUTION

for region in "${REGIONS[@]}"; do
  printf "\n==== Processing region %s ====\n" "$region"
  scale_down_ecs_clusters "$region"
  scale_down_eks_clusters "$region"
  stop_metric_streams "$region"
  set_log_group_retention "$region"
  stop_instances "$region"
done

lib-thanks $VERSION
