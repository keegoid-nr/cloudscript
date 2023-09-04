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

# Define the North American AWS regions
REGIONS=("us-east-1" "us-east-2" "us-west-2" "ca-central-1")

# --------------------------  FUNCTIONS

# Function to stop EC2 instances
stop_ec2_instances() {
    local region=$1
    # Fetch the list of running instances
    local instance_ids=$(aws ec2 describe-instances --region $region --query 'Reservations[*].Instances[*].[InstanceId]' --filters Name=instance-state-name,Values=running --output text)

    # Stop the instances
    if [ -n "$instance_ids" ]; then
        echo "Stopping instances: $instance_ids in region $region"
        aws ec2 stop-instances --instance-ids $instance_ids --region $region
    else
        echo "No running instances found in region $region"
    fi
}

# Function to scale down EKS nodes
scale_down_eks_nodes() {
    local region=$1
    # Get the list of EKS clusters in the region
    local cluster_names=$(aws eks list-clusters --region $region --query 'clusters' --output text)

    # Loop through each cluster to update kubeconfig and scale down nodes
    for cluster in $cluster_names; do
        # Update kubeconfig for the cluster
        aws eks update-kubeconfig --name $cluster --region $region

        # Get the list of nodegroups for the cluster
        local nodegroup_names=$(aws eks list-nodegroups --cluster-name $cluster --region $region --query 'nodegroups' --output text)

        # Scale down each nodegroup
        for nodegroup in $nodegroup_names; do
            kubectl scale --replicas=0 deployment/$nodegroup
        done
    done
}

# --------------------------  MAIN

# Call functions for each region
for REGION in "${REGIONS[@]}"; do
    stop_ec2_instances $REGION
    scale_down_eks_nodes $REGION
done
