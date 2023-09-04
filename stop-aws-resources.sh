#!/usr/bin/env bash
# -----------------------------------------------------
# Stop AWS Resources
#
# Author : Keegan Mullaney
# Company: New Relic
# Email  : kmullaney@newrelic.com
# Website: github.com/keegoid-nr/stop-aws-resources
# License: MIT
# -----------------------------------------------------

# --------------------------  SETUP PARAMETERS

# Global variables
REGIONS=("us-east-1" "us-east-2" "us-west-2" "ca-central-1")
EXCLUDE_INSTANCES=("i-1234567890abcdef0" "i-abcdef1234567890a")
EXCLUDE_CLUSTERS=("important-cluster1" "important-cluster2")

# --------------------------  FUNCTIONS

# Function to stop EC2 instances
stop_ec2_instances() {
  local region=$1
  local instance_ids
  instance_ids=$(aws ec2 describe-instances --region "$region" --query 'Reservations[*].Instances[*].[InstanceId]' --filters Name=instance-state-name,Values=running --output text)

  local filtered_ids=""
  for id in $instance_ids; do
    # shellcheck disable=SC2076
    if [[ ! " ${EXCLUDE_INSTANCES[*]} " =~ " ${id} " ]]; then
      filtered_ids="$filtered_ids $id"
    fi
  done

  if [ -n "$filtered_ids" ]; then
    echo "Stopping instances: $filtered_ids in region $region"
    aws ec2 stop-instances --instance-ids "$filtered_ids" --region "$region"
  else
    echo "No instances to stop in region $region after applying exclusion list"
  fi
}

# Function to scale down EKS nodes
scale_down_eks_nodes() {
  local region=$1
  local cluster_names
  cluster_names=$(aws eks list-clusters --region "$region" --query 'clusters' --output text)

  local filtered_clusters=""
  for cluster in $cluster_names; do
    # shellcheck disable=SC2076
    if [[ ! " ${EXCLUDE_CLUSTERS[*]} " =~ " ${cluster} " ]]; then
      filtered_clusters="$filtered_clusters $cluster"
    fi
  done

  for cluster in $filtered_clusters; do
    aws eks update-kubeconfig --name "$cluster" --region "$region"
    local nodegroup_names
    nodegroup_names=$(aws eks list-nodegroups --cluster-name "$cluster" --region "$region" --query 'nodegroups' --output text)
    for nodegroup in $nodegroup_names; do
      kubectl scale --replicas=0 "deployment/$nodegroup"
    done
  done
}

# --------------------------  MAIN

# Main execution loop
for REGION in "${REGIONS[@]}"; do
  stop_ec2_instances "$REGION"
  scale_down_eks_nodes "$REGION"
done
