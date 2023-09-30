#!/usr/bin/env bash
# -----------------------------------------------------
# Stop AWS Resources
#
# Author : Keegan Mullaney
# Company: New Relic
# Email  : kmullaney@newrelic.com
# Website: github.com/keegoid-nr/cloudscript
# License: MIT
# Cron US: 0 9 * * * /usr/local/bin/stop-aws-resources-us.sh >> /var/log/stop-aws-resources-us.log 2>&1
# Cron AP: 0 12 * * * /usr/local/bin/stop-aws-resources-ap.sh >> /var/log/stop-aws-resources-ap.log 2>&1
# Cron EU: 0 19 * * * /usr/local/bin/stop-aws-resources-eu.sh >> /var/log/stop-aws-resources-eu.log 2>&1
# -----------------------------------------------------

# --------------------------  SETUP PARAMETERS

# ensure brace expansion is on for the shell
set -o braceexpand && [[ $CS_DEBUG -eq 1 ]] && set -o && echo "$SHELL"

VERSION="v0.1"

REGIONS=("us-east-1" "us-east-2" "us-west-2" "ca-central-1")
# REGIONS=("ap-east-1" "ap-south-1" "ap-northeast-3" "ap-northeast-2" "ap-southeast-1" "ap-southeast-2" "ap-northeast-1")
# REGIONS=("af-south-1" "eu-central-1" "eu-west-1" "eu-west-2" "eu-west-3" "eu-north-1")

# Updated Global variables for exclusion lists
EXCLUDE_METRIC_STREAMS=()
EXCLUDE_INSTANCES=()
EXCLUDE_ECS_CLUSTERS=()
EXCLUDE_EKS_CLUSTERS=()

# --------------------------  HELPER FUNCTIONS

# Function to be executed when Ctrl+C is pressed
handle_ctrl_c() {
  # shellcheck disable=SC2317
  echo "Ctrl+C pressed. Exiting the script."
  # shellcheck disable=SC2317
  exit 1
}

# Associate the function with the SIGINT signal (Ctrl+C)
trap handle_ctrl_c SIGINT

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

# unset functions to free up memmory
lib-unset() {
  unset -f scale_down_eks_clusters stop_metric_streams stop_instances
  exit $1
}

# output message with encoded characters
# $1 -> string
lib-msg() {
  echo -e "$1"
}

lib-version() {
  lib-msg "stop-aws-resources-ap $1"
}

# display message before exit
# $1 -> version string
lib-thanks() {
  echo
  lib-version "$1"
  lib-msg "Made with <3 by Keegan Mullaney, a Lead Technical Support Engineer at New Relic."
  lib-unset 0
}

is_excluded() {
  local resource=$1
  local -n exclusion_list=$2  # -n makes exclusion_list a reference to the array passed
  for exclude in "${exclusion_list[@]}"; do
    [[ "$resource" == "$exclude" ]] && echo "  -> skipping $resource" && return 0
  done
  return 1
}

# --------------------------  FUNCTIONS

stop_metric_streams() {
  lib-checks "aws"
  local region=$1
  echo "Stopping Metric Streams in region $region"
  for stream in $(aws cloudwatch list-metric-streams --region "$region" --query 'Entries[].Name' --output text --no-paginate); do
    is_excluded "$stream" EXCLUDE_METRIC_STREAMS && continue
    stream_state=$(aws cloudwatch get-metric-stream --region "$region" --name "$stream" --query 'State' --output text --no-paginate)
    if [ "$stream_state" != "stopped" ]; then
      echo "  -> stopping $stream"
      aws cloudwatch stop-metric-streams --region "$region" --names "$stream"
    fi
  done
}

stop_instances() {
  lib-checks "aws"
  local region=$1
  echo "Stopping EC2 instances in region $region"
  # shellcheck disable=SC2016
  for instance in $(aws ec2 describe-instances --region "$region" --query 'Reservations[].Instances[?State.Name==`running`].InstanceId' --output text --no-paginate); do
    is_excluded "$instance" EXCLUDE_INSTANCES && continue
    echo "  -> stopping $instance"
    aws ec2 stop-instances --region "$region" --instance-ids "$instance" --output text --no-paginate
  done
}

# scale_down_ecs_clusters() {
#   lib-checks "aws"
#   local region=$1
#   echo "Stopping ECS clusters in region $region"
#   for cluster in $(aws ecs list-clusters --region "$region" --query 'clusterArns[]' --output text --no-paginate); do
#     is_excluded "$cluster" EXCLUDE_ECS_CLUSTERS && continue

#     # Update all services to have 0 desired count
#     for service in $(aws ecs list-services --region "$region" --cluster "$cluster" --query 'serviceArns[]' --output text --no-paginate); do
#       echo "  -> scaling $service in $cluster to 0"
#       aws ecs update-service --region "$region" --cluster "$cluster" --service "$service" --desired-count 0
#     done

#     # Stop all running tasks
#     for task in $(aws ecs list-tasks --region "$region" --cluster "$cluster" --query 'taskArns[]' --output text --no-paginate); do
#       echo "  -> stopping task $task in $cluster"
#       aws ecs stop-task --region "$region" --cluster "$cluster" --task "$task" --output text --no-paginate
#     done
#   done
# }

scale_down_eks_clusters() {
  lib-checks "aws"
  lib-checks "eksctl"
  lib-checks "kubectl"
  local region=$1
  echo "Scaling down EKS clusters in region $region"
  for eks_cluster in $(aws eks list-clusters --region "$region" --query 'clusters[]' --output text --no-paginate); do
    is_excluded "$eks_cluster" EXCLUDE_EKS_CLUSTERS && continue

    # if the node groups were created with aws
    aws_nodegroup_names=$(aws eks list-nodegroups --region "$region" --cluster-name "$eks_cluster" --query 'nodegroups[]' --output text --no-paginate)
    for nodegroup in $aws_nodegroup_names; do
      current_node_count=$(aws eks describe-nodegroup --region "$region" --cluster-name "$eks_cluster" --nodegroup-name "$nodegroup" --query 'nodegroup.scalingConfig.desiredSize')
      if [ "$current_node_count" != "0" ]; then
        echo "  -> scaling $nodegroup in $eks_cluster to 0"
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
        echo "  -> scaling $nodegroup in $eks_cluster to 0"
        eksctl scale ng "$nodegroup" --region "$region" --cluster "$eks_cluster" -N 0
      fi
    done
  done
}

# --------------------------  MAIN EXECUTION

for region in "${REGIONS[@]}"; do
  printf "\n==== Processing region %s ====\n" "$region"
  # scale_down_ecs_clusters "$region"
  scale_down_eks_clusters "$region"
  stop_metric_streams "$region"
  stop_instances "$region"
done

lib-thanks $VERSION
